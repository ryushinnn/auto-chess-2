using System;
using System.Collections.Generic;
using AutoChess.Core.Net;

namespace AutoChess.Core {
    /// <summary>
    /// Online session: a local replica of the server's <see cref="MatchSimulation"/> driven by the frames the
    /// server relays (<see cref="Lockstep"/>). Commands are sent, not applied: they take effect when the frame
    /// that carries them is played, on every client at the same tick. Frames are buffered a little
    /// (<see cref="TargetBuffer"/>) so network jitter does not stall the playback, and played faster when they
    /// pile up. Transport-agnostic: the owner feeds <see cref="Receive"/> and provides the send callback.
    /// </summary>
    public sealed class LockstepSession : IGameSession {
        public readonly MatchSimulation Match;
        public readonly MatchRecorder Recorder;
        public readonly LockstepStart Info;

        /// <summary>Frames buffered before the playback starts / resumes.</summary>
        public int TargetBuffer = 2;
        /// <summary>Above this many buffered frames the playback catches up (up to <see cref="MaxTicksPerAdvance"/> per call).</summary>
        public int MaxBuffer = 6;
        public int MaxTicksPerAdvance = 8;

        readonly Action<byte[]> send;
        readonly Queue<LockstepFrame> frames = new();
        readonly Dictionary<int, Action<CommandResult>> callbacks = new();
        readonly Dictionary<int, ulong> hashes = new();
        readonly List<GameEvent> dispatch = new();
        int lastSeq;
        float accumulator;
        bool playing;
        bool flushing;

        public event Action<GameEvent> EventRaised;
        /// <summary>(frame, local hash, server hash): this replica no longer matches the server.</summary>
        public event Action<int, ulong, ulong> Desynced;
        public event Action<int> PlayerLeft;
        public event Action<Lockstep.EndReason> Ended;
        public event Action<string> ProtocolError;
        /// <summary>After each frame was played (frame number).</summary>
        public event Action<int> FramePlayed;

        public LockstepSession(GameDatabase db, LockstepStart info, Action<byte[]> send, Combat.AbilityRegistry abilities = null,
            bool record = true) {
            Info = info ?? throw new ArgumentNullException(nameof(info));
            this.send = send ?? throw new ArgumentNullException(nameof(send));
            Match = new MatchSimulation(db, info.ToConfig(abilities));
            if (record) Recorder = new MatchRecorder(Match);
        }

        /// <summary>Frames played so far (= the server frame this replica is at).</summary>
        public int Frame { get; private set; }
        public int BufferedFrames => frames.Count;
        public bool IsDesynced { get; private set; }
        public bool IsEnded { get; private set; }

        public GameDatabase Db => Match.Db;
        public MatchState State => Match.State;
        public int LocalPlayerId => Info.LocalPlayer;
        public PlayerState LocalPlayer => Match.Player(LocalPlayerId);
        public float PhaseTimeLeft => (float)Match.PhaseTimeLeft;
        public float PhaseDuration => (float)Match.PhaseDuration;
        public float TickProgress => (float)Math.Clamp(accumulator / Match.Rules.TickDelta, 0, 1);
        public bool BoardLocked => Match.BoardLocked;

        public void Start() {
            Match.Start();
            Flush();
        }

        /// <summary>Handles one server message (any type; the ones that are not about the match are ignored).</summary>
        public void Receive(byte[] message) {
            if (message == null || message.Length == 0) return;
            try {
                var r = new NetReader(message, 1, message.Length - 1);
                switch (message[0]) {
                    case Lockstep.Frame:
                        frames.Enqueue(LockstepFrame.Read(r));
                        break;
                    case Lockstep.CommandRejected: {
                        var seq = r.ReadVarInt();
                        var error = (CommandError)r.ReadVarInt();
                        var text = r.ReadString();
                        if (callbacks.TryGetValue(seq, out var callback)) {
                            callbacks.Remove(seq);
                            callback?.Invoke(CommandResult.Fail(error, text));
                        }
                        break;
                    }
                    case Lockstep.Desync: {
                        var frame = r.ReadVarInt();
                        var serverHash = r.ReadULong();
                        IsDesynced = true;
                        Desynced?.Invoke(frame, hashes.TryGetValue(frame, out var local) ? local : 0, serverHash);
                        break;
                    }
                    case Lockstep.PlayerLeft:
                        PlayerLeft?.Invoke(r.ReadVarInt());
                        break;
                    case Lockstep.End:
                        IsEnded = true;
                        Ended?.Invoke((Lockstep.EndReason)r.ReadVarInt());
                        break;
                }
            }
            catch (FormatException e) {
                ProtocolError?.Invoke($"bad message {message[0]}: {e.Message}");
            }
        }

