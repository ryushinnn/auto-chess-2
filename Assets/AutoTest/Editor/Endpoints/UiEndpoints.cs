using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AutoTest
{
    /// <summary>/ui/components, /ui/find, /ui/click.</summary>
    internal static class UiEndpoints
    {
        private static readonly string[] AllKinds =
            { "button", "toggle", "slider", "input", "dropdown", "scroll", "text", "image", "scrollbar", "selectable" };
        private static readonly string[] SelectableKinds =
            { "button", "toggle", "slider", "input", "dropdown", "scrollbar", "selectable" };

        // ---- target resolution (shared with /input/drag and /world/screen-point) -----------------------------

        public sealed class Target
        {
            public GameObject go;     // the object a click means (a text label resolves to its Selectable)
            public GameObject label;  // the text that matched, for text= lookups
            public string query;
        }

        /// <summary>path= (full path or suffix), text= (caption substring), match= (path substring); null when not found.</summary>
        public static Target Resolve(Request r, string pathKey = "path")
        {
            if (r.Has(pathKey))
            {
                string p = r.RequireStr(pathKey);
                return new Target { go = SceneQuery.FindByPath(p), query = "path " + p };
            }
            if (r.Has("text"))
            {
                string t = r.RequireStr("text");
                var (owner, label) = SceneQuery.FindByText(t);
                return new Target { go = owner, label = label, query = "text " + t };
            }
            if (r.Has("match"))
            {
                string m = r.RequireStr("match");
                return new Target { go = SceneQuery.FindByPathSubstring(m), query = "match " + m };
            }
            return null;
        }

        // ---- /ui/find ----------------------------------------------------------------------------------------

        public static object Find(Request r)
        {
            var t = Resolve(r) ?? throw new ArgumentException("pass path, text or match");
            if (t.go == null) return new { found = false, query = t.query };
            var go = t.go;
            var info = ScreenProjection.Of(go);
            return new
            {
                found = true,
                path = SceneQuery.PathOf(go),
                name = go.name,
                active = go.activeSelf,
                go.activeInHierarchy,
                interactable = SceneQuery.Interactable(go),
                caption = SceneQuery.Caption(go),
                components = SceneQuery.ComponentNames(go),
                rect = info.RectArray,
                center = info.CenterArray,
                visible = ScreenProjection.Visible(go, info, false),
                onScreen = info.ok ? info.onScreen : (bool?)null,
                labelPath = t.label != null && t.label != go ? SceneQuery.PathOf(t.label) : null,
                scene = go.scene.name,
                query = t.query,
            };
        }

        // ---- /ui/click ---------------------------------------------------------------------------------------

        public static object Click(Request r)
        {
            GameObject target = null;
            Vector2 point;
            var t = Resolve(r);
            if (t != null)
            {
                target = t.go ?? throw new NotFoundException("no object for " + t.query);
                string targetPath = SceneQuery.PathOf(target);
                if (!target.activeInHierarchy)
                    return new { executed = false, targetPath, mode = "none", message = "target is inactive: " + targetPath };
                var info = ScreenProjection.Of(target);
                if (!info.ok || !info.onScreen)
                    return new { executed = false, targetPath, mode = "none", message = "target is not on screen: " + targetPath, point = info.CenterArray };
                point = info.center;
            }
            else if (r.Has("x") || r.Has("y"))
                point = new Vector2(r.RequireFloat("x"), r.RequireFloat("y"));
            else throw new ArgumentException("pass path, text or x,y");

            var pointArr = new[] { point.x, point.y };
            var hit = PointerSim.HitTest(point);
            if (hit == null)
                return new
                {
                    executed = false, targetPath = SceneQuery.PathOf(target), mode = "none", point = pointArr,
                    message = "nothing under (" + point.x + ", " + point.y + ")",
                };
            string hitPath = SceneQuery.PathOf(hit.go);
            if (target != null && !PointerSim.Reaches(hit.go, target))
                return new
                {
                    executed = false, targetPath = SceneQuery.PathOf(target), mode = "none", point = pointArr, hitPath,
                    message = "blocked by " + hitPath,
                };

            var outcome = PointerSim.Click(hit, point);
            string consumerPath = outcome.consumerPath ?? hitPath;
            return new
            {
                outcome.executed,
                targetPath = consumerPath,
                mode = hit.Mode,
                message = outcome.executed
                    ? string.Join("+", outcome.fired) + " on " + consumerPath
                    : "no pointer handler (IPointerDown/IPointerClick/OnMouse*) at or above " + hitPath,
                point = pointArr,
                hitPath,
                fired = outcome.fired,
            };
        }

        // ---- /ui/components ----------------------------------------------------------------------------------

        private sealed class Item
        {
            public string kind, name, path, caption, component;
            public bool active, activeSelf, interactable, visible;
            public float[] rect, center;
            public object value;
            public string sprite, placeholder;
        }

        public static object Components(Request r)
        {
            var kinds = ParseKinds(r.Str("kinds"));
            bool includeInactive = r.Bool("includeInactive", false);
            string match = r.Str("match");
            int limit = r.Int("limit", 500, 1, 5000);
            Transform root = null;
            if (r.Has("root"))
            {
                string rp = r.RequireStr("root");
                root = (SceneQuery.FindByPath(rp) ?? throw new NotFoundException("no GameObject at path '" + rp + "'")).transform;
            }

            var items = new List<Item>();
            Item Add(string kind, Component c, string caption, bool interactable, bool checkGraphic, object value = null)
            {
                var go = c.gameObject;
                if (root != null && !go.transform.IsChildOf(root)) return null;
                string path = SceneQuery.PathOf(go);
                if (match != null && path.IndexOf(match, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (caption == null || caption.IndexOf(match, StringComparison.OrdinalIgnoreCase) < 0))
                    return null;
                var info = ScreenProjection.Of(go);
                var item = new Item
                {
                    kind = kind, name = go.name, path = path, caption = caption, component = c.GetType().Name,
                    active = go.activeInHierarchy, activeSelf = go.activeSelf,
                    interactable = interactable && go.activeInHierarchy,
                    rect = info.RectArray, center = info.CenterArray,
                    visible = ScreenProjection.Visible(go, info, checkGraphic),
                    value = value,
                };
                items.Add(item);
                return item;
            }

            if (kinds.Overlaps(SelectableKinds))
                foreach (var sel in SceneQuery.SceneComponents<Selectable>(includeInactive))
                {
                    string kind = KindOf(sel);
                    if (!kinds.Contains(kind)) continue;
                    string caption = SceneQuery.Caption(sel.gameObject);
                    object value = null;
                    string placeholder = null;
                    switch (sel)
                    {
                        case Toggle tg: value = tg.isOn; break;
                        case Slider sl: value = sl.value; break;
                        case Scrollbar sb: value = sb.value; break;
                        case Dropdown dd: value = dd.value; break;
                        case TMP_Dropdown tdd: value = tdd.value; break;
                        case InputField inf:
                            value = inf.text;
                            caption = SceneQuery.Condense(inf.text, 80);
                            placeholder = inf.placeholder is Text pt ? SceneQuery.Condense(pt.text, 80) : null;
                            break;
                        case TMP_InputField tif:
                            value = tif.text;
                            caption = SceneQuery.Condense(tif.text, 80);
                            placeholder = tif.placeholder != null ? SceneQuery.Condense(SceneQuery.RawText(tif.placeholder), 80) : null;
                            break;
                    }
                    var row = Add(kind, sel, caption, sel.enabled && sel.IsInteractable(), false, value);
                    if (row != null) row.placeholder = placeholder;
                }

            if (kinds.Contains("scroll"))
                foreach (var sr in SceneQuery.SceneComponents<ScrollRect>(includeInactive))
                    Add("scroll", sr, null, sr.enabled, false, new[] { sr.normalizedPosition.x, sr.normalizedPosition.y });

            if (kinds.Contains("text"))
            {
                foreach (var tx in SceneQuery.SceneComponents<Text>(includeInactive))
                    Add("text", tx, SceneQuery.Condense(tx.text, 200), tx.raycastTarget && tx.enabled, true);
                foreach (var tx in SceneQuery.SceneComponents<TMP_Text>(includeInactive))
                    Add("text", tx, SceneQuery.Condense(tx.text, 200), tx.raycastTarget && tx.enabled, true);
            }

            if (kinds.Contains("image"))
            {
                foreach (var img in SceneQuery.SceneComponents<Image>(includeInactive))
                {
                    var row = Add("image", img, null, img.raycastTarget && img.enabled, true);
                    if (row != null && img.sprite != null) row.sprite = img.sprite.name;
                }
                foreach (var raw in SceneQuery.SceneComponents<RawImage>(includeInactive))
                    Add("image", raw, null, raw.raycastTarget && raw.enabled, true);
            }

            // Reading order: top to bottom, left to right; rows without a screen position last.
            items.Sort((a, b) =>
            {
                if (a.center == null || b.center == null) return a.center == null ? (b.center == null ? 0 : 1) : -1;
                int y = b.center[1].CompareTo(a.center[1]);
                return y != 0 ? y : a.center[0].CompareTo(b.center[0]);
            });
            int total = items.Count;
            if (items.Count > limit) items = items.GetRange(0, limit);
            return new
            {
                count = total,
                truncated = total > limit ? true : (bool?)null,
                kinds = kinds.ToList(),
                screen = new[] { ScreenProjection.ScreenSize().x, ScreenProjection.ScreenSize().y },
                items,
            };
        }

        private static HashSet<string> ParseKinds(string csv)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(csv) || csv.Trim().Equals("selectables", StringComparison.OrdinalIgnoreCase))
            {
                set.UnionWith(SelectableKinds);
                return set;
            }
            if (csv.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                set.UnionWith(AllKinds);
                return set;
            }
            foreach (string part in csv.Split(','))
            {
                string k = part.Trim().ToLowerInvariant();
                if (k.Length == 0) continue;
                if (k == "inputfield") k = "input";
                if (k == "scrollrect" || k == "scrollview") k = "scroll";
                if (k == "selectables") { set.UnionWith(SelectableKinds); continue; }
                if (Array.IndexOf(AllKinds, k) < 0)
                    throw new ArgumentException("unknown kind '" + part.Trim() + "' (use " + string.Join(",", AllKinds) + ", selectables or all)");
                set.Add(k);
            }
            return set;
        }

        private static string KindOf(Selectable sel) => sel switch
        {
            Button => "button",
            Toggle => "toggle",
            Slider => "slider",
            Scrollbar => "scrollbar",
            InputField => "input",
            TMP_InputField => "input",
            Dropdown => "dropdown",
            TMP_Dropdown => "dropdown",
            _ => "selectable",
        };
    }
}
