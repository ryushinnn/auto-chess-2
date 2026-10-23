using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AutoTest
{
    /// <summary>
    /// /input/drag as an EditorApplication.update state machine spread over real player frames (ScrollRect, sliders
    /// and game drag code read per-frame positions). Begin: enter, (OnMouseDown), pointerDown, initializePotentialDrag.
    /// Each step: position/delta/pointerCurrentRaycast refreshed, beginDrag on the first, drag (and OnMouseDrag).
    /// End: (OnMouseUp[AsButton]), pointerUp, click if no drag ever started, drop under the end point, endDrag, exit.
    /// The client polls /input/drag-status. A domain reload drops the gesture (like every other id).
    /// </summary>
    [InitializeOnLoad]
    internal static class DragDriver
    {
        public sealed class Result
        {
            public bool executed;
            public string mode, pressTarget, dragTarget, dropTarget, message;
            public bool aborted;
            public List<string> fired = new List<string>();
        }

        private sealed class Gesture
        {
            public PointerEventData ped;
            public PointerHit start;
            public GameObject enterHandler, dragHandler;
            public Vector2 from, to;
            public int steps, framesPerStep, step, nextFrame;
            public int dragEventIndex = -1, dragEvents;
            public bool mouseDragReceiver;
            public Result result;
        }

        private static Gesture s_Active;
        private static Result s_Last;

        static DragDriver()
        {
            EditorApplication.update += Tick;
        }

        public static void Touch() { }

        public static object Begin(Vector2 from, Vector2 to, int steps, int framesPerStep)
        {
            if (s_Active != null)
                throw new ArgumentException("a drag is already running (poll /input/drag-status until done)");

            var hit = PointerSim.HitTest(from);
            if (hit == null)
            {
                s_Last = new Result { message = "nothing under the start point (" + from.x + ", " + from.y + ")", mode = "none" };
                return new { started = false, mode = "none", message = s_Last.message };
            }

            var g = new Gesture
            {
                start = hit, from = from, to = to, steps = steps, framesPerStep = framesPerStep,
                result = new Result { mode = hit.Mode },
            };
            var ped = g.ped = PointerSim.NewPointer(from, hit.raycast, hit);
            ped.useDragThreshold = false; // a deliberate gesture: no dead zone
            var go = hit.go;
            var fired = g.result.fired;

            g.enterHandler = ExecuteEvents.GetEventHandler<IPointerEnterHandler>(go);
            if (g.enterHandler != null && ExecuteEvents.Execute(g.enterHandler, ped, ExecuteEvents.pointerEnterHandler))
                fired.Add("pointerEnter");
            ped.pointerEnter = go;

            if (hit.world && PointerSim.SendMouse(hit.mouseTargets, "OnMouseDown")) Mouse(g, "OnMouseDown");

            var down = ExecuteEvents.ExecuteHierarchy(go, ped, ExecuteEvents.pointerDownHandler);
            if (down != null) { fired.Add("pointerDown"); g.result.executed = true; }
            ped.pointerPress = down != null ? down : ExecuteEvents.GetEventHandler<IPointerClickHandler>(go);
            ped.rawPointerPress = go;
            g.result.pressTarget = SceneQuery.PathOf(ped.pointerPress);

            g.dragHandler = ExecuteEvents.GetEventHandler<IDragHandler>(go);
            ped.pointerDrag = g.dragHandler;
            if (g.dragHandler != null && ExecuteEvents.Execute(g.dragHandler, ped, ExecuteEvents.initializePotentialDrag))
                fired.Add("initializePotentialDrag");

            // A collider object dragged through OnMouseDrag counts as the drag target too.
            g.mouseDragReceiver = hit.world && HasMouseDrag(hit);
            var dragObject = g.dragHandler != null ? g.dragHandler : g.mouseDragReceiver ? go : null;
            g.result.dragTarget = SceneQuery.PathOf(dragObject);

            g.nextFrame = Time.frameCount + framesPerStep;
            s_Active = g;
            s_Last = null;
            return new
            {
                started = true,
                dragTarget = g.result.dragTarget,
                pressTarget = g.result.pressTarget,
                mode = hit.Mode,
                startPath = SceneQuery.PathOf(go),
                steps,
                frames = framesPerStep,
                message = dragObject == null
                    ? "nothing under the start point handles IDragHandler / OnMouseDrag: the moves fire but nothing drags"
                    : null,
            };
        }

        public static object Status()
        {
            var g = s_Active;
            if (g != null)
                return new { active = true, done = false, progress = new { step = g.step, steps = g.steps } };
            return new { active = false, done = s_Last != null, result = s_Last };
        }

        private static bool HasMouseDrag(PointerHit hit) => hit.mouseTargets.Exists(go => PointerSim.HasMethod(go, "OnMouseDrag"));

        private static void Mouse(Gesture g, string what)
        {
            g.result.fired.Add(what);
            g.result.executed = true;
        }

        private static void Tick()
        {
            var g = s_Active;
            if (g == null) return;
            if (!EditorApplication.isPlaying) { Abort(g, "play mode exited"); return; }
            if (Time.frameCount < g.nextFrame) return; // frames only advance while playing: a pause waits, /step moves one step
            try
            {
                Step(g);
                g.nextFrame = Time.frameCount + g.framesPerStep;
                if (g.step >= g.steps) Finish(g);
            }
            catch (Exception e)
            {
                Abort(g, "drag step threw " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static void Step(Gesture g)
        {
            var ped = g.ped;
            g.step++;
            var pos = Vector2.Lerp(g.from, g.to, g.step / (float)g.steps);
            ped.delta = pos - ped.position;
            ped.position = pos;
            ped.pointerCurrentRaycast = PointerSim.CurrentRaycast(pos, out _);

            if (g.dragHandler != null)
            {
                if (!ped.dragging)
                {
                    ExecuteEvents.Execute(g.dragHandler, ped, ExecuteEvents.beginDragHandler);
                    ped.dragging = true;
                    g.result.fired.Add("beginDrag");
                    g.result.executed = true;
                    // StandaloneInputModule: once a drag starts, a different press target is released and no click follows.
                    if (ped.pointerPress != null && ped.pointerPress != g.dragHandler)
                    {
                        ExecuteEvents.Execute(ped.pointerPress, ped, ExecuteEvents.pointerUpHandler);
                        g.result.fired.Add("pointerUp");
                        ped.pointerPress = null;
                        ped.rawPointerPress = null;
                    }
                    ped.eligibleForClick = false;
                }
                ExecuteEvents.Execute(g.dragHandler, ped, ExecuteEvents.dragHandler);
                CountDrag(g);
            }
            if (g.start.world && g.mouseDragReceiver)
            {
                PointerSim.SendMouse(g.start.mouseTargets, "OnMouseDrag");
                if (g.dragHandler == null) CountDrag(g, "OnMouseDrag");
                g.result.executed = true;
            }
        }

        private static void CountDrag(Gesture g, string name = "drag")
        {
            g.dragEvents++;
            string label = name + " x" + g.dragEvents;
            if (g.dragEventIndex < 0) { g.dragEventIndex = g.result.fired.Count; g.result.fired.Add(label); }
            else g.result.fired[g.dragEventIndex] = label;
        }

        private static void Finish(Gesture g)
        {
            var ped = g.ped;
            var r = g.result;
            var endRaycast = PointerSim.CurrentRaycast(ped.position, out var over);
            ped.pointerCurrentRaycast = endRaycast;

            if (g.start.world)
            {
                if (PointerSim.SendMouse(g.start.mouseTargets, "OnMouseUp")) Mouse(g, "OnMouseUp");
                // Unity sends OnMouseUpAsButton only when released over the collider that was pressed.
                if (over != null && g.start.mouseTargets.Contains(over) &&
                    PointerSim.SendMouse(g.start.mouseTargets, "OnMouseUpAsButton"))
                    Mouse(g, "OnMouseUpAsButton");
            }

            if (ped.pointerPress != null)
            {
                ExecuteEvents.Execute(ped.pointerPress, ped, ExecuteEvents.pointerUpHandler);
                r.fired.Add("pointerUp");
                var clickHandler = over != null ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(over) : null;
                if (ped.eligibleForClick && clickHandler != null && clickHandler == ped.pointerPress &&
                    ExecuteEvents.Execute(clickHandler, ped, ExecuteEvents.pointerClickHandler))
                {
                    r.fired.Add("pointerClick");
                    r.executed = true;
                }
            }

            if (ped.dragging)
            {
                if (over != null)
                {
                    var drop = ExecuteEvents.ExecuteHierarchy(over, ped, ExecuteEvents.dropHandler);
                    if (drop != null)
                    {
                        r.fired.Add("drop");
                        r.dropTarget = SceneQuery.PathOf(drop);
                    }
                }
                if (g.dragHandler != null)
                {
                    ExecuteEvents.Execute(g.dragHandler, ped, ExecuteEvents.endDragHandler);
                    r.fired.Add("endDrag");
                }
            }
            if (r.dropTarget == null && over != null) r.message = "released over " + SceneQuery.PathOf(over);

            if (g.enterHandler != null) ExecuteEvents.Execute(g.enterHandler, ped, ExecuteEvents.pointerExitHandler);
            ped.dragging = false;
            Complete(g);
        }

        private static void Abort(Gesture g, string why)
        {
            g.result.aborted = true;
            g.result.message = "aborted: " + why;
            Complete(g);
        }

        private static void Complete(Gesture g)
        {
            s_Last = g.result;
            if (s_Active == g) s_Active = null;
        }
    }
}
