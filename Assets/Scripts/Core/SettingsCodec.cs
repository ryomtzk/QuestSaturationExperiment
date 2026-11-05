using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace QuestSaturation.Core
{
    /// <summary>
    /// FreeControlSettings と JSON の相互変換。バージョン番号を付けて保存し、読み込み時に不正値を補正する。
    /// JsonUtility はエディタ外のテストで使えないため、平らなオブジェクトだけを扱う小さな実装にしている。
    /// </summary>
    public static class SettingsCodec
    {
        public const int CurrentVersion = 1;

        public static string Encode(FreeControlSettings s)
        {
            var sb = new StringBuilder(256);
            sb.Append('{');
            Append(sb, "version", CurrentVersion.ToString(CultureInfo.InvariantCulture), first: true);
            Append(sb, "brightness", F(s.Brightness));
            Append(sb, "contrast", F(s.Contrast));
            Append(sb, "saturation", F(s.Saturation));
            Append(sb, "waveform", Quote(s.Waveform.ToString()));
            Append(sb, "from", F(s.From));
            Append(sb, "to", F(s.To));
            Append(sb, "periodSec", F(s.PeriodSec));
            Append(sb, "steps", s.Steps.ToString(CultureInfo.InvariantCulture));
            Append(sb, "participantId", s.ParticipantId.ToString(CultureInfo.InvariantCulture));
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// JSON を読む。壊れている・空のときは既定値を返し、warning に理由を入れる。
        /// 項目ごとに、欠けていれば既定値、範囲外なら範囲内に補正する。
        /// </summary>
        public static FreeControlSettings Decode(string json, out string warning)
        {
            warning = null;
            var result = FreeControlSettings.CreateDefault();
            if (string.IsNullOrWhiteSpace(json))
            {
                warning = "empty";
                return result;
            }

            Dictionary<string, string> map;
            try
            {
                map = FlatJson.Parse(json);
            }
            catch (FormatException e)
            {
                warning = "parse_error: " + e.Message;
                return result;
            }

            if (!map.TryGetValue("version", out var ver) || !int.TryParse(ver, NumberStyles.Integer, CultureInfo.InvariantCulture, out int version))
                warning = "missing_version";
            else if (version > CurrentVersion)
                warning = "newer_version:" + version.ToString(CultureInfo.InvariantCulture);

            ReadFloat(map, "brightness", MenuItem.Brightness, result);
            ReadFloat(map, "contrast", MenuItem.Contrast, result);
            ReadFloat(map, "saturation", MenuItem.Saturation, result);
            ReadFloat(map, "from", MenuItem.From, result);
            ReadFloat(map, "to", MenuItem.To, result);
            ReadFloat(map, "periodSec", MenuItem.Period, result);
            ReadFloat(map, "steps", MenuItem.Steps, result);
            ReadFloat(map, "participantId", MenuItem.ParticipantId, result);

            if (map.TryGetValue("waveform", out var w) && Enum.TryParse(w, ignoreCase: false, out Waveform wf) &&
                Enum.IsDefined(typeof(Waveform), wf) && !int.TryParse(w, out _))
            {
                result.Waveform = wf;
            }

            return result;
        }

        private static void ReadFloat(Dictionary<string, string> map, string key, MenuItem item, FreeControlSettings s)
        {
            if (!map.TryGetValue(key, out var raw)) return;
            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) return;
            s.Set(item, v); // Set が範囲内に補正する
        }

        private static void Append(StringBuilder sb, string key, string rawValue, bool first = false)
        {
            if (!first) sb.Append(',');
            sb.Append('"').Append(key).Append("\":").Append(rawValue);
        }

        private static string F(float v) => float.IsNaN(v) || float.IsInfinity(v)
            ? "0"
            : v.ToString("R", CultureInfo.InvariantCulture);

        private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        /// <summary>入れ子のない JSON オブジェクトを、キー → 値の文字列に分解する。</summary>
        internal static class FlatJson
        {
            public static Dictionary<string, string> Parse(string json)
            {
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                int i = 0;
                SkipWs(json, ref i);
                Expect(json, ref i, '{');
                SkipWs(json, ref i);
                if (Peek(json, i) == '}') return map;

                while (true)
                {
                    SkipWs(json, ref i);
                    string key = ReadString(json, ref i);
                    SkipWs(json, ref i);
                    Expect(json, ref i, ':');
                    SkipWs(json, ref i);
                    string value = Peek(json, i) == '"' ? ReadString(json, ref i) : ReadBare(json, ref i);
                    map[key] = value;
                    SkipWs(json, ref i);
                    char c = Peek(json, i);
                    if (c == ',') { i++; continue; }
                    if (c == '}') { i++; break; }
                    throw new FormatException("expected , or } at " + i);
                }

                SkipWs(json, ref i);
                if (i != json.Length) throw new FormatException("trailing characters at " + i);
                return map;
            }

            private static char Peek(string s, int i) => i < s.Length ? s[i] : '\0';

            private static void SkipWs(string s, ref int i)
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            }

            private static void Expect(string s, ref int i, char c)
            {
                if (Peek(s, i) != c) throw new FormatException("expected '" + c + "' at " + i);
                i++;
            }

            private static string ReadString(string s, ref int i)
            {
                Expect(s, ref i, '"');
                var sb = new StringBuilder();
                while (true)
                {
                    if (i >= s.Length) throw new FormatException("unterminated string");
                    char c = s[i++];
                    if (c == '"') break;
                    if (c == '\\')
                    {
                        if (i >= s.Length) throw new FormatException("bad escape");
                        char e = s[i++];
                        switch (e)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'u':
                                sb.Append(ReadHex4(s, ref i));
                                break;
                            default: throw new FormatException("bad escape \\" + e);
                        }
                    }
                    else sb.Append(c);
                }
                return sb.ToString();
            }

            // \uXXXX の4桁。Convert.ToInt32 は "-041" なども受け付けて別の例外を投げるので、1桁ずつ読む
            private static char ReadHex4(string s, ref int i)
            {
                if (i + 4 > s.Length) throw new FormatException("bad \\u escape");
                int code = 0;
                for (int k = 0; k < 4; k++)
                {
                    char h = s[i + k];
                    int d;
                    if (h >= '0' && h <= '9') d = h - '0';
                    else if (h >= 'a' && h <= 'f') d = h - 'a' + 10;
                    else if (h >= 'A' && h <= 'F') d = h - 'A' + 10;
                    else throw new FormatException("bad \\u escape");
                    code = code * 16 + d;
                }
                i += 4;
                return (char)code;
            }

            private static string ReadBare(string s, ref int i)
            {
                int start = i;
                while (i < s.Length && s[i] != ',' && s[i] != '}' && !char.IsWhiteSpace(s[i]))
                {
                    if (s[i] == '{' || s[i] == '[' || s[i] == '"') throw new FormatException("nested values are not supported");
                    i++;
                }
                if (i == start) throw new FormatException("missing value at " + start);
                return s.Substring(start, i - start);
            }
        }
    }
}
