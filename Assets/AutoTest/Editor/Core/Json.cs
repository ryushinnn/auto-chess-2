using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// The one serializer every reply goes through (Newtonsoft, nulls dropped). Unity value types become arrays
    /// (Vector3 -> [x,y,z], Rect -> [xMin,yMin,xMax,yMax]) and Unity objects a small descriptor, so an adapter can
    /// put Vector3s, Components or destroyed objects into its state without a self-reference loop or a crash.
    /// Main-thread routes convert their result to a JToken inside the main-thread job (Unity getters).
    /// </summary>
    internal static class Json
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            DateParseHandling = DateParseHandling.None,
            MaxDepth = 64,
            Converters = { new UnityValueConverter(), new UnityObjectConverter(), new StringEnumConverter() },
            // A throwing property getter in adapter data drops that member instead of failing the reply.
            Error = (_, e) => e.ErrorContext.Handled = true,
        };

        [ThreadStatic] private static JsonSerializer t_Serializer;

        /// <summary>One serializer per thread: the HTTP pool and the main thread serialize concurrently.</summary>
        public static JsonSerializer Serializer => t_Serializer ??= JsonSerializer.Create(Settings);

        public static JToken ToToken(object value) =>
            value == null ? JValue.CreateNull() : value as JToken ?? JToken.FromObject(value, Serializer) ?? JValue.CreateNull();

        /// <summary>Success reply: the handler's object with "ok": true first; a non-object result goes under "result".</summary>
        public static JObject ToReply(object body)
        {
            JObject reply;
            var token = ToToken(body);
            if (token is JObject obj)
            {
                obj.Remove("ok");
                obj.AddFirst(new JProperty("ok", true));
                reply = obj;
            }
            else
            {
                reply = new JObject { ["ok"] = true };
                if (token.Type != JTokenType.Null) reply["result"] = token;
            }
            StripNulls(reply);
            return reply;
        }

        /// <summary>NullValueHandling.Ignore does not apply to dictionary entries; this makes the rule hold everywhere.</summary>
        public static void StripNulls(JToken token)
        {
            switch (token)
            {
                case JObject o:
                    foreach (var p in o.Properties().ToList())
                    {
                        if (p.Value.Type == JTokenType.Null) p.Remove();
                        else StripNulls(p.Value);
                    }
                    break;
                case JArray a:
                    foreach (var item in a) StripNulls(item);
                    break;
            }
        }

        public static string Compact(JToken token) => token.ToString(Formatting.None);
    }

    /// <summary>Unity math/value structs as plain JSON arrays (write-only).</summary>
    internal sealed class UnityValueConverter : JsonConverter
    {
        private static readonly HashSet<Type> s_Types = new HashSet<Type>
        {
            typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Vector2Int), typeof(Vector3Int),
            typeof(Quaternion), typeof(Color), typeof(Color32), typeof(Rect), typeof(RectInt), typeof(Bounds),
            typeof(Matrix4x4), typeof(LayerMask),
        };

        public override bool CanRead => false;
        public override bool CanConvert(Type t) => s_Types.Contains(Nullable.GetUnderlyingType(t) ?? t);

        public override object ReadJson(JsonReader reader, Type t, object existing, JsonSerializer serializer) =>
            throw new NotSupportedException();

        public override void WriteJson(JsonWriter w, object value, JsonSerializer serializer)
        {
            switch (value)
            {
                case Vector2 v: Floats(w, v.x, v.y); break;
                case Vector3 v: Floats(w, v.x, v.y, v.z); break;
                case Vector4 v: Floats(w, v.x, v.y, v.z, v.w); break;
                case Vector2Int v: Ints(w, v.x, v.y); break;
                case Vector3Int v: Ints(w, v.x, v.y, v.z); break;
                case Quaternion q: Floats(w, q.x, q.y, q.z, q.w); break;
                case Color c: Floats(w, c.r, c.g, c.b, c.a); break;
                case Color32 c: Ints(w, c.r, c.g, c.b, c.a); break;
                case Rect r: Floats(w, r.xMin, r.yMin, r.xMax, r.yMax); break;
                case RectInt r: Ints(w, r.xMin, r.yMin, r.xMax, r.yMax); break;
                case LayerMask m: w.WriteValue(m.value); break;
                case Bounds b:
                    w.WriteStartObject();
                    w.WritePropertyName("center"); Floats(w, b.center.x, b.center.y, b.center.z);
                    w.WritePropertyName("size"); Floats(w, b.size.x, b.size.y, b.size.z);
                    w.WriteEndObject();
                    break;
                case Matrix4x4 m:
                    w.WriteStartArray();
                    for (int i = 0; i < 16; i++) w.WriteValue(m[i]);
                    w.WriteEndArray();
                    break;
                default: w.WriteNull(); break;
            }
        }

        private static void Floats(JsonWriter w, params float[] values)
        {
            w.WriteStartArray();
            foreach (float f in values) w.WriteValue(f);
            w.WriteEndArray();
        }

        private static void Ints(JsonWriter w, params int[] values)
        {
            w.WriteStartArray();
            foreach (int i in values) w.WriteValue(i);
            w.WriteEndArray();
        }
    }

    /// <summary>Any UnityEngine.Object as {name, type, id[, path]}; a destroyed one (Unity's fake null) as null.</summary>
    internal sealed class UnityObjectConverter : JsonConverter
    {
        public override bool CanRead => false;
        public override bool CanConvert(Type t) => typeof(UnityEngine.Object).IsAssignableFrom(t);

        public override object ReadJson(JsonReader reader, Type t, object existing, JsonSerializer serializer) =>
            throw new NotSupportedException();

        public override void WriteJson(JsonWriter w, object value, JsonSerializer serializer)
        {
            var o = value as UnityEngine.Object;
            if (o == null) { w.WriteNull(); return; }
            serializer.Serialize(w, Describe(o));
        }

        public static Dictionary<string, object> Describe(UnityEngine.Object o)
        {
            var d = new Dictionary<string, object> { ["name"] = o.name, ["type"] = o.GetType().Name, ["id"] = o.GetInstanceID() };
            var go = o as GameObject ?? (o as Component)?.gameObject;
            if (go != null && go.scene.IsValid())
            {
                d["path"] = SceneQuery.PathOf(go.transform);
                d["activeInHierarchy"] = go.activeInHierarchy;
            }
            return d;
        }
    }
}
