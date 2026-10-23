using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// Console messages (Application.logMessageReceivedThreaded, so worker-thread exceptions too) in a ring of
    /// 2000 entries with a monotonic index. Served by /logs without the main thread, so it answers while the editor
    /// is stalled. The index survives domain reloads (entering Play), and the newest entries are carried over, so a
    /// client cursor (`since`) stays valid across a Play transition.
    /// </summary>
    [InitializeOnLoad]
    internal static class LogBuffer
    {
        private const int Capacity = 2000, MaxMessage = 2000, MaxStack = 1500, CarryOver = 300;
        private const string SessionKey = "AutoTest.LogBuffer";

        [Serializable]
        private sealed class Entry
        {
            public long index;
            public string time, type, message, stackTrace;
        }

        [Serializable]
        private sealed class Snapshot
        {
            public long nextIndex, evictedUpTo;
            public List<Entry> entries = new List<Entry>();
        }

        private static readonly object s_Lock = new object();
        private static readonly Entry[] s_Ring = new Entry[Capacity];
        private static long s_Next;        // index the next entry gets
        private static long s_Oldest;      // smallest index still held
        private static long s_EvictedUpTo; // entries below this index were lost to overflow (not to /logs/clear)

        static LogBuffer()
        {
            Restore();
            Application.logMessageReceivedThreaded -= OnLog;
            Application.logMessageReceivedThreaded += OnLog;
            AssemblyReloadEvents.beforeAssemblyReload += Persist;
        }

        public static void Touch() { }

        public static long NextIndex { get { lock (s_Lock) return s_Next; } }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            var e = new Entry
            {
                time = DateTime.Now.ToString("HH:mm:ss.fff"),
                type = type.ToString(),
                message = Clip(message, MaxMessage),
                // Debug.Log stacks are noise on a polled route; warnings and errors keep theirs.
                stackTrace = type == LogType.Log ? null : Clip(stackTrace, MaxStack),
            };
            lock (s_Lock) Add(e);
        }

        private static void Add(Entry e)
        {
            e.index = s_Next;
            s_Ring[s_Next % Capacity] = e;
            s_Next++;
            if (s_Next - s_Oldest > Capacity)
            {
                s_Oldest = s_Next - Capacity;
                s_EvictedUpTo = s_Oldest;
            }
        }

        private static string Clip(string s, int max) =>
            string.IsNullOrEmpty(s) ? null : s.Length <= max ? s : s.Substring(0, max) + "…";

        private static bool IsRed(string type) => type == "Error" || type == "Exception" || type == "Assert";

        /// <summary>Entries with index >= since, oldest first, at most `limit`; nextIndex is the cursor to pass back.</summary>
        public static object Query(long since, string level, int limit)
        {
            Func<string, bool> match = (level ?? "all").Trim().ToLowerInvariant() switch
            {
                "" or "all" => _ => true,
                "warning" or "warnings" => t => IsRed(t) || t == "Warning",
                "error" or "errors" => IsRed,
                _ => throw new ArgumentException("level must be error, warning or all"),
            };
            if (since < 0) since = 0;
            lock (s_Lock)
            {
                var entries = new List<Entry>();
                int red = 0;
                long next = s_Next;
                bool truncated = false;
                for (long i = Math.Max(since, s_Oldest); i < s_Next; i++)
                {
                    var e = s_Ring[i % Capacity];
                    if (e == null || !match(e.type)) continue;
                    if (entries.Count >= limit) { truncated = true; next = i; break; }
                    entries.Add(e);
                    if (IsRed(e.type)) red++;
                }
                return new
                {
                    nextIndex = next,
                    dropped = Math.Max(0, s_EvictedUpTo - since), // entries of the asked range that are gone
                    redCount = red,
                    truncated,
                    entries,
                };
            }
        }

        public static long Clear()
        {
            lock (s_Lock)
            {
                Array.Clear(s_Ring, 0, Capacity);
                s_Oldest = s_Next;
                return s_Next;
            }
        }

        // ---- domain reload carry-over (main thread: SessionState) --------------------------------------------

        private static void Persist()
        {
            try
            {
                var snap = new Snapshot();
                lock (s_Lock)
                {
                    long from = Math.Max(s_Oldest, s_Next - CarryOver);
                    snap.nextIndex = s_Next;
                    snap.evictedUpTo = from > s_Oldest ? from : s_EvictedUpTo;
                    for (long i = from; i < s_Next; i++)
                        if (s_Ring[i % Capacity] != null) snap.entries.Add(s_Ring[i % Capacity]);
                }
                SessionState.SetString(SessionKey, JsonUtility.ToJson(snap));
            }
            catch { /* best effort: losing the carry-over only resets nothing but history */ }
        }

        private static void Restore()
        {
            try
            {
                string json = SessionState.GetString(SessionKey, "");
                if (string.IsNullOrEmpty(json)) return;
                var snap = JsonUtility.FromJson<Snapshot>(json);
                if (snap == null) return;
                lock (s_Lock)
                {
                    s_Next = snap.nextIndex;
                    s_Oldest = snap.entries.Count > 0 ? snap.entries[0].index : s_Next;
                    s_EvictedUpTo = snap.evictedUpTo;
                    foreach (var e in snap.entries)
                    {
                        if (string.IsNullOrEmpty(e.stackTrace)) e.stackTrace = null; // JsonUtility writes "" for null
                        if (string.IsNullOrEmpty(e.message)) e.message = null;
                        if (e.index >= s_Oldest && e.index < s_Next) s_Ring[e.index % Capacity] = e;
                    }
                }
            }
            catch { /* corrupt snapshot: start fresh */ }
        }
    }
}
