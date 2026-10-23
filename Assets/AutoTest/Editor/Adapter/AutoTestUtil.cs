using System.Collections.Generic;
using UnityEngine;

namespace AutoTest
{
    /// <summary>Helpers for game adapters: the same paths, captions and screen projection the bridge itself uses.</summary>
    public static class AutoTestUtil
    {
        /// <summary>`/`-joined names from the scene root, the form every route accepts as `path`.</summary>
        public static string PathOf(GameObject go) => SceneQuery.PathOf(go);

        /// <summary>Full path or a suffix of one, across all loaded scenes and DontDestroyOnLoad; inactive objects too.</summary>
        public static GameObject Find(string path) => SceneQuery.FindByPath(path);

        /// <summary>First non-empty Text / TMP_Text caption on the object or below it.</summary>
        public static string Caption(GameObject go) => SceneQuery.Caption(go);

        /// <summary>Screen rect (xMin,yMin,xMax,yMax) and center in Game-view pixels, bottom-left origin. UI goes through its
        /// canvas camera; world objects project their collider/renderer bounds through the camera that renders them.</summary>
        public static bool TryGetScreenRect(GameObject go, out float[] rect, out float[] center)
        {
            var info = ScreenProjection.Of(go);
            rect = info.RectArray;
            center = info.CenterArray;
            return info.ok && info.onScreen;
        }

        /// <summary>A TargetInfo for `go` with path, rect, center, visible and interactable filled in.</summary>
        public static TargetInfo Target(string id, string kind, GameObject go, string label = null, Dictionary<string, object> data = null)
        {
            var info = ScreenProjection.Of(go);
            return new TargetInfo
            {
                id = id,
                kind = kind,
                label = label ?? SceneQuery.Caption(go),
                path = SceneQuery.PathOf(go),
                center = info.CenterArray,
                rect = info.RectArray,
                visible = ScreenProjection.Visible(go, info, false),
                interactable = SceneQuery.Interactable(go),
                data = data,
            };
        }
    }
}
