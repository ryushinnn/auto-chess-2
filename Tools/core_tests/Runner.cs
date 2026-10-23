// Minimal NUnit-compatible test runner for the engine-free core (no Unity, no NuGet).
// Discovers public fixtures by attribute *names* (Test, TestCase, SetUp, TearDown, OneTimeSetUp,
// OneTimeTearDown, Ignore) so it works with Unity's custom nunit.framework build.
// Usage: dotnet Runner.dll <tests.dll> [--filter <substring>] [--json <out.json>]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

static class Program {
    sealed class Result {
        public string Name;
        public string Outcome; // Passed, Failed, Skipped
        public string Message;
        public double Seconds;
    }

    static int Main(string[] args) {
        if (args.Length == 0) {
            Console.Error.WriteLine("usage: Runner <tests.dll> [--filter text] [--json out.json]");
            return 2;
        }
        var dllPath = Path.GetFullPath(args[0]);
        string filter = null, jsonOut = null;
        for (var i = 1; i < args.Length - 1; i++) {
            if (args[i] == "--filter") filter = args[i + 1];
            if (args[i] == "--json") jsonOut = args[i + 1];
        }

        var dir = Path.GetDirectoryName(dllPath);
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) => {
            var candidate = Path.Combine(dir, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        };

        var assembly = Assembly.LoadFrom(dllPath);
        var results = new List<Result>();
        var total = Stopwatch.StartNew();
        foreach (var type in assembly.GetTypes().Where(t => t.IsClass && t.IsPublic && !t.IsAbstract).OrderBy(t => t.FullName)) {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            var tests = methods.Where(m => Has(m, "TestAttribute") || Has(m, "TestCaseAttribute")).ToList();
            if (tests.Count == 0) continue;
            RunFixture(type, methods, tests, filter, results);
        }
        total.Stop();

        var failed = results.Where(r => r.Outcome == "Failed").ToList();
        var skipped = results.Count(r => r.Outcome == "Skipped");
        foreach (var r in results.Where(r => r.Outcome == "Skipped")) Console.WriteLine($"  SKIP {r.Name}: {r.Message}");
        foreach (var r in failed) {
            Console.WriteLine($"  FAIL {r.Name}");
            foreach (var line in (r.Message ?? "").Split('\n').Take(12)) Console.WriteLine("       " + line.TrimEnd());
        }
        Console.WriteLine($"[core_tests] {results.Count} tests: {results.Count - failed.Count - skipped} passed, {failed.Count} failed, " +
                          $"{skipped} skipped in {total.Elapsed.TotalSeconds:0.00}s");

        if (jsonOut != null) File.WriteAllText(jsonOut, ToJson(results, total.Elapsed.TotalSeconds));
        return failed.Count == 0 ? 0 : 1;
    }

    static bool Has(MemberInfo m, string attributeName) =>
        m.GetCustomAttributes(true).Any(a => a.GetType().Name == attributeName);

    static IEnumerable<object> Attributes(MemberInfo m, string name) =>
        m.GetCustomAttributes(true).Where(a => a.GetType().Name == name);

