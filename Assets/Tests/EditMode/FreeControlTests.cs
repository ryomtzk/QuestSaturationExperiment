using NUnit.Framework;
using QuestSaturation.Core;

namespace QuestSaturation.Tests
{
    public class FreeControlModelTests
    {
        private const float Dt = 1f / 90f;

        private static ColorAdjustment Settle(FreeControlModel m, float seconds = 1f)
        {
            ColorAdjustment o = m.Output;
            for (float t = 0; t < seconds; t += Dt) o = m.Tick(Dt, out _);
            return o;
        }

        private static FreeControlModel Make()
        {
            var s = FreeControlSettings.CreateDefault();
            s.Brightness = 0.1f;
            s.Contrast = 0.2f;
            s.Saturation = 0.3f;
            s.Waveform = Waveform.LinearPingPong;
            s.From = -0.5f;
            s.To = 0.5f;
            s.PeriodSec = 4f;
            return new FreeControlModel(s);
        }

        [Test]
        public void EnterStartsManualStoppedAndFadesFromCurrentOutput()
        {
            var m = Make();
            m.SetMode(FreeMode.Auto);
            m.TogglePlayPause();
            m.Enter(ColorAdjustment.Neutral);
            Assert.That(m.Mode, Is.EqualTo(FreeMode.Manual));
            Assert.That(m.Player.IsPlaying, Is.False);
            Assert.That(m.IsTransitioning, Is.True);
            var first = m.Tick(0f, out _);
            Assert.That(first.ApproximatelyEquals(ColorAdjustment.Neutral), Is.True);
            var settled = Settle(m);
            Assert.That(settled.ApproximatelyEquals(new ColorAdjustment(0.1f, 0.2f, 0.3f)), Is.True);
        }

