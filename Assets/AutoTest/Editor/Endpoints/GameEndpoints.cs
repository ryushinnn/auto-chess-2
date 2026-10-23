using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AutoTest
{
    /// <summary>/state, /game-op, /invariants, /targets: everything game-specific goes through the adapter.</summary>
    internal static class GameEndpoints
    {
        public static object State(Request r)
        {
            var a = AdapterRegistry.Require();
            string gameId = AdapterRegistry.Call("GameId", () => a.GameId);
            bool available = AdapterRegistry.Call("IsAvailable", a.IsAvailable);
            if (!available) return new { gameId, available };

            // Serialized here, on the main thread, because adapter state may hold Unity objects.
            var state = Json.ToToken(AdapterRegistry.Call("GetState", a.GetState));
            if (!r.Has("path")) return new { gameId, available, state };

            string path = r.RequireStr("path");
            return new { gameId, available, path, value = Navigate(state, path) };
        }

        /// <summary>Dotted path into JSON: object keys (exact, then case-insensitive) and array indexes ("board.cells.3.hero").</summary>
        private static JToken Navigate(JToken token, string path)
        {
            var walked = new List<string>();
            foreach (string seg in path.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries))
            {
                JToken next = null;
                if (token is JObject o)
                    next = o.TryGetValue(seg, out var v) ? v : o.TryGetValue(seg, StringComparison.OrdinalIgnoreCase, out v) ? v : null;
                else if (token is JArray a && int.TryParse(seg, out int i))
                    next = i >= 0 && i < a.Count ? a[i] : null;
                walked.Add(seg);
                token = next ?? throw new NotFoundException("state has no '" + string.Join(".", walked) + "'");
            }
            return token;
        }

        public static object GameOp(Request r)
        {
            string op = r.RequireStr("op").Trim();
            var ops = AdapterRegistry.Require<IAutoTestGameOps>();
            var names = AdapterRegistry.Call("Ops", () => ops.Ops) ?? Array.Empty<string>();
            if (op.Equals("list", StringComparison.OrdinalIgnoreCase))
                return new { op = "list", result = names, ops = names };
            if (names.Count > 0 && !names.Contains(op))
                throw new ArgumentException("unknown op '" + op + "' (ops: " + string.Join(", ", names) + ")");
            var args = r.AsStrings("op");
            var result = AdapterRegistry.Call("GameOp(" + op + ")", () => ops.GameOp(op, args), argumentIsBadRequest: true);
            return new { op, result = Json.ToToken(result) };
        }

        public static object Invariants(Request r)
        {
            var inv = AdapterRegistry.Require<IAutoTestInvariants>();
            var rules = (AdapterRegistry.Call("Rules", () => inv.Rules) ?? Array.Empty<string>()).ToList();
            HashSet<string> only = null;
            if (r.Has("rules"))
            {
                only = new HashSet<string>(r.RequireStr("rules").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0));
                var unknown = only.Where(s => !rules.Contains(s)).ToList();
                if (rules.Count > 0 && unknown.Count > 0)
                    throw new ArgumentException("unknown rule(s) " + string.Join(", ", unknown) + " (rules: " + string.Join(", ", rules) + ")");
                if (only.Count == 0) only = null;
            }
            var adapter = AdapterRegistry.Require();
            if (!AdapterRegistry.Call("IsAvailable", adapter.IsAvailable))
                return new { rules, findings = Array.Empty<object>(), highCount = 0, available = false };

            var findings = (AdapterRegistry.Call("Check", () => inv.Check(only), argumentIsBadRequest: true) ?? new List<InvariantFinding>())
                .Where(f => f != null).ToList();
            return new
            {
                rules,
                findings = Json.ToToken(findings),
                highCount = findings.Count(f => string.Equals(f.severity, "high", StringComparison.OrdinalIgnoreCase)),
                available = true,
            };
        }

        public static object Targets(Request r)
        {
            var src = AdapterRegistry.Require<IAutoTestTargets>();
            var adapter = AdapterRegistry.Require();
            if (!AdapterRegistry.Call("IsAvailable", adapter.IsAvailable))
                return new { targets = Array.Empty<object>(), count = 0, available = false };
            var targets = (AdapterRegistry.Call("GetTargets", src.GetTargets) ?? new List<TargetInfo>()).Where(t => t != null);
            string kind = r.Str("kind");
            if (kind != null) targets = targets.Where(t => string.Equals(t.kind, kind, StringComparison.OrdinalIgnoreCase));
            var list = targets.ToList();
            return new { targets = Json.ToToken(list), count = list.Count, available = true };
        }
    }
}
