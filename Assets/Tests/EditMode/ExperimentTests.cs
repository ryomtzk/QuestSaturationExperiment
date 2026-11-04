using System.Linq;
using NUnit.Framework;
using QuestSaturation.Core;
using UnityEngine;

namespace QuestSaturation.Tests
{
    public class ExperimentProfileTests
    {
        // 変更前の SaturationCurve.Evaluate をそのまま写したもの
        private static float OriginalTargetFor(Condition c)
        {
            switch (c)
            {
                case Condition.A: return 0.0f;
                case Condition.B: return -0.8f;
                case Condition.C: return 0.8f;
                default: return 0.0f;
            }
        }

        private static float OriginalEvaluate(Condition c, float elapsedSec)
        {
            const float HOLD_SEC = 3f, RAMP_SEC = 10f;
            float target = OriginalTargetFor(c);
            if (elapsedSec < HOLD_SEC) return 0f;
            float rampElapsed = elapsedSec - HOLD_SEC;
            if (rampElapsed >= RAMP_SEC) return target;
            return Mathf.Lerp(0f, target, rampElapsed / RAMP_SEC);
        }

        [Test]
        public void CanonicalProfileMatchesOriginalCurveExactly()
        {
            foreach (Condition c in new[] { Condition.A, Condition.B, Condition.C })
            {
                Assert.That(SaturationCurve.TargetFor(c), Is.EqualTo(OriginalTargetFor(c)));
                for (int i = -100; i <= 20000; i++)
                {
                    float t = i * 0.001f;
                    Assert.That(SaturationCurve.Evaluate(c, t), Is.EqualTo(OriginalEvaluate(c, t)), $"{c} at {t}");
                    Assert.That(ExperimentProfile.Canonical.Evaluate(c, t), Is.EqualTo(OriginalEvaluate(c, t)));
                }
            }
        }

        [Test]
        public void ConditionValuesAreStable()
        {
            Assert.That((int)Condition.A, Is.EqualTo(0));
            Assert.That((int)Condition.B, Is.EqualTo(1));
            Assert.That((int)Condition.C, Is.EqualTo(2));
        }

        [Test]
        public void StagesFollowHoldRampTarget()
        {
            var p = ExperimentProfile.Canonical;
            Assert.That(p.StageAt(0f), Is.EqualTo(TrialStage.Hold));
            Assert.That(p.StageAt(2.999f), Is.EqualTo(TrialStage.Hold));
            Assert.That(p.StageAt(3f), Is.EqualTo(TrialStage.Ramp));
            Assert.That(p.StageAt(12.999f), Is.EqualTo(TrialStage.Ramp));
            Assert.That(p.StageAt(13f), Is.EqualTo(TrialStage.Target));
        }