        [Test]
        public void AutoOnlyDrivesSaturation()
        {
            var m = Make();
            m.Enter(m.Target);
            m.TogglePlayPause(); // Manual → Auto + 再生
            Assert.That(m.Mode, Is.EqualTo(FreeMode.Auto));
            Assert.That(m.Player.IsPlaying, Is.True);
            var o = Settle(m, 1f); // フェードを抜ける
            Assert.That(o.Brightness, Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(o.Contrast, Is.EqualTo(0.2f).Within(1e-5f));
            Assert.That(o.Saturation, Is.EqualTo(m.Player.Value).Within(1e-5f));
        }

        [Test]
        public void LeavingAutoReturnsToManualSaturation()
        {
            var m = Make();
            m.Enter(m.Target);
            m.TogglePlayPause();
            Settle(m, 1.3f);
            Assert.That(m.Output.Saturation, Is.Not.EqualTo(0.3f).Within(0.01f));
            m.SetMode(FreeMode.Manual);
            Assert.That(m.IsTransitioning, Is.True);
            var o = Settle(m, 1f);
            Assert.That(o.Saturation, Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(m.Player.IsPlaying, Is.False);
        }

        [Test]
        public void PlayPauseKeepsValueAndStopFades()
        {
            var m = Make();
            m.Enter(m.Target);
            m.TogglePlayPause();
            Settle(m, 1.2f);
            Assert.That(m.TogglePlayPause(), Is.False); // 一時停止
            float paused = m.Tick(Dt, out _).Saturation;
            Assert.That(m.IsTransitioning, Is.False);
            Assert.That(Settle(m, 0.5f).Saturation, Is.EqualTo(paused).Within(1e-6f));
            m.Stop();
            Assert.That(m.IsTransitioning, Is.True);
            Assert.That(Settle(m, 1f).Saturation, Is.EqualTo(-0.5f).Within(1e-5f));
        }

        [Test]
        public void ChangingWaveformInAutoFades()
        {
            var m = Make();
            m.Enter(m.Target);
            m.TogglePlayPause();
            Settle(m, 1.2f);
            float before = m.Output.Saturation;
            Assert.That(m.Adjust(MenuItem.Waveform, +1), Is.True);
            Assert.That(m.Settings.Waveform, Is.EqualTo(Waveform.Step));
            Assert.That(m.IsTransitioning, Is.True);
            float next = m.Tick(Dt, out _).Saturation;
            Assert.That(System.Math.Abs(next - before), Is.LessThan(0.05f), "no visible jump");
        }

        [Test]
        public void SmallManualAdjustIsImmediate()
        {
            var m = Make();
            m.Enter(m.Target);
            Settle(m);
            Assert.That(m.Adjust(MenuItem.Saturation, +1), Is.True);
            Assert.That(m.Settings.Saturation, Is.EqualTo(0.35f).Within(1e-6f));
            Assert.That(m.IsTransitioning, Is.False);
            Assert.That(m.Tick(Dt, out _).Saturation, Is.EqualTo(0.35f).Within(1e-6f));
        }

        [Test]
        public void ResetToDefaultFades()
        {
            var m = Make();
            m.Enter(m.Target);
            Settle(m);
            Assert.That(m.ResetToDefault(MenuItem.Saturation), Is.True);
            Assert.That(m.Settings.Saturation, Is.EqualTo(0f));
            Assert.That(m.IsTransitioning, Is.True);
            Assert.That(m.ResetToDefault(MenuItem.Saturation), Is.False, "already default");
        }

        [Test]
        public void ModeIsAMenuItem()
        {
            var m = Make();
            m.Enter(m.Target);
            Assert.That(m.Adjust(MenuItem.Mode, +1), Is.True);
            Assert.That(m.Mode, Is.EqualTo(FreeMode.Auto));
            Assert.That(m.Adjust(MenuItem.Mode, +1), Is.True); // 折り返し
            Assert.That(m.Mode, Is.EqualTo(FreeMode.Manual));
        }

        [Test]
        public void FinishedRampReplaysWithFade()
        {
            var m = Make();
            m.Settings.Waveform = Waveform.Ramp;
            m.Settings.PeriodSec = 1f;
            m.Enter(m.Target);
            m.TogglePlayPause();
            bool finished = false;
            for (int i = 0; i < 200 && !finished; i++) m.Tick(Dt, out finished);
            Assert.That(finished, Is.True);
            Assert.That(m.Player.IsPlaying, Is.False);
            Assert.That(m.TogglePlayPause(), Is.True);
            Assert.That(m.IsTransitioning, Is.True);
        }
    }

    public class ParameterMenuTests
    {
        [Test]
        public void DefinitionsAreIndexedByItem()
        {
            for (int i = 0; i < ParameterDefs.All.Length; i++)
                Assert.That((int)ParameterDefs.All[i].Item, Is.EqualTo(i));
            Assert.That(ParameterDefs.All.Length, Is.EqualTo(System.Enum.GetValues(typeof(MenuItem)).Length));
        }

        [Test]
        public void RangesMatchTheSpec()
        {
            var b = ParameterDefs.Get(MenuItem.Brightness);
            Assert.That(b.Min, Is.EqualTo(-0.5f));
            Assert.That(b.Max, Is.EqualTo(0.5f));
            foreach (var item in new[] { MenuItem.Contrast, MenuItem.Saturation, MenuItem.From, MenuItem.To })
            {
                var d = ParameterDefs.Get(item);
                Assert.That(d.Min, Is.EqualTo(-1f));
                Assert.That(d.Max, Is.EqualTo(1f));
                Assert.That(d.Step, Is.EqualTo(0.05f));
            }
            var p = ParameterDefs.Get(MenuItem.Period);
            Assert.That(p.Min, Is.EqualTo(1f), "no full-field flicker faster than once per second");
            Assert.That(p.Max, Is.EqualTo(120f));
            Assert.That(p.Step, Is.EqualTo(0.5f));
            Assert.That(ParameterDefs.Get(MenuItem.Steps).Min, Is.EqualTo(2f));
            Assert.That(ParameterDefs.Get(MenuItem.Steps).Max, Is.EqualTo(20f));
            Assert.That(ParameterDefs.Get(MenuItem.ParticipantId).Max, Is.EqualTo(999f));
        }

        [Test]
        public void FloatStepsLandOnTheGridWithoutDrift()
        {
            var d = ParameterDefs.Get(MenuItem.Saturation);
            float v = 0f;
            for (int i = 0; i < 20; i++) v = d.Adjust(v, +1);
            Assert.That(v, Is.EqualTo(1f));
            for (int i = 0; i < 45; i++) v = d.Adjust(v, -1); // 40 刻みで -1 に届き、その先は端で止まる
            Assert.That(v, Is.EqualTo(-1f));
            for (int i = 0; i < 3; i++) v = d.Adjust(v, +1);
            Assert.That(v, Is.EqualTo(-0.85f));
            Assert.That(d.Format(v), Is.EqualTo("-0.85"));
        }

        [Test]
        public void OffGridValuesMoveToTheNextGridPoint()
        {
            var d = ParameterDefs.Get(MenuItem.Saturation);
            Assert.That(d.Adjust(0.33f, +1), Is.EqualTo(0.35f));
            Assert.That(d.Adjust(0.33f, -1), Is.EqualTo(0.3f));
        }

        [Test]
        public void EnumsWrapAndIntsClamp()
        {
            var w = ParameterDefs.Get(MenuItem.Waveform);
            Assert.That(w.Adjust(3, +1), Is.EqualTo(0f));
            Assert.That(w.Adjust(0, -1), Is.EqualTo(3f));
            var s = ParameterDefs.Get(MenuItem.Steps);
            Assert.That(s.Adjust(20, +1), Is.EqualTo(20f));
            Assert.That(s.Adjust(2, -1), Is.EqualTo(2f));
            Assert.That(ParameterDefs.Get(MenuItem.ParticipantId).Adjust(0, -1), Is.EqualTo(0f));
        }

        [Test]
        public void CursorWraps()
        {
            var m = new ParameterMenu();
            Assert.That(m.Current, Is.EqualTo(MenuItem.Mode));
            m.MoveCursor(-1);
            Assert.That(m.Current, Is.EqualTo(MenuItem.ParticipantId));
            m.MoveCursor(+1);
            m.MoveCursor(+1);
            Assert.That(m.Current, Is.EqualTo(MenuItem.Saturation));
        }

        [Test]
        public void FormatIsCultureIndependent()
        {
            var prev = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                Assert.That(ParameterDefs.Get(MenuItem.Saturation).Format(0.25f), Is.EqualTo("+0.25"));
                Assert.That(ParameterDefs.Get(MenuItem.Period).Format(12.5f), Is.EqualTo("12.5"));
                Assert.That(ParameterDefs.Get(MenuItem.Waveform).Format(3), Is.EqualTo("Ramp"));
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = prev;
            }
        }
    }
}
