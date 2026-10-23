using System.Collections.Generic;
using AutoChess.Core.Combat;

namespace AutoChess.Core {
    public sealed class MatchConfig {
        public ulong Seed = 1;
        public int PlayerCount = 1;
        public string[] PlayerNames;
        /// <summary>Accept cheat commands (editor, tests). Must stay false on a server.</summary>
        public bool AllowCheats;
        /// <summary>Skip the starter pick: every player starts in preparation with nothing.</summary>
        public bool SkipStarterPack;
        public IUnitPool Pool;
        public AbilityRegistry Abilities;
    }

    /// <summary>Authoritative state of one match. Everything a client needs to draw the game.</summary>
    public sealed class MatchState {
        public MatchPhase Phase = MatchPhase.None;
        public int Tick;
        public int PhaseTicksTotal;
        public int PhaseTicksLeft;
        public int StageIndex;
        public int RoundIndex;
        public int LastUnitId;
        public int LastBattleId;
        public readonly List<PlayerState> Players = new();
        /// <summary>Running battle of each player (by player id).</summary>
        public readonly Dictionary<int, BattleSimulation> Battles = new();

        public PlayerState Player(int id) {
            foreach (var p in Players) {
                if (p.Id == id) return p;
            }
            return null;
        }

        public BattleSimulation BattleOf(int playerId) => Battles.TryGetValue(playerId, out var b) ? b : null;
    }
}
