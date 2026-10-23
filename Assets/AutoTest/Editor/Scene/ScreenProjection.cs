using UnityEditor;
using UnityEngine;

namespace AutoTest
{
    /// <summary>Where an object is on screen: Game-view render pixels, origin bottom-left (Unity Screen space).</summary>
    internal struct ScreenInfo
    {
        public bool ok;          // a screen position could be computed
        public bool isUi;        // RectTransform under a Canvas
        public Rect rect;
        public Vector2 center;
        public Vector3 world;    // the anchor that was projected (rect center for UI, bounds center for world objects)
        public float depth;      // camera-space z of the anchor (> 0 = in front of the camera); 0 for overlay UI
        public bool onScreen;    // anchor in front of the camera and inside the render target
        public Camera camera;    // null for screen-space-overlay UI

        public float[] RectArray => ok ? new[] { rect.xMin, rect.yMin, rect.xMax, rect.yMax } : null;
        public float[] CenterArray => ok ? new[] { center.x, center.y } : null;
    }

    internal static class ScreenProjection
    {
        private static readonly Vector3[] s_Corners = new Vector3[4];
        private static readonly Vector3[] s_Box = new Vector3[8];

        /// <summary>The Game view's render target size (what Screen.width/height are inside the game).</summary>
        public static Vector2 ScreenSize()
        {
            var size = Handles.GetMainGameViewSize();
            if (size.x >= 1 && size.y >= 1) return size;
            var cam = Camera.main;
            return cam != null ? new Vector2(cam.pixelWidth, cam.pixelHeight) : new Vector2(Screen.width, Screen.height);
        }

        public static ScreenInfo Of(GameObject go)
        {
            if (go == null) return default;
            if (go.transform is RectTransform rt)
            {
                var canvas = go.GetComponentInParent<Canvas>(true);
                if (canvas != null) return OfUi(rt, canvas);
            }
            return OfWorld(go);
        }

        /// <summary>The camera a canvas is drawn with; null = overlay (a Screen Space - Camera canvas without a camera renders as overlay).</summary>
        public static Camera CanvasCamera(Canvas canvas)
        {
            var root = canvas.rootCanvas;
            switch (root.renderMode)
            {
                case RenderMode.ScreenSpaceOverlay: return null;
                case RenderMode.ScreenSpaceCamera: return root.worldCamera;
                default: return root.worldCamera != null ? root.worldCamera : Camera.main;
            }
        }

        private static ScreenInfo OfUi(RectTransform rt, Canvas canvas)
        {
            var cam = CanvasCamera(canvas);
            rt.GetWorldCorners(s_Corners);
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
            bool inFront = true;
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = cam != null ? cam.WorldToScreenPoint(s_Corners[i]) : s_Corners[i];
                if (cam != null && p.z <= 0) inFront = false;
                xMin = Mathf.Min(xMin, p.x); xMax = Mathf.Max(xMax, p.x);
                yMin = Mathf.Min(yMin, p.y); yMax = Mathf.Max(yMax, p.y);
            }
            var world = (s_Corners[0] + s_Corners[2]) * 0.5f;
            var info = new ScreenInfo
            {
                ok = true,
                isUi = true,
                camera = cam,
                world = world,
                rect = Rect.MinMaxRect(xMin, yMin, xMax, yMax),
                center = new Vector2((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f),
                depth = cam != null ? cam.WorldToScreenPoint(world).z : 0f,
            };
            info.onScreen = inFront && Inside(info.center);
            return info;
        }

        private static ScreenInfo OfWorld(GameObject go)
        {
            var cam = CameraFor(go);
            if (cam == null) return default;
            bool hasBounds = TryBounds(go, out var bounds);
            var anchor = hasBounds ? bounds.center : go.transform.position;
            var c = cam.WorldToScreenPoint(anchor);
            var info = new ScreenInfo { ok = true, camera = cam, world = anchor, depth = c.z, center = new Vector2(c.x, c.y) };
            if (hasBounds)
            {
                Vector3 min = bounds.min, max = bounds.max;
                for (int i = 0; i < 8; i++)
                    s_Box[i] = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
                float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
                foreach (var corner in s_Box)
                {
                    var p = cam.WorldToScreenPoint(corner);
                    if (p.z <= 0) continue;
                    xMin = Mathf.Min(xMin, p.x); xMax = Mathf.Max(xMax, p.x);
                    yMin = Mathf.Min(yMin, p.y); yMax = Mathf.Max(yMax, p.y);
                }
                info.rect = xMin <= xMax ? Rect.MinMaxRect(xMin, yMin, xMax, yMax) : new Rect(info.center, Vector2.zero);
            }
            else info.rect = new Rect(info.center, Vector2.zero);
            info.onScreen = c.z > 0 && Inside(info.center);
            return info;
        }

