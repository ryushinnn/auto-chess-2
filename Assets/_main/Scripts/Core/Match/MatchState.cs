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
        /// <summary>Overrides GameRules.SetupSeconds when set (online matches run on a clock).</summary>
        public double? SetupSeconds;
        /// <summary>Overrides GameRules.PreparationSeconds when set (online matches run on a clock).</summary>
        public double? PreparationSeconds;
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
        /// <summary>Rounds started so far (all stages), 0 during the first one: drives the early income.</summary>
        public int RoundNumber;
        public RoundKind RoundKind;
        public readonly List<PlayerState> Players = new();
        /// <summary>Battle of each player (by player id); the two players of a PvP pair share theirs.</summary>
        public readonly Dictionary<int, BattleSimulation> Battles = new();
        /// <summary>Every battle of the round once, in creation order (step / start these).</summary>
        public readonly List<BattleSimulation> BattleList = new();
        /// <summary>The finite unit pool of a multiplayer match (null when the pool is endless).</summary>
        public SharedUnitPool SharedPool;

        public PlayerState Player(int id) {
            foreach (var p in Players) {
                if (p.Id == id) return p;
            }
            return null;
        }

        public BattleSimulation BattleOf(int playerId) => Battles.TryGetValue(playerId, out var b) ? b : null;

        public int AliveCount {
            get {
                var n = 0;
                foreach (var p in Players) {
                    if (!p.Eliminated) n++;
                }
                return n;
            }
        }
    }
}
