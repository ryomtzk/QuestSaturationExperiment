using NUnit.Framework;
using QuestSaturation.Core;

namespace QuestSaturation.Tests
{
    public class WaveformTests
    {
        private const float Eps = 1e-5f;

        [TestCase(Waveform.Sine)]
        [TestCase(Waveform.LinearPingPong)]
        [TestCase(Waveform.Step)]
        [TestCase(Waveform.Ramp)]
        public void EveryWaveformStartsAtFrom(Waveform w)
        {
            Assert.That(WaveformMath.Normalized(w, 0.0, 5), Is.EqualTo(0f).Within(Eps));
            Assert.That(WaveformMath.Evaluate(w, 0.0, 5, 0.3f, -0.7f), Is.EqualTo(0.3f).Within(Eps));
        }

        [TestCase(Waveform.Sine)]
        [TestCase(Waveform.LinearPingPong)]
        [TestCase(Waveform.Step)]
        public void PeriodicWaveformsPeakAtHalfCycle(Waveform w)
        {
            Assert.That(WaveformMath.Normalized(w, 0.5, 5), Is.EqualTo(1f).Within(Eps));
        }

        [Test]
        public void SineIsSmoothAndSymmetric()
        {
            Assert.That(WaveformMath.Normalized(Waveform.Sine, 0.25, 5), Is.EqualTo(0.5f).Within(Eps));
            Assert.That(WaveformMath.Normalized(Waveform.Sine, 0.75, 5), Is.EqualTo(0.5f).Within(Eps));
            Assert.That(WaveformMath.Normalized(Waveform.Sine, 1.0, 5), Is.EqualTo(0f).Within(Eps));
        }

        [Test]
        public void StepGoesUpAndDownOneLevelAtATime()
        {
            const int n = 4; // 0, 1/3, 2/3, 1
            float prev = WaveformMath.Normalized(Waveform.Step, 0.0, n);
            float max = 0f;
            for (int i = 1; i <= 2000; i++)
            {
                double p = i / 2000.0;
                float v = WaveformMath.Normalized(Waveform.Step, p, n);
                float jump = System.Math.Abs(v - prev);
                Assert.That(jump, Is.LessThanOrEqualTo(1f / (n - 1) + Eps), $"jump at p={p}");
                if (v > max) max = v;
                prev = v;
            }
            Assert.That(max, Is.EqualTo(1f).Within(Eps));
            // 最大値から最小値へ一気に跳ばない（周期の終わりは1段目）
            Assert.That(WaveformMath.Normalized(Waveform.Step, 0.999, n), Is.EqualTo(1f / 3f).Within(Eps));
        }

        [Test]
        public void StepClampsStepCount()
        {
            Assert.That(WaveformMath.Normalized(Waveform.Step, 0.5, 0), Is.EqualTo(1f).Within(Eps));
            Assert.That(WaveformMath.Normalized(Waveform.Step, 0.3, 1000), Is.InRange(0f, 1f));
        }

        [Test]
        public void RampIsOneWayAndHolds()
        {
            Assert.That(WaveformMath.Normalized(Waveform.Ramp, 0.5, 5), Is.EqualTo(0.5f).Within(Eps));
            Assert.That(WaveformMath.Normalized(Waveform.Ramp, 1.0, 5), Is.EqualTo(1f));
            Assert.That(WaveformMath.Normalized(Waveform.Ramp, 7.3, 5), Is.EqualTo(1f));
        }

        [Test]
        public void NaNPhaseIsTreatedAsZero()
        {
            Assert.That(WaveformMath.Normalized(Waveform.Sine, double.NaN, 5), Is.EqualTo(0f));
        }
    }

    public class WaveformPlayerTests
    {
        private const float Eps = 1e-4f;

        private static WaveformPlayer Make(Waveform w, float period = 10f)
        {
            return new WaveformPlayer { Waveform = w, PeriodSec = period, From = 0f, To = -1f, Steps = 5 };
        }