        /// <summary>Collider bounds (what a click ray hits) before renderer bounds; own components before children's.</summary>
        public static bool TryBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            var col = go.GetComponent<Collider>();
            if (col != null && col.enabled) { bounds = col.bounds; return true; }
            var rend = go.GetComponent<Renderer>();
            if (rend != null) { bounds = rend.bounds; return true; }
            bool any = false;
            foreach (var c in go.GetComponentsInChildren<Collider>())
            {
                if (!c.enabled) continue;
                if (!any) { bounds = c.bounds; any = true; } else bounds.Encapsulate(c.bounds);
            }
            if (any) return true;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
            }
            return any;
        }

        /// <summary>Camera.main when it renders the object's layer, else the top-most enabled camera that does.</summary>
        public static Camera CameraFor(GameObject go)
        {
            var main = Camera.main;
            int bit = go != null ? 1 << go.layer : ~0;
            if (main != null && (main.cullingMask & bit) != 0) return main;
            Camera best = null;
            foreach (var cam in Camera.allCameras)
            {
                if (cam == null || !cam.isActiveAndEnabled || cam.targetTexture != null || (cam.cullingMask & bit) == 0) continue;
                if (best == null || cam.depth > best.depth) best = cam;
            }
            return best != null ? best : main;
        }

        /// <summary>The camera physics clicks are cast from: Camera.main, else the top-most enabled screen camera.</summary>
        public static Camera PointerCamera()
        {
            var main = Camera.main;
            if (main != null && main.isActiveAndEnabled) return main;
            Camera best = null;
            foreach (var cam in Camera.allCameras)
                if (cam != null && cam.isActiveAndEnabled && cam.targetTexture == null && (best == null || cam.depth > best.depth))
                    best = cam;
            return best;
        }

        public static bool Inside(Vector2 p)
        {
            var size = ScreenSize();
            return p.x >= 0 && p.y >= 0 && p.x <= size.x && p.y <= size.y;
        }

        public static bool Overlaps(Rect r)
        {
            var size = ScreenSize();
            return r.xMax > 0 && r.yMax > 0 && r.xMin < size.x && r.yMin < size.y;
        }

        /// <summary>Drawn and not hidden: active, on screen, with area, canvases enabled, CanvasGroup alpha above zero,
        /// and (checkGraphic) its own Graphic enabled, opaque enough and not culled. World objects: an enabled renderer.</summary>
        public static bool Visible(GameObject go, ScreenInfo info, bool checkGraphic)
        {
            if (go == null || !go.activeInHierarchy || !info.ok) return false;
            if (!info.isUi)
            {
                if (info.depth <= 0 || !Overlaps(info.rect.width > 0 ? info.rect : new Rect(info.center, Vector2.one))) return false;
                var rend = go.GetComponentInChildren<Renderer>();
                return rend == null || rend.enabled;
            }
            if (info.rect.width < 0.5f || info.rect.height < 0.5f || !Overlaps(info.rect)) return false;
            if (info.camera != null && info.depth <= 0) return false;
            foreach (var canvas in go.GetComponentsInParent<Canvas>(true))
                if (!canvas.enabled) return false;
            float alpha = 1f;
            foreach (var group in go.GetComponentsInParent<CanvasGroup>(true))
            {
                if (!group.enabled) continue;
                alpha *= group.alpha;
                if (group.ignoreParentGroups) break;
            }
            if (alpha <= 0.01f) return false;
            if (checkGraphic && go.TryGetComponent<UnityEngine.UI.Graphic>(out var g))
                if (!g.enabled || g.color.a * alpha <= 0.01f || g.canvasRenderer.cull) return false;
            return true;
        }
    }
}
