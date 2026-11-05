using System;

namespace QuestSaturation.Core
{
    /// <summary>
    /// 波形の再生位置を持つ。位相を dt / 周期 で積算するので、再生中に周期を変えても値は飛ばない。
    /// Pause は位相を保ち、Stop は位相を 0 に戻す。
    /// </summary>
    public sealed class WaveformPlayer
    {
        public const float MinPeriodSec = 0.01f;

        private Waveform waveform = Waveform.Sine;
        private double phase; // 周期波形は [0, 1)、Ramp は [0, 1]
        private float periodSec = 10f;

        public Waveform Waveform
        {
            get => waveform;
            set
            {
                if (value == waveform) return;
                waveform = value;
                phase = 0.0; // 波形を変えたら頭から。値のつなぎは呼び出し側がクロスフェードで行う
            }
        }

        public float PeriodSec
        {
            get => periodSec;
            set => periodSec = float.IsNaN(value) || value < MinPeriodSec ? MinPeriodSec : value;
        }

        public int Steps { get; set; } = 5;
        public float From { get; set; }
        public float To { get; set; } = -1f;

        public bool IsPlaying { get; private set; }

        /// <summary>現在の位相。周期波形は [0, 1)、Ramp は [0, 1]。</summary>
        public double Phase => phase;

        /// <summary>Ramp が終了値に達した。</summary>
        public bool IsFinished => waveform == Waveform.Ramp && phase >= 1.0;

        public float Normalized => WaveformMath.Normalized(waveform, phase, Steps);

        public float Value => From + (To - From) * Normalized;

        /// <summary>再生する。終わった Ramp は頭から再生し直す。</summary>
        public void Play()
        {
            if (IsFinished) phase = 0.0;
            IsPlaying = true;
        }

        public void Pause() => IsPlaying = false;

        public void Stop()
        {
            IsPlaying = false;
            phase = 0.0;
        }

        /// <summary>
        /// 時間を進める。Ramp がこの呼び出しで終わったら true を返す（再生は止まる）。
        /// </summary>
        public bool Tick(float dt)
        {
            if (!IsPlaying || !(dt > 0f) || float.IsInfinity(dt)) return false;

            phase += dt / (double)periodSec;

            if (waveform == Waveform.Ramp)
            {
                if (phase >= 1.0)
                {
                    phase = 1.0;
                    IsPlaying = false;
                    return true;
                }
                return false;
            }

            phase -= Math.Floor(phase);
            return false;
        }
    }
}