        /// <summary>Plays the buffered frames at the simulation rate, then dispatches the events.</summary>
        public void Advance(float deltaTime) {
            var dt = (float)Match.Rules.TickDelta;
            if (!playing) {
                if (frames.Count < TargetBuffer && !IsEnded) return;
                playing = true;
            }
            accumulator += Math.Max(0, deltaTime);
            var ticks = 0;
            while (frames.Count > 0 && ticks < MaxTicksPerAdvance && (accumulator >= dt || frames.Count > MaxBuffer)) {
                Play(frames.Dequeue());
                accumulator = Math.Max(0, accumulator - dt);
                ticks++;
            }
            if (frames.Count == 0 && accumulator >= dt) {
                // Starved: hold at the end of the tick and buffer again before resuming.
                accumulator = dt * 0.999f;
                playing = IsEnded;
            }
            Flush();
        }

        /// <summary>Plays every buffered frame now (tests, tools).</summary>
        public void PlayAll() {
            while (frames.Count > 0) Play(frames.Dequeue());
            Flush();
        }

        void Play(LockstepFrame frame) {
            if (frame.Number != Frame + 1) ProtocolError?.Invoke($"frame {frame.Number} after {Frame}");
            foreach (var entry in frame.Entries) {
                var command = entry.Command;
                var result = Recorder != null ? Recorder.Submit(command) : Match.Submit(command);
                if (!result.Ok && !IsDesynced) {
                    // The server's simulation accepted it: this replica has diverged.
                    IsDesynced = true;
                    Desynced?.Invoke(frame.Number, StateHasher.Hash(Match.State), 0);
                }
                if (command.PlayerId == LocalPlayerId && callbacks.TryGetValue(entry.Seq, out var callback)) {
                    callbacks.Remove(entry.Seq);
                    callback?.Invoke(result);
                }
            }
            if (Recorder != null) Recorder.Tick();
            else Match.Tick();
            Frame = frame.Number;

            var interval = Info.HashInterval;
            if (interval > 0 && Frame % interval == 0) {
                var hash = StateHasher.Hash(Match.State);
                hashes[Frame] = hash;
                hashes.Remove(Frame - interval * 16);
                send(Lockstep.EncodeHash(Frame, hash));
            }
            FramePlayed?.Invoke(Frame);
        }

        public void Submit(GameCommand command, Action<CommandResult> onResult = null) {
            if (command == null) {
                onResult?.Invoke(CommandResult.Fail(CommandError.Unsupported, "null command"));
                return;
            }
            command.PlayerId = LocalPlayerId;
            if (IsEnded) {
                onResult?.Invoke(CommandResult.Fail(CommandError.WrongPhase, "the match is over"));
                return;
            }
            if (command.IsCheat && !Info.AllowCheats) {
                onResult?.Invoke(CommandError.CheatsDisabled);
                return;
            }
            var seq = ++lastSeq;
            if (onResult != null) callbacks[seq] = onResult;
            send(Lockstep.EncodeCommand(seq, command));
        }

        public CommandError CanMove(int unitId, UnitLocation target) => Match.CanMove(LocalPlayerId, unitId, target);
        public bool CanEquip(int unitId, string itemId) => Match.CanEquip(LocalPlayerId, unitId, itemId);

        void Flush() {
            if (flushing) return;
            flushing = true;
            try {
                while (Match.Events.Count > 0) {
                    dispatch.Clear();
                    Match.Events.DrainTo(dispatch);
                    foreach (var e in dispatch) EventRaised?.Invoke(e);
                }
            }
            finally {
                dispatch.Clear();
                flushing = false;
            }
        }
    }
}
