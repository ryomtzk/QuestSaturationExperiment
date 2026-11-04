using System;
using System.Globalization;

namespace QuestSaturation.Core
{
    /// <summary>FreeControl のメニュー項目。表示順もこの順。</summary>
    public enum MenuItem
    {
        Mode = 0,
        Saturation = 1,
        Brightness = 2,
        Contrast = 3,
        Waveform = 4,
        From = 5,
        To = 6,
        Period = 7,
        Steps = 8,
        ParticipantId = 9,
    }

    public enum ParameterKind
    {
        Float, // 押し続けるとリピート
        Int,   // 押し続けるとリピート
        Enum,  // スティックを倒すたびに1つ。端で折り返す
    }

    /// <summary>メニュー項目1つの範囲・刻み幅・既定値。</summary>
    public sealed class ParameterDef
    {
        public readonly MenuItem Item;
        public readonly string Label;
        public readonly ParameterKind Kind;
        public readonly float Min;
        public readonly float Max;
        public readonly float Step;
        public readonly float Default;
        public readonly string[] Options; // Enum のときの表示名

        public ParameterDef(MenuItem item, string label, ParameterKind kind, float min, float max, float step,
            float @default, string[] options = null)
        {
            Item = item;
            Label = label;
            Kind = kind;
            Min = min;
            Max = max;
            Step = step;
            Default = @default;
            Options = options;
        }

        public bool Repeats => Kind != ParameterKind.Enum;

        /// <summary>
        /// 不正値を補正する。NaN と無限大は既定値に、範囲外は端に、Int と Enum は整数に丸める。
        /// Float は刻みに丸めない（保存された値をそのまま使えるように）。
        /// </summary>
        public float Sanitize(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return Default;
            if (Kind != ParameterKind.Float) v = (float)Math.Round(v, MidpointRounding.AwayFromZero);
            if (v < Min) return Min;
            if (v > Max) return Max;
            return v;
        }

        /// <summary>
        /// 1刻み動かした値。Float は刻みの格子に合わせ、Enum は端で折り返す。
        /// </summary>
        public float Adjust(float current, int direction)
        {
            if (direction == 0) return Sanitize(current);
            int dir = direction > 0 ? 1 : -1;

            if (Kind == ParameterKind.Enum)
            {
                int count = (int)(Max - Min) + 1;
                int index = (int)Sanitize(current) - (int)Min;
                index = ((index + dir) % count + count) % count;
                return Min + index;
            }

            float cur = Sanitize(current);
            // 格子からずれた値（古い設定など）でも、次の格子点へ動かす
            double steps = ((double)cur - Min) / Step;
            double nearest = Math.Round(steps);
            if (Math.Abs(steps - nearest) < 1e-3) steps = nearest; // float の誤差で格子点の手前に見える値を吸収
            double snapped = dir > 0 ? Math.Floor(steps) + 1 : Math.Ceiling(steps) - 1;
            double next = Min + snapped * Step;
            // float の誤差を残さないよう、刻みの桁で丸める
            next = Math.Round(next, 4);
            if (next < Min) next = Min;
            if (next > Max) next = Max;
            return (float)next;
        }

        public string Format(float v)
        {
            switch (Kind)
            {
                case ParameterKind.Enum:
                {
                    int i = (int)Sanitize(v) - (int)Min;
                    return Options != null && i >= 0 && i < Options.Length ? Options[i] : i.ToString(CultureInfo.InvariantCulture);
                }
                case ParameterKind.Int:
                    return ((int)Sanitize(v)).ToString(CultureInfo.InvariantCulture);
                default:
                    return v.ToString(Item == MenuItem.Period ? "0.0" : "+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
            }
        }
    }

    public static class ParameterDefs
    {
        public static readonly string[] ModeNames = { "Manual", "Auto" };
        public static readonly string[] WaveformNames = { "Sine", "Linear", "Step", "Ramp" };

        /// <summary>
        /// 周期の下限 1 秒は、全視野が毎秒1回より速く明滅しないようにするため。
        /// 明るさは見た目の変化が大きいので ±0.5 に抑える。
        /// </summary>
        public static readonly ParameterDef[] All =
        {
            new ParameterDef(MenuItem.Mode, "Mode", ParameterKind.Enum, 0, 1, 1, 0, ModeNames),
            new ParameterDef(MenuItem.Saturation, "Saturation", ParameterKind.Float, -1f, 1f, 0.05f, 0f),
            new ParameterDef(MenuItem.Brightness, "Brightness", ParameterKind.Float, -0.5f, 0.5f, 0.05f, 0f),
            new ParameterDef(MenuItem.Contrast, "Contrast", ParameterKind.Float, -1f, 1f, 0.05f, 0f),
            new ParameterDef(MenuItem.Waveform, "Waveform", ParameterKind.Enum, 0, 3, 1, 0, WaveformNames),
            new ParameterDef(MenuItem.From, "Wave from", ParameterKind.Float, -1f, 1f, 0.05f, 0f),
            new ParameterDef(MenuItem.To, "Wave to", ParameterKind.Float, -1f, 1f, 0.05f, -1f),
            new ParameterDef(MenuItem.Period, "Period s", ParameterKind.Float, 1f, 120f, 0.5f, 10f),
            new ParameterDef(MenuItem.Steps, "Steps", ParameterKind.Int, WaveformMath.MinSteps, WaveformMath.MaxSteps, 1, 5),
            new ParameterDef(MenuItem.ParticipantId, "Participant", ParameterKind.Int, 0, 999, 1, 0),
        };

        public static ParameterDef Get(MenuItem item) => All[(int)item];
    }

    /// <summary>メニューのカーソル。上下で折り返す。</summary>
    public sealed class ParameterMenu
    {
        public int Cursor { get; private set; }

        public MenuItem Current => ParameterDefs.All[Cursor].Item;

        public ParameterDef CurrentDef => ParameterDefs.All[Cursor];

        public void MoveCursor(int delta)
        {
            if (delta == 0) return;
            int n = ParameterDefs.All.Length;
            Cursor = ((Cursor + (delta > 0 ? 1 : -1)) % n + n) % n;
        }

        public void ResetCursor() => Cursor = 0;
    }
}
