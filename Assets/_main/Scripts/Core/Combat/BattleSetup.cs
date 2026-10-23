using System;
using System.Collections.Generic;

namespace AutoChess.Core.Combat {
    /// <summary>Everything needed to (re)play a battle deterministically.</summary>
    public sealed class BattleSetup {
        public ulong Seed;
        public TeamSetup Home = new();
        public TeamSetup Away = new();

        public TeamSetup Team(TeamSide side) => side == TeamSide.Home ? Home : Away;
    }

    public sealed class TeamSetup {
        /// <summary>Owning player, or -1 for a PvE wave.</summary>
        public int PlayerId = -1;
        /// <summary>Activate role / realm synergies for this team (players: yes, PvE waves: no).</summary>
        public bool ApplyTraits;
        /// <summary>A copy of <see cref="PlayerId"/>'s board fighting a player left without opponent: that player takes no damage.</summary>
        public bool Ghost;
        public readonly List<CombatantSetup> Units = new();
    }

    public sealed class CombatantSetup {
        /// <summary>Owned unit this combatant comes from, or -1.</summary>
        public int SourceUnitId = -1;
        public string UnitId;
        public HeroRank Rank;
        /// <summary>Cell on the full battle grid.</summary>
        public HexCoord Cell;
        public string[] Items = Array.Empty<string>();
        public double HpScale = 1;
        public double DamageScale = 1;
    }

    public static class BattleSetups {
        /// <summary>Home team from a player's board (cells as placed, rows 0..3).</summary>
        public static TeamSetup FromBoard(PlayerState p) {
            var team = new TeamSetup { PlayerId = p.Id, ApplyTraits = true };
            foreach (var u in p.Units) {
                if (!u.Location.IsBoard) continue;
                team.Units.Add(new CombatantSetup {
                    SourceUnitId = u.Id, UnitId = u.DefinitionId, Rank = u.Rank, Cell = u.Location.Cell,
                    Items = u.Items.ToArray()
                });
            }
            return team;
        }

        /// <summary>A visiting player's board, rotated onto the away half (for PvP rounds).</summary>
        public static TeamSetup FromBoardMirrored(PlayerState p, GameRules rules) {
            var team = FromBoard(p);
            foreach (var c in team.Units) c.Cell = c.Cell.Mirror(rules.BoardRows, rules.BoardColumns);
            return team;
        }

        public static TeamSetup FromRound(RoundDefinition round) {
            var team = new TeamSetup { PlayerId = -1, ApplyTraits = false };
            foreach (var e in round.Enemies) {
                team.Units.Add(new CombatantSetup {
                    UnitId = e.UnitId, Rank = e.Rank, Cell = e.Cell, Items = e.Items,
                    HpScale = e.HpScale, DamageScale = e.DamageScale
                });
            }
            return team;
        }
    }
}
