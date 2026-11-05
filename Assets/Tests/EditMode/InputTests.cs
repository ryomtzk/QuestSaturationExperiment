using NUnit.Framework;
using QuestSaturation.Core;

namespace QuestSaturation.Tests
{
    public class ShortcutMapTests
    {
        [Test]
        public void TrialOnlyAcceptsResetAndMarker()
        {
            foreach (Shortcut s in System.Enum.GetValues(typeof(Shortcut)))
            {
                bool expected = s == Shortcut.ResetHold || s == Shortcut.Marker;
                Assert.That(ShortcutMap.IsAllowed(FlowState.Trial, s), Is.EqualTo(expected), s.ToString());
            }
        }

        [Test]
        public void SelectionAcceptsBothTriggersMenuAndMarker()
        {
            Assert.That(ShortcutMap.IsAllowed(FlowState.Selection, Shortcut.SelectRightTrigger), Is.True);
            Assert.That(ShortcutMap.IsAllowed(FlowState.Selection, Shortcut.SelectLeftTrigger), Is.True);
            Assert.That(ShortcutMap.IsAllowed(FlowState.Selection, Shortcut.MenuHold), Is.True);
            Assert.That(ShortcutMap.IsAllowed(FlowState.Selection, Shortcut.Marker), Is.True);
            Assert.That(ShortcutMap.IsAllowed(FlowState.Selection, Shortcut.ResetHold), Is.False);
            Assert.That(ShortcutMap.IsAllowed(FlowState.Selection, Shortcut.TogglePanel), Is.False);
            Assert.That(ShortcutMap.IsAllowed(FlowState.Selection, Shortcut.PlayPause), Is.False);
        }

        [Test]
        public void FreeControlAcceptsOperatorControlsButNotSelection()
        {
            Assert.That(ShortcutMap.IsAllowed(FlowState.FreeControl, Shortcut.SelectRightTrigger), Is.False);
            Assert.That(ShortcutMap.IsAllowed(FlowState.FreeControl, Shortcut.ResetHold), Is.False);
            foreach (var s in new[] { Shortcut.MenuHold, Shortcut.Marker, Shortcut.TogglePanel, Shortcut.RecallPanel,
                         Shortcut.StickUp, Shortcut.StickDown, Shortcut.StickLeft, Shortcut.StickRight,
                         Shortcut.StickClick, Shortcut.PlayPause, Shortcut.Stop })
                Assert.That(ShortcutMap.IsAllowed(FlowState.FreeControl, s), Is.True, s.ToString());
        }
    }

    public class TapOrHoldDetectorTests
    {
        [Test]
        public void ShortPressIsTapOnRelease()
        {
            var d = new TapOrHoldDetector(1f);
            Assert.That(d.Update(true, 0f), Is.EqualTo(PressResult.None));
            Assert.That(d.Update(true, 0.5f), Is.EqualTo(PressResult.None));
            Assert.That(d.Update(false, 0.6f), Is.EqualTo(PressResult.Tap));
            Assert.That(d.Update(false, 0.7f), Is.EqualTo(PressResult.None));
        }

        [Test]
        public void HoldFiresOnceAtThresholdAndReleaseDoesNothing()
        {
            var d = new TapOrHoldDetector(1.5f);
            d.Update(true, 0f);
            Assert.That(d.Update(true, 1.49f), Is.EqualTo(PressResult.None));
            Assert.That(d.Update(true, 1.5f), Is.EqualTo(PressResult.Hold));
            Assert.That(d.Update(true, 3f), Is.EqualTo(PressResult.None));
            Assert.That(d.Update(false, 3.1f), Is.EqualTo(PressResult.None));
        }

        [Test]
        public void HoldOnlyButtonIgnoresTaps()
        {
            var d = new TapOrHoldDetector(2f, tapEnabled: false);
            d.Update(true, 0f);
            Assert.That(d.Update(false, 0.2f), Is.EqualTo(PressResult.None));
        }

        [Test]
        public void ResetRequiresReleaseBeforeNextPress()
        {
            var d = new TapOrHoldDetector(1f);
            d.Update(true, 0f);
            d.Update(true, 0.9f);
            d.Reset(); // フォーカスを失った
            Assert.That(d.Update(true, 1.0f), Is.EqualTo(PressResult.None));
            Assert.That(d.Update(true, 5.0f), Is.EqualTo(PressResult.None), "held through focus loss: no hold");
            Assert.That(d.Update(false, 5.1f), Is.EqualTo(PressResult.None), "and no tap");
            d.Update(true, 6f);
            Assert.That(d.Update(true, 7f), Is.EqualTo(PressResult.Hold));
        }

