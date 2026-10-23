using System.IO;
using UnityEditor;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// Where the bridge writes files the Python toolkit reads back: `<project>/AutoTest Server/auto_touch/out/`.
    /// Anchored on Application.dataPath (resolved once on the main thread), never on the process working directory.
    /// </summary>
    [InitializeOnLoad]
    public static class AutoTestPaths
    {
        public static readonly string ProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        public static string ProjectName => Path.GetFileName(ProjectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        public static string OutRoot => Path.Combine(ProjectRoot, "AutoTest Server", "auto_touch", "out");

        static AutoTestPaths() { }

        internal static void Touch() { }

        /// <summary>`out/<sub>`, created on demand.</summary>
        public static string OutDir(string sub)
        {
            string dir = string.IsNullOrEmpty(sub) ? OutRoot : Path.Combine(OutRoot, sub);
            Directory.CreateDirectory(dir);
            return Path.GetFullPath(dir);
        }
    }
}
