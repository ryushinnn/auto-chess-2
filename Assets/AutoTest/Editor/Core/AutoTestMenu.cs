using System;
using UnityEditor;
using UnityEngine;

namespace AutoTest
{
    internal static class AutoTestMenu
    {
        private const string Root = "Tools/AutoTest/";

        [MenuItem(Root + "Start Server", false, 1)]
        private static void Start() => AutoTestServer.StartServer();

        [MenuItem(Root + "Start Server", true)]
        private static bool StartValidate() => !AutoTestServer.IsRunning;

        [MenuItem(Root + "Stop Server", false, 2)]
        private static void Stop() => AutoTestServer.StopServer();

        [MenuItem(Root + "Stop Server", true)]
        private static bool StopValidate() => AutoTestServer.IsRunning;

        [MenuItem(Root + "Autostart Server", false, 20)]
        private static void ToggleAutostart()
        {
            bool on = !EditorPrefs.GetBool(AutoTestServer.AutoStartKey, true);
            EditorPrefs.SetBool(AutoTestServer.AutoStartKey, on);
            Debug.Log("[AutoTest] autostart " + (on ? "enabled" : "disabled"));
        }

        [MenuItem(Root + "Autostart Server", true)]
        private static bool ToggleAutostartValidate()
        {
            Menu.SetChecked(Root + "Autostart Server", EditorPrefs.GetBool(AutoTestServer.AutoStartKey, true));
            return true;
        }

        [MenuItem(Root + "Open Output Folder", false, 40)]
        private static void OpenOutputFolder()
        {
            string dir = AutoTestPaths.OutDir(null);
            Application.OpenURL(new Uri(dir).AbsoluteUri);
        }
    }
}
