using System;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// Localhost HTTP bridge for `AutoTest Server/auto_touch` (PROTOCOL.md). HttpListener on 127.0.0.1:&lt;port&gt;,
    /// one accept thread, requests on the thread pool, Unity work marshalled through MainThread.
    /// Every domain reload (entering Play, a compile) kills this class: the listener is closed in
    /// beforeAssemblyReload and reopened by the next domain's static constructor, with EditorApplication.update
    /// retries (delayCall does not fire while Unity is unfocused). Clients retry around Play transitions.
    /// </summary>
    [InitializeOnLoad]
    public static class AutoTestServer
    {
        public const string Version = "1.0.0";
        public const int DefaultPort = 8766;

        internal const string EnabledKey = "AutoTest.Enabled";     // SessionState: server wanted in this editor session
        // EditorPrefs are shared by every project on the machine (other rigs use "AutoTest.AutoStart" too), so
        // these two are scoped to this project's folder: toggling autostart or the port elsewhere leaves it alone.
        internal static readonly string AutoStartKey = "AutoTest.AutoStart:" + Application.dataPath; // default true
        internal static readonly string PortKey = "AutoTest.Port:" + Application.dataPath;           // int

        private static HttpListener s_Listener;
        private static Thread s_AcceptThread;
        private static volatile int s_Port = DefaultPort;
        private static bool s_SlowRetryHooked, s_BindFailureLogged;
        private static double s_NextSlowRetryAt;

        public static int Port => s_Port;
        public static bool IsRunning => s_Listener != null && s_Listener.IsListening;

        static AutoTestServer()
        {
            // Every static that is main-thread-only (SessionState, Application.dataPath, update hooks) is initialized
            // here, before the first request can reach it from a pool thread.
            Init(MainThread.Touch, LogBuffer.Touch, CompileStatus.Touch, PlayModeTracker.Touch, AutoTestPaths.Touch,
                 SceneQuery.Touch, DragDriver.Touch, TestRunnerBridge.Touch);

            AssemblyReloadEvents.beforeAssemblyReload += StopListener;
            EditorApplication.quitting += StopListener;
            if (EditorPrefs.GetBool(AutoStartKey, true)) SessionState.SetBool(EnabledKey, true);
            if (SessionState.GetBool(EnabledKey, false)) StartWithRetry(10);
        }

        private static void Init(params Action[] touches)
        {
            foreach (var touch in touches)
            {
                try { touch(); }
                catch (Exception e) { Debug.LogWarning("[AutoTest] init: " + Errors.Unwrap(e)); }
            }
        }

        // ---- control (menu) ----------------------------------------------------------------------------------

        internal static void StartServer()
        {
            SessionState.SetBool(EnabledKey, true);
            s_BindFailureLogged = false;
            StartWithRetry(10);
        }

        internal static void StopServer()
        {
            SessionState.SetBool(EnabledKey, false);
            StopListener();
            Debug.Log("[AutoTest] server stopped for this session" +
                      (EditorPrefs.GetBool(AutoStartKey, true) ? " (autostart restarts it after the next domain reload)" : ""));
        }

        // ---- listener lifecycle ------------------------------------------------------------------------------

        private static void OnNextUpdate(Action action)
        {
            EditorApplication.CallbackFunction once = null;
            once = () =>
            {
                EditorApplication.update -= once;
                action();
            };
            EditorApplication.update += once;
        }

        private static void StartWithRetry(int attemptsLeft)
        {
            // A queued retry must not resurrect a server the user stopped: SessionState is the single source of truth.
            if (!SessionState.GetBool(EnabledKey, false) || IsRunning) return;
            s_Port = EditorPrefs.GetInt(PortKey, DefaultPort);
            try
            {
                var listener = new HttpListener();
                listener.Prefixes.Add("http://127.0.0.1:" + s_Port + "/");
                listener.Start();
                s_Listener = listener;
                s_AcceptThread = new Thread(() => AcceptLoop(listener)) { IsBackground = true, Name = "AutoTestServer" };
                s_AcceptThread.Start();
                s_BindFailureLogged = false;
                Debug.Log("[AutoTest] server " + Version + " listening on http://127.0.0.1:" + s_Port + "/");
            }
            catch (Exception e)
            {
                // Usually the previous domain's socket not released yet; else a foreign process on the port. Fast retries
                // on the next updates, then a slow permanent retry so the server heals once the port frees up.
                if (attemptsLeft > 0)
                {
                    OnNextUpdate(() => StartWithRetry(attemptsLeft - 1));
                    return;
                }
                if (!s_BindFailureLogged)
                {
                    s_BindFailureLogged = true;
                    Debug.LogWarning("[AutoTest] cannot listen on port " + s_Port + " (" + e.Message + "): retrying every 5 s");
                }
                if (!s_SlowRetryHooked)
                {
                    s_SlowRetryHooked = true;
                    EditorApplication.update += SlowRetry;
                }
            }
        }

        private static void SlowRetry()
        {
            if (IsRunning || !SessionState.GetBool(EnabledKey, false)) return;
            if (EditorApplication.timeSinceStartup < s_NextSlowRetryAt) return;
            s_NextSlowRetryAt = EditorApplication.timeSinceStartup + 5.0;
            StartWithRetry(0);
        }

        private static void StopListener()
        {
            var listener = s_Listener;
            s_Listener = null;
            if (listener == null) return;
            try { listener.Stop(); listener.Close(); } catch { /* shutting down */ }
            try { s_AcceptThread?.Join(500); } catch { /* best effort */ }
            s_AcceptThread = null;
        }

        private static void AcceptLoop(HttpListener listener)
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); }
                catch { return; } // Stop() aborts GetContext: normal shutdown
                ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
            }
        }

        // ---- request handling (thread-pool thread) -----------------------------------------------------------

        private static void Handle(HttpListenerContext ctx)
        {
            int status = 200;
            JObject reply;
            try
            {
                reply = Routes.Dispatch(Request.From(ctx.Request));
            }
            catch (Exception raw)
            {
                var e = Errors.Unwrap(raw);
                string kind;
                (status, kind) = Errors.Classify(e);
                reply = Errors.Reply(e, status, kind);
            }
            Write(ctx.Response, status, reply);
        }

        private static void Write(HttpListenerResponse resp, int status, JObject reply)
        {
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(Json.Compact(reply));
                resp.StatusCode = status;
                resp.ContentType = "application/json; charset=utf-8";
                resp.ContentLength64 = bytes.Length;
                resp.KeepAlive = false;
                resp.OutputStream.Write(bytes, 0, bytes.Length);
                resp.OutputStream.Close();
            }
            catch { /* client went away */ }
        }
    }
}
