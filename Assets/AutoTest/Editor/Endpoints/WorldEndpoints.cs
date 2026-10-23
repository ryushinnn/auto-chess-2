using System;
using System.Linq;
using UnityEngine;

namespace AutoTest
{
    /// <summary>/world/raycast, /world/screen-point, /input/drag, /input/drag-status.</summary>
    internal static class WorldEndpoints
    {
        public static object Raycast(Request r)
        {
            var p = new Vector2(r.RequireFloat("x"), r.RequireFloat("y"));
            var eventSystem = PointerSim.RaycastUi(p).Select(h => new
            {
                path = SceneQuery.PathOf(h.gameObject),
                name = h.gameObject.name,
                module = h.module != null ? h.module.GetType().Name : null,
                distance = h.distance,
                sortingOrder = h.sortingOrder,
                depth = h.depth,
            }).ToList();

            var cam = ScreenProjection.PointerCamera();
            object physics = Array.Empty<object>();
            if (cam != null)
            {
                var ray = cam.ScreenPointToRay(new Vector3(p.x, p.y, 0f));
                physics = Physics.RaycastAll(ray, cam.farClipPlane, cam.cullingMask & Physics.DefaultRaycastLayers,
                                             QueryTriggerInteraction.Collide)
                    .Where(h => h.collider != null)
                    .OrderBy(h => h.distance)
                    .Select(h => new
                    {
                        path = SceneQuery.PathOf(h.collider.transform),
                        distance = h.distance,
                        point = new[] { h.point.x, h.point.y, h.point.z },
                        collider = h.collider.GetType().Name,
                        isTrigger = h.collider.isTrigger,
                        rigidbody = h.rigidbody != null ? SceneQuery.PathOf(h.rigidbody.transform) : null,
                    }).ToList();
            }
            return new
            {
                point = new[] { p.x, p.y },
                eventSystem,
                physics,
                camera = cam != null ? cam.name : null,
                eventSystemPresent = UnityEngine.EventSystems.EventSystem.current != null,
            };
        }

        public static object ScreenPoint(Request r)
        {
            string path = r.RequireStr("path");
            var go = SceneQuery.FindByPath(path);
            if (go == null) return new { found = false, path };
            var info = ScreenProjection.Of(go);
            var pos = go.transform.position;
            return new
            {
                found = true,
                path = SceneQuery.PathOf(go),
                world = new[] { info.world.x, info.world.y, info.world.z },
                screen = info.CenterArray,
                onScreen = info.ok && info.onScreen,
                rect = info.RectArray,
                position = new[] { pos.x, pos.y, pos.z },
                depth = info.depth,
                isUi = info.isUi,
                camera = info.camera != null ? info.camera.name : null,
                go.activeInHierarchy,
            };
        }

        // ---- drag --------------------------------------------------------------------------------------------

        public static object Drag(Request r)
        {
            int steps = r.Int("steps", 12, 2, 120);
            int frames = r.Int("frames", 1, 1, 10);
            if (!Point(r, "x0", "y0", "from", out var from, out string why) || !Point(r, "x1", "y1", "to", out var to, out why))
                return new { started = false, mode = "none", message = why };
            return DragDriver.Begin(from, to, steps, frames);
        }

        public static object DragStatus(Request r) => DragDriver.Status();

        /// <summary>(x, y) from two coordinates or the screen center of an object given by path; false (with the
        /// reason) when that object is inactive or off screen. A missing object is a 404.</summary>
        private static bool Point(Request r, string xKey, string yKey, string pathKey, out Vector2 point, out string why)
        {
            point = default;
            why = null;
            if (r.Has(pathKey))
            {
                string path = r.RequireStr(pathKey);
                var go = SceneQuery.FindByPath(path) ?? throw new NotFoundException("no GameObject at path '" + path + "' (" + pathKey + ")");
                var info = ScreenProjection.Of(go);
                if (!go.activeInHierarchy) why = pathKey + " '" + path + "' is inactive";
                else if (!info.ok || !info.onScreen) why = pathKey + " '" + path + "' is not on screen";
                point = info.center;
                return why == null;
            }
            if (r.Has(xKey) || r.Has(yKey))
            {
                point = new Vector2(r.RequireFloat(xKey), r.RequireFloat(yKey));
                return true;
            }
            throw new ArgumentException("pass " + xKey + "," + yKey + " or " + pathKey);
        }
    }
}
