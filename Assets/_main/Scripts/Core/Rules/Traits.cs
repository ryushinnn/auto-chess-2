using System.Collections.Generic;

namespace AutoChess.Core {
    public readonly struct TraitStatus {
        public readonly TraitId Id;
        public readonly int Count;
        /// <summary>Index of the reached checkpoint, -1 when inactive.</summary>
        public readonly int Tier;

        public TraitStatus(TraitId id, int count, int tier) {
            Id = id;
            Count = count;
            Tier = tier;
        }

        public bool Active => Tier >= 0;
        public override string ToString() => $"{Id} x{Count} tier {Tier}";
    }

    /// <summary>Synergies (roles and realms) counted over distinct unit definitions (was Destinies).</summary>
    public static class Traits {
        /// <summary>
        /// Trait counts of a set of unit definitions, each definition counted once. Sorted for display: active
        /// tier descending, then count descending, then id.
        /// </summary>
        public static List<TraitStatus> Compute(GameDatabase db, IEnumerable<string> unitDefinitionIds) {
            var counts = new Dictionary<TraitId, int>();
            var seen = new HashSet<string>();
            var roleBuffer = new Role[6];
            foreach (var id in unitDefinitionIds) {
                if (!seen.Add(id)) continue;
                var def = db.Unit(id);
                if (def.Summoned) continue;
                var n = def.Roles.GetRoles(roleBuffer);
                for (var i = 0; i < n; i++) Increment(counts, TraitId.Of(roleBuffer[i]));
                if (def.Realm != Realm.None) Increment(counts, TraitId.Of(def.Realm));
            }

            var result = new List<TraitStatus>(counts.Count);
            foreach (var (id, count) in counts) {
                var def = db.Trait(id);
                result.Add(new TraitStatus(id, count, def?.TierFor(count) ?? -1));
            }
            result.Sort((a, b) => a.Tier != b.Tier ? b.Tier.CompareTo(a.Tier)
                : a.Count != b.Count ? b.Count.CompareTo(a.Count)
                : a.Id.CompareTo(b.Id));
            return result;
        }

        public static List<TraitStatus> ForBoard(GameDatabase db, PlayerState p) {
            var ids = new List<string>();
            foreach (var u in p.Units) {
                if (u.Location.IsBoard) ids.Add(u.DefinitionId);
            }
            return Compute(db, ids);
        }

        static void Increment(Dictionary<TraitId, int> counts, TraitId id) {
            counts.TryGetValue(id, out var n);
            counts[id] = n + 1;
        }
    }
}
