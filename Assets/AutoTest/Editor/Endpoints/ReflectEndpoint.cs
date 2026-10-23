using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// /reflect: reach game code no click can (types, members, get, set, call by name, JSON arguments).
    ///   op=types&amp;type=Shop                      types whose full name contains it
    ///   op=members&amp;type=Shop[&amp;match=Refresh]
    ///   op=get&amp;type=GameManager&amp;member=Instance.coins          (dotted; list indexes allowed)
    ///   op=set&amp;type=Shop&amp;member=gold&amp;value=99[&amp;path=Canvas/Shop|&amp;static=1]
    ///   op=call&amp;type=Shop&amp;method=Refresh[&amp;args=[1,"x"]]
    /// An instance member is read from the component of `type` on `path`, else from the first live instance
    /// (active first). A method that throws is a 200 with threw:true: that is the finding, not a transport error.
    /// </summary>
    internal static class ReflectEndpoint
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private const BindingFlags Declared = Any | BindingFlags.DeclaredOnly;

        private static readonly Dictionary<string, Type> s_TypeCache = new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly UnityValueConverter s_UnityValues = new UnityValueConverter();

        public static object Handle(Request r)
        {
            string op = r.RequireStr("op").Trim().ToLowerInvariant();
            switch (op)
            {
                case "types": return Types(r);
                case "members": return Members(r);
                case "get": return Get(r);
                case "set": return Set(r);
                case "call": return Call(r);
                default: throw new ArgumentException("op must be types|members|get|set|call");
            }
        }

        // ---- types ---------------------------------------------------------------------------------------

        private static object Types(Request r)
        {
            string needle = (r.Str("type") ?? r.Str("match") ?? "").Trim();
            int limit = r.Int("limit", 200, 1, 5000);
            var all = GameTypes().Where(t => needle.Length == 0 || t.FullName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            var types = all.Take(limit).Select(t => new
            {
                name = t.Name,
                fullName = t.FullName,
                assembly = t.Assembly.GetName().Name,
                isComponent = typeof(Component).IsAssignableFrom(t),
                isStatic = t.IsAbstract && t.IsSealed,
            }).ToList();
            return new { count = all.Count, truncated = all.Count > limit ? true : (bool?)null, types };
        }

        private static bool IsEngineAssembly(Assembly asm)
        {
            string n = asm.GetName().Name;
            return n == "mscorlib" || n == "netstandard" || n.StartsWith("System", StringComparison.Ordinal) ||
                   n.StartsWith("Unity.", StringComparison.Ordinal) || n.StartsWith("UnityEngine", StringComparison.Ordinal) ||
                   n.StartsWith("UnityEditor", StringComparison.Ordinal) || n.StartsWith("Mono.", StringComparison.Ordinal) ||
                   n.StartsWith("nunit", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Newtonsoft", StringComparison.Ordinal) ||
                   n.StartsWith("Bee.", StringComparison.Ordinal) || n.StartsWith("JetBrains", StringComparison.Ordinal) ||
                   n.StartsWith("Microsoft.", StringComparison.Ordinal) || n.StartsWith("ExCSS", StringComparison.Ordinal) ||
                   n.StartsWith("Sirenix", StringComparison.Ordinal) || n.StartsWith("log4net", StringComparison.Ordinal) ||
                   n == "AutoTest.Editor";
        }

        private static IEnumerable<Type> TypesOf(Assembly asm)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; }
            catch { yield break; }
            foreach (var t in types)
                if (t != null && !t.Name.StartsWith("<", StringComparison.Ordinal) && !t.FullName.Contains("<")) yield return t;
        }

        private static IEnumerable<Type> GameTypes() =>
            AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && !IsEngineAssembly(a)).SelectMany(TypesOf);

        private static IEnumerable<Type> EngineTypes() =>
            AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && IsEngineAssembly(a)).SelectMany(TypesOf);

        /// <summary>Full name anywhere; else a short name in game code, then in engine code. Ambiguity is a 400.</summary>
        private static Type RequireType(Request r)
        {
            string name = r.RequireStr("type").Trim();
            if (s_TypeCache.TryGetValue(name, out var cached)) return cached;
            Type found = GameTypes().Concat(EngineTypes()).FirstOrDefault(t => t.FullName == name || t.FullName == name.Replace('.', '+'));
            if (found == null)
                foreach (var tier in new[] { GameTypes(), EngineTypes() })
                {
                    var hits = tier.Where(t => t.Name == name).ToList();
                    if (hits.Count > 1)
                        throw new ArgumentException("type '" + name + "' is ambiguous: " +
                                                    string.Join(", ", hits.Select(t => t.FullName).Take(8)) + " (pass the full name)");
                    if (hits.Count == 1) { found = hits[0]; break; }
                }
            if (found == null)
                throw new NotFoundException("no type named '" + name + "' (op=types&type=... searches)");
            s_TypeCache[name] = found;
            return found;
        }

        // ---- members -------------------------------------------------------------------------------------

        private static bool IsBoringBase(Type t) =>
            t == null || t == typeof(object) || t == typeof(UnityEngine.Object) || t == typeof(Component) ||
            t == typeof(Behaviour) || t == typeof(MonoBehaviour) || t == typeof(ScriptableObject) || t == typeof(ValueType);

        private static object Members(Request r)
        {
            var type = RequireType(r);
            string match = r.Str("match");
            var rows = new List<object>();
            for (var t = type; !IsBoringBase(t); t = t.BaseType)
                foreach (var m in t.GetMembers(Declared))
                {
                    if (m.Name.StartsWith("<", StringComparison.Ordinal) || m is MethodInfo { IsSpecialName: true } || m is ConstructorInfo || m is Type) continue;
                    if (match != null && m.Name.IndexOf(match, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    rows.Add(m switch
                    {
                        FieldInfo f => (object)new { name = f.Name, kind = "field", type = TypeName(f.FieldType), @static = f.IsStatic, @public = f.IsPublic, declaringType = t.Name },
                        PropertyInfo p => new
                        {
                            name = p.Name, kind = "property", type = TypeName(p.PropertyType), @static = IsStatic(p),
                            @public = (p.GetGetMethod() ?? p.GetSetMethod()) != null, canRead = p.CanRead, canWrite = p.CanWrite, declaringType = t.Name,
                        },
                        MethodInfo mi => new
                        {
                            name = mi.Name, kind = "method", type = TypeName(mi.ReturnType), @static = mi.IsStatic, @public = mi.IsPublic,
                            parameters = string.Join(", ", mi.GetParameters().Select(pp => TypeName(pp.ParameterType) + " " + pp.Name)),
                            declaringType = t.Name,
                        },
                        EventInfo e => new { name = e.Name, kind = "event", type = TypeName(e.EventHandlerType), declaringType = t.Name },
                        _ => new { name = m.Name, kind = m.MemberType.ToString().ToLowerInvariant(), declaringType = t.Name },
                    });
                }
            return new { type = type.FullName, count = rows.Count, members = rows };
        }

        // ---- get / set -----------------------------------------------------------------------------------

        private static object Get(Request r)
        {
            var type = RequireType(r);
            string member = r.RequireStr("member").Trim();
            var (value, valueType, how) = Read(r, type, member.Split('.'));
            return new { type = type.FullName, member, target = how, value = ToJson(value, 0), valueType = TypeName(valueType) };
        }

        private static object Set(Request r)
        {
            var type = RequireType(r);
            string member = r.RequireStr("member").Trim();
            if (!r.Has("value")) throw new ArgumentException("missing parameter 'value' (JSON)");
            var segs = member.Split('.');
            string last = segs[segs.Length - 1];

            object owner;
            Type ownerType;
            string how;
            if (segs.Length == 1)
            {
                var m = FindMember(type, last) ?? throw new NotFoundException(type.Name + " has no field/property '" + last + "'");
                owner = ResolveTarget(r, type, IsStatic(m), out how);
                ownerType = type;
            }
            else
            {
                (owner, ownerType, how) = Read(r, type, segs.Take(segs.Length - 1).ToArray());
                if (owner == null) throw new ArgumentException(string.Join(".", segs.Take(segs.Length - 1)) + " is null");
                if (ownerType.IsValueType) throw new ArgumentException("cannot set through the value-type member " + ownerType.Name + " (set the whole value instead)");
                ownerType = owner.GetType();
            }
            var target = FindMember(ownerType, last) ?? throw new NotFoundException(ownerType.Name + " has no field/property '" + last + "'");
            bool readable = !(target is PropertyInfo { CanRead: false });
            object before = readable ? ReadMember(target, owner) : null;
            var valueToken = r.Json("value");
            var memberType = MemberType(target);
            // A string member takes the text as sent (value=007 stays "007"); everything else is parsed as JSON.
            object parsed = memberType == typeof(string) && valueToken != null &&
                            valueToken.Type != JTokenType.String && valueToken.Type != JTokenType.Null
                ? r.Str("value")
                : Convert(valueToken, memberType, "value");
            switch (target)
            {
                case FieldInfo f:
                    if (f.IsLiteral || f.IsInitOnly) throw new ArgumentException(ownerType.Name + "." + last + " is read-only");
                    f.SetValue(f.IsStatic ? null : owner, parsed);
                    break;
                case PropertyInfo p:
                    var setter = p.GetSetMethod(true) ?? throw new ArgumentException(ownerType.Name + "." + last + " has no setter");
                    try { setter.Invoke(setter.IsStatic ? null : owner, new[] { parsed }); }
                    catch (TargetInvocationException e) when (e.InnerException != null)
                    {
                        return new { type = type.FullName, member, target = how, threw = true, exception = Describe(e.InnerException), stack = Stack(e.InnerException) };
                    }
                    break;
            }
            return new
            {
                type = type.FullName, member, target = how,
                before = ToJson(before, 0), value = readable ? ToJson(ReadMember(target, owner), 0) : null,
            };
        }

        /// <summary>Walks a dotted member path; the first segment is resolved on `type` (static or a live instance).</summary>
        private static (object value, Type type, string how) Read(Request r, Type type, string[] segs)
        {
            var first = FindMember(type, segs[0]) ?? throw new NotFoundException(type.Name + " has no field/property '" + segs[0] + "'");
            object owner = ResolveTarget(r, type, IsStatic(first), out string how);
            object value = ReadMember(first, owner);
            Type valueType = MemberType(first);
            for (int i = 1; i < segs.Length; i++)
            {
                string seg = segs[i];
                string walked = string.Join(".", segs.Take(i));
                if (value == null) throw new NotFoundException(walked + " is null, cannot read ." + seg);
                if (int.TryParse(seg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) && value is IList list)
                {
                    if (index < 0 || index >= list.Count) throw new NotFoundException(walked + " has " + list.Count + " items, no [" + index + "]");
                    value = list[index];
                    valueType = value?.GetType() ?? typeof(object);
                    continue;
                }
                if (value is IDictionary dict && !(value is UnityEngine.Object))
                {
                    var key = dict.Keys.Cast<object>().FirstOrDefault(k => string.Equals(k?.ToString(), seg, StringComparison.Ordinal));
                    if (key == null) throw new NotFoundException(walked + " has no key '" + seg + "'");
                    value = dict[key];
                    valueType = value?.GetType() ?? typeof(object);
                    continue;
                }
                var m = FindMember(value.GetType(), seg) ?? throw new NotFoundException(value.GetType().Name + " has no field/property '" + seg + "'");
                value = ReadMember(m, value);
                valueType = MemberType(m);
            }
            return (value, value?.GetType() ?? valueType, how);
        }

        private static MemberInfo FindMember(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                MemberInfo m = t.GetField(name, Declared);
                m ??= t.GetProperties(Declared).FirstOrDefault(p => p.Name == name && p.GetIndexParameters().Length == 0);
                if (m != null) return m;
            }
            // case-insensitive fallback: a scenario writes `coins`, the property is `Coins`
            for (var t = type; t != null; t = t.BaseType)
                foreach (var m in t.GetMembers(Declared))
                    if ((m is FieldInfo || m is PropertyInfo { } p && p.GetIndexParameters().Length == 0) &&
                        string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
                        return m;
            return null;
        }

        private static Type MemberType(MemberInfo m) => m is FieldInfo f ? f.FieldType : ((PropertyInfo)m).PropertyType;

        private static bool IsStatic(MemberInfo m) => m switch
        {
            FieldInfo f => f.IsStatic,
            PropertyInfo p => (p.GetGetMethod(true) ?? p.GetSetMethod(true))?.IsStatic ?? false,
            MethodInfo mi => mi.IsStatic,
            _ => false,
        };

        private static object ReadMember(MemberInfo m, object owner)
        {
            try
            {
                if (m is FieldInfo f) return f.GetValue(f.IsStatic ? null : owner);
                var p = (PropertyInfo)m;
                var getter = p.GetGetMethod(true) ?? throw new ArgumentException(p.Name + " has no getter");
                return getter.Invoke(getter.IsStatic ? null : owner, null);
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                throw new AutoTestException(500, "error", "reading " + m.Name + " threw " + e.InnerException.GetType().Name + ": " + e.InnerException.Message, e.InnerException);
            }
        }

        /// <summary>The object an instance member is read from / called on.</summary>
        private static object ResolveTarget(Request r, Type type, bool memberIsStatic, out string how)
        {
            if (memberIsStatic) { how = "static"; return null; }
            if (r.Bool("static", false))
                throw new ArgumentException("the member is not static (drop static=1 and pass path= or let the first live instance be used)");

            if (r.Has("path"))
            {
                string path = r.RequireStr("path");
                how = "path " + path;
                var go = SceneQuery.FindByPath(path) ?? throw new NotFoundException("no GameObject at path '" + path + "'");
                if (type == typeof(GameObject)) return go;
                if (!typeof(Component).IsAssignableFrom(type) && !type.IsInterface)
                    throw new ArgumentException("path= needs a Component (or GameObject) type, " + type.Name + " is neither");
                var c = go.GetComponent(type);
                return c != null ? c : throw new NotFoundException("'" + path + "' has no " + type.Name);
            }

            if (typeof(Component).IsAssignableFrom(type) || type == typeof(GameObject))
            {
                var live = UnityEngine.Object.FindObjectsOfType(type, true)
                    .Where(o => o != null && (o is Component c ? c.gameObject.scene.IsValid() : ((GameObject)o).scene.IsValid()))
                    .OrderBy(o => (o is Component c ? c.gameObject : (GameObject)o).activeInHierarchy ? 0 : 1)
                    .FirstOrDefault();
                if (live == null) throw new NotFoundException("no live instance of " + type.Name + " in the loaded scenes (pass path= or static=1)");
                how = "first instance " + SceneQuery.PathOf(live is Component lc ? lc.gameObject : (GameObject)live);
                return live;
            }
            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                var loaded = Resources.FindObjectsOfTypeAll(type).FirstOrDefault(o => o != null);
                if (loaded == null) throw new NotFoundException("no loaded instance of " + type.Name);
                how = "first loaded " + loaded.name;
                return loaded;
            }
            throw new ArgumentException(type.Name + " is not a UnityEngine.Object: only its static members can be reached");
        }

        // ---- call ----------------------------------------------------------------------------------------

        private static object Call(Request r)
        {
            var type = RequireType(r);
            string name = (r.Str("method") ?? r.Str("member") ?? throw new ArgumentException("missing parameter 'method'")).Trim();
            var argsToken = r.Json("args");
            JArray args = argsToken switch
            {
                null => new JArray(),
                JArray a => a,
                JValue { Type: JTokenType.Null } => new JArray(),
                _ => throw new ArgumentException("args must be a JSON array"),
            };

            var candidates = new List<MethodInfo>();
            for (var t = type; t != null; t = t.BaseType)
                candidates.AddRange(t.GetMethods(Declared).Where(m => m.Name == name && !m.IsGenericMethodDefinition));
            if (candidates.Count == 0)
                candidates.AddRange(type.GetMethods(Any).Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase) && !m.IsGenericMethodDefinition));
            if (candidates.Count == 0) throw new NotFoundException(type.Name + " has no method '" + name + "' (op=members to list)");

            MethodInfo method = null;
            object[] bound = null;
            Exception bindError = null;
            foreach (var cand in candidates.OrderBy(m => m.GetParameters().Length == args.Count ? 0 : 1))
            {
                try { bound = Bind(cand, args); method = cand; break; }
                catch (ArgumentException e) { bindError ??= e; }
            }
            if (method == null)
                throw bindError ?? new ArgumentException(type.Name + "." + name + ": no overload takes " + args.Count + " argument(s)");

            object target = ResolveTarget(r, type, method.IsStatic, out string how);
            string signature = name + "(" + string.Join(", ", method.GetParameters().Select(p => TypeName(p.ParameterType) + " " + p.Name)) + ")";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                object returned = method.Invoke(target, bound);
                return new
                {
                    type = type.FullName, method = signature, target = how, threw = false,
                    returned = ToJson(returned, 0), returnType = TypeName(method.ReturnType), durationMs = sw.ElapsedMilliseconds,
                };
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                return new
                {
                    type = type.FullName, method = signature, target = how, threw = true,
                    exception = Describe(e.InnerException), exceptionType = e.InnerException.GetType().FullName,
                    stack = Stack(e.InnerException), durationMs = sw.ElapsedMilliseconds,
                };
            }
        }

        private static object[] Bind(MethodInfo m, JArray args)
        {
            var ps = m.GetParameters();
            if (args.Count > ps.Length || ps.Skip(args.Count).Any(p => !p.IsOptional))
                throw new ArgumentException(m.Name + " takes " + ps.Count(p => !p.IsOptional) +
                                            (ps.Any(p => p.IsOptional) ? ".." + ps.Length : "") + " argument(s), got " + args.Count);
            var bound = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                var pt = ps[i].ParameterType.IsByRef ? ps[i].ParameterType.GetElementType() : ps[i].ParameterType;
                bound[i] = i < args.Count ? Convert(args[i], pt, ps[i].Name) : ps[i].DefaultValue;
            }
            return bound;
        }

        private static string Describe(Exception e) => e.GetType().Name + ": " + e.Message;

        private static string Stack(Exception e)
        {
            if (string.IsNullOrEmpty(e.StackTrace)) return null;
            var lines = e.StackTrace.Split('\n').Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.Contains("System.Reflection.") && !l.Contains("AutoTest.ReflectEndpoint"))
                .Take(20);
            return string.Join("\n", lines);
        }

        // ---- JSON in -------------------------------------------------------------------------------------

        private static object Convert(JToken tok, Type t, string what)
        {
            var under = Nullable.GetUnderlyingType(t) ?? t;
            if (tok == null || tok.Type == JTokenType.Null)
            {
                if (t.IsValueType && Nullable.GetUnderlyingType(t) == null) throw new ArgumentException(what + ": null is not a " + TypeName(t));
                return null;
            }
            try
            {
                if (under == typeof(string)) return tok.Type == JTokenType.String ? (string)tok : tok.ToString(Formatting.None);
                if (under.IsEnum)
                    return tok.Type == JTokenType.Integer ? Enum.ToObject(under, (long)tok) : Enum.Parse(under, (string)tok, true);
                if (under == typeof(bool) && tok.Type == JTokenType.String)
                {
                    string s = ((string)tok).Trim().ToLowerInvariant();
                    if (s == "1" || s == "true") return true;
                    if (s == "0" || s == "false") return false;
                }
                if (tok is JArray arr && (under == typeof(Vector2) || under == typeof(Vector3) || under == typeof(Vector4) ||
                                          under == typeof(Color) || under == typeof(Quaternion) || under == typeof(Vector2Int) || under == typeof(Vector3Int)))
                {
                    float F(int i) => i < arr.Count ? (float)arr[i] : 0f;
                    if (under == typeof(Vector2)) return new Vector2(F(0), F(1));
                    if (under == typeof(Vector3)) return new Vector3(F(0), F(1), F(2));
                    if (under == typeof(Vector4)) return new Vector4(F(0), F(1), F(2), F(3));
                    if (under == typeof(Quaternion)) return new Quaternion(F(0), F(1), F(2), F(3));
                    if (under == typeof(Vector2Int)) return new Vector2Int((int)F(0), (int)F(1));
                    if (under == typeof(Vector3Int)) return new Vector3Int((int)F(0), (int)F(1), (int)F(2));
                    return new Color(F(0), F(1), F(2), arr.Count > 3 ? F(3) : 1f);
                }
                if (typeof(UnityEngine.Object).IsAssignableFrom(under)) return ToUnityObject(tok, under, what);
                if (under.IsArray && tok is JArray items)
                {
                    var et = under.GetElementType();
                    var result = Array.CreateInstance(et, items.Count);
                    for (int i = 0; i < items.Count; i++) result.SetValue(Convert(items[i], et, what + "[" + i + "]"), i);
                    return result;
                }
                if (under.IsGenericType && under.GetGenericTypeDefinition() == typeof(List<>) && tok is JArray litems)
                {
                    var et = under.GetGenericArguments()[0];
                    var list = (IList)Activator.CreateInstance(under);
                    for (int i = 0; i < litems.Count; i++) list.Add(Convert(litems[i], et, what + "[" + i + "]"));
                    return list;
                }
                return tok.ToObject(under, Json.Serializer);
            }
            catch (AutoTestException) { throw; }
            catch (ArgumentException) { throw; }
            catch (Exception e)
            {
                throw new ArgumentException(what + ": cannot convert " + tok.ToString(Formatting.None) + " to " + TypeName(t) + " (" + e.Message + ")");
            }
        }

        /// <summary>"#Canvas/Shop" or "Canvas/Shop" = the object at that path (its component of the wanted type); a number = an instance id.</summary>
        private static object ToUnityObject(JToken tok, Type type, string what)
        {
            if (tok.Type == JTokenType.Integer)
            {
                var o = UnityEditor.EditorUtility.InstanceIDToObject((int)tok);
                if (o == null) throw new NotFoundException(what + ": no object with instance id " + (int)tok);
                if (type.IsInstanceOfType(o)) return o;
                var go = o as GameObject ?? (o as Component)?.gameObject;
                var c = go != null && typeof(Component).IsAssignableFrom(type) ? go.GetComponent(type) : null;
                return c != null ? c : throw new ArgumentException(what + ": instance " + (int)tok + " is no " + type.Name);
            }
            string path = ((string)tok).TrimStart('#');
            var target = SceneQuery.FindByPath(path) ?? throw new NotFoundException(what + ": no GameObject at path '" + path + "'");
            if (type == typeof(GameObject) || type == typeof(UnityEngine.Object)) return target;
            var comp = typeof(Component).IsAssignableFrom(type) ? target.GetComponent(type) : null;
            return comp != null ? comp : throw new ArgumentException(what + ": '" + path + "' has no " + type.Name);
        }

        // ---- JSON out ------------------------------------------------------------------------------------

        /// <summary>A bounded JSON-friendly copy: Unity objects as descriptors, collections capped, plain objects one level of public members.</summary>
        internal static object ToJson(object v, int depth)
        {
            switch (v)
            {
                case null: return null;
                case string _: case bool _: case int _: case long _: case short _: case byte _: case uint _: case ulong _: case decimal _:
                    return v;
                case float f: return float.IsNaN(f) || float.IsInfinity(f) ? (object)f.ToString(CultureInfo.InvariantCulture) : f;
                case double d: return double.IsNaN(d) || double.IsInfinity(d) ? (object)d.ToString(CultureInfo.InvariantCulture) : d;
                case char c: return c.ToString();
                case Enum e: return e.ToString();
                case DateTime dt: return dt.ToString("o");
                case TimeSpan ts: return ts.ToString();
                case Type ty: return ty.FullName;
                case Delegate del: return "delegate " + del.Method.Name;
                case UnityEngine.Object uo: return uo == null ? null : UnityObjectConverter.Describe(uo);
                case JToken tok: return tok;
            }
            var type = v.GetType();
            if (type.IsPrimitive) return v.ToString();
            if (s_UnityValues.CanConvert(type)) return JToken.FromObject(v, Json.Serializer);
            if (depth >= 3) return v.ToString();
            if (v is IDictionary dict)
            {
                var d = new Dictionary<string, object>();
                foreach (DictionaryEntry e in dict)
                {
                    if (d.Count >= 100) { d["…"] = "+" + (dict.Count - 100) + " more"; break; }
                    d[KeyText(e.Key)] = ToJson(e.Value, depth + 1);
                }
                return d;
            }
            if (v is IEnumerable seq)
            {
                var list = new List<object>();
                foreach (var item in seq)
                {
                    if (list.Count >= 200) { list.Add("…"); break; }
                    list.Add(ToJson(item, depth + 1));
                }
                return list;
            }
            var obj = new Dictionary<string, object> { ["$type"] = TypeName(type) };
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance).Take(40))
            {
                try { obj[f.Name] = ToJson(f.GetValue(v), depth + 1); }
                catch (Exception e) { obj[f.Name] = "<" + e.GetType().Name + ">"; }
            }
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.GetIndexParameters().Length == 0).Take(40))
            {
                try { obj[p.Name] = ToJson(p.GetValue(v), depth + 1); }
                catch (Exception e) { obj[p.Name] = "<" + (e.InnerException ?? e).GetType().Name + ">"; }
            }
            return obj;
        }

        private static string KeyText(object key) => key switch
        {
            null => "null",
            UnityEngine.Object uo => uo == null ? "null" : uo.name,
            _ => key.ToString(),
        };

        internal static string TypeName(Type t)
        {
            if (t == null) return null;
            if (t.IsArray) return TypeName(t.GetElementType()) + "[]";
            if (!t.IsGenericType) return t.Name;
            int tick = t.Name.IndexOf('`');
            string bare = tick > 0 ? t.Name.Substring(0, tick) : t.Name;
            return bare + "<" + string.Join(", ", t.GetGenericArguments().Select(TypeName)) + ">";
        }
    }
}