        [Test]
        public void HoldProgressGoesFromZeroToOne()
        {
            var d = new TapOrHoldDetector(2f);
            Assert.That(d.HoldProgress(0f), Is.EqualTo(0f));
            d.Update(true, 0f);
            Assert.That(d.HoldProgress(1f), Is.EqualTo(0.5f).Within(1e-6f));
            d.Update(true, 2f);
            Assert.That(d.HoldProgress(2.5f), Is.EqualTo(0f), "after hold fired");
        }
    }

    public class StickRepeaterTests
    {
        [Test]
        public void FlickGivesOneStep()
        {
            var r = new StickRepeater();
            Assert.That(r.Update(0.9f, false, false, 0f), Is.EqualTo(1));
            Assert.That(r.Update(0.9f, false, false, 5f), Is.EqualTo(0), "no repeat for enums");
            Assert.That(r.Update(0f, false, false, 5.1f), Is.EqualTo(0));
            Assert.That(r.Update(-0.9f, false, false, 5.2f), Is.EqualTo(-1));
        }

        [Test]
        public void HoldingRepeatsAfterDelay()
        {
            var r = new StickRepeater { InitialDelaySec = 0.4f, RepeatIntervalSec = 0.1f };
            int total = 0;
            for (int i = 0; i <= 90; i++) total += r.Update(1f, false, true, i / 90f); // 1秒
            // 0秒で1回、0.4秒から0.1秒ごとに 0.4,0.5,…,1.0 の7回
            Assert.That(total, Is.InRange(7, 8));
        }

        [Test]
        public void HysteresisPreventsChatter()
        {
            var r = new StickRepeater();
            Assert.That(r.Update(0.65f, false, false, 0f), Is.EqualTo(1));
            Assert.That(r.Update(0.5f, false, false, 0.1f), Is.EqualTo(0));
            Assert.That(r.Update(0.65f, false, false, 0.2f), Is.EqualTo(0), "never went back to center");
        }

        [Test]
        public void ClickSuppressesAxisUntilCentered()
        {
            var r = new StickRepeater();
            Assert.That(r.Update(0.9f, true, true, 0f), Is.EqualTo(0));
            Assert.That(r.Update(0.9f, false, true, 0.1f), Is.EqualTo(0), "still deflected after click");
            Assert.That(r.Update(0f, false, true, 0.2f), Is.EqualTo(0));
            Assert.That(r.Update(0.9f, false, true, 0.3f), Is.EqualTo(1));
        }
    }

    public class StickAxisLockTests
    {
        [Test]
        public void StaysOnTheFirstAxisThroughTheDiagonal()
        {
            var l = new StickAxisLock();
            Assert.That(l.Update(0.1f, 0.3f), Is.EqualTo(StickAxis.None), "below the press threshold");
            Assert.That(l.Update(0.7f, 0.2f), Is.EqualTo(StickAxis.Horizontal));
            Assert.That(l.Update(0.6f, 0.75f), Is.EqualTo(StickAxis.Horizontal), "y is now larger but the lock holds");
            Assert.That(l.Update(0.1f, 0.9f), Is.EqualTo(StickAxis.Horizontal), "still not back at center");
            Assert.That(l.Update(0.2f, 0.2f), Is.EqualTo(StickAxis.None), "both below release");
            Assert.That(l.Update(0.1f, -0.8f), Is.EqualTo(StickAxis.Vertical));
        }

        [Test]
        public void HoldingRightThroughADiagonalKeepsRepeating()
        {
            // 斜めを通っても横のリピートが途切れず、戻しても余分な1回が出ない
            var l = new StickAxisLock();
            var h = new StickRepeater { InitialDelaySec = 0.4f, RepeatIntervalSec = 0.1f };
            var v = new StickRepeater();
            int hTotal = 0, vTotal = 0;
            for (int i = 0; i <= 90; i++)
            {
                float t = i / 90f;
                float x = 0.8f;
                float y = (i >= 30 && i < 50) ? 0.85f : 0f; // 途中で少し上に入る
                var axis = l.Update(x, y);
                hTotal += h.Update(axis == StickAxis.Horizontal ? x : 0f, false, true, t);
                vTotal += v.Update(axis == StickAxis.Vertical ? y : 0f, false, false, t);
            }
            Assert.That(vTotal, Is.EqualTo(0), "the cursor must not move");
            Assert.That(hTotal, Is.InRange(7, 8), "same as holding straight right for 1 s");
        }

        [Test]
        public void NaNCountsAsCenter()
        {
            var l = new StickAxisLock();
            Assert.That(l.Update(float.NaN, float.NaN), Is.EqualTo(StickAxis.None));
            l.Update(0.9f, 0f);
            Assert.That(l.Update(float.NaN, 0f), Is.EqualTo(StickAxis.None));
        }
    }
}
