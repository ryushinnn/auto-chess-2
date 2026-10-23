using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEditor;

namespace AutoTest
{
    /// <summary>
    /// Marshals work from HttpListener thread-pool threads onto the editor main thread. Jobs are pumped from
    /// EditorApplication.update (ticks in edit mode, play mode, pause, and while the editor is unfocused; not during
    /// a synchronous import/compile, hence the caller-side timeout). A job whose caller already timed out is
    /// abandoned and never runs late: a stale /ui/click executing after a freeze would replay a game move.
    /// Jobs must never wait for frames; multi-frame work is an update state machine the client polls.
    /// </summary>
    [InitializeOnLoad]
    internal static class MainThread
    {
        private const int Pending = 0, Taken = 1, Abandoned = 2;

        private sealed class Job
        {
            public Func<object> Fn;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
            public object Result;
            public Exception Error;
            public int State;
        }

        private static readonly ConcurrentQueue<Job> s_Queue = new ConcurrentQueue<Job>();
        private static readonly int s_MainThreadId;

        static MainThread()
        {
            s_MainThreadId = Thread.CurrentThread.ManagedThreadId;
            EditorApplication.update += Pump;
        }

        /// <summary>Forces the static constructor to run (on the calling thread, which must be the main thread).</summary>
        public static void Touch() { }

        public static bool IsMainThread => Thread.CurrentThread.ManagedThreadId == s_MainThreadId;

        public static T Run<T>(Func<T> fn, int timeoutMs)
        {
            if (IsMainThread) return fn();

            var job = new Job { Fn = () => fn() };
            s_Queue.Enqueue(job);
            if (!job.Done.Wait(timeoutMs))
            {
                if (Interlocked.CompareExchange(ref job.State, Abandoned, Pending) == Pending)
                    throw new TimeoutException("the editor main thread did not run the job within " + timeoutMs +
                                               " ms (compiling, importing or a modal dialog?); the job was abandoned");
                // The pump claimed it right at the deadline: it is running now, so report its real outcome.
                if (!job.Done.Wait(Math.Max(2000, timeoutMs)))
                    throw new TimeoutException("the main-thread job overran its " + timeoutMs + " ms budget");
            }
            if (job.Error != null) throw new MainThreadJobException(job.Error);
            return (T)job.Result;
        }

        private static void Pump()
        {
            while (s_Queue.TryDequeue(out var job))
            {
                if (Interlocked.CompareExchange(ref job.State, Taken, Pending) != Pending)
                {
                    job.Done.Set(); // abandoned by its caller: skip
                    continue;
                }
                try { job.Result = job.Fn(); }
                catch (Exception e) { job.Error = e; }
                finally { job.Done.Set(); }
            }
        }
    }
}
