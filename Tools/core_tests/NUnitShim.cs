// Source-compatible subset of NUnit 3 for running the core tests on plain .NET (Unity's custom NUnit build
// depends on .NET Framework remoting and cannot load on .NET 6). Only what the test suite uses; extend it
// when a test needs more. Inside Unity the real NUnit is used.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NUnit.Framework {
    [AttributeUsage(AttributeTargets.Method)] public sealed class TestAttribute : Attribute { public string Description { get; set; } }
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class TestCaseAttribute : Attribute {
        public TestCaseAttribute(params object[] arguments) { Arguments = arguments ?? new object[] { null }; }
        public object[] Arguments { get; }
        public string TestName { get; set; }
    }
    [AttributeUsage(AttributeTargets.Method)] public sealed class SetUpAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class TearDownAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class OneTimeSetUpAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class OneTimeTearDownAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public sealed class TestFixtureAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class CategoryAttribute : Attribute { public CategoryAttribute(string name) { Name = name; } public string Name { get; } }
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class IgnoreAttribute : Attribute { public IgnoreAttribute(string reason) { Reason = reason; } public string Reason { get; } }
    [AttributeUsage(AttributeTargets.Method)] public sealed class TimeoutAttribute : Attribute { public TimeoutAttribute(int ms) { } }

    public class AssertionException : Exception { public AssertionException(string message) : base(message) { } }
    public class IgnoreException : Exception { public IgnoreException(string message) : base(message) { } }
    public class InconclusiveException : Exception { public InconclusiveException(string message) : base(message) { } }
    public class SuccessException : Exception { public SuccessException(string message) : base(message) { } }

    public delegate void TestDelegate();

    public static class Assert {
        static void Fail(string what, string message) =>
            throw new AssertionException(string.IsNullOrEmpty(message) ? what : message + "\n" + what);

        internal static string Show(object o) => o switch {
            null => "null",
            string s => "\"" + s + "\"",
            float f => f.ToString("R", CultureInfo.InvariantCulture),
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            IEnumerable e => "[" + string.Join(", ", e.Cast<object>().Select(Show)) + "]",
            _ => Convert.ToString(o, CultureInfo.InvariantCulture)
        };

        internal static bool Same(object expected, object actual) {
            if (expected == null || actual == null) return expected == null && actual == null;
            if (IsNumeric(expected) && IsNumeric(actual)) {
                if (expected is float || expected is double || actual is float || actual is double) {
                    return Convert.ToDouble(expected, CultureInfo.InvariantCulture).Equals(Convert.ToDouble(actual, CultureInfo.InvariantCulture))
                           || (expected is float fe && actual is float fa && fe.Equals(fa));
                }
                return Convert.ToDecimal(expected, CultureInfo.InvariantCulture) == Convert.ToDecimal(actual, CultureInfo.InvariantCulture);
            }
            if (!(expected is string) && expected is IEnumerable ea && actual is IEnumerable aa) {
                var x = ea.Cast<object>().ToList();
                var y = aa.Cast<object>().ToList();
                return x.Count == y.Count && x.Zip(y, Same).All(b => b);
            }
            return expected.Equals(actual);
        }

        internal static bool IsNumeric(object o) => o is sbyte || o is byte || o is short || o is ushort || o is int || o is uint
                                                    || o is long || o is ulong || o is float || o is double || o is decimal;

        internal static int Compare(object a, object b) {
            if (IsNumeric(a) && IsNumeric(b)) return Convert.ToDouble(a, CultureInfo.InvariantCulture).CompareTo(Convert.ToDouble(b, CultureInfo.InvariantCulture));
            return ((IComparable)a).CompareTo(b);
        }

        public static void AreEqual(object expected, object actual, string message = null, params object[] args) {
            if (!Same(expected, actual)) Fail($"Expected: {Show(expected)}\n  But was:  {Show(actual)}", Format(message, args));
        }

        public static void AreEqual(double expected, double actual, double delta, string message = null, params object[] args) {
            if (!(Math.Abs(expected - actual) <= delta) && !expected.Equals(actual)) {
                Fail($"Expected: {Show(expected)} +/- {Show(delta)}\n  But was:  {Show(actual)}", Format(message, args));
            }
        }

        public static void AreNotEqual(object expected, object actual, string message = null, params object[] args) {
            if (Same(expected, actual)) Fail($"Expected: not {Show(expected)}\n  But was:  {Show(actual)}", Format(message, args));
        }

        public static void AreSame(object expected, object actual, string message = null, params object[] args) {
            if (!ReferenceEquals(expected, actual)) Fail($"Expected same object: {Show(expected)}\n  But was:  {Show(actual)}", Format(message, args));
        }

        public static void AreNotSame(object expected, object actual, string message = null, params object[] args) {
            if (ReferenceEquals(expected, actual)) Fail("Expected different objects", Format(message, args));
        }

        public static void IsTrue(bool condition, string message = null, params object[] args) {
            if (!condition) Fail("Expected: True\n  But was:  False", Format(message, args));
        }

        public static void True(bool condition, string message = null, params object[] args) => IsTrue(condition, message, args);

        public static void IsFalse(bool condition, string message = null, params object[] args) {
            if (condition) Fail("Expected: False\n  But was:  True", Format(message, args));
        }

        public static void False(bool condition, string message = null, params object[] args) => IsFalse(condition, message, args);

        public static void IsNull(object o, string message = null, params object[] args) {
            if (o != null) Fail($"Expected: null\n  But was:  {Show(o)}", Format(message, args));
        }

        public static void Null(object o, string message = null, params object[] args) => IsNull(o, message, args);

        public static void IsNotNull(object o, string message = null, params object[] args) {
            if (o == null) Fail("Expected: not null\n  But was:  null", Format(message, args));
        }

        public static void NotNull(object o, string message = null, params object[] args) => IsNotNull(o, message, args);

        public static void IsEmpty(IEnumerable e, string message = null, params object[] args) {
            if (e.Cast<object>().Any()) Fail($"Expected: empty\n  But was:  {Show(e)}", Format(message, args));
        }

        public static void IsNotEmpty(IEnumerable e, string message = null, params object[] args) {
            if (!e.Cast<object>().Any()) Fail("Expected: not empty", Format(message, args));
        }

        public static void Greater(object a, object b, string message = null, params object[] args) {
            if (Compare(a, b) <= 0) Fail($"Expected: greater than {Show(b)}\n  But was:  {Show(a)}", Format(message, args));
        }

        public static void GreaterOrEqual(object a, object b, string message = null, params object[] args) {
            if (Compare(a, b) < 0) Fail($"Expected: greater than or equal to {Show(b)}\n  But was:  {Show(a)}", Format(message, args));
        }

        public static void Less(object a, object b, string message = null, params object[] args) {
            if (Compare(a, b) >= 0) Fail($"Expected: less than {Show(b)}\n  But was:  {Show(a)}", Format(message, args));
        }

        public static void LessOrEqual(object a, object b, string message = null, params object[] args) {
            if (Compare(a, b) > 0) Fail($"Expected: less than or equal to {Show(b)}\n  But was:  {Show(a)}", Format(message, args));
        }

        public static void That(bool condition, string message = null, params object[] args) => IsTrue(condition, message, args);

        public static void That(object actual, IResolveConstraint constraint, string message = null, params object[] args) {
            var c = constraint.Resolve();
            if (!c.Matches(actual)) Fail($"Expected: {c.Description}\n  But was:  {Show(actual)}", Format(message, args));
        }

        public static void DoesNotThrow(TestDelegate code, string message = null, params object[] args) {
            try {
                code();
            }
            catch (Exception e) when (!(e is AssertionException) && !(e is IgnoreException)) {
                Fail($"Expected: no exception\n  But was:  {e.GetType().Name}: {e.Message}\n{e.StackTrace}", Format(message, args));
            }
        }

        public static T Throws<T>(TestDelegate code, string message = null, params object[] args) where T : Exception {
            try {
                code();
            }
            catch (T e) when (e.GetType() == typeof(T)) {
                return e;
            }
            catch (Exception e) {
                Fail($"Expected: {typeof(T).Name}\n  But was:  {e.GetType().Name}: {e.Message}", Format(message, args));
            }
            Fail($"Expected: {typeof(T).Name}\n  But was:  no exception", Format(message, args));
            return null;
        }

        public static T Catch<T>(TestDelegate code, string message = null, params object[] args) where T : Exception {
            try {
                code();
            }
            catch (T e) {
                return e;
            }
            Fail($"Expected: {typeof(T).Name}\n  But was:  no exception", Format(message, args));
            return null;
        }

        public static void Fail(string message = null, params object[] args) => throw new AssertionException(Format(message, args) ?? "failed");
        public static void Ignore(string message = null, params object[] args) => throw new IgnoreException(Format(message, args) ?? "ignored");
        public static void Inconclusive(string message = null, params object[] args) => throw new InconclusiveException(Format(message, args) ?? "inconclusive");
        public static void Pass(string message = null, params object[] args) => throw new SuccessException(Format(message, args) ?? "passed");

        static string Format(string message, object[] args) =>
            message == null ? null : args != null && args.Length > 0 ? string.Format(CultureInfo.InvariantCulture, message, args) : message;
    }

    public static class CollectionAssert {
        public static void AreEqual(IEnumerable expected, IEnumerable actual, string message = null, params object[] args) =>
            Assert.AreEqual(expected, actual, message, args);

        public static void AreEquivalent(IEnumerable expected, IEnumerable actual, string message = null, params object[] args) {
            var x = expected.Cast<object>().Select(Assert.Show).OrderBy(s => s, StringComparer.Ordinal).ToList();
            var y = actual.Cast<object>().Select(Assert.Show).OrderBy(s => s, StringComparer.Ordinal).ToList();
            Assert.AreEqual(x, y, message, args);
        }

        public static void Contains(IEnumerable collection, object item, string message = null, params object[] args) {
            if (!collection.Cast<object>().Any(o => Assert.Same(item, o))) Assert.Fail($"Expected: collection containing {Assert.Show(item)}\n  But was:  {Assert.Show(collection)}" + (message == null ? "" : "\n" + message), args);
        }

        public static void IsEmpty(IEnumerable collection, string message = null, params object[] args) => Assert.IsEmpty(collection, message, args);
    }

    public interface IResolveConstraint {
        Constraint Resolve();
    }

    public abstract class Constraint : IResolveConstraint {
        public abstract string Description { get; }
        public abstract bool Matches(object actual);
        public Constraint Resolve() => this;
        public ConstraintBuilder And => new(this, true);
        public ConstraintBuilder Or => new(this, false);
    }

    sealed class PredicateConstraint : Constraint {
        readonly Func<object, bool> predicate;
        readonly string description;

        public PredicateConstraint(string description, Func<object, bool> predicate) {
            this.description = description;
            this.predicate = predicate;
        }

        public override string Description => description;
        public override bool Matches(object actual) => predicate(actual);
    }

    public sealed class EqualConstraint : Constraint {
        readonly object expected;
        double? tolerance;

        public EqualConstraint(object expected) {
            this.expected = expected;
        }

        public EqualConstraint Within(double amount) {
            tolerance = amount;
            return this;
        }

        public override string Description => Assert.Show(expected) + (tolerance.HasValue ? $" +/- {tolerance}" : "");

        public override bool Matches(object actual) {
            if (tolerance.HasValue && Assert.IsNumeric(actual) && Assert.IsNumeric(expected)) {
                return Math.Abs(Convert.ToDouble(actual, CultureInfo.InvariantCulture) - Convert.ToDouble(expected, CultureInfo.InvariantCulture)) <= tolerance.Value;
            }
            return Assert.Same(expected, actual);
        }
    }

    public sealed class ConstraintBuilder {
        readonly Constraint left;
        readonly bool and;

        internal ConstraintBuilder(Constraint left, bool and) {
            this.left = left;
            this.and = and;
        }

        Constraint Combine(Constraint right) => new PredicateConstraint(
            $"{left.Description} {(and ? "and" : "or")} {right.Description}",
            a => and ? left.Matches(a) && right.Matches(a) : left.Matches(a) || right.Matches(a));

        public Constraint GreaterThan(object x) => Combine(Is.GreaterThan(x));
        public Constraint GreaterThanOrEqualTo(object x) => Combine(Is.GreaterThanOrEqualTo(x));
        public Constraint LessThan(object x) => Combine(Is.LessThan(x));
        public Constraint LessThanOrEqualTo(object x) => Combine(Is.LessThanOrEqualTo(x));
        public Constraint EqualTo(object x) => Combine(Is.EqualTo(x));
    }

    public static class Is {
        public static EqualConstraint EqualTo(object expected) => new(expected);
        public static Constraint GreaterThan(object x) => new PredicateConstraint($"greater than {Assert.Show(x)}", a => Assert.Compare(a, x) > 0);
        public static Constraint GreaterThanOrEqualTo(object x) => new PredicateConstraint($"greater than or equal to {Assert.Show(x)}", a => Assert.Compare(a, x) >= 0);
        public static Constraint LessThan(object x) => new PredicateConstraint($"less than {Assert.Show(x)}", a => Assert.Compare(a, x) < 0);
        public static Constraint LessThanOrEqualTo(object x) => new PredicateConstraint($"less than or equal to {Assert.Show(x)}", a => Assert.Compare(a, x) <= 0);
        public static Constraint InRange(object from, object to) => new PredicateConstraint($"in range ({Assert.Show(from)},{Assert.Show(to)})",
            a => Assert.Compare(a, from) >= 0 && Assert.Compare(a, to) <= 0);
        public static Constraint Null => new PredicateConstraint("null", a => a == null);
        public static Constraint True => new PredicateConstraint("True", a => a is bool b && b);
        public static Constraint False => new PredicateConstraint("False", a => a is bool b && !b);
        public static Constraint Empty => new PredicateConstraint("empty", a => a is IEnumerable e && !e.Cast<object>().Any());
        public static Constraint SameAs(object x) => new PredicateConstraint($"same as {Assert.Show(x)}", a => ReferenceEquals(a, x));
        public static NotBuilder Not => new();
    }

    public sealed class NotBuilder {
        public Constraint Null => new PredicateConstraint("not null", a => a != null);
        public Constraint Empty => new PredicateConstraint("not empty", a => a is IEnumerable e && e.Cast<object>().Any());
        public Constraint EqualTo(object x) => new PredicateConstraint($"not {Assert.Show(x)}", a => !Assert.Same(x, a));
    }

    public static class Does {
        public static Constraint Contain(object item) => new PredicateConstraint($"collection containing {Assert.Show(item)}",
            a => a is string s && item is string sub ? s.Contains(sub) : a is IEnumerable e && e.Cast<object>().Any(o => Assert.Same(item, o)));
        public static Constraint StartWith(string prefix) => new PredicateConstraint($"starting with \"{prefix}\"", a => a is string s && s.StartsWith(prefix, StringComparison.Ordinal));
        public static Constraint EndWith(string suffix) => new PredicateConstraint($"ending with \"{suffix}\"", a => a is string s && s.EndsWith(suffix, StringComparison.Ordinal));
    }

    public static class Has {
        public static Constraint Member(object item) => Does.Contain(item);
    }
}