        [Test]
        public void StoppedPlayerShowsFrom()
        {
            var p = Make(Waveform.Sine);
            p.From = 0.4f;
            Assert.That(p.Value, Is.EqualTo(0.4f).Within(Eps));
            p.Tick(3f); // 停止中は進まない
            Assert.That(p.Value, Is.EqualTo(0.4f).Within(Eps));
        }

        [Test]
        public void PauseKeepsPhaseAndStopRewinds()
        {
            var p = Make(Waveform.LinearPingPong);
            p.Play();
            p.Tick(2.5f); // 1/4 周期
            float v = p.Value;
            p.Pause();
            p.Tick(3f);
            Assert.That(p.Value, Is.EqualTo(v).Within(Eps));
            Assert.That(p.Phase, Is.EqualTo(0.25).Within(1e-9));

            p.Play();
            p.Tick(2.5f);
            Assert.That(p.Phase, Is.EqualTo(0.5).Within(1e-9));

            p.Stop();
            Assert.That(p.Phase, Is.EqualTo(0.0));
            Assert.That(p.IsPlaying, Is.False);
        }

        [Test]
        public void ChangingPeriodDoesNotJump()
        {
            var p = Make(Waveform.Sine, 10f);
            p.Play();
            p.Tick(3f);
            float before = p.Value;
            p.PeriodSec = 2f;
            Assert.That(p.Value, Is.EqualTo(before).Within(Eps));
            p.Tick(0.001f);
            Assert.That(System.Math.Abs(p.Value - before), Is.LessThan(0.01f));
        }

        [Test]
        public void RampFinishesOnceAndHolds()
        {
            var p = Make(Waveform.Ramp, 2f);
            p.Play();
            Assert.That(p.Tick(1f), Is.False);
            Assert.That(p.Value, Is.EqualTo(-0.5f).Within(Eps));
            Assert.That(p.Tick(1.5f), Is.True);
            Assert.That(p.IsFinished, Is.True);
            Assert.That(p.IsPlaying, Is.False);
            Assert.That(p.Value, Is.EqualTo(-1f).Within(Eps));
            Assert.That(p.Tick(1f), Is.False);

            p.Play(); // 終わった Ramp は頭から
            Assert.That(p.Phase, Is.EqualTo(0.0));
            Assert.That(p.IsPlaying, Is.True);
        }

        [Test]
        public void ChangingWaveformRewinds()
        {
            var p = Make(Waveform.Sine);
            p.Play();
            p.Tick(4f);
            p.Waveform = Waveform.Step;
            Assert.That(p.Phase, Is.EqualTo(0.0));
            Assert.That(p.IsPlaying, Is.True);
        }

        [Test]
        public void LongRunsStayPrecise()
        {
            var p = Make(Waveform.LinearPingPong, 10f);
            p.Play();
            // 90Hz で 10 時間
            const float dt = 1f / 90f;
            const int frames = 90 * 3600 * 10;
            for (int i = 0; i < frames; i++) p.Tick(dt);
            double expected = (frames * (double)dt / 10.0) % 1.0;
            Assert.That(p.Phase, Is.EqualTo(expected).Within(1e-4));
            Assert.That(p.Phase, Is.InRange(0.0, 1.0));
        }

        [Test]
        public void BadPeriodAndDtAreIgnored()
        {
            var p = Make(Waveform.Sine);
            p.PeriodSec = 0f;
            Assert.That(p.PeriodSec, Is.EqualTo(WaveformPlayer.MinPeriodSec));
            p.PeriodSec = float.NaN;
            Assert.That(p.PeriodSec, Is.EqualTo(WaveformPlayer.MinPeriodSec));
            p.PeriodSec = 10f;
            p.Play();
            p.Tick(float.NaN);
            p.Tick(-1f);
            p.Tick(float.PositiveInfinity);
            Assert.That(p.Phase, Is.EqualTo(0.0));
        }
    }
}
