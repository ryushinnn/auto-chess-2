using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AutoTest
{
    /// <summary>
    /// One request's parameters: the query string, with the keys of a JSON object body merged over it (body wins).
    /// Keys are case-insensitive. Query values are strings; body values keep their JSON type, and the typed getters
    /// accept both (`on=1`, `"on": true`). A parse failure is an ArgumentException, i.e. 400 bad_request.
    /// </summary>
    internal sealed class Request
    {
        private readonly Dictionary<string, JToken> m_Params = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);

        public string Method { get; private set; } = "GET";
        public string Path { get; private set; } = "/";

        public static Request From(HttpListenerRequest http)
        {
            var r = new Request { Method = http.HttpMethod ?? "GET" };
            string raw = http.RawUrl ?? "/";
            if (raw.StartsWith("http", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(raw, UriKind.Absolute, out var abs))
                raw = abs.PathAndQuery; // absolute-form request target
            int q = raw.IndexOf('?');
            string path = Uri.UnescapeDataString(q < 0 ? raw : raw.Substring(0, q));
            path = path.TrimEnd('/');
            r.Path = path.Length == 0 ? "/" : path.ToLowerInvariant();
            if (q >= 0) r.ParseQuery(raw.Substring(q + 1));
            if (http.HasEntityBody) r.ParseBody(http);
            return r;
        }

        private void ParseQuery(string query)
        {
            foreach (string part in query.Split('&'))
            {
                if (part.Length == 0) continue;
                int eq = part.IndexOf('=');
                string key = Decode(eq < 0 ? part : part.Substring(0, eq));
                string value = eq < 0 ? "" : Decode(part.Substring(eq + 1));
                if (key.Length > 0) m_Params[key] = new JValue(value);
            }
        }

        private static string Decode(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));

        private void ParseBody(HttpListenerRequest http)
        {
            // JSON is UTF-8 (RFC 8259); HttpListener's ContentEncoding falls back to the ANSI code page without a charset.
            string text;
            using (var reader = new StreamReader(http.InputStream, new UTF8Encoding(false)))
                text = reader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(text)) return;
            JToken body;
            try { body = ParseJson(text); }
            catch (JsonException e) { throw new ArgumentException("request body is not valid JSON: " + e.Message); }
            if (!(body is JObject obj))
                throw new ArgumentException("request body must be a JSON object");
            foreach (var p in obj.Properties()) m_Params[p.Name] = p.Value;
        }

        // ---- access ------------------------------------------------------------------------------------------

        public bool Has(string key) =>
            m_Params.TryGetValue(key, out var t) && t != null && t.Type != JTokenType.Null &&
            !(t.Type == JTokenType.String && ((string)t).Length == 0);

        /// <summary>All parameters as strings (JSON body values in their compact JSON form).</summary>
        public Dictionary<string, string> AsStrings(params string[] except)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in m_Params)
                if (!Array.Exists(except, e => string.Equals(e, kv.Key, StringComparison.OrdinalIgnoreCase)))
                    d[kv.Key] = ToText(kv.Value);
            return d;
        }

        public string Str(string key, string fallback = null) => Has(key) ? ToText(m_Params[key]) : fallback;

        public string RequireStr(string key) =>
            Has(key) ? ToText(m_Params[key]) : throw new ArgumentException("missing parameter '" + key + "'");

        public float Float(string key, float fallback) => Has(key) ? ParseFloat(key) : fallback;

        public float RequireFloat(string key) =>
            Has(key) ? ParseFloat(key) : throw new ArgumentException("missing parameter '" + key + "'");

        public int Int(string key, int fallback, int min = int.MinValue, int max = int.MaxValue) =>
            (int)Long(key, fallback, min, max);

        public long Long(string key, long fallback, long min = long.MinValue, long max = long.MaxValue)
        {
            if (!Has(key)) return fallback;
            string s = ToText(m_Params[key]).Trim();
            if (!long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v))
            {
                // 12.0 from a JSON body is still an integer
                if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) || d != Math.Floor(d) ||
                    d < long.MinValue || d > long.MaxValue)
                    throw new ArgumentException("parameter '" + key + "' must be an integer, got '" + s + "'");
                v = (long)d;
            }
            if (v < min || v > max)
                throw new ArgumentException("parameter '" + key + "' must be in " + min + ".." + max + ", got " + v);
            return v;
        }

        public bool Bool(string key, bool fallback)
        {
            if (!Has(key)) return fallback;
            string s = ToText(m_Params[key]).Trim().ToLowerInvariant();
            switch (s)
            {
                case "1": case "true": case "yes": case "on": return true;
                case "0": case "false": case "no": case "off": return false;
                default: throw new ArgumentException("parameter '" + key + "' must be 1/0/true/false, got '" + s + "'");
            }
        }

        /// <summary>A JSON-valued parameter: a body value as-is, a query string parsed as JSON (a bare word stays a string).</summary>
        public JToken Json(string key)
        {
            if (!m_Params.TryGetValue(key, out var t) || t == null) return null;
            if (t.Type != JTokenType.String) return t;
            string s = (string)t;
            if (LooksLikeJson(s))
            {
                try { return ParseJson(s); }
                catch (JsonException) { /* not JSON after all: keep it a string */ }
            }
            return t;
        }

        /// <summary>JSON parse that keeps date-looking strings as strings.</summary>
        public static JToken ParseJson(string text)
        {
            using var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None };
            var token = JToken.ReadFrom(reader);
            if (reader.Read() && reader.TokenType != JsonToken.Comment)
                throw new JsonReaderException("unexpected content after the JSON value");
            return token;
        }

        private static bool LooksLikeJson(string s)
        {
            s = s.Trim();
            if (s.Length == 0) return false;
            char c = s[0];
            return c == '[' || c == '{' || c == '"' || c == '-' || char.IsDigit(c) || s == "true" || s == "false" || s == "null";
        }

        private float ParseFloat(string key)
        {
            string s = ToText(m_Params[key]).Trim();
            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) || float.IsNaN(f) || float.IsInfinity(f))
                throw new ArgumentException("parameter '" + key + "' must be a number, got '" + s + "'");
            return f;
        }

        private static string ToText(JToken t) =>
            t == null || t.Type == JTokenType.Null ? null
            : t.Type == JTokenType.String ? (string)t
            : t.ToString(Formatting.None);
    }
}
