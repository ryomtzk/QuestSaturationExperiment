using NUnit.Framework;
using QuestSaturation.Core;

namespace QuestSaturation.Tests
{
    public class ColorAdjustmentTests
    {
        [Test]
        public void ClampKeepsValuesInRangeAndDropsNaN()
        {
            var c = new ColorAdjustment(2f, -3f, float.NaN).Clamped();
            Assert.That(c.Brightness, Is.EqualTo(1f));
            Assert.That(c.Contrast, Is.EqualTo(-1f));
            Assert.That(c.Saturation, Is.EqualTo(0f));
            Assert.That(ColorAdjustment.ClampValue(float.PositiveInfinity), Is.EqualTo(0f));
        }

        [Test]
        public void NeutralIsAllZero()
        {
            Assert.That(ColorAdjustment.Neutral, Is.EqualTo(new ColorAdjustment(0f, 0f, 0f)));
        }

        [Test]
        public void LerpClampsT()
        {
            var a = new ColorAdjustment(0f, 0f, 0f);
            var b = new ColorAdjustment(1f, -1f, 0.5f);
            Assert.That(ColorAdjustment.Lerp(a, b, 0.5f).ApproximatelyEquals(new ColorAdjustment(0.5f, -0.5f, 0.25f)), Is.True);
            Assert.That(ColorAdjustment.Lerp(a, b, 2f), Is.EqualTo(b));
            Assert.That(ColorAdjustment.Lerp(a, b, -1f), Is.EqualTo(a));
        }
    }

    public class ColorTransitionTests
    {
        [Test]
        public void InactiveTransitionPassesTargetThrough()
        {
            var t = new ColorTransition();
            var target = new ColorAdjustment(0.1f, 0.2f, 0.3f);
            Assert.That(t.Step(target, 0.01f), Is.EqualTo(target));
        }

        [Test]
        public void FadesFromCurrentOutputToTarget()
        {
            var t = new ColorTransition();
            var from = ColorAdjustment.SaturationOnly(0.8f);
            var target = ColorAdjustment.SaturationOnly(-0.8f);
            t.Begin(from, 0.5f);
            var first = t.Step(target, 0f);
            Assert.That(first.ApproximatelyEquals(from), Is.True, "starts exactly at the current output");
            var mid = t.Step(target, 0.25f);
            Assert.That(mid.Saturation, Is.EqualTo(0f).Within(1e-5f));
            var end = t.Step(target, 0.3f);
            Assert.That(end, Is.EqualTo(target));
            Assert.That(t.IsActive, Is.False);
        }

        [Test]
        public void OutputIsMonotonicDuringFade()
        {
            var t = new ColorTransition();
            t.Begin(ColorAdjustment.SaturationOnly(-1f));
            float prev = -1f;
            for (int i = 0; i < 60; i++)
            {
                float s = t.Step(ColorAdjustment.SaturationOnly(1f), 1f / 90f).Saturation;
                Assert.That(s, Is.GreaterThanOrEqualTo(prev - 1e-6f));
                prev = s;
            }
            Assert.That(prev, Is.EqualTo(1f));
        }
    }
}
