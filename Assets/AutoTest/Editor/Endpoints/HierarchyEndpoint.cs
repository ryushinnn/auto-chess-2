using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AutoTest
{
    /// <summary>/hierarchy: the scene tree (all loaded scenes + DontDestroyOnLoad), or one subtree.</summary>
    internal static class HierarchyEndpoint
    {
        private const int MaxNodes = 5000;

        private sealed class Node
        {
            public string name, path;
            public bool active, activeInHierarchy;
            public int childCount;
            public List<string> components;
            public List<Node> children;
        }

        public static object Get(Request r)
        {
            int depth = r.Int("depth", 3, 0, 64);
            bool includeInactive = r.Bool("includeInactive", true);
            bool components = r.Bool("components", false);

            List<GameObject> start;
            if (r.Has("root"))
            {
                string root = r.RequireStr("root");
                var go = SceneQuery.FindByPath(root) ?? throw new NotFoundException("no GameObject at path '" + root + "'");
                start = new List<GameObject> { go };
            }
            else start = SceneQuery.Roots();

            int budget = MaxNodes;
            var nodes = new List<Node>();
            foreach (var go in start)
            {
                if (!includeInactive && !go.activeInHierarchy) continue;
                var n = Build(go.transform, SceneQuery.PathOf(go.transform), depth, includeInactive, components, ref budget);
                if (n != null) nodes.Add(n);
            }
            return new
            {
                scenes = r.Has("root") ? null : SceneQuery.LoadedScenes().Select(s => new
                {
                    s.name, s.path, rootCount = s.rootCount, isDirty = s.isDirty,
                }).ToList(),
                nodes,
                truncated = budget <= 0 ? true : (bool?)null,
            };
        }

        private static Node Build(Transform t, string path, int depth, bool includeInactive, bool components, ref int budget)
        {
            if (budget-- <= 0) return null;
            var go = t.gameObject;
            var n = new Node
            {
                name = go.name, path = path, active = go.activeSelf, activeInHierarchy = go.activeInHierarchy,
                childCount = t.childCount,
                components = components ? SceneQuery.ComponentNames(go) : null,
            };
            if (depth > 0 && t.childCount > 0)
            {
                n.children = new List<Node>(t.childCount);
                for (int i = 0; i < t.childCount; i++)
                {
                    var child = t.GetChild(i);
                    if (!includeInactive && !child.gameObject.activeInHierarchy) continue;
                    var c = Build(child, path + "/" + child.name, depth - 1, includeInactive, components, ref budget);
                    if (c == null) break;
                    n.children.Add(c);
                }
            }
            return n;
        }
    }
}
