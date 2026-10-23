using System.Collections.Generic;

namespace AutoChess.Core {
    /// <summary>
    /// PvP pairings of a round (TFT): random pairs, avoiding last round's opponent when possible. With an odd
    /// number of players the one left over fights a ghost: a copy of another player's board, and that player
    /// takes no damage from it.
    /// </summary>
    public static class Pairing {
        const int Attempts = 8;

        /// <summary>(home, visitor, ghost): the home player hosts the battle; ghost = the visitor is only a copy.</summary>
        public static List<(PlayerState home, PlayerState visitor, bool ghost)> Pair(IReadOnlyList<PlayerState> players, DeterministicRandom rng) {
            var result = new List<(PlayerState, PlayerState, bool)>();
            if (players.Count < 2) return result;

            PlayerState[] best = null;
            var bestRepeats = int.MaxValue;
            var order = new PlayerState[players.Count];
            for (var attempt = 0; attempt < Attempts && bestRepeats > 0; attempt++) {
                for (var i = 0; i < order.Length; i++) order[i] = players[i];
                for (var i = order.Length - 1; i > 0; i--) {
                    var j = rng.Range(0, i + 1);
                    (order[i], order[j]) = (order[j], order[i]);
                }
                var repeats = 0;
                for (var i = 0; i + 1 < order.Length; i += 2) {
                    if (order[i].OpponentId == order[i + 1].Id) repeats++;
                }
                if (repeats >= bestRepeats) continue;
                bestRepeats = repeats;
                best = (PlayerState[])order.Clone();
            }

            for (var i = 0; i + 1 < best.Length; i += 2) result.Add((best[i], best[i + 1], false));
            if (best.Length % 2 == 1) {
                var alone = best[best.Length - 1];
                var copied = best[rng.Range(0, best.Length - 1)];
                result.Add((alone, copied, true));
            }
            return result;
        }
    }
}
