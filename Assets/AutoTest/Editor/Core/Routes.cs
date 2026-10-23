using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace AutoTest
{
    internal enum RouteMode { Any, Play, Edit }

    internal sealed class Route
    {
        public string Path;
        public Func<Request, object> Handler;
        public RouteMode Mode;
        public bool MainThread = true;
        public int TimeoutMs = 5000;
    }

    /// <summary>
    /// The route table (PROTOCOL.md §3): one line per endpoint. Every route answers GET and POST. A main-thread
    /// route checks its mode and builds its JSON inside the main-thread job; the rest run on the HTTP thread and must
    /// not touch Unity APIs (/ping, /logs, /logs/clear, /compile-status).
    /// </summary>
    internal static class Routes
    {
        private static readonly Dictionary<string, Route> s_Table = Build();

        private static Dictionary<string, Route> Build()
        {
            var t = new Dictionary<string, Route>(StringComparer.OrdinalIgnoreCase);

            void Add(string path, Func<Request, object> handler, RouteMode mode = RouteMode.Any, bool mainThread = true, int timeoutMs = 5000) =>
                t[path] = new Route { Path = path, Handler = handler, Mode = mode, MainThread = mainThread, TimeoutMs = timeoutMs };

            Add("/ping", EditorEndpoints.Ping, mainThread: false);
            Add("/logs", EditorEndpoints.Logs, mainThread: false);
            Add("/logs/clear", EditorEndpoints.LogsClear, mainThread: false);
            Add("/compile-status", EditorEndpoints.CompileStatusRoute, mainThread: false);

            Add("/status", EditorEndpoints.Status);
            Add("/refresh", EditorEndpoints.Refresh);                       // edit; exits Play itself with exitPlay=1
            Add("/play", EditorEndpoints.Play, timeoutMs: 15000);            // may open a scene first
            Add("/pause", EditorEndpoints.Pause, RouteMode.Play);
            Add("/step", EditorEndpoints.Step, RouteMode.Play);
            Add("/timescale", EditorEndpoints.TimeScale);
            Add("/screenshot", EditorEndpoints.Screenshot, RouteMode.Play);
            Add("/menu", EditorEndpoints.Menu, RouteMode.Edit, timeoutMs: 15000);

            Add("/hierarchy", HierarchyEndpoint.Get, timeoutMs: 10000);
            Add("/ui/components", UiEndpoints.Components, RouteMode.Play, timeoutMs: 10000);
            Add("/ui/find", UiEndpoints.Find, RouteMode.Play);
            Add("/ui/click", UiEndpoints.Click, RouteMode.Play);
            Add("/input/drag", WorldEndpoints.Drag, RouteMode.Play);
            Add("/input/drag-status", WorldEndpoints.DragStatus);
            Add("/world/raycast", WorldEndpoints.Raycast, RouteMode.Play);
            Add("/world/screen-point", WorldEndpoints.ScreenPoint, RouteMode.Play);
            Add("/reflect", ReflectEndpoint.Handle, timeoutMs: 15000);

            Add("/state", GameEndpoints.State, timeoutMs: 10000);           // the adapter's IsAvailable decides
            Add("/game-op", GameEndpoints.GameOp, RouteMode.Play, timeoutMs: 15000);
            Add("/invariants", GameEndpoints.Invariants, RouteMode.Play, timeoutMs: 10000);
            Add("/targets", GameEndpoints.Targets, RouteMode.Play, timeoutMs: 10000);

            Add("/tests/run", TestRunnerBridge.Run, RouteMode.Edit, timeoutMs: 15000);
            Add("/tests/status", TestRunnerBridge.Status);

            Add("/routes", _ => new { routes = t.Values.OrderBy(x => x.Path).Select(x => new { path = x.Path, mode = x.Mode.ToString().ToLowerInvariant(), mainThread = x.MainThread }).ToList() }, mainThread: false);
            return t;
        }

        public static JObject Dispatch(Request req)
        {
            if (!s_Table.TryGetValue(req.Path, out var route))
                throw new NotFoundException("unknown route " + req.Path + " (GET /routes lists them)");
            if (!route.MainThread) return Json.ToReply(route.Handler(req));
            return MainThread.Run(() =>
            {
                CheckMode(route);
                return Json.ToReply(route.Handler(req));
            }, route.TimeoutMs);
        }

        private static void CheckMode(Route route)
        {
            switch (route.Mode)
            {
                case RouteMode.Play when !EditorApplication.isPlaying:
                    throw new PlayModeRequiredException(route.Path + " needs Play mode");
                case RouteMode.Edit when EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode:
                    throw new EditModeRequiredException(route.Path + " needs Edit mode (exit Play first)");
            }
        }
    }
}
