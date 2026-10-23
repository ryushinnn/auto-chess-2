using System;
using System.Collections.Generic;

namespace AutoChess.Core.Net {
    /// <summary>
    /// Server side of one online match, minus the sockets: runs the authoritative <see cref="MatchSimulation"/>,
    /// applies the players' commands in arrival order, relays the accepted ones as a <see cref="LockstepFrame"/>
    /// per tick, answers refused ones to their sender only, and checks the state hashes the clients report.
    /// The owner calls <see cref="Receive"/> as messages arrive and <see cref="Step"/> once per tick
    /// (1 / TicksPerSecond), always from the same thread.
    /// </summary>
    public sealed class LockstepHost {
        public readonly MatchSimulation Match;
        public readonly MatchRecorder Recorder;
        public readonly LockstepStart Info;
        /// <summary>Commands one player may queue per frame; the rest are refused (flood guard).</summary>
        public int MaxCommandsPerFrame = 16;

        /// <summary>Message for every connected player.</summary>
        public Action<byte[]> Broadcast;
        /// <summary>Message for one player.</summary>
        public Action<int, byte[]> SendTo;
        /// <summary>(player, frame, client hash, server hash).</summary>
        public event Action<int, int, ulong, ulong> Desynced;

        readonly List<(int player, int seq, GameCommand command)> pending = new();
        readonly int[] queued;
        readonly Dictionary<int, ulong> hashes = new();
        readonly NetWriter writer = new(256);
        readonly LockstepFrame frame = new();

        public LockstepHost(GameDatabase db, LockstepStart info, Combat.AbilityRegistry abilities = null) {
            Info = info ?? throw new ArgumentNullException(nameof(info));
            Match = new MatchSimulation(db, info.ToConfig(abilities));
            Recorder = new MatchRecorder(Match);
            queued = new int[info.PlayerCount];
            Match.Start();
            Match.Events.Clear();
        }

        public int Frame { get; private set; }
        public bool IsFinished => Match.Phase == MatchPhase.Finished;

        /// <summary>The start message of <paramref name="player"/> (same match, their own player id).</summary>
        public byte[] StartMessageFor(int player) {
            var copy = new LockstepStart {
                MatchId = Info.MatchId, Seed = Info.Seed, TickRate = Info.TickRate, HashInterval = Info.HashInterval,
                AllowCheats = Info.AllowCheats, SkipStarterPack = Info.SkipStarterPack, SetupSeconds = Info.SetupSeconds,
                PreparationSeconds = Info.PreparationSeconds, LocalPlayer = player, Names = Info.Names
            };
            return copy.Encode();
        }

        /// <summary>Queues a command for the next frame on behalf of <paramref name="player"/> (server-side bots).</summary>
        public void Submit(int player, GameCommand command) {
            if (player < 0 || player >= queued.Length || command == null) return;
            command.PlayerId = player;
            pending.Add((player, 0, command));
        }

        /// <summary>A match message from <paramref name="player"/> (Command or Hash; other types are ignored).</summary>
        public void Receive(int player, byte[] message) {
            if (player < 0 || player >= queued.Length || message == null || message.Length == 0) return;
            var r = new NetReader(message, 1, message.Length - 1);
            switch (message[0]) {
                case Lockstep.Command: {
                    int seq;
                    GameCommand command;
                    try {
                        seq = r.ReadVarInt();
                        command = CommandCodec.Read(r);
                    }
                    catch (FormatException e) {
                        SendTo?.Invoke(player, Lockstep.EncodeRejected(0, CommandResult.Fail(CommandError.Unsupported, e.Message)));
                        return;
                    }
                    command.PlayerId = player;
                    if (command.IsCheat && !Info.AllowCheats) {
                        SendTo?.Invoke(player, Lockstep.EncodeRejected(seq, CommandError.CheatsDisabled));
                        return;
                    }
                    if (queued[player] >= MaxCommandsPerFrame) {
                        SendTo?.Invoke(player, Lockstep.EncodeRejected(seq, CommandResult.Fail(CommandError.Unsupported, "too many commands")));
                        return;
                    }
                    queued[player]++;
                    pending.Add((player, seq, command));
                    break;
                }
                case Lockstep.Hash: {
                    int at;
                    ulong hash;
                    try {
                        at = r.ReadVarInt();
                        hash = r.ReadULong();
                    }
                    catch (FormatException) {
                        return;
                    }
                    if (hashes.TryGetValue(at, out var expected) && expected != hash) {
                        Desynced?.Invoke(player, at, hash, expected);
                        SendTo?.Invoke(player, Lockstep.EncodeDesync(at, expected));
                    }
                    break;
                }
            }
        }

        /// <summary>Applies the queued commands, ticks once and broadcasts the frame.</summary>
        public void Step() {
            frame.Number = Frame + 1;
            frame.Entries.Clear();
            foreach (var (player, seq, command) in pending) {
                var result = Recorder.Submit(command);
                if (result.Ok) frame.Entries.Add(new LockstepFrame.Entry { Seq = seq, Command = command });
                else SendTo?.Invoke(player, Lockstep.EncodeRejected(seq, result));
            }
            pending.Clear();
            Array.Clear(queued, 0, queued.Length);
            Recorder.Tick();
            Match.Events.Clear(); // nobody listens on the server
            Frame = frame.Number;

            var interval = Info.HashInterval;
            if (interval > 0 && Frame % interval == 0) {
                hashes[Frame] = StateHasher.Hash(Match.State);
                hashes.Remove(Frame - interval * 64);
            }

            writer.Reset();
            frame.Write(writer);
            Broadcast?.Invoke(writer.ToArray());
        }
    }
}
