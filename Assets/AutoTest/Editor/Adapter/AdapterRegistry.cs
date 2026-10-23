using System;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using UnityEditor;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// Finds the game adapter (first concrete IAutoTestGameAdapter in TypeCache, cached per domain) and calls into it
    /// so that adapter exceptions become replies: AutoTestException keeps its status, NotSupported/NotImplemented is
    /// 501, ArgumentException is 400 where the interface makes it part of the contract, anything else 500.
    /// </summary>
    internal static class AdapterRegistry
    {
        private static IAutoTestGameAdapter s_Adapter;
        private static bool s_Scanned;
        private static string s_LoadError;

        public static IAutoTestGameAdapter Adapter
        {
            get
            {
                if (!s_Scanned) Scan();
                return s_Adapter;
            }
        }

        private static void Scan()
        {
            s_Scanned = true;
            var types = TypeCache.GetTypesDerivedFrom<IAutoTestGameAdapter>()
                .Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters)
                .OrderBy(t => t.FullName, StringComparer.Ordinal);
            foreach (var type in types)
            {
                try
                {
                    s_Adapter = (IAutoTestGameAdapter)Activator.CreateInstance(type, nonPublic: true);
                    s_LoadError = null;
                    Debug.Log("[AutoTest] game adapter: " + type.FullName);
                    return;
                }
                catch (Exception e)
                {
                    var inner = e is TargetInvocationException { InnerException: { } i } ? i : e;
                    s_LoadError = type.FullName + " constructor threw " + inner.GetType().Name + ": " + inner.Message;
                    Debug.LogWarning("[AutoTest] " + s_LoadError);
                }
            }
        }

        public static IAutoTestGameAdapter Require()
        {
            var a = Adapter;
            if (a != null) return a;
            throw new NotSupportedException(s_LoadError ?? "no game adapter: implement AutoTest.IAutoTestGameAdapter in an Editor assembly of the game");
        }

        public static T Require<T>() where T : class
        {
            var a = Require();
            return a as T ?? throw new NotSupportedException("game adapter " + a.GetType().Name + " does not implement " + typeof(T).Name);
        }

        /// <summary>Calls adapter code; `argumentIsBadRequest` for members whose contract says "throw ArgumentException for bad args".</summary>
        public static T Call<T>(string member, Func<T> fn, bool argumentIsBadRequest = false)
        {
            try
            {
                return fn();
            }
            catch (Exception raw)
            {
                var e = Errors.Unwrap(raw);
                string where = (s_Adapter?.GetType().Name ?? "adapter") + "." + member;
                switch (e)
                {
                    case AutoTestException:
                        ExceptionDispatchInfo.Capture(e).Throw();
                        throw;
                    case NotSupportedException:
                    case NotImplementedException:
                        throw new AutoTestException(501, "not_supported", where + ": " + e.Message, e);
                    case ArgumentException when argumentIsBadRequest:
                        throw new AutoTestException(400, "bad_request", e.Message, e);
                    default:
                        throw new AdapterException(where, e);
                }
            }
        }

        /// <summary>The /status block. Never throws.</summary>
        public static object Describe()
        {
            IAutoTestGameAdapter a;
            try { a = Adapter; }
            catch (Exception e) { return new { present = false, available = false, error = e.Message }; }
            if (a == null) return new { present = false, available = false, error = s_LoadError };

            bool available = false;
            string gameId = null, error = null;
            try { gameId = a.GameId; }
            catch (Exception e) { error = "GameId threw " + e.GetType().Name + ": " + e.Message; }
            try { available = a.IsAvailable(); }
            catch (Exception e) { error = "IsAvailable threw " + e.GetType().Name + ": " + e.Message; }
            return new
            {
                present = true,
                available,
                gameId,
                type = a.GetType().FullName,
                ops = a is IAutoTestGameOps,
                invariants = a is IAutoTestInvariants,
                targets = a is IAutoTestTargets,
                error,
            };
        }
    }
}
