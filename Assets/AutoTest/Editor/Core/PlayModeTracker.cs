using System;
using System.Globalization;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// Play-mode lifecycle state that must outlive the domain reload sitting in the middle of every transition:
    /// `playModeChanging` (ExitingEditMode..EnteredPlayMode, ExitingPlayMode..EnteredEditMode), the time scale
    /// requested while in edit mode, and a /refresh deferred until play mode has been left. All in SessionState.
    /// </summary>
    [InitializeOnLoad]
    internal static class PlayModeTracker
    {
        private const string KeyTransition = "AutoTest.PlayTransition";     // 0 none, 1 entering, 2 exiting
        private const string KeyTransitionAt = "AutoTest.PlayTransitionAt"; // UTC ticks
        private const string KeyTimeScale = "AutoTest.PendingTimeScale";
        private const string KeyRefresh = "AutoTest.PendingRefresh";
        private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

        static PlayModeTracker()
        {
            EditorApplication.playModeStateChanged += OnChanged;
            EditorApplication.update += Tick;
        }

        public static void Touch() { }

        private static void OnChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.ExitingEditMode: Mark(1); break;
                case PlayModeStateChange.ExitingPlayMode: Mark(2); break;
                case PlayModeStateChange.EnteredPlayMode:
                    Mark(0);
                    ApplyPendingTimeScale();
                    break;
                case PlayModeStateChange.EnteredEditMode: Mark(0); break;
            }
        }

        private static void Mark(int transition)
        {
            SessionState.SetInt(KeyTransition, transition);
            SessionState.SetString(KeyTransitionAt, DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>True between "a play-mode change was requested / started" and the matching Entered* event.</summary>
        public static bool Changing
        {
            get
            {
                if (EditorApplication.isPlaying != EditorApplication.isPlayingOrWillChangePlaymode) return true;
                if (SessionState.GetInt(KeyTransition, 0) == 0) return false;
                // Unity can abandon a transition without an Entered* event (entry cancelled): expire it once stable.
                long.TryParse(SessionState.GetString(KeyTransitionAt, "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks);
                if (DateTime.UtcNow - new DateTime(Math.Max(0, ticks), DateTimeKind.Utc) > StaleAfter)
                {
                    Mark(0);
                    return false;
                }
                return true;
            }
        }

        // ---- time scale requested in edit mode --------------------------------------------------------------

        /// <summary>
        /// Setting Time.timeScale in edit mode writes the TimeManager project setting, so a value set outside Play
        /// is parked here and applied on the next EnteredPlayMode instead.
        /// </summary>
        public static float? PendingTimeScale
        {
            get
            {
                string s = SessionState.GetString(KeyTimeScale, "");
                return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : (float?)null;
            }
            set => SessionState.SetString(KeyTimeScale, value?.ToString("R", CultureInfo.InvariantCulture) ?? "");
        }

        private static void ApplyPendingTimeScale()
        {
            var pending = PendingTimeScale;
            if (pending == null) return;
            Time.timeScale = pending.Value;
            PendingTimeScale = null;
        }

        // ---- deferred /refresh ------------------------------------------------------------------------------

        /// <summary>Runs AssetDatabase.Refresh + RequestScriptCompilation on a later editor update, once in edit mode.</summary>
        public static void RequestRefresh() => SessionState.SetBool(KeyRefresh, true);

        public static bool RefreshPending => SessionState.GetBool(KeyRefresh, false);

        private static void Tick()
        {
            if (!SessionState.GetBool(KeyRefresh, false)) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            SessionState.SetBool(KeyRefresh, false);
            try
            {
                // ForceUpdate + an explicit compile request: neither depends on the editor having OS focus.
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                CompilationPipeline.RequestScriptCompilation();
            }
            catch (Exception e)
            {
                Debug.LogError("[AutoTest] refresh failed: " + e);
            }
        }
    }
}
