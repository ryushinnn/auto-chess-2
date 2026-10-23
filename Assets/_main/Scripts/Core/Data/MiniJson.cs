using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AutoChess.Core {
    /// <summary>
    /// Minimal JSON reader / writer (objects -> Dictionary&lt;string, object&gt;, arrays -> List&lt;object&gt;, numbers ->
    /// double). Lets the engine-free core load exported game data without a third-party library.
    /// </summary>
    public static class MiniJson {
        public static object Parse(string json) {
            var p = new Parser(json ?? throw new ArgumentNullException(nameof(json)));
            var value = p.ReadValue();
            p.SkipWhitespace();
            if (!p.AtEnd) throw p.Error("trailing characters");
            return value;
        }

        public static string Serialize(object value, bool pretty = true) {
            var sb = new StringBuilder();
            Write(sb, value, pretty, 0);
            return sb.ToString();
        }

        sealed class Parser {
            readonly string s;
            int i;

            public Parser(string s) {
                this.s = s;
            }

            public bool AtEnd => i >= s.Length;

            public FormatException Error(string message) => new($"JSON: {message} at offset {i}");

            public void SkipWhitespace() {
                while (i < s.Length) {
                    var c = s[i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') i++;
                    else if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') {
                        while (i < s.Length && s[i] != '\n') i++;
                    }
                    else break;
                }
            }

            public object ReadValue() {
                SkipWhitespace();
                if (AtEnd) throw Error("unexpected end");
                var c = s[i];
                switch (c) {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Error($"unexpected '{c}'");
                }
            }

            void Expect(string word) {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw Error($"expected {word}");
                i += word.Length;
            }

            Dictionary<string, object> ReadObject() {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                i++;
                SkipWhitespace();
                if (i < s.Length && s[i] == '}') {
                    i++;
                    return result;
                }
                while (true) {
                    SkipWhitespace();
                    if (AtEnd || s[i] != '"') throw Error("expected key");
                    var key = ReadString();
                    SkipWhitespace();
                    if (AtEnd || s[i] != ':') throw Error("expected ':'");
                    i++;
                    result[key] = ReadValue();
                    SkipWhitespace();
                    if (AtEnd) throw Error("unterminated object");
                    if (s[i] == ',') {
                        i++;
                        continue;
                    }
                    if (s[i] == '}') {
                        i++;
                        return result;
                    }
                    throw Error("expected ',' or '}'");
                }
            }

            List<object> ReadArray() {
                var result = new List<object>();
                i++;
                SkipWhitespace();
                if (i < s.Length && s[i] == ']') {
                    i++;
                    return result;
                }
                while (true) {
                    result.Add(ReadValue());
                    SkipWhitespace();
                    if (AtEnd) throw Error("unterminated array");
                    if (s[i] == ',') {
                        i++;
                        continue;
                    }
                    if (s[i] == ']') {
                        i++;
                        return result;
                    }
                    throw Error("expected ',' or ']'");
                }
            }

            string ReadString() {
                var sb = new StringBuilder();
                i++;
                while (i < s.Length) {
                    var c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') {
                        sb.Append(c);
                        continue;
                    }
                    if (i >= s.Length) break;
                    var e = s[i++];
                    switch (e) {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw Error("bad \\u escape");
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default: throw Error($"bad escape \\{e}");
                    }
                }
                throw Error("unterminated string");
            }

            double ReadNumber() {
                var start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }

        static void Write(StringBuilder sb, object value, bool pretty, int depth) {
            switch (value) {
                case null:
                    sb.Append("null");
                    break;
                case string str:
                    WriteString(sb, str);
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case float f:
                    sb.Append(((double)f).ToString("R", CultureInfo.InvariantCulture));
                    break;
                case double d:
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case int or long or short or byte or uint or ulong:
                    sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    break;
                case Enum e:
                    WriteString(sb, e.ToString());
                    break;
                case IDictionary<string, object> dict: {
                    sb.Append('{');
                    var first = true;
                    foreach (var kv in dict) {
                        if (!first) sb.Append(',');
                        first = false;
                        NewLine(sb, pretty, depth + 1);
                        WriteString(sb, kv.Key);
                        sb.Append(pretty ? ": " : ":");
                        Write(sb, kv.Value, pretty, depth + 1);
                    }
                    if (!first) NewLine(sb, pretty, depth);
                    sb.Append('}');
                    break;
                }
                case System.Collections.IEnumerable list: {
                    sb.Append('[');
                    var first = true;
                    foreach (var item in list) {
                        if (!first) sb.Append(',');
                        first = false;
                        NewLine(sb, pretty, depth + 1);
                        Write(sb, item, pretty, depth + 1);
                    }
                    if (!first) NewLine(sb, pretty, depth);
                    sb.Append(']');
                    break;
                }
                default:
                    WriteString(sb, value.ToString());
                    break;
            }
        }

        static void NewLine(StringBuilder sb, bool pretty, int depth) {
            if (!pretty) return;
            sb.Append('\n');
            sb.Append(' ', depth * 2);
        }

        static void WriteString(StringBuilder sb, string s) {
            sb.Append('"');
            foreach (var c in s) {
                switch (c) {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
