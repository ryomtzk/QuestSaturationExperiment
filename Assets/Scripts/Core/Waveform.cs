using System;

namespace QuestSaturation.Core
{
    /// <summary>
    /// Auto モードの波形。値は設定ファイルに名前で保存するので、並べ替えても互換性は保たれる。
    /// </summary>
    public enum Waveform
    {
        Sine = 0,           // 開始値 → 終了値 → 開始値 をなめらかに往復
        LinearPingPong = 1, // 同じ往復を直線で
        Step = 2,           // 同じ往復を階段状に（1段ずつ上がって1段ずつ下がる）
        Ramp = 3,           // 開始値 → 終了値 の片道。終わったら終了値で止まる
    }

    /// <summary>
    /// 位相 p（1周期 = 1）から 0〜1 の正規化値を返す。0 が開始値、1 が終了値に対応する。
    /// どの波形も p = 0 で 0 から始まる。
    /// </summary>
    public static class WaveformMath
    {
        public const int MinSteps = 2;
        public const int MaxSteps = 20;

        public static bool IsPeriodic(Waveform w) => w != Waveform.Ramp;

        public static float Normalized(Waveform w, double phase, int steps)
        {
            if (double.IsNaN(phase) || double.IsInfinity(phase)) phase = 0.0;

            if (w == Waveform.Ramp)
            {
                if (phase <= 0.0) return 0f;
                if (phase >= 1.0) return 1f;
                return (float)phase;
            }

            // 周期波形は [0, 1) に折り返す
            double p = phase - Math.Floor(phase);

            switch (w)
            {
                case Waveform.Sine:
                    return (float)((1.0 - Math.Cos(2.0 * Math.PI * p)) * 0.5);

                case Waveform.LinearPingPong:
                    return (float)(p < 0.5 ? 2.0 * p : 2.0 - 2.0 * p);

                case Waveform.Step:
                {
                    int n = ClampSteps(steps);
                    // n 段（0, 1/(n-1), …, 1）を上って下りる。1周期は 2(n-1) 区間。
                    int segments = 2 * (n - 1);
                    int k = (int)Math.Floor(p * segments);
                    if (k >= segments) k = segments - 1;
                    int level = k <= n - 1 ? k : segments - k;
                    return (float)level / (n - 1);
                }

                default:
                    return 0f;
            }
        }

        public static float Evaluate(Waveform w, double phase, int steps, float from, float to) =>
            from + (to - from) * Normalized(w, phase, steps);

        public static int ClampSteps(int steps) =>
            steps < MinSteps ? MinSteps : steps > MaxSteps ? MaxSteps : steps;
    }
}
