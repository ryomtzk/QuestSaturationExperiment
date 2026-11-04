using System;
using System.Globalization;
using System.Text;

namespace QuestSaturation.Core
{
    /// <summary>セッション CSV の列。<see cref="CsvFormat.Columns"/> と同じ順・同じ数。</summary>
    public enum CsvColumn
    {
        SessionId = 0,
        ParticipantId,
        WallClockUtc,
        TRealtimeSec,
        TGameSec,
        Frame,
        UnscaledDt,
        State,
        TrialIndex,
        Condition,
        TrialStage,
        TrialElapsedSec,
        TargetSaturation,
        PassthroughVisible,
        Saturation,
        Brightness,
        Contrast,
        FreeMode,
        Waveform,
        Playing,
        ActiveController,
        Event,
        Detail,
    }

    /// <summary>CSV の書式。数値は必ず InvariantCulture で書く（端末の言語設定で小数点が変わらないように）。</summary>
    public static class CsvFormat
    {
        public static readonly string[] Columns =
        {
            "session_id",
            "participant_id",
            "wall_clock_utc",
            "t_realtime_s",
            "t_game_s",
            "frame",
            "unscaled_dt",
            "state",
            "trial_index",
            "condition",
            "trial_stage",
            "trial_elapsed_s",
            "target_saturation",
            "passthrough_visible",
            "saturation",
            "brightness",
            "contrast",
            "free_mode",
            "waveform",
            "playing",
            "active_controller",
            "event",
            "detail",
        };

        public static int ColumnCount => Columns.Length;

        public static string Header => string.Join(",", Columns);

        /// <summary>カンマ・引用符・改行を含む値は引用符で囲み、中の引用符は2つ重ねる。</summary>
        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            bool needs = value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0 ||
                         value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0 ||
                         value[0] == ' ' || value[value.Length - 1] == ' ';
            return needs ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        public static string Float(float v) => float.IsNaN(v) || float.IsInfinity(v)
            ? ""
            : v.ToString("0.######", CultureInfo.InvariantCulture);

        public static string Double(double v) => double.IsNaN(v) || double.IsInfinity(v)
            ? ""
            : v.ToString("0.######", CultureInfo.InvariantCulture);

        public static string Int(long v) => v.ToString(CultureInfo.InvariantCulture);

        public static string Bool(bool v) => v ? "1" : "0";

        /// <summary>ISO 8601（UTC、ミリ秒まで）。</summary>
        public static string Utc(DateTime t) =>
            t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// CSV の1行。列数は常にヘッダーと同じになる。使い回すときは <see cref="Clear"/> する。
    /// </summary>
    public sealed class CsvRow
    {
        private readonly string[] values = new string[CsvFormat.ColumnCount];
        private readonly StringBuilder sb = new StringBuilder(256);

        public CsvRow Set(CsvColumn c, string v)
        {
            values[(int)c] = v;
            return this;
        }

        public CsvRow Set(CsvColumn c, float v) => Set(c, CsvFormat.Float(v));
        public CsvRow Set(CsvColumn c, double v) => Set(c, CsvFormat.Double(v));
        public CsvRow Set(CsvColumn c, int v) => Set(c, CsvFormat.Int(v));
        public CsvRow Set(CsvColumn c, long v) => Set(c, CsvFormat.Int(v));
        public CsvRow Set(CsvColumn c, bool v) => Set(c, CsvFormat.Bool(v));

        public string Get(CsvColumn c) => values[(int)c] ?? "";

        public void Clear() => Array.Clear(values, 0, values.Length);

        /// <summary>改行なしの1行。</summary>
        public string ToLine()
        {
            sb.Clear();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(CsvFormat.Escape(values[i]));
            }
            return sb.ToString();
        }
    }
}
