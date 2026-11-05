using System;
using System.Globalization;

namespace QuestSaturation.Core
{
    /// <summary>
    /// OVRPassthroughLayer.SetBrightnessContrastSaturation に渡す3つの値。
    /// どれも [-1, 1] で、0 は「変化なし」。
    /// </summary>
    public readonly struct ColorAdjustment : IEquatable<ColorAdjustment>
    {
        public const float Min = -1f;
        public const float Max = 1f;

        public readonly float Brightness;
        public readonly float Contrast;
        public readonly float Saturation;

        public ColorAdjustment(float brightness, float contrast, float saturation)
        {
            Brightness = brightness;
            Contrast = contrast;
            Saturation = saturation;
        }

        public static ColorAdjustment Neutral => default;

        public static ColorAdjustment SaturationOnly(float saturation) => new ColorAdjustment(0f, 0f, saturation);

        /// <summary>[-1, 1] に収める。NaN と無限大はネイティブ側に渡らないよう 0 にする。</summary>
        public static float ClampValue(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            if (v < Min) return Min;
            if (v > Max) return Max;
            return v;
        }

        public ColorAdjustment Clamped() =>
            new ColorAdjustment(ClampValue(Brightness), ClampValue(Contrast), ClampValue(Saturation));

        public ColorAdjustment WithSaturation(float saturation) => new ColorAdjustment(Brightness, Contrast, saturation);

        /// <summary>線形補間。t は [0, 1] に収める。</summary>
        public static ColorAdjustment Lerp(ColorAdjustment a, ColorAdjustment b, float t)
        {
            if (float.IsNaN(t) || t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
            return new ColorAdjustment(
                a.Brightness + (b.Brightness - a.Brightness) * t,
                a.Contrast + (b.Contrast - a.Contrast) * t,
                a.Saturation + (b.Saturation - a.Saturation) * t);
        }

        public bool ApproximatelyEquals(ColorAdjustment other, float epsilon = 1e-5f) =>
            Math.Abs(Brightness - other.Brightness) <= epsilon &&
            Math.Abs(Contrast - other.Contrast) <= epsilon &&
            Math.Abs(Saturation - other.Saturation) <= epsilon;

        public bool Equals(ColorAdjustment other) =>
            Brightness.Equals(other.Brightness) && Contrast.Equals(other.Contrast) && Saturation.Equals(other.Saturation);

        public override bool Equals(object obj) => obj is ColorAdjustment other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Brightness.GetHashCode();
                h = (h * 397) ^ Contrast.GetHashCode();
                h = (h * 397) ^ Saturation.GetHashCode();
                return h;
            }
        }

        public static bool operator ==(ColorAdjustment a, ColorAdjustment b) => a.Equals(b);
        public static bool operator !=(ColorAdjustment a, ColorAdjustment b) => !a.Equals(b);

        public override string ToString() => string.Format(CultureInfo.InvariantCulture,
            "(b={0:0.###}, c={1:0.###}, s={2:0.###})", Brightness, Contrast, Saturation);
    }
}
