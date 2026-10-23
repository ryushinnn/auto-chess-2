using System;
using System.Collections.Generic;

namespace AutoChess.Core.Net {
    /// <summary>
    /// Wire format of an online match (one WebSocket binary message = [type byte][payload]). The server
    /// (auto-chess-2-server) runs the authoritative <see cref="MatchSimulation"/>: it applies the commands
    /// it receives, relays the accepted ones as frames, and every client runs the same simulation on those
    /// frames (lockstep). Frame n = the accepted commands in order, then one simulation tick. Bump
    /// <see cref="Version"/> on any change; the server is built from these same sources.
    /// </summary>
    public static class Lockstep {
        public const int Version = 3;

        // client -> server
        /// <summary>[version varint][data hash ulong][name string][device id string].</summary>
        public const byte Hello = 1;
        public const byte FindMatch = 2;
        public const byte CancelFind = 3;
        /// <summary>[seq varint][command (CommandCodec)]. The server overwrites the player id.</summary>
        public const byte Command = 4;
        /// <summary>[frame varint][hash ulong]: StateHasher after that frame.</summary>
        public const byte Hash = 5;
        public const byte LeaveMatch = 6;

        // server -> client
        /// <summary>[client id varint].</summary>
        public const byte Welcome = 100;
        /// <summary>[RefuseReason varint][message string]; the server closes the connection after it.</summary>
        public const byte Refused = 101;
        /// <summary>[waiting varint][match size varint].</summary>
        public const byte Queue = 102;
        /// <summary><see cref="LockstepStart"/>.</summary>
        public const byte Start = 103;
        /// <summary><see cref="LockstepFrame"/>.</summary>
        public const byte Frame = 104;
        /// <summary>[frame varint][server hash ulong]: the reported hash differs from the server's.</summary>
        public const byte Desync = 105;
        /// <summary>[player varint]: the player left; the simulation keeps playing their roster.</summary>
        public const byte PlayerLeft = 106;
        /// <summary>[seq varint][CommandError varint][message string]: the server's simulation refused it.</summary>
        public const byte CommandRejected = 107;
        /// <summary>[EndReason varint].</summary>
        public const byte End = 108;

        public enum RefuseReason {
            Version = 1,
            GameData = 2,
            Malformed = 3,
            ServerFull = 4,
        }

        public enum EndReason {
            Finished = 0,
            Aborted = 1,
        }

        public static NetWriter Message(byte type, int capacity = 32) {
            var w = new NetWriter(capacity);
            w.WriteByte(type);
            return w;
        }

        public static byte[] Empty(byte type) => new[] { type };

        public static byte[] Ints(byte type, params int[] values) {
            var w = Message(type);
            foreach (var v in values) w.WriteVarInt(v);
            return w.ToArray();
        }

        public static byte[] EncodeHello(ulong dataHash, string name, string deviceId) {
            var w = Message(Hello, 64);
            w.WriteVarInt(Version);
            w.WriteULong(dataHash);
            w.WriteString(name);
            w.WriteString(deviceId);
            return w.ToArray();
        }

        public static byte[] EncodeCommand(int seq, GameCommand command) {
            var w = Message(Command);
            w.WriteVarInt(seq);
            CommandCodec.Write(w, command);
            return w.ToArray();
        }

        public static byte[] EncodeHash(int frame, ulong hash) {
            var w = Message(Hash, 16);
            w.WriteVarInt(frame);
            w.WriteULong(hash);
            return w.ToArray();
        }

        public static byte[] EncodeRefused(RefuseReason reason, string message) {
            var w = Message(Refused, 64);
            w.WriteVarInt((int)reason);
            w.WriteString(message);
            return w.ToArray();
        }

        public static byte[] EncodeRejected(int seq, CommandResult result) {
            var w = Message(CommandRejected);
            w.WriteVarInt(seq);
            w.WriteVarInt((int)result.Error);
            w.WriteString(result.Message);
            return w.ToArray();
        }

        public static byte[] EncodeDesync(int frame, ulong serverHash) {
            var w = Message(Desync, 16);
            w.WriteVarInt(frame);
            w.WriteULong(serverHash);
            return w.ToArray();
        }

        /// <summary>
        /// FNV-1a 64 of the game data text, ignoring line endings and trailing newlines. Client and server must
        /// run on the same data: the client hashes GameDataJson.Write of its ScriptableObjects, the server the
        /// exported GameData/game_data.json.
        /// </summary>
        public static ulong DataHash(string gameDataJson) {
            const ulong prime = 1099511628211UL;
            var h = 14695981039346656037UL;
            unchecked {
                foreach (var b in System.Text.Encoding.UTF8.GetBytes(gameDataJson.Replace("\r", "").TrimEnd('\n'))) {
                    h ^= b;
                    h *= prime;
                }
            }
            return h;
        }
    }

