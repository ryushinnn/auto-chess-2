using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AutoTest
{
    /// <summary>Editor control: status, play/pause/step, time scale, refresh, menu, screenshot, logs.</summary>
    internal static class EditorEndpoints
    {
        // ---- off the main thread -----------------------------------------------------------------------------

        public static object Ping(Request r) => new
        {
            serverVersion = AutoTestServer.Version,
            port = AutoTestServer.Port,
            project = AutoTestPaths.ProjectName,
        };

        public static object Logs(Request r) =>
            LogBuffer.Query(r.Long("since", 0, 0), r.Str("level", "all"), r.Int("limit", 200, 1, 2000));

        public static object LogsClear(Request r) => new { nextIndex = LogBuffer.Clear() };

        public static object CompileStatusRoute(Request r) => CompileStatus.Snapshot();

        // ---- status ------------------------------------------------------------------------------------------

        public static object Status(Request r)
        {
            var active = SceneManager.GetActiveScene();
            return new
            {
                serverVersion = AutoTestServer.Version,
                project = AutoTestPaths.ProjectName,
                projectPath = AutoTestPaths.ProjectRoot,
                unityVersion = Application.unityVersion,
                isPlaying = EditorApplication.isPlaying,
                isPaused = EditorApplication.isPaused,
                isCompiling = EditorApplication.isCompiling,
                isUpdating = EditorApplication.isUpdating,
                playModeChanging = PlayModeTracker.Changing,
                timeScale = Time.timeScale,
                pendingTimeScale = EditorApplication.isPlaying ? null : PlayModeTracker.PendingTimeScale,
                frameCount = Time.frameCount,
                activeScene = string.IsNullOrEmpty(active.path) ? active.name : active.path,
                loadedScenes = SceneQuery.LoadedScenes().Select(s => string.IsNullOrEmpty(s.path) ? s.name : s.path).ToList(),
                lastCompileErrors = CompileStatus.LastErrorCount,
                scriptCompilationFailed = EditorUtility.scriptCompilationFailed,
                refreshPending = PlayModeTracker.RefreshPending,
                adapter = AdapterRegistry.Describe(),
                port = AutoTestServer.Port,
            };
        }

        // ---- refresh / play / pause / step / timescale -------------------------------------------------------

        public static object Refresh(Request r)
        {
            bool exitPlay = r.Bool("exitPlay", false);
            bool playing = EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode;
            if (playing && !exitPlay)
                throw new EditModeRequiredException("/refresh needs Edit mode: pass exitPlay=1 to leave Play first");
            if (playing) EditorApplication.isPlaying = false;
            // Runs on a later editor update (not inside this request), so a synchronous import/compile can never
            // turn this reply into a timeout. EditorApplication.update keeps ticking while Unity is unfocused.
            PlayModeTracker.RequestRefresh();
            return new
            {
                requested = true,
                exitingPlay = playing,
                previousCompileFinishedAtUtc = CompileStatus.LastFinishedAtUtc,
            };
        }

        public static object Play(Request r)
        {
            bool on = r.Bool("on", true);
            string scene = r.Str("scene");
            bool playing = EditorApplication.isPlaying;
            bool changed = playing != on;
            string note = null, openedScene = null;

            if (on && !playing)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    return new { isPlaying = false, requested = true, changed = false, note = "already entering Play" };
                if (scene != null) openedScene = OpenSceneForPlay(scene);
                if (EditorUtility.scriptCompilationFailed)
                    note = "scripts have compile errors: Unity will refuse to enter Play (see /compile-status)";
            }
            else if (on && scene != null)
                note = "already playing: scene ignored";

            // From here until the Exiting* event, isPlaying != isPlayingOrWillChangePlaymode reports playModeChanging;
            // an entry Unity refuses (compile errors) simply never starts, so nothing is marked by hand.
            if (changed) EditorApplication.isPlaying = on;
            return new { isPlaying = playing, requested = on, changed, scene = openedScene, note };
        }

        /// <summary>Opens `scene` (asset path, or a unique scene name) unless it is already the only loaded scene.</summary>
        private static string OpenSceneForPlay(string scene)
        {
            string path = ResolveScenePath(scene);
            bool alreadyOpen = SceneManager.sceneCount == 1 &&
                               string.Equals(SceneManager.GetSceneAt(0).path, path, StringComparison.OrdinalIgnoreCase);
            if (alreadyOpen) return path;
            var dirty = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Where(s => s.isDirty).ToList();
            if (dirty.Count > 0)
                throw new DirtySceneException("cannot open " + path + ": unsaved changes in " +
                                              string.Join(", ", dirty.Select(s => string.IsNullOrEmpty(s.path) ? s.name : s.path)) +
                                              " (save or discard them first; Unity would block on a save prompt)");
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            return path;
        }

        private static string ResolveScenePath(string scene)
        {
            scene = scene.Trim().Replace('\\', '/');
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scene) != null) return scene;
            if (!scene.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) &&
                AssetDatabase.LoadAssetAtPath<SceneAsset>(scene + ".unity") != null)
                return scene + ".unity";
            string name = Path.GetFileNameWithoutExtension(scene);
            var hits = AssetDatabase.FindAssets("t:Scene " + name)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => string.Equals(Path.GetFileNameWithoutExtension(p), name, StringComparison.OrdinalIgnoreCase)
                            && p.StartsWith("Assets/", StringComparison.Ordinal))
                .ToList();
            if (hits.Count == 1) return hits[0];
            if (hits.Count > 1) throw new ArgumentException("scene name '" + name + "' is ambiguous: " + string.Join(", ", hits));
            throw new NotFoundException("no scene asset '" + scene + "'");
        }

        public static object Pause(Request r)
        {
            EditorApplication.isPaused = r.Bool("on", true);
            return new { isPaused = EditorApplication.isPaused };
        }

        public static object Step(Request r)
        {
            EditorApplication.Step(); // pauses if needed; the frame advances on the next player loop
            return new { frameCount = Time.frameCount, isPaused = EditorApplication.isPaused };
        }

        public static object TimeScale(Request r)
        {
            bool playing = EditorApplication.isPlaying;
            if (r.Has("value"))
            {
                float v = r.RequireFloat("value");
                if (v < 0f || v > 100f) throw new ArgumentException("value must be in 0..100, got " + v);
                if (playing)
                {
                    Time.timeScale = v;
                    PlayModeTracker.PendingTimeScale = null;
                }
                else PlayModeTracker.PendingTimeScale = v;
            }
            var pending = playing ? null : PlayModeTracker.PendingTimeScale;
            return new { timeScale = pending ?? Time.timeScale, appliesOnPlay = pending != null ? true : (bool?)null };
        }

        // ---- menu / screenshot -------------------------------------------------------------------------------

        public static object Menu(Request r)
        {
            string item = r.RequireStr("item").Trim();
            bool executed = EditorApplication.ExecuteMenuItem(item);
            return new { executed, item, message = executed ? null : "no such menu item, or it is disabled" };
        }

        public static object Screenshot(Request r)
        {
            int superSize = r.Int("superSize", 1, 1, 4);
            string stem = r.Str("name");
            stem = string.IsNullOrWhiteSpace(stem) ? "shot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") : SanitizeFileName(stem);
            if (stem.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) stem = stem.Substring(0, stem.Length - 4);
            string file = Path.Combine(AutoTestPaths.OutDir("screens"), stem + ".png");
            // The client waits for the file to exist: a leftover from an earlier capture must not count.
            if (File.Exists(file))
            {
                try { File.Delete(file); }
                catch (Exception e) { throw new IOException("cannot overwrite " + file + " (open in a viewer?): " + e.Message); }
            }
            ScreenCapture.CaptureScreenshot(file, superSize);
            return new
            {
                path = file, pending = true, superSize,
                note = EditorApplication.isPaused ? "paused: the file is written when the Game view renders the next frame (/step)" : null,
            };
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.Trim().Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray();
            string s = new string(chars).Trim('.', ' ');
            return s.Length == 0 ? "shot" : s;
        }
    }
}
