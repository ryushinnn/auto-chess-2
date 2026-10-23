using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AutoTest
{
    /// <summary>
    /// Hierarchy access across every loaded scene plus the DontDestroyOnLoad scene. A path is the `/`-joined names
    /// from the scene root (`Canvas/Shop/Refresh`), without the scene name. Main thread only.
    /// </summary>
    [InitializeOnLoad]
    internal static class SceneQuery
    {
        private static Scene s_DontDestroy;

        static SceneQuery()
        {
            EditorApplication.playModeStateChanged += _ => s_DontDestroy = default;
        }

        public static void Touch() { }

        // ---- roots / walking ---------------------------------------------------------------------------------

        public static List<Scene> LoadedScenes()
        {
            var scenes = new List<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.IsValid() && s.isLoaded) scenes.Add(s);
            }
            var ddol = DontDestroyOnLoadScene();
            if (ddol.IsValid() && ddol.isLoaded) scenes.Add(ddol);
            return scenes;
        }

        public static List<GameObject> Roots()
        {
            var roots = new List<GameObject>();
            foreach (var s in LoadedScenes()) roots.AddRange(s.GetRootGameObjects());
            return roots;
        }

        /// <summary>SceneManager does not enumerate the DontDestroyOnLoad scene: reach it once per play session through a probe object.</summary>
        private static Scene DontDestroyOnLoadScene()
        {
            if (!EditorApplication.isPlaying) return default;
            if (s_DontDestroy.IsValid()) return s_DontDestroy;
            GameObject probe = null;
            try
            {
                probe = new GameObject("AutoTest.DontDestroyProbe") { hideFlags = HideFlags.HideAndDontSave };
                UnityEngine.Object.DontDestroyOnLoad(probe);
                s_DontDestroy = probe.scene;
            }
            catch { s_DontDestroy = default; }
            finally
            {
                if (probe != null) UnityEngine.Object.DestroyImmediate(probe);
            }
            return s_DontDestroy;
        }

        /// <summary>Every transform, depth-first in hierarchy order; inactive subtrees skipped unless asked for.</summary>
        public static IEnumerable<Transform> AllTransforms(bool includeInactive)
        {
            var stack = new Stack<Transform>();
            var roots = Roots();
            for (int i = roots.Count - 1; i >= 0; i--) stack.Push(roots[i].transform);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                if (t == null) continue;
                if (!includeInactive && !t.gameObject.activeInHierarchy) continue;
                yield return t;
                for (int c = t.childCount - 1; c >= 0; c--) stack.Push(t.GetChild(c));
            }
        }

        /// <summary>Live scene instances of a component type (not prefab assets).</summary>
        public static IEnumerable<T> SceneComponents<T>(bool includeInactive) where T : Component =>
            UnityEngine.Object.FindObjectsOfType<T>(includeInactive).Where(c => c != null && c.gameObject.scene.IsValid());

        // ---- paths -------------------------------------------------------------------------------------------

        public static string PathOf(Transform t)
        {
            if (t == null) return null;
            var names = new List<string>();
            for (var p = t; p != null; p = p.parent) names.Add(p.name);
            names.Reverse();
            return string.Join("/", names);
        }

        public static string PathOf(GameObject go) => go == null ? null : PathOf(go.transform);

        private static int Depth(Transform t)
        {
            int d = 0;
            for (var p = t.parent; p != null; p = p.parent) d++;
            return d;
        }

        /// <summary>
        /// Full path from a scene root, else a suffix of one (`Shop/Refresh` finds `Canvas/Shop/Refresh`): the shortest
        /// matching full path wins, an active object over an inactive one. A leading `/` allows only the full path.
        /// Inactive objects are found too.
        /// </summary>
        public static GameObject FindByPath(string path)
        {
            path = path?.Trim();
            if (string.IsNullOrEmpty(path)) return null;
            bool anchored = path.StartsWith("/", StringComparison.Ordinal);
            path = path.Trim('/');
            if (path.Length == 0) return null;
            var segs = path.Split('/');

            var exact = new List<Transform>();
            foreach (var root in Roots())
                if (root.name == segs[0]) Descend(root.transform, segs, 1, exact);
            if (exact.Count > 0) return Best(exact);
            if (anchored) return null;

            string leaf = segs[segs.Length - 1];
            var suffix = AllTransforms(true).Where(t => t.name == leaf && EndsWith(t, segs)).ToList();
            return suffix.Count == 0 ? null : Best(suffix);
        }

        private static void Descend(Transform t, string[] segs, int i, List<Transform> into)
        {
            if (i == segs.Length) { into.Add(t); return; }
            for (int c = 0; c < t.childCount; c++)
            {
                var child = t.GetChild(c);
                if (child.name == segs[i]) Descend(child, segs, i + 1, into);
            }
        }

        private static bool EndsWith(Transform t, string[] segs)
        {
            var cur = t;
            for (int i = segs.Length - 1; i >= 0; i--, cur = cur.parent)
                if (cur == null || cur.name != segs[i]) return false;
            return true;
        }

        private static GameObject Best(List<Transform> hits) =>
            hits.OrderBy(Depth).ThenBy(t => t.gameObject.activeInHierarchy ? 0 : 1).First().gameObject;

        /// <summary>First object whose path contains `needle` (case-insensitive): active first, then name hits, then shortest path.</summary>
        public static GameObject FindByPathSubstring(string needle)
        {
            if (string.IsNullOrWhiteSpace(needle)) return null;
            needle = needle.Trim();
            GameObject best = null;
            (int, int, int) bestKey = (int.MaxValue, int.MaxValue, int.MaxValue);
            foreach (var t in AllTransforms(true))
            {
                string p = PathOf(t);
                if (p.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var key = (t.gameObject.activeInHierarchy ? 0 : 1,
                           t.name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1,
                           p.Length);
                if (key.CompareTo(bestKey) < 0) { best = t.gameObject; bestKey = key; }
            }
            return best;
        }

        /// <summary>
        /// The text element whose caption contains `needle` (exact before substring, active before inactive, shorter
        /// before longer), and the object a click on it means: its nearest Selectable, else the text itself.
        /// </summary>
        public static (GameObject owner, GameObject label) FindByText(string needle)
        {
            if (string.IsNullOrWhiteSpace(needle)) return (null, null);
            needle = Condense(needle, int.MaxValue) ?? "";
            Graphic best = null;
            (int, int, int) bestKey = (int.MaxValue, int.MaxValue, int.MaxValue);
            foreach (var g in TextGraphics())
            {
                string text = Condense(RawText(g), int.MaxValue);
                if (text == null) continue;
                int tier = string.Equals(text, needle, StringComparison.OrdinalIgnoreCase) ? 0
                    : text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : -1;
                if (tier < 0) continue;
                var key = (tier, g.isActiveAndEnabled ? 0 : 1, text.Length);
                if (key.CompareTo(bestKey) < 0) { best = g; bestKey = key; }
            }
            if (best == null) return (null, null);
            var selectable = best.GetComponentInParent<Selectable>(true);
            return (selectable != null ? selectable.gameObject : best.gameObject, best.gameObject);
        }

        private static IEnumerable<Graphic> TextGraphics() =>
            SceneComponents<Text>(true).Cast<Graphic>().Concat(SceneComponents<TMP_Text>(true));

        // ---- captions / components ---------------------------------------------------------------------------

        public static string RawText(Component c) => c switch
        {
            TMP_Text tmp => tmp.text,
            Text text => text.text,
            _ => null,
        };

        /// <summary>The visible caption of an element: the first non-empty Text / TMP_Text on it or below it.</summary>
        public static string Caption(GameObject go, int maxChars = 80)
        {
            if (go == null) return null;
            bool includeInactive = !go.activeInHierarchy;
            foreach (var g in go.GetComponentsInChildren<Graphic>(includeInactive))
            {
                if (!(g is Text) && !(g is TMP_Text)) continue;
                string s = Condense(RawText(g), maxChars);
                if (s != null) return s;
            }
            return null;
        }

        private static readonly Regex s_RichText = new Regex("<[^<>]{1,64}>", RegexOptions.Compiled);

        /// <summary>One line, single-spaced, rich-text tags and zero-width characters removed, capped at maxChars.</summary>
        public static string Condense(string s, int maxChars)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (s.IndexOf('<') >= 0) s = s_RichText.Replace(s, "");
            var sb = new StringBuilder(Math.Min(s.Length, 256));
            bool space = false;
            foreach (char c in s)
            {
                if (c == '​' || c == '‌' || c == '‍' || c == '﻿') continue;
                if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
                if (space) { sb.Append(' '); space = false; }
                sb.Append(c);
                if (sb.Length >= maxChars) { sb.Append('…'); break; }
            }
            return sb.Length == 0 ? null : sb.ToString();
        }

        public static List<string> ComponentNames(GameObject go)
        {
            var names = new List<string>();
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) { names.Add("(missing script)"); continue; }
                if (c.GetType() == typeof(Transform)) continue;
                names.Add(c.GetType().Name);
            }
            return names;
        }

        /// <summary>Selectable: active and IsInteractable (CanvasGroups included). Otherwise: active, not blocked by a
        /// CanvasGroup, and something on it or above it handles pointer events (or it has an enabled collider).</summary>
        public static bool Interactable(GameObject go)
        {
            if (go == null || !go.activeInHierarchy) return false;
            var sel = go.GetComponent<Selectable>();
            if (sel != null) return sel.enabled && sel.IsInteractable();
            foreach (var group in go.GetComponentsInParent<CanvasGroup>())
            {
                if (!group.enabled) continue;
                if (!group.interactable || !group.blocksRaycasts) return false;
                if (group.ignoreParentGroups) break;
            }
            if (go.GetComponentInParent<UnityEngine.EventSystems.IEventSystemHandler>() != null) return true;
            var col = go.GetComponent<Collider>();
            return col != null && col.enabled;
        }
    }
}
