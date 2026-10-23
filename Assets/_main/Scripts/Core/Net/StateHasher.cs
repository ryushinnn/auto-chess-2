using System;
using AutoChess.Core.Combat;

namespace AutoChess.Core.Net {
    /// <summary>
    /// 64-bit FNV-1a fingerprint of the gameplay-relevant state. Server and clients compare it to detect a
    /// desync; tests use it to prove determinism. Numbers are hashed by their bit pattern.
    /// </summary>
    public static class StateHasher {
        const ulong Offset = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;

        public static ulong Hash(MatchState state) {
            var h = Offset;
            Mix(ref h, (int)state.Phase);
            Mix(ref h, state.Tick);
            Mix(ref h, state.PhaseTicksLeft);
            Mix(ref h, state.StageIndex);
            Mix(ref h, state.RoundIndex);
            Mix(ref h, state.LastUnitId);
            Mix(ref h, state.RoundNumber);
            foreach (var p in state.Players) MixPlayer(ref h, p);
            foreach (var battle in state.BattleList) MixBattle(ref h, battle);
            if (state.SharedPool != null) {
                foreach (var n in state.SharedPool.Remaining) Mix(ref h, n);
            }
            return h;
        }

        public static ulong Hash(BattleSimulation battle) {
            var h = Offset;
            MixBattle(ref h, battle);
            return h;
        }

        static void MixPlayer(ref ulong h, PlayerState p) {
            Mix(ref h, p.Id);
            Mix(ref h, p.Gold);
            Mix(ref h, p.Level);
            Mix(ref h, p.Xp);
            Mix(ref h, p.Wins);
            Mix(ref h, p.Losses);
            Mix(ref h, p.Health);
            Mix(ref h, p.Eliminated ? 1 : 0);
            Mix(ref h, p.Placement);
            Mix(ref h, p.Streak);
            Mix(ref h, p.OpponentId);
            Mix(ref h, p.StarterChosen ? 1 : 0);
            foreach (var u in p.Units) {
                Mix(ref h, u.Id);
                Mix(ref h, u.DefinitionId);
                Mix(ref h, (int)u.Rank);
                Mix(ref h, (int)u.Location.Kind);
                Mix(ref h, u.Location.Slot);
                Mix(ref h, u.Location.Cell.Row);
                Mix(ref h, u.Location.Cell.Col);
                foreach (var item in u.Items) Mix(ref h, item);
            }
            foreach (var s in p.Items) {
                Mix(ref h, s.ItemId);
                Mix(ref h, s.Count);
            }
            foreach (var slot in p.Shop.Slots) Mix(ref h, slot ?? "-");
            Mix(ref h, p.Shop.Locked ? 1 : 0);
            Mix(ref h, p.Shop.Insurance);
            var rng = p.Rng.GetState();
            Mix(ref h, (int)rng.S0);
            Mix(ref h, (int)rng.S1);
            Mix(ref h, (int)rng.S2);
            Mix(ref h, (int)rng.S3);
        }

        static void MixBattle(ref ulong h, BattleSimulation b) {
            Mix(ref h, b.Id);
            Mix(ref h, b.Tick);
            Mix(ref h, (int)b.Outcome);
            foreach (var u in b.Units) {
                Mix(ref h, u.Id);
                Mix(ref h, u.Alive ? 1 : 0);
                Mix(ref h, u.Hp);
                Mix(ref h, u.Energy);
                Mix(ref h, u.Position.X);
                Mix(ref h, u.Position.Y);
                Mix(ref h, u.OccupiedCell);
            }
        }

        static void Mix(ref ulong h, int value) {
            unchecked {
                for (var i = 0; i < 4; i++) {
                    h ^= (byte)(value >> (8 * i));
                    h *= Prime;
                }
            }
        }

        static void Mix(ref ulong h, double value) {
            var bits = BitConverter.DoubleToInt64Bits(value);
            Mix(ref h, (int)bits);
            Mix(ref h, (int)(bits >> 32));
        }

        static void Mix(ref ulong h, string value) {
            unchecked {
                foreach (var c in value) {
                    h ^= c;
                    h *= Prime;
                }
                h ^= 0xFF;
                h *= Prime;
            }
        }
    }
}
