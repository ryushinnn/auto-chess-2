using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// Compiler results for /compile-status (served without the main thread). Compiler messages never reach the
    /// console log callback, so this is the only remote view of a broken build. A failed compile keeps this domain
    /// (statics survive); a successful one reloads it, so the last result is parked in SessionState and restored.
    /// </summary>
    [InitializeOnLoad]
    internal static class CompileStatus
    {
        private const string SessionKey = "AutoTest.CompileStatus";
        private const int MaxErrors = 50;

        [Serializable]
        private sealed class ErrorEntry
        {
            public string assembly, file, message;
            public int line, column;
        }

        [Serializable]
        private sealed class Result
        {
            public string startedAtUtc, finishedAtUtc;
            public int errorCount, warningCount;
            public List<ErrorEntry> errors = new List<ErrorEntry>();
        }

        private static readonly object s_Lock = new object();
        private static bool s_Compiling;
        private static Result s_Last = new Result();
        private static Result s_Current;

        static CompileStatus()
        {
            try
            {
                string json = SessionState.GetString(SessionKey, "");
                if (!string.IsNullOrEmpty(json)) s_Last = JsonUtility.FromJson<Result>(json) ?? new Result();
            }
            catch { s_Last = new Result(); }

            CompilationPipeline.compilationStarted += OnStarted;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyFinished;
            CompilationPipeline.compilationFinished += OnFinished;
        }

        public static void Touch() { }

        public static int LastErrorCount { get { lock (s_Lock) return s_Last.errorCount; } }

        public static string LastFinishedAtUtc { get { lock (s_Lock) return NullIfEmpty(s_Last.finishedAtUtc); } }

        private static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

        private static void OnStarted(object _)
        {
            lock (s_Lock)
            {
                s_Compiling = true;
                s_Current = new Result { startedAtUtc = Now() };
            }
        }

        private static void OnAssemblyFinished(string assemblyPath, CompilerMessage[] messages)
        {
            lock (s_Lock)
            {
                s_Current ??= new Result { startedAtUtc = Now() };
                string asm = Path.GetFileNameWithoutExtension(assemblyPath);
                foreach (var m in messages ?? Array.Empty<CompilerMessage>())
                {
                    if (m.type == CompilerMessageType.Warning) { s_Current.warningCount++; continue; }
                    if (m.type != CompilerMessageType.Error) continue;
                    s_Current.errorCount++;
                    if (s_Current.errors.Count < MaxErrors)
                        s_Current.errors.Add(new ErrorEntry
                        {
                            assembly = asm, file = m.file, line = m.line, column = m.column, message = m.message,
                        });
                }
            }
        }

        private static void OnFinished(object _)
        {
            string json;
            lock (s_Lock)
            {
                s_Compiling = false;
                s_Last = s_Current ?? new Result { startedAtUtc = Now() };
                s_Last.finishedAtUtc = Now();
                s_Current = null;
                json = JsonUtility.ToJson(s_Last);
            }
            try { SessionState.SetString(SessionKey, json); } // pipeline events arrive on the main thread
            catch { /* best effort */ }
        }

        /// <summary>Locked statics only: safe on any thread, answers while game assemblies are broken.</summary>
        public static object Snapshot()
        {
            lock (s_Lock)
            {
                var last = s_Last;
                return new
                {
                    isCompiling = s_Compiling,
                    lastCompile = new
                    {
                        startedAtUtc = NullIfEmpty(last.startedAtUtc),
                        finishedAtUtc = NullIfEmpty(last.finishedAtUtc),
                        last.errorCount,
                        last.warningCount,
                        errors = last.errors.Select(e => new
                        {
                            e.assembly, e.file, e.line, e.column, e.message,
                        }).ToList(),
                        truncated = last.errorCount > last.errors.Count,
                    },
                };
            }
        }

        private static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;
    }
}
