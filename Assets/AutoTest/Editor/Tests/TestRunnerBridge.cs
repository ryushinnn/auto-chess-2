using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AutoTest
{
    /// <summary>
    /// /tests/run and /tests/status over UnityEditor.TestTools.TestRunner.Api. The run state lives in SessionState
    /// (saved before every domain reload, so a PlayMode run that reloads keeps its progress) and the last finished
    /// run in out/unity-tests/last.json (survives an editor restart). Callbacks are registered on every domain load,
    /// so runs started from the Test Runner window are reported too.
    /// </summary>
    [InitializeOnLoad]
    internal static class TestRunnerBridge
    {
        private const string SessionKey = "AutoTest.Tests.Run";

        [Serializable]
        private sealed class Row
        {
            public string name, fullName, result, resultState, message, stack;
            public double durationSec;
        }

        [Serializable]
        private sealed class RunState
        {
            public string runId, mode, filter, assembly, startedAtUtc, finishedAtUtc, error;
            public bool running, finished;
            public int expected;
            public double durationSec;
            public List<Row> results = new List<Row>();
        }

        private static RunState s_Run;
        private static TestRunnerApi s_Api;
        private static readonly Callbacks s_Callbacks = new Callbacks();

        static TestRunnerBridge()
        {
            Load();
            try { Api.RegisterCallbacks(s_Callbacks); }
            catch (Exception e) { Debug.LogWarning("[AutoTest] cannot register test callbacks: " + e.Message); }
            AssemblyReloadEvents.beforeAssemblyReload += Save;
            EditorApplication.update += Watchdog;
        }

        public static void Touch() { }

        private static TestRunnerApi Api
        {
            get
            {
                if (s_Api == null)
                {
                    s_Api = ScriptableObject.CreateInstance<TestRunnerApi>();
                    s_Api.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_Api;
            }
        }

        private static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

        // ---- /tests/run --------------------------------------------------------------------------------------

        public static object Run(Request r)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new ArgumentException("the editor is compiling/importing: retry when /status isCompiling is false");
            if (s_Run != null && s_Run.running)
                throw new ArgumentException("test run " + s_Run.runId + " is still running (poll /tests/status)");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new DirtySceneException("scene " + SceneManager.GetSceneAt(i).path +
                                                  " has unsaved changes: the Test Runner would block on a save prompt");

            string modeText = r.Str("mode", "EditMode").Trim();
            TestMode mode;
            if (modeText.Equals("EditMode", StringComparison.OrdinalIgnoreCase) || modeText.Equals("edit", StringComparison.OrdinalIgnoreCase))
                mode = TestMode.EditMode;
            else if (modeText.Equals("PlayMode", StringComparison.OrdinalIgnoreCase) || modeText.Equals("play", StringComparison.OrdinalIgnoreCase))
                mode = TestMode.PlayMode;
            else throw new ArgumentException("mode must be EditMode or PlayMode");

            var filter = new Filter { testMode = mode };
            string text = r.Str("filter")?.Trim();
            // groupNames are regexes matched against full names: an escaped one is a substring / exact-name match.
            if (!string.IsNullOrEmpty(text)) filter.groupNames = new[] { Regex.Escape(text) };
            string assembly = r.Str("assembly")?.Trim();
            if (!string.IsNullOrEmpty(assembly))
            {
                if (assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) assembly = assembly.Substring(0, assembly.Length - 4);
                filter.assemblyNames = new[] { assembly };
            }

            s_Run = new RunState
            {
                running = true, mode = mode.ToString(), filter = text, assembly = assembly, startedAtUtc = Now(),
            };
            try
            {
                s_Run.runId = Api.Execute(new ExecutionSettings(filter));
            }
            catch (Exception e)
            {
                Finish("could not start the run: " + e.Message);
                throw;
            }
            Save();
            return new { started = true, runId = s_Run.runId, mode = s_Run.mode, filter = text, assembly };
        }

        // ---- /tests/status -----------------------------------------------------------------------------------

        public static object Status(Request r)
        {
            string runId = r.Str("runId");
            var run = s_Run;
            if (runId != null && (run == null || run.runId != runId))
                throw new NotFoundException("no test run " + runId + (run != null ? " (last run: " + run.runId + ")" : ""));
            return Summary(run);
        }

        private static JObject Summary(RunState run)
        {
            if (run == null)
                return JObject.FromObject(new { running = false, finished = false, total = 0, passed = 0, failed = 0, skipped = 0, durationSec = 0.0, results = new object[0] });
            int Count(string result) => run.results.Count(x => x.result == result);
            double duration = run.running && DateTime.TryParse(run.startedAtUtc, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var started)
                ? (DateTime.UtcNow - started).TotalSeconds
                : run.durationSec;
            var ordered = run.results.OrderBy(x => x.result == "Failed" ? 0 : x.result == "Inconclusive" ? 1 : x.result == "Skipped" ? 2 : 3);
            return JObject.FromObject(new
            {
                runId = run.runId,
                running = run.running,
                finished = run.finished,
                total = run.results.Count,
                passed = Count("Passed"),
                failed = Count("Failed"),
                skipped = Count("Skipped"),
                inconclusive = Count("Inconclusive"),
                expected = run.expected,
                durationSec = Math.Round(duration, 3),
                mode = run.mode,
                filter = run.filter,
                assembly = run.assembly,
                startedAtUtc = run.startedAtUtc,
                finishedAtUtc = run.finishedAtUtc,
                error = run.error,
                results = ordered.Select(x => new
                {
                    x.name, x.fullName, x.result, x.resultState, x.message, x.stack, durationSec = Math.Round(x.durationSec, 4),
                }).ToList(),
            }, Json.Serializer);
        }

        // ---- callbacks ---------------------------------------------------------------------------------------

        private sealed class Callbacks : IErrorCallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                if (s_Run == null || !s_Run.running) // started from the Test Runner window
                    s_Run = new RunState { runId = "external-" + DateTime.UtcNow.Ticks, running = true, startedAtUtc = Now(), mode = "unknown" };
                try { s_Run.expected = testsToRun?.TestCaseCount ?? 0; } catch { /* informative only */ }
            }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (s_Run == null || result == null || result.HasChildren) return;
                var row = ToRow(result);
                s_Run.results.RemoveAll(x => x.fullName == row.fullName);
                s_Run.results.Add(row);
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                if (s_Run == null) return;
                if (result != null)
                {
                    var leaves = new List<Row>();
                    Collect(result, leaves);
                    if (leaves.Count > 0 || s_Run.results.Count == 0) s_Run.results = leaves;
                    s_Run.durationSec = result.Duration;
                }
                Finish(null);
            }

            public void OnError(string message) => Finish(message ?? "test run error");
        }

        private static void Collect(ITestResultAdaptor node, List<Row> into)
        {
            if (!node.HasChildren) { into.Add(ToRow(node)); return; }
            foreach (var child in node.Children) Collect(child, into);
        }

        private static Row ToRow(ITestResultAdaptor r) => new Row
        {
            name = r.Name,
            fullName = r.FullName,
            result = r.TestStatus.ToString(),
            resultState = r.ResultState,
            message = Clip(r.Message, 2000),
            stack = r.TestStatus == TestStatus.Failed ? Clip(r.StackTrace, 1500) : null,
            durationSec = r.Duration,
        };

        private static string Clip(string s, int max) =>
            string.IsNullOrEmpty(s) ? null : s.Length <= max ? s.Trim() : s.Substring(0, max) + "…";

        private static void Finish(string error)
        {
            if (s_Run == null) return;
            s_Run.running = false;
            s_Run.finished = true;
            s_Run.error = error;
            s_Run.finishedAtUtc = Now();
            if (s_Run.durationSec <= 0 && DateTime.TryParse(s_Run.startedAtUtc, null,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var started))
                s_Run.durationSec = (DateTime.UtcNow - started).TotalSeconds;
            Save();
            WriteFile();
        }

        // ---- persistence -------------------------------------------------------------------------------------

        private static string LastFile => Path.Combine(AutoTestPaths.OutRoot, "unity-tests", "last.json");

        private static void Save()
        {
            try { SessionState.SetString(SessionKey, s_Run == null ? "" : JsonUtility.ToJson(s_Run)); }
            catch { /* best effort */ }
        }

        private static void WriteFile()
        {
            try
            {
                var summary = Summary(s_Run);
                summary.AddFirst(new JProperty("ok", true));
                Json.StripNulls(summary);
                AutoTestPaths.OutDir("unity-tests");
                File.WriteAllText(LastFile, summary.ToString(Formatting.Indented));
            }
            catch (Exception e) { Debug.LogWarning("[AutoTest] cannot write " + LastFile + ": " + e.Message); }
        }

        private static void Load()
        {
            try
            {
                string json = SessionState.GetString(SessionKey, "");
                if (!string.IsNullOrEmpty(json)) { s_Run = Normalize(JsonUtility.FromJson<RunState>(json)); return; }
                if (File.Exists(LastFile))
                {
                    s_Run = Normalize(JsonConvert.DeserializeObject<RunState>(File.ReadAllText(LastFile)));
                    if (s_Run != null) s_Run.running = false;
                }
            }
            catch { s_Run = null; }
        }

        /// <summary>JsonUtility turns null strings into "": undo that so replies stay compact.</summary>
        private static RunState Normalize(RunState run)
        {
            if (run == null) return null;
            static string N(string s) => string.IsNullOrEmpty(s) ? null : s;
            run.runId = N(run.runId); run.mode = N(run.mode); run.filter = N(run.filter); run.assembly = N(run.assembly);
            run.startedAtUtc = N(run.startedAtUtc); run.finishedAtUtc = N(run.finishedAtUtc); run.error = N(run.error);
            run.results ??= new List<Row>();
            foreach (var x in run.results)
            {
                x.message = N(x.message); x.stack = N(x.stack); x.resultState = N(x.resultState);
            }
            return run;
        }

        // ---- watchdog: a run that died without RunFinished (cancelled, crashed) must not stay "running" ------

        private static double s_NextCheck, s_MissingSince = -1;
        private static bool s_WatchdogBroken;

        private static void Watchdog()
        {
            var run = s_Run;
            if (run == null || !run.running || run.runId == null || run.runId.StartsWith("external-", StringComparison.Ordinal) || s_WatchdogBroken)
            {
                s_MissingSince = -1;
                return;
            }
            double now = EditorApplication.timeSinceStartup;
            if (now < s_NextCheck) return;
            s_NextCheck = now + 1.0;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || PlayModeTracker.Changing) { s_MissingSince = -1; return; }
            bool? alive = JobAlive(run.runId);
            if (alive == null) { s_WatchdogBroken = true; return; }
            if (alive.Value) { s_MissingSince = -1; return; }
            if (s_MissingSince < 0) { s_MissingSince = now; return; }
            if (now - s_MissingSince > 5.0)
            {
                s_MissingSince = -1;
                Finish("the run ended without a RunFinished callback (cancelled, or the runner failed)");
            }
        }

        /// <summary>Is the Test Runner still holding a running job with this id? null when its internals cannot be read.</summary>
        private static bool? JobAlive(string runId)
        {
            try
            {
                var holder = typeof(TestRunnerApi).Assembly.GetType("UnityEditor.TestTools.TestRunner.TestRun.TestJobDataHolder");
                var instance = holder?.GetProperty("instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null);
                if (!(holder?.GetField("TestRuns")?.GetValue(instance) is IList runs)) return null;
                foreach (var job in runs)
                {
                    var t = job.GetType();
                    if ((string)t.GetField("guid")?.GetValue(job) == runId) return (bool)(t.GetField("isRunning")?.GetValue(job) ?? false);
                }
                return false;
            }
            catch { return null; }
        }
    }
}