    static void RunFixture(Type type, MethodInfo[] methods, List<MethodInfo> tests, string filter, List<Result> results) {
        var fixtureIgnore = Attributes(type, "IgnoreAttribute").FirstOrDefault();
        object instance = null;
        var setUp = methods.Where(m => Has(m, "SetUpAttribute")).ToList();
        var tearDown = methods.Where(m => Has(m, "TearDownAttribute")).ToList();
        var oneTimeSetUp = methods.Where(m => Has(m, "OneTimeSetUpAttribute")).ToList();
        var oneTimeTearDown = methods.Where(m => Has(m, "OneTimeTearDownAttribute")).ToList();

        var cases = new List<(MethodInfo method, object[] args, string name)>();
        foreach (var m in tests.OrderBy(m => m.Name)) {
            var testCases = Attributes(m, "TestCaseAttribute").ToList();
            if (testCases.Count == 0) {
                cases.Add((m, Array.Empty<object>(), $"{type.FullName}.{m.Name}"));
                continue;
            }
            foreach (var tc in testCases) {
                var arguments = (object[])tc.GetType().GetProperty("Arguments")?.GetValue(tc) ?? Array.Empty<object>();
                cases.Add((m, arguments, $"{type.FullName}.{m.Name}({string.Join(",", arguments.Select(a => a ?? "null"))})"));
            }
        }
        cases = cases.Where(c => filter == null || c.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        if (cases.Count == 0) return;

        if (fixtureIgnore != null) {
            foreach (var c in cases) results.Add(new Result { Name = c.name, Outcome = "Skipped", Message = "fixture ignored" });
            return;
        }

        try {
            if (!type.IsAbstract || !type.IsSealed) instance = Activator.CreateInstance(type);
            foreach (var m in oneTimeSetUp) m.Invoke(m.IsStatic ? null : instance, null);
        }
        catch (Exception e) {
            foreach (var c in cases) results.Add(new Result { Name = c.name, Outcome = "Failed", Message = "OneTimeSetUp: " + Describe(e) });
            return;
        }

        foreach (var (method, arguments, name) in cases) {
            var ignore = Attributes(method, "IgnoreAttribute").FirstOrDefault();
            if (ignore != null) {
                results.Add(new Result { Name = name, Outcome = "Skipped", Message = ignore.GetType().GetProperty("Reason")?.GetValue(ignore) as string });
                continue;
            }
            var sw = Stopwatch.StartNew();
            var result = new Result { Name = name, Outcome = "Passed" };
            try {
                foreach (var m in setUp) m.Invoke(m.IsStatic ? null : instance, null);
                method.Invoke(method.IsStatic ? null : instance, Convert(method, arguments));
            }
            catch (Exception e) {
                Classify(e, result);
            }
            finally {
                try {
                    foreach (var m in tearDown) m.Invoke(m.IsStatic ? null : instance, null);
                }
                catch (Exception e) {
                    if (result.Outcome == "Passed") Classify(e, result);
                }
            }
            sw.Stop();
            result.Seconds = sw.Elapsed.TotalSeconds;
            results.Add(result);
        }

        try {
            foreach (var m in oneTimeTearDown) m.Invoke(m.IsStatic ? null : instance, null);
        }
        catch {
            // ignored: teardown problems do not change test outcomes
        }
    }

    static object[] Convert(MethodInfo method, object[] arguments) {
        var parameters = method.GetParameters();
        if (parameters.Length != arguments.Length) return arguments;
        var converted = new object[arguments.Length];
        for (var i = 0; i < arguments.Length; i++) {
            var target = parameters[i].ParameterType;
            var a = arguments[i];
            converted[i] = a == null || target.IsInstanceOfType(a) ? a
                : target.IsEnum ? Enum.ToObject(target, a)
                : System.Convert.ChangeType(a, target, System.Globalization.CultureInfo.InvariantCulture);
        }
        return converted;
    }

    static void Classify(Exception e, Result result) {
        while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
        var typeName = e.GetType().Name;
        if (typeName == "SuccessException") return;
        if (typeName == "IgnoreException" || typeName == "InconclusiveException") {
            result.Outcome = "Skipped";
            result.Message = e.Message.Trim();
            return;
        }
        result.Outcome = "Failed";
        result.Message = Describe(e);
    }

    static string Describe(Exception e) {
        while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
        var prefix = e.GetType().Name == "AssertionException" ? "" : e.GetType().Name + ": ";
        var stack = string.Join("\n", (e.StackTrace ?? "").Split('\n')
            .Where(l => !l.Contains("NUnit.Framework") && !l.Contains("System.Reflection") && !l.Contains("System.RuntimeMethodHandle"))
            .Take(6).Select(l => l.Trim()));
        return prefix + e.Message.Trim() + "\n" + stack;
    }

    static string ToJson(List<Result> results, double seconds) {
        var sb = new StringBuilder();
        sb.Append("{\"seconds\":").Append(seconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(",\"total\":").Append(results.Count);
        sb.Append(",\"passed\":").Append(results.Count(r => r.Outcome == "Passed"));
        sb.Append(",\"failed\":").Append(results.Count(r => r.Outcome == "Failed"));
        sb.Append(",\"skipped\":").Append(results.Count(r => r.Outcome == "Skipped"));
        sb.Append(",\"results\":[");
        for (var i = 0; i < results.Count; i++) {
            var r = results[i];
            if (i > 0) sb.Append(',');
            sb.Append("{\"name\":").Append(Quote(r.Name)).Append(",\"outcome\":").Append(Quote(r.Outcome))
                .Append(",\"seconds\":").Append(r.Seconds.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
            if (r.Message != null) sb.Append(",\"message\":").Append(Quote(r.Message));
            sb.Append('}');
        }
        sb.Append("]}");
        return sb.ToString();
    }

    static string Quote(string s) {
        var sb = new StringBuilder("\"");
        foreach (var c in s) {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\r') continue;
            else if (c < 0x20) sb.Append(' ');
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }
}
