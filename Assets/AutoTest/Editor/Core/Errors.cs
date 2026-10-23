using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace AutoTest
{
    /// <summary>An error that carries its wire status and kind (PROTOCOL.md §1). Adapter code may throw these too.</summary>
    public class AutoTestException : Exception
    {
        public int Status { get; }
        public string Kind { get; }

        public AutoTestException(int status, string kind, string message, Exception inner = null)
            : base(message, inner)
        {
            Status = status;
            Kind = kind;
        }
    }

    /// <summary>404 not_found: the object / member asked for does not exist.</summary>
    public sealed class NotFoundException : AutoTestException
    {
        public NotFoundException(string message) : base(404, "not_found", message) { }
    }

    public sealed class PlayModeRequiredException : AutoTestException
    {
        public PlayModeRequiredException(string message = "this route needs Play mode")
            : base(409, "play_mode_required", message) { }
    }

    public sealed class EditModeRequiredException : AutoTestException
    {
        public EditModeRequiredException(string message = "this route needs Edit mode (exit Play first)")
            : base(409, "edit_mode_required", message) { }
    }

    /// <summary>409 dirty_scene: the operation would raise Unity's modal "save modified scenes" prompt.</summary>
    public sealed class DirtySceneException : AutoTestException
    {
        public DirtySceneException(string message) : base(409, "dirty_scene", message) { }
    }

    /// <summary>Wraps an exception thrown by game adapter code so it becomes a 500 with the adapter's own stack.</summary>
    internal sealed class AdapterException : AutoTestException
    {
        public AdapterException(string where, Exception inner)
            : base(500, "error", where + " threw " + inner.GetType().Name + ": " + inner.Message, inner) { }
    }

    /// <summary>Marshals a main-thread job's exception back to the HTTP thread unchanged.</summary>
    internal sealed class MainThreadJobException : Exception
    {
        public MainThreadJobException(Exception inner) : base(inner.Message, inner) { }
    }

    internal static class Errors
    {
        public static Exception Unwrap(Exception e)
        {
            while (true)
            {
                switch (e)
                {
                    case MainThreadJobException { InnerException: { } inner }: e = inner; continue;
                    case TargetInvocationException { InnerException: { } inner }: e = inner; continue;
                    case TypeInitializationException { InnerException: { } inner }: e = inner; continue;
                    case AggregateException a when a.InnerExceptions.Count == 1: e = a.InnerExceptions[0]; continue;
                    default: return e;
                }
            }
        }

        public static (int status, string kind) Classify(Exception e) => e switch
        {
            AutoTestException a => (a.Status, a.Kind),
            ArgumentException => (400, "bad_request"),
            NotSupportedException => (501, "not_supported"),
            NotImplementedException => (501, "not_supported"),
            TimeoutException => (503, "timeout"),
            _ => (500, "error"),
        };

        public static JObject Reply(Exception e, int status, string kind)
        {
            var reply = new JObject { ["ok"] = false, ["error"] = e.Message, ["kind"] = kind };
            if (status == 500)
            {
                // AdapterException: the adapter's own frames are the useful ones.
                var source = e is AdapterException && e.InnerException != null ? e.InnerException : e;
                reply["exception"] = source.GetType().FullName;
                string stack = FirstLines(source.StackTrace, 12);
                if (!string.IsNullOrEmpty(stack)) reply["stack"] = stack;
            }
            return reply;
        }

        public static string FirstLines(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).Take(max);
            return string.Join("\n", lines);
        }
    }
}