    /// <summary>Sent once by the server when a match is formed. Everything a client needs to build its replica.</summary>
    public sealed class LockstepStart {
        public int MatchId;
        public ulong Seed;
        public int TickRate = 30;
        /// <summary>Report a state hash every this many frames (0 = never).</summary>
        public int HashInterval = 30;
        public bool AllowCheats;
        public bool SkipStarterPack;
        /// <summary>Phase clocks in seconds (0 = the game data's value).</summary>
        public int SetupSeconds;
        public int PreparationSeconds;
        /// <summary>Player id of the receiving client.</summary>
        public int LocalPlayer;
        /// <summary>Display names in player-id order.</summary>
        public string[] Names = Array.Empty<string>();

        public int PlayerCount => Names.Length;

        public MatchConfig ToConfig(Combat.AbilityRegistry abilities = null) => new() {
            Seed = Seed, PlayerCount = PlayerCount, PlayerNames = Names, AllowCheats = AllowCheats,
            SkipStarterPack = SkipStarterPack, Abilities = abilities,
            SetupSeconds = SetupSeconds > 0 ? SetupSeconds : null,
            PreparationSeconds = PreparationSeconds > 0 ? PreparationSeconds : null,
        };

        public byte[] Encode() {
            var w = Lockstep.Message(Lockstep.Start, 64);
            w.WriteVarInt(MatchId);
            w.WriteULong(Seed);
            w.WriteVarInt(TickRate);
            w.WriteVarInt(HashInterval);
            w.WriteByte((byte)((AllowCheats ? 1 : 0) | (SkipStarterPack ? 2 : 0)));
            w.WriteVarInt(SetupSeconds);
            w.WriteVarInt(PreparationSeconds);
            w.WriteVarInt(LocalPlayer);
            w.WriteVarInt(Names.Length);
            foreach (var name in Names) w.WriteString(name);
            return w.ToArray();
        }

        /// <summary>Reads the payload (after the type byte).</summary>
        public static LockstepStart Read(NetReader r) {
            var s = new LockstepStart {
                MatchId = r.ReadVarInt(), Seed = r.ReadULong(), TickRate = r.ReadVarInt(), HashInterval = r.ReadVarInt()
            };
            var flags = r.ReadByte();
            s.AllowCheats = (flags & 1) != 0;
            s.SkipStarterPack = (flags & 2) != 0;
            s.SetupSeconds = r.ReadVarInt();
            s.PreparationSeconds = r.ReadVarInt();
            s.LocalPlayer = r.ReadVarInt();
            var n = r.ReadVarInt();
            if (n < 1 || n > 64) throw new FormatException($"bad player count {n}");
            if (s.LocalPlayer < 0 || s.LocalPlayer >= n) throw new FormatException($"bad local player {s.LocalPlayer}");
            s.Names = new string[n];
            for (var i = 0; i < n; i++) s.Names[i] = r.ReadString() ?? $"Player {i + 1}";
            return s;
        }
    }

    /// <summary>One relayed frame: the accepted commands to submit (in order) before the frame's tick.</summary>
    public sealed class LockstepFrame {
        public struct Entry {
            /// <summary>Sender's sequence number (lets the sender match the command to its callback).</summary>
            public int Seq;
            public GameCommand Command;
        }

        public int Number;
        public readonly List<Entry> Entries = new();

        public void Write(NetWriter w) {
            w.WriteByte(Lockstep.Frame);
            w.WriteVarInt(Number);
            w.WriteVarInt(Entries.Count);
            foreach (var e in Entries) {
                w.WriteVarInt(e.Seq);
                CommandCodec.Write(w, e.Command);
            }
        }

        public byte[] Encode() {
            var w = new NetWriter();
            Write(w);
            return w.ToArray();
        }

        /// <summary>Reads the payload (after the type byte).</summary>
        public static LockstepFrame Read(NetReader r) {
            var f = new LockstepFrame { Number = r.ReadVarInt() };
            var n = r.ReadVarInt();
            if (n < 0 || n > 4096) throw new FormatException($"bad entry count {n}");
            for (var i = 0; i < n; i++) f.Entries.Add(new Entry { Seq = r.ReadVarInt(), Command = CommandCodec.Read(r) });
            return f;
        }
    }
}
