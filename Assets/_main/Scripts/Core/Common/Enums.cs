using System;

namespace AutoChess.Core {
    // Values are persisted in ScriptableObject assets (HeroTrait, Stage...). Never reorder or renumber.

    public enum Realm {
        None,
        Mortal,
        Divine,
        Nether,
        Mecha,
        Chaos
    }

    [Flags]
    public enum Role {
        None = 0x00,
        Duelist = 0x01,
        Sorcerer = 0x02,
        Marksman = 0x04,
        Assassin = 0x08,
        Bruiser = 0x10,
        Cultist = 0x20,
    }

    public enum Reputation {
        None,
        Unknown,
        Elite,
        Legendary,
    }

    public enum HeroRank {
        B,
        A,
        S
    }

    public enum DamageType {
        Physical,
        Magical,
        True
    }

    /// <summary>Side of a battle. Home is the player who owns the battle; Away is the PvE wave or the visiting player.</summary>
    public enum TeamSide {
        Home,
        Away
    }

    public enum MatchPhase {
        None,
        /// <summary>Players pick their starter pack.</summary>
        Setup,
        Preparation,
        Transition,
        Battle,
        Summary,
        Finished,
    }

    public enum RoundKind {
        /// <summary>Against the round's creeps (authored stages, and round x-7 of later stages).</summary>
        PvE,
        /// <summary>Against another player's board (or a ghost copy of one when the count is odd).</summary>
        PvP,
    }

    public enum BattleOutcome {
        None,
        HomeWin,
        AwayWin,
        /// <summary>The battle ran out of time.</summary>
        Draw,
    }

    public static class EnumExtensions {
        public static TeamSide Opponent(this TeamSide side) => side == TeamSide.Home ? TeamSide.Away : TeamSide.Home;

        public static bool HasRole(this Role roles, Role role) => role != Role.None && (roles & role) == role;

        public static HeroRank Next(this HeroRank rank) => rank == HeroRank.S ? HeroRank.S : rank + 1;

        /// <summary>Single roles contained in a flag set, in declaration order, without allocating.</summary>
        public static int GetRoles(this Role roles, Role[] buffer) {
            var count = 0;
            for (var bit = 1; bit <= (int)Role.Cultist && count < buffer.Length; bit <<= 1) {
                if (((int)roles & bit) != 0) buffer[count++] = (Role)bit;
            }
            return count;
        }
    }
}