        [Test]
        public void DescribeIsCultureIndependent()
        {
            var prev = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                Assert.That(ExperimentProfile.Canonical.Describe(), Is.EqualTo("canonical-v1;hold=3;ramp=10;A=0;B=-0.8;C=0.8"));
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = prev;
            }
        }
    }

    public class ExperimentFlowTests
    {
        private static FlowCommand[] Commands(ExperimentFlow f) => f.Actions.Select(a => a.Command).ToArray();

        private static void AssertNeutralBeforeEnable(ExperimentFlow f, float expectedSaturation = 0f)
        {
            var cmds = Commands(f);
            int enable = System.Array.IndexOf(cmds, FlowCommand.EnablePassthrough);
            Assert.That(enable, Is.GreaterThan(0), "EnablePassthrough must exist and not be first");
            int lastColorBefore = System.Array.LastIndexOf(cmds, FlowCommand.ApplyColor, enable);
            Assert.That(lastColorBefore, Is.GreaterThanOrEqualTo(0), "ApplyColor must come before EnablePassthrough");
            var color = f.Actions[lastColorBefore].Color;
            Assert.That(color, Is.EqualTo(ColorAdjustment.SaturationOnly(expectedSaturation)));
        }

        [Test]
        public void BeginsInSelectionWithNeutralColorAndHiddenPanel()
        {
            var f = new ExperimentFlow();
            f.Begin(0f);
            Assert.That(f.State, Is.EqualTo(FlowState.Selection));
            var cmds = Commands(f);
            Assert.That(cmds, Does.Contain(FlowCommand.DisablePassthrough));
            Assert.That(cmds, Does.Contain(FlowCommand.HidePanel));
            Assert.That(cmds, Does.Contain(FlowCommand.ShowSpheres));
            Assert.That(cmds, Does.Contain(FlowCommand.UseSelectionBackground));
            Assert.That(f.Actions.First(a => a.Command == FlowCommand.ApplyColor).Color, Is.EqualTo(ColorAdjustment.Neutral));
        }

        [Test]
        public void SelectingSendsTimeZeroValueBeforeEnablingPassthrough()
        {
            var f = new ExperimentFlow();
            f.Begin(0f);
            Assert.That(f.SelectCondition(Condition.C, 5f), Is.True);
            Assert.That(f.State, Is.EqualTo(FlowState.Trial));
            Assert.That(f.TrialIndex, Is.EqualTo(1));
            AssertNeutralBeforeEnable(f);
            Assert.That(Commands(f), Does.Contain(FlowCommand.HidePanel));
            Assert.That(f.Events.Select(e => e.Name), Does.Contain("trial_start"));
        }

        [Test]
        public void TrialFollowsCanonicalCurveFromSelectionTime()
        {
            var f = new ExperimentFlow();
            f.Begin(0f);
            f.SelectCondition(Condition.B, 100f);
            foreach (float dt in new[] { 0f, 1f, 3f, 5f, 8f, 13f, 20f })
            {
                f.Tick(100f + dt);
                Assert.That(f.Actions.Single().Color.Saturation, Is.EqualTo(SaturationCurve.Evaluate(Condition.B, dt)));
            }
        }

        [Test]
        public void RampEventsFireOnceEach()
        {
            var f = new ExperimentFlow();
            f.Begin(0f);
            f.SelectCondition(Condition.C, 0f);
            int starts = 0, ends = 0;
            for (int i = 0; i <= 20 * 90; i++)
            {
                f.Tick(i / 90f);
                starts += f.Events.Count(e => e.Name == "ramp_start");
                ends += f.Events.Count(e => e.Name == "ramp_end");
            }
            Assert.That(starts, Is.EqualTo(1));
            Assert.That(ends, Is.EqualTo(1));
        }

        [Test]
        public void BigFrameGapStillReportsBothRampEvents()
        {
            var f = new ExperimentFlow();
            f.Begin(0f);
            f.SelectCondition(Condition.C, 0f);
            f.Tick(1f);
            f.Tick(30f);
            var names = f.Events.Select(e => e.Name).ToArray();
            Assert.That(names, Is.EqualTo(new[] { "ramp_start", "ramp_end" }));
        }

        [Test]
        public void ResetReturnsToSelectionWithNeutralColor()
        {
            var f = new ExperimentFlow();
            f.Begin(0f);
            f.SelectCondition(Condition.C, 0f);
            f.Tick(20f);
            Assert.That(f.RequestReset(21f), Is.True);
            Assert.That(f.State, Is.EqualTo(FlowState.Selection));
            var cmds = Commands(f);
            int color = System.Array.IndexOf(cmds, FlowCommand.ApplyColor);
            int disable = System.Array.IndexOf(cmds, FlowCommand.DisablePassthrough);
            Assert.That(color, Is.GreaterThanOrEqualTo(0));
            Assert.That(color, Is.LessThan(disable));
            Assert.That(f.Actions[color].Color, Is.EqualTo(ColorAdjustment.Neutral));
            var end = f.Events.First(e => e.Name == "trial_end");
            Assert.That(end.Detail, Does.Contain("reason=reset"));
            Assert.That(end.Detail, Does.Contain("interrupted=false"));
        }

        [Test]
        public void SecondTrialAfterCDoesNotStartFromPreviousValue()
        {
            var f = new ExperimentFlow();
            f.Begin(0f);
            f.SelectCondition(Condition.C, 0f);
            f.Tick(20f); // +0.8
            f.RequestReset(21f);
            f.SelectCondition(Condition.B, 30f);
            AssertNeutralBeforeEnable(f);
            Assert.That(f.TrialIndex, Is.EqualTo(2));
        }

        [Test]
        public void InterruptionsAreReportedInTrialEnd()
        {
            var f = new ExperimentFlow();
            f.Begin(0f);
            f.NoteInterruption("hmd_unmounted"); // Selection 中は記録しない
            f.SelectCondition(Condition.A, 0f);
            f.NoteInterruption("hmd_unmounted");
            f.NoteInterruption("input_focus_lost");
            f.NoteInterruption("hmd_unmounted");
            f.RequestReset(5f);
            var end = f.Events.First(e => e.Name == "trial_end");
            Assert.That(end.Detail, Does.Contain("interrupted=true"));
            Assert.That(end.Detail, Does.Contain("interruptions=hmd_unmounted|input_focus_lost"));
        }

        [Test]
        public void FreeControlIsReachableOnlyFromSelection()
        {
            var f = new ExperimentFlow();
            f.Begin(0f);
            f.SelectCondition(Condition.A, 0f);
            Assert.That(f.ToggleFreeControl(1f), Is.False);
            Assert.That(f.State, Is.EqualTo(FlowState.Trial));
            Assert.That(f.Actions, Is.Empty);

            f.RequestReset(2f);
            Assert.That(f.ToggleFreeControl(3f), Is.True);
            Assert.That(f.State, Is.EqualTo(FlowState.FreeControl));
            AssertNeutralBeforeEnable(f);
            var cmds = Commands(f);
            Assert.That(System.Array.IndexOf(cmds, FlowCommand.EnterFreeControl), Is.GreaterThan(System.Array.IndexOf(cmds, FlowCommand.EnablePassthrough)));
            Assert.That(cmds, Does.Contain(FlowCommand.ShowPanel));

            Assert.That(f.ToggleFreeControl(4f), Is.True);
            Assert.That(f.State, Is.EqualTo(FlowState.Selection));
            cmds = Commands(f);
            Assert.That(cmds[0], Is.EqualTo(FlowCommand.ExitFreeControl));
            Assert.That(cmds, Does.Contain(FlowCommand.HidePanel));
        }

        [Test]
        public void SelectionIsIgnoredOutsideSelection()
        {
            var f = new ExperimentFlow();
            Assert.That(f.SelectCondition(Condition.A, 0f), Is.False, "before Begin");
            f.Begin(0f);
            f.ToggleFreeControl(0f);
            Assert.That(f.SelectCondition(Condition.A, 1f), Is.False);
            Assert.That(f.TrialIndex, Is.EqualTo(0));
            Assert.That(f.RequestReset(1f), Is.False);
        }
    }
}
