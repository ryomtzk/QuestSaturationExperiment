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
}
