using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AutoTest
{
    /// <summary>What a pointer at a screen point would reach.</summary>
    internal sealed class PointerHit
    {
        public GameObject go;                // the raycast hit
        public RaycastResult raycast;        // EventSystem result, or one synthesized from the physics hit
        public bool world;                   // a 3D object (PhysicsRaycaster hit or the physics fallback): OnMouse* too
        public bool viaEventSystem;
        public readonly List<GameObject> mouseTargets = new List<GameObject>(); // OnMouse* receivers (collider / rigidbody)

        public string Mode => world ? "world" : "ui";
    }

    /// <summary>
    /// Engine-side pointer input with no OS mouse and no focus: EventSystem.RaycastAll at a point (uGUI plus any
    /// PhysicsRaycaster), else a Physics.Raycast from the pointer camera; then the ExecuteEvents sequence the
    /// StandaloneInputModule would fire. World (collider) hits also get OnMouseDown/Up/UpAsButton/Drag through
    /// SendMessage, the way Unity's own mouse events reach colliders. Main thread only.
    /// </summary>
    internal static class PointerSim
    {
        public static List<RaycastResult> RaycastUi(Vector2 screen)
        {
            var results = new List<RaycastResult>();
            var es = EventSystem.current;
            if (es == null) return results;
            es.RaycastAll(new PointerEventData(es) { position = screen }, results);
            results.RemoveAll(r => r.gameObject == null);
            return results;
        }

        public static bool RaycastPhysics(Vector2 screen, out RaycastHit hit, out Camera cam)
        {
            hit = default;
            cam = ScreenProjection.PointerCamera();
            if (cam == null) return false;
            var ray = cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));
            return Physics.Raycast(ray, out hit, cam.farClipPlane, cam.cullingMask & Physics.DefaultRaycastLayers,
                                   QueryTriggerInteraction.UseGlobal) && hit.collider != null;
        }

        /// <summary>Top EventSystem hit, else the nearest physics hit, else null.</summary>
        public static PointerHit HitTest(Vector2 screen)
        {
            var ui = RaycastUi(screen);
            if (ui.Count > 0)
            {
                var top = ui[0];
                var hit = new PointerHit { go = top.gameObject, raycast = top, viaEventSystem = true };
                // A PhysicsRaycaster / Physics2DRaycaster hit is a world object; a GraphicRaycaster hit is UI.
                hit.world = !(top.module is UnityEngine.UI.GraphicRaycaster) && !(top.gameObject.transform is RectTransform);
                if (hit.world) AddMouseTargets(hit, top.gameObject.GetComponent<Collider>());
                return hit;
            }
            if (RaycastPhysics(screen, out var ph, out var cam))
            {
                var hit = new PointerHit { go = ph.collider.gameObject, raycast = FromPhysics(ph, screen), world = true };
                AddMouseTargets(hit, ph.collider);
                return hit;
            }
            return null;
        }

        public static RaycastResult FromPhysics(RaycastHit ph, Vector2 screen) => new RaycastResult
        {
            gameObject = ph.collider.gameObject,
            distance = ph.distance,
            worldPosition = ph.point,
            worldNormal = ph.normal,
            screenPosition = screen,
            index = 0,
        };

        private static void AddMouseTargets(PointerHit hit, Collider col)
        {
            // Unity sends OnMouse* to the collider's attached rigidbody object for compound colliders; send to both.
            hit.mouseTargets.Add(hit.go);
            var body = col != null ? col.attachedRigidbody : null;
            if (body != null && body.gameObject != hit.go) hit.mouseTargets.Add(body.gameObject);
        }

        /// <summary>The current top hit as the pointerCurrentRaycast of a moving pointer.</summary>
        public static RaycastResult CurrentRaycast(Vector2 screen, out GameObject over)
        {
            var hit = HitTest(screen);
            over = hit?.go;
            return hit?.raycast ?? new RaycastResult { screenPosition = screen };
        }

        public static PointerEventData NewPointer(Vector2 screen, RaycastResult raycast, PointerHit hit)
        {
            var ped = new PointerEventData(EventSystem.current)
            {
                pointerId = -1, // PointerInputModule.kMouseLeftId
                button = PointerEventData.InputButton.Left,
                position = screen,
                pressPosition = screen,
                delta = Vector2.zero,
                clickCount = 1,
                clickTime = Time.unscaledTime,
                eligibleForClick = true,
                useDragThreshold = true,
                pointerCurrentRaycast = raycast,
                pointerPressRaycast = raycast,
            };
            if (hit != null)
                for (var t = hit.go.transform; t != null; t = t.parent) ped.hovered.Add(t.gameObject);
            return ped;
        }

        // ---- legacy OnMouse* ---------------------------------------------------------------------------------

        /// <summary>SendMessage(DontRequireReceiver) to each target; true when some MonoBehaviour there defines the method.</summary>
        public static bool SendMouse(List<GameObject> targets, string message)
        {
            bool received = false;
            foreach (var go in targets)
            {
                if (go == null) continue;
                received |= HasMethod(go, message);
                go.SendMessage(message, SendMessageOptions.DontRequireReceiver);
            }
            return received;
        }

        /// <summary>Some MonoBehaviour on `go` declares a parameterless method `name` (any visibility, base classes included).</summary>
        public static bool HasMethod(GameObject go, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (var mb in go.GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;
                for (var t = mb.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                    if (t.GetMethod(name, flags, null, System.Type.EmptyTypes, null) != null) return true;
            }
            return false;
        }

        // ---- click -------------------------------------------------------------------------------------------

        public sealed class ClickOutcome
        {
            public bool executed;
            public string consumerPath;   // taken before the handler ran: a click may switch screens and destroy it
            public readonly List<string> fired = new List<string>();
        }

        /// <summary>
        /// enter, (OnMouseDown), down via ExecuteHierarchy, (OnMouseUp, OnMouseUpAsButton), up on the press handler,
        /// click on the IPointerClickHandler owner, exit. pointerClick is what Button.onClick / Toggle listen to.
        /// </summary>
        public static ClickOutcome Click(PointerHit hit, Vector2 screen)
        {
            var outcome = new ClickOutcome();
            var go = hit.go;
            string hitPath = SceneQuery.PathOf(go);
            var ped = NewPointer(screen, hit.raycast, hit);

            var enter = ExecuteEvents.GetEventHandler<IPointerEnterHandler>(go);
            if (enter != null && ExecuteEvents.Execute(enter, ped, ExecuteEvents.pointerEnterHandler)) outcome.fired.Add("pointerEnter");
            ped.pointerEnter = go;

            bool mouse = false;
            if (hit.world && SendMouse(hit.mouseTargets, "OnMouseDown")) { outcome.fired.Add("OnMouseDown"); mouse = true; }

            var clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(go);
            string clickPath = SceneQuery.PathOf(clickHandler);
            var down = ExecuteEvents.ExecuteHierarchy(go, ped, ExecuteEvents.pointerDownHandler);
            string downPath = SceneQuery.PathOf(down);
            if (down != null) outcome.fired.Add("pointerDown");
            ped.pointerPress = down != null ? down : clickHandler;
            ped.rawPointerPress = go;

            if (hit.world)
            {
                if (SendMouse(hit.mouseTargets, "OnMouseUp")) { outcome.fired.Add("OnMouseUp"); mouse = true; }
                if (SendMouse(hit.mouseTargets, "OnMouseUpAsButton")) { outcome.fired.Add("OnMouseUpAsButton"); mouse = true; }
            }

            if (ped.pointerPress != null && ExecuteEvents.Execute(ped.pointerPress, ped, ExecuteEvents.pointerUpHandler))
                outcome.fired.Add("pointerUp");

            bool clicked = false;
            if (clickHandler != null)
            {
                ped.pointerClick = clickHandler;
                clicked = ExecuteEvents.Execute(clickHandler, ped, ExecuteEvents.pointerClickHandler);
                if (clicked) outcome.fired.Add("pointerClick");
            }

            // The click may have destroyed the object (screen switch): Unity's == catches that.
            if (enter != null) ExecuteEvents.Execute(enter, ped, ExecuteEvents.pointerExitHandler);

            outcome.consumerPath = clicked ? clickPath : downPath ?? (mouse ? hitPath : null);
            outcome.executed = clicked || downPath != null || mouse;
            return outcome;
        }

        /// <summary>
        /// Does a pointer that hit `hit` reach `target`? Yes when it hit the target or something below it; also when
        /// it hit an ancestor whose click goes to the same IPointerClickHandler the target's would (a label that is
        /// not a raycast target inside its button).
        /// </summary>
        public static bool Reaches(GameObject hit, GameObject target)
        {
            if (hit == null || target == null) return false;
            if (hit.transform.IsChildOf(target.transform)) return true;
            if (!target.transform.IsChildOf(hit.transform)) return false;
            var a = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit);
            return a != null && a == ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
        }
    }
}
