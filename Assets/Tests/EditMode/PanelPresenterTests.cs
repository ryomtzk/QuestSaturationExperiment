using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using QuestSaturation.Core;

namespace QuestSaturation.Tests
{
    public class PanelPresenterTests
    {
        private static readonly float[] Xs = new float[PanelPresenter.MaxSamples];
        private static readonly float[] Ys = new float[PanelPresenter.MaxSamples];

        // ---------------------------------------------------------------- 表示してよい状態

        [Test]
        public void ThePanelIsAllowedOnlyInFreeControl()
        {
            Assert.That(PanelVisibility.Allowed(FlowState.FreeControl), Is.True);
            Assert.That(PanelVisibility.Allowed(FlowState.Selection), Is.False);
            Assert.That(PanelVisibility.Allowed(FlowState.Trial), Is.False);
        }

        [Test]
        public void TheFlowShowsThePanelOnlyInFreeControlAndHidesItOnEveryParticipantState()
        {
            var f = new ExperimentFlow();
            float t = 0f;

            void Step(string what, Func<bool> action, FlowState expected)
            {
                action();
                Assert.That(f.State, Is.EqualTo(expected), what);
                var cmds = f.Actions.Select(a => a.Command).ToArray();
                if (cmds.Contains(FlowCommand.ShowPanel))
                    Assert.That(PanelVisibility.Allowed(f.State), Is.True, what + ": ShowPanel outside FreeControl");
                if (!PanelVisibility.Allowed(expected))
                    Assert.That(cmds, Does.Contain(FlowCommand.HidePanel), what + ": entering " + expected + " must hide the panel");
                else
                    Assert.That(cmds, Does.Contain(FlowCommand.ShowPanel), what);
            }

            Step("begin", () => { f.Begin(t); return true; }, FlowState.Selection);
            Step("menu hold", () => f.ToggleFreeControl(t += 1f), FlowState.FreeControl);
            Step("menu hold back", () => f.ToggleFreeControl(t += 1f), FlowState.Selection);
            Step("select", () => f.SelectCondition(Condition.B, t += 1f), FlowState.Trial);
            Step("reset", () => f.RequestReset(t += 1f), FlowState.Selection);
            Step("menu hold again", () => f.ToggleFreeControl(t += 1f), FlowState.FreeControl);
        }

        // ---------------------------------------------------------------- 再生

        [Test]
        public void PlaybackStatusFollowsThePlayer()
        {
            var p = new WaveformPlayer { Waveform = Waveform.Ramp, PeriodSec = 2f };
            Assert.That(PanelPresenter.PlaybackOf(p), Is.EqualTo(PlaybackStatus.Stopped));
            p.Play();
            Assert.That(PanelPresenter.PlaybackOf(p), Is.EqualTo(PlaybackStatus.Playing));
            p.Tick(0.5f);
            p.Pause();
            Assert.That(PanelPresenter.PlaybackOf(p), Is.EqualTo(PlaybackStatus.Paused));
            p.Play();
            p.Tick(5f);
            Assert.That(PanelPresenter.PlaybackOf(p), Is.EqualTo(PlaybackStatus.Finished));
            p.Stop();
            Assert.That(PanelPresenter.PlaybackOf(p), Is.EqualTo(PlaybackStatus.Stopped));
            Assert.That(PanelPresenter.PlaybackOf(null), Is.EqualTo(PlaybackStatus.Stopped));
        }

        [Test]
        public void TransportShowsManualOrThePlaybackState()
        {
            Assert.That(PanelPresenter.TransportLabel(FreeMode.Manual, PlaybackStatus.Playing), Is.EqualTo("MANUAL"));
            Assert.That(PanelPresenter.TransportLabel(FreeMode.Auto, PlaybackStatus.Playing), Is.EqualTo("PLAYING"));
            Assert.That(PanelPresenter.TransportLabel(FreeMode.Auto, PlaybackStatus.Finished), Is.EqualTo("FINISHED"));
        }

        [Test]
        public void PositionLabelShowsElapsedAndPeriod()
        {
            Assert.That(PanelPresenter.PositionLabel(0.42, 10f), Is.EqualTo("4.2 / 10.0 s"));
            Assert.That(PanelPresenter.PositionLabel(1.0, 30f), Is.EqualTo("30.0 / 30.0 s"));
            Assert.That(PanelPresenter.PositionLabel(double.NaN, 5f), Is.EqualTo("0.0 / 5.0 s"));
            Assert.That(PanelPresenter.PositionLabel(0.999, 120f), Is.EqualTo("119.8 / 120.0 s")); // 切り捨て（先の時刻を出さない）
        }

        [Test]
        public void ThePositionTheCardWritesIsTheSameAsTheLabel()
        {
            // 波形カードは PositionTenths を TMP の書式（"{0:1}"）で書く。0.1 秒の境目でも切り捨てで、先の時刻を出さない
            Assert.That(PanelPresenter.PositionTenths(0.42, 10f), Is.EqualTo(42));
            Assert.That(PanelPresenter.PositionTenths(0.4199, 10f), Is.EqualTo(41));
            Assert.That(PanelPresenter.PositionTenths(1.0, 37.5f), Is.EqualTo(375));
            Assert.That(PanelPresenter.PositionTenths(2.0, 10f), Is.EqualTo(100));
            Assert.That(PanelPresenter.PositionTenths(-0.5, 10f), Is.EqualTo(0));
            Assert.That(PanelPresenter.PositionTenths(double.NaN, 10f), Is.EqualTo(0));
            Assert.That(PanelPresenter.PositionTenths(0.5, 0f), Is.EqualTo(0));
            Assert.That(PanelPresenter.ClampPhase(double.NaN), Is.EqualTo(0f));
            Assert.That(PanelPresenter.ClampPhase(1.5), Is.EqualTo(1f));
            // 周期は 0.5 秒刻み（1〜120 秒）。どの周期でも、終わりの位置は周期そのものになる
            for (float period = 1f; period <= 120f; period += 0.5f)
                Assert.That(PanelPresenter.PositionLabel(1.0, period), Does.StartWith(period.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " / "));
        }

        // ---------------------------------------------------------------- 項目

        [Test]
        public void BipolarBarsFillFromZero()
        {
            var sat = ParameterDefs.Get(MenuItem.Saturation); // -1..1
            var bar = PanelPresenter.Bar(sat, -0.5f);
            Assert.That(bar.Origin, Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(bar.Value, Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(bar.Default, Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(bar.FillMin, Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(bar.FillMax, Is.EqualTo(0.5f).Within(1e-6f));

            var bri = ParameterDefs.Get(MenuItem.Brightness); // -0.5..0.5
            Assert.That(PanelPresenter.Bar(bri, 0.5f).Value, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(PanelPresenter.Bar(bri, 0f).Origin, Is.EqualTo(0.5f).Within(1e-6f));
        }

        [Test]
        public void PositiveRangesFillFromTheMinimum()
        {
            var period = ParameterDefs.Get(MenuItem.Period); // 1..120, default 10
            var bar = PanelPresenter.Bar(period, 120f);
            Assert.That(bar.Origin, Is.EqualTo(0f));
            Assert.That(bar.Value, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(bar.Default, Is.EqualTo(9f / 119f).Within(1e-6f));

            var steps = ParameterDefs.Get(MenuItem.Steps); // 2..20
            Assert.That(PanelPresenter.Bar(steps, 2f).Value, Is.EqualTo(0f));
        }

        [Test]
        public void FractionsAreClampedAndSanitized()
        {
            var sat = ParameterDefs.Get(MenuItem.Saturation);
            Assert.That(PanelPresenter.Fraction(sat, 5f), Is.EqualTo(1f));
            Assert.That(PanelPresenter.Fraction(sat, -5f), Is.EqualTo(0f));
            Assert.That(PanelPresenter.Fraction(sat, float.NaN), Is.EqualTo(0.5f).Within(1e-6f)); // NaN → 既定値 0
        }

        [Test]
        public void LimitsDimTheArrowsExceptOnWrappingItems()
        {
            var bri = ParameterDefs.Get(MenuItem.Brightness);
            Assert.That(PanelPresenter.AtMax(bri, 0.5f), Is.True);
            Assert.That(PanelPresenter.AtMin(bri, 0.5f), Is.False);
            Assert.That(PanelPresenter.AtMin(bri, -0.5f), Is.True);
            Assert.That(PanelPresenter.AtMin(bri, 0f), Is.False);
            var id = ParameterDefs.Get(MenuItem.ParticipantId);
            Assert.That(PanelPresenter.AtMin(id, 0f), Is.True);
            // Enum は端で折り返すので、どちらにも押せる
            var wave = ParameterDefs.Get(MenuItem.Waveform);
            Assert.That(PanelPresenter.AtMin(wave, wave.Min), Is.False);
            Assert.That(PanelPresenter.AtMax(wave, wave.Max), Is.False);
        }

        [Test]
        public void StepsAreOnlyRelevantForTheStepWaveform()
        {
            Assert.That(PanelPresenter.IsRelevant(MenuItem.Steps, Waveform.Step), Is.True);
            Assert.That(PanelPresenter.IsRelevant(MenuItem.Steps, Waveform.Sine), Is.False);
            Assert.That(PanelPresenter.IsRelevant(MenuItem.Steps, Waveform.Ramp), Is.False);
            foreach (var def in ParameterDefs.All.Where(d => d.Item != MenuItem.Steps))
                Assert.That(PanelPresenter.IsRelevant(def.Item, Waveform.Sine), Is.True, def.Label);
        }

        [Test]
        public void OnlyManualSaturationIsOverriddenInAuto()
        {
            Assert.That(PanelPresenter.IsOverriddenByAuto(MenuItem.Saturation, FreeMode.Auto), Is.True);
            Assert.That(PanelPresenter.IsOverriddenByAuto(MenuItem.Saturation, FreeMode.Manual), Is.False);
            Assert.That(PanelPresenter.IsOverriddenByAuto(MenuItem.Brightness, FreeMode.Auto), Is.False);
            Assert.That(PanelPresenter.IsOverriddenByAuto(MenuItem.Contrast, FreeMode.Auto), Is.False);
        }

        [Test]
        public void ValuesUseTheLogFormatPlusUnits()
        {
            Assert.That(PanelPresenter.FormatValue(ParameterDefs.Get(MenuItem.Saturation), -0.8f), Is.EqualTo("-0.80"));
            Assert.That(PanelPresenter.FormatValue(ParameterDefs.Get(MenuItem.Saturation), 0f), Is.EqualTo("0.00"));
            Assert.That(PanelPresenter.FormatValue(ParameterDefs.Get(MenuItem.Period), 10f), Is.EqualTo("10.0 s"));
            Assert.That(PanelPresenter.FormatValue(ParameterDefs.Get(MenuItem.ParticipantId), 7f), Is.EqualTo("P007"));
            Assert.That(PanelPresenter.FormatValue(ParameterDefs.Get(MenuItem.Waveform), 2f), Is.EqualTo("Step"));
            Assert.That(PanelPresenter.ParticipantLabel(-3), Is.EqualTo("P000"));
            Assert.That(PanelPresenter.DisplayLabel(ParameterDefs.Get(MenuItem.Period)), Is.EqualTo("Period"));
            Assert.That(PanelPresenter.DisplayLabel(ParameterDefs.Get(MenuItem.To)), Is.EqualTo("Wave to"));
        }

        [Test]
        public void HintsNameTheStepAndTheDefault()
        {
            foreach (FreeMode mode in Enum.GetValues(typeof(FreeMode)))
            foreach (var def in ParameterDefs.All)
            {
                string hint = PanelPresenter.Hint(def, mode);
                Assert.That(hint, Is.Not.Empty, def.Label);
                if (def.Item != MenuItem.Mode && def.Item != MenuItem.ParticipantId)
                    Assert.That(hint, Does.Contain("reset to " + PanelPresenter.FormatValue(def, def.Default)), def.Label);
                if (def.Item != MenuItem.Mode)
                    Assert.That(hint, Is.EqualTo(PanelPresenter.Hint(def, FreeMode.Manual)), def.Label + ": only the Mode row depends on the mode");
            }
            Assert.That(PanelPresenter.Hint(ParameterDefs.Get(MenuItem.Period), FreeMode.Manual), Does.Contain("±0.5 s"));
            Assert.That(PanelPresenter.Hint(ParameterDefs.Get(MenuItem.Contrast), FreeMode.Manual), Does.Contain("±0.05"));
            Assert.That(PanelPresenter.Hint(ParameterDefs.Get(MenuItem.Steps), FreeMode.Manual), Does.Contain("Step waveform only"));
        }

        [Test]
        public void TheModeHintDescribesWhatBDoesInEachMode()
        {
            // Manual では B で Auto に切り替わって再生、Auto では再生/一時停止（FreeControlModel.TogglePlayPause）
            var mode = ParameterDefs.Get(MenuItem.Mode);
            Assert.That(PanelPresenter.Hint(mode, FreeMode.Manual), Does.Contain("switches to Auto"));
            Assert.That(PanelPresenter.Hint(mode, FreeMode.Auto), Does.Contain("play / pause"));
            Assert.That(PanelPresenter.Hint(mode, FreeMode.Auto), Does.Not.Contain("switches to Auto"));
        }

        [Test]
        public void TheParticipantHintNamesWhatCommitsTheNewId()
        {
            // README：別の項目の変更、再生・一時停止・停止、マーカー、FreeControl を出たときに確定する
            string hint = PanelPresenter.Hint(ParameterDefs.Get(MenuItem.ParticipantId), FreeMode.Manual);
            Assert.That(hint, Does.Contain("other change"));
            Assert.That(hint, Does.Contain("B"));
            Assert.That(hint, Does.Contain("marker"));
            Assert.That(hint, Does.Contain("exit"));
        }

        // ---------------------------------------------------------------- 長押し

        [Test]
        public void ReleasingBEarlyDescribesThePlayPauseItTriggers()
        {
            // B は短く押すと再生/一時停止なので、長押しをやめても「取り消し」にはならない
            Assert.That(PanelPresenter.StopHoldRelease(FreeMode.Manual, PlaybackStatus.Stopped), Does.Contain("switches to Auto"));
            Assert.That(PanelPresenter.StopHoldRelease(FreeMode.Auto, PlaybackStatus.Playing), Is.EqualTo("Release now: pause"));
            Assert.That(PanelPresenter.StopHoldRelease(FreeMode.Auto, PlaybackStatus.Paused), Is.EqualTo("Release now: play"));
            Assert.That(PanelPresenter.StopHoldRelease(FreeMode.Auto, PlaybackStatus.Stopped), Is.EqualTo("Release now: play"));
            Assert.That(PanelPresenter.StopHoldRelease(FreeMode.Auto, PlaybackStatus.Finished), Is.EqualTo("Release now: play again"));
            foreach (FreeMode m in Enum.GetValues(typeof(FreeMode)))
            foreach (PlaybackStatus p in Enum.GetValues(typeof(PlaybackStatus)))
                Assert.That(PanelPresenter.StopHoldRelease(m, p), Does.Not.Contain("cancel"), m + " " + p);
        }

        [Test]
        public void TheMenuHoldCanBeCancelledAndMentionsAPendingId()
        {
            Assert.That(PanelPresenter.MenuHoldRelease(false, -1), Is.EqualTo("Release to cancel"));
            Assert.That(PanelPresenter.MenuHoldRelease(true, 8), Does.Contain("P008"));
        }

        [Test]
        public void HoldIndicatorsWaitPastAnOrdinaryTap()
        {
            // B の長押しは 1 秒。0.3 秒より前に離すふつうのタップでは帯を出さない
            Assert.That(PanelPresenter.StopHoldShowAt, Is.GreaterThanOrEqualTo(0.3f).And.LessThan(1f));
            Assert.That(PanelPresenter.MenuHoldShowAt, Is.GreaterThan(0f).And.LessThan(PanelPresenter.StopHoldShowAt));
        }

        // ---------------------------------------------------------------- 状態表示

        [Test]
        public void BatteryLabelAndTone()
        {
            Assert.That(PanelPresenter.BatteryLabel("HMD", -1f), Is.Empty);
            Assert.That(PanelPresenter.BatteryLabel("HMD", 0.875f), Is.EqualTo("HMD 88%"));
            Assert.That(PanelPresenter.BatteryLabel("R", 1.2f), Is.EqualTo("R 100%"));
            Assert.That(PanelPresenter.BatteryTone(0.05f, charging: false), Is.EqualTo(StatusTone.Error));
            Assert.That(PanelPresenter.BatteryTone(0.15f, charging: false), Is.EqualTo(StatusTone.Warning));
            Assert.That(PanelPresenter.BatteryTone(0.15f, charging: true), Is.EqualTo(StatusTone.Neutral));
            Assert.That(PanelPresenter.BatteryTone(0.9f, charging: false), Is.EqualTo(StatusTone.Neutral));
            Assert.That(PanelPresenter.BatteryTone(-1f, charging: false), Is.EqualTo(StatusTone.Neutral));
        }

        [Test]
        public void PassthroughIsGoodOnlyWhenItIsActuallyVisible()
        {
            Assert.That(PanelPresenter.PassthroughTone(PassthroughInit.Ready, true, true, true), Is.EqualTo(StatusTone.Good));
            Assert.That(PanelPresenter.PassthroughTone(PassthroughInit.Ready, true, true, false), Is.EqualTo(StatusTone.Warning));
            Assert.That(PanelPresenter.PassthroughTone(PassthroughInit.Ready, true, false, true), Is.EqualTo(StatusTone.Warning));
            Assert.That(PanelPresenter.PassthroughTone(PassthroughInit.Starting, true, true, false), Is.EqualTo(StatusTone.Warning));
            Assert.That(PanelPresenter.PassthroughTone(PassthroughInit.Failed, true, true, false), Is.EqualTo(StatusTone.Error));
            Assert.That(PanelPresenter.PassthroughTone(PassthroughInit.Unsupported, true, true, false), Is.EqualTo(StatusTone.Error));
            Assert.That(PanelPresenter.PassthroughTone(PassthroughInit.Ready, false, false, false), Is.EqualTo(StatusTone.Error));
            Assert.That(PanelPresenter.PassthroughLabel(PassthroughInit.Ready, true, true, true), Is.EqualTo("Passthrough"));
            Assert.That(PanelPresenter.PassthroughLabel(PassthroughInit.Ready, true, true, false), Is.EqualTo("Passthrough waiting…"));
            Assert.That(PanelPresenter.PassthroughLabel(PassthroughInit.Ready, false, true, true), Is.EqualTo("No passthrough layer"));
        }

        [Test]
        public void LogStatus()
        {
            Assert.That(PanelPresenter.LogLabel("a.csv", failed: true), Is.EqualTo("LOG STOPPED"));
            Assert.That(PanelPresenter.LogTone("a.csv", failed: true), Is.EqualTo(StatusTone.Error));
            Assert.That(PanelPresenter.LogLabel("", failed: false), Is.EqualTo("NOT LOGGING"));
            Assert.That(PanelPresenter.LogTone("", failed: false), Is.EqualTo(StatusTone.Warning));
            Assert.That(PanelPresenter.LogLabel("a.csv", failed: false), Is.EqualTo("REC"));
            Assert.That(PanelPresenter.LogTone("a.csv", failed: false), Is.EqualTo(StatusTone.Good));
        }

        [Test]
        public void TheLogStartComesFromTheFileNameStamp()
        {
            Assert.That(PanelPresenter.LogStartLabel("session_20261107T123045Z_P007.csv"), Is.EqualTo("Since 12:30 UTC"));
            Assert.That(PanelPresenter.LogStartLabel("session_20261107T235959Z_P012_2.csv"), Is.EqualTo("Since 23:59 UTC"));
            Assert.That(PanelPresenter.LogStartLabel("other.csv"), Is.EqualTo("Recording"));
            Assert.That(PanelPresenter.LogStartLabel("session_garbage_here.csv"), Is.EqualTo("Recording"));
            Assert.That(PanelPresenter.LogStartLabel(""), Is.Empty);
            Assert.That(PanelPresenter.LogStartLabel(null), Is.Empty);
        }

        [Test]
        public void SessionLabels()
        {
            Assert.That(PanelPresenter.TrialsLabel(0), Is.EqualTo("No trials yet"));
            Assert.That(PanelPresenter.TrialsLabel(1), Is.EqualTo("1 trial"));
            Assert.That(PanelPresenter.TrialsLabel(12), Is.EqualTo("12 trials"));
            Assert.That(PanelPresenter.PendingLabel(8), Does.StartWith("Next: P008"));
        }

        [Test]
        public void PendingParticipantOnlyWhenDifferent()
        {
            var state = new OperatorPanelState { ParticipantId = 7, PendingParticipantId = -1 };
            Assert.That(state.HasPendingParticipant, Is.False);
            state.PendingParticipantId = 7;
            Assert.That(state.HasPendingParticipant, Is.False);
            state.PendingParticipantId = 8;
            Assert.That(state.HasPendingParticipant, Is.True);
        }

        // ---------------------------------------------------------------- 通知

        [Test]
        public void ToastTexts()
        {
            Assert.That(PanelPresenter.MarkerToast(1, 1234.56), Is.EqualTo("Marker logged   •   t = 1234.6 s"));
            Assert.That(PanelPresenter.MarkerToast(3, 10.0), Does.StartWith("Marker ×3"));
            var period = ParameterDefs.Get(MenuItem.Period);
            Assert.That(PanelPresenter.ResetToast(period, 37.5f, 10f), Is.EqualTo("Period reset to 10.0 s   (was 37.5 s)"));
            Assert.That(PanelPresenter.ParticipantCommittedToast(8), Does.Contain("P008"));
            Assert.That(PanelPresenter.ParticipantCommittedToast(8, 99.94), Is.EqualTo("Now logging P008   •   marker t = 99.9 s"));
            Assert.That(PanelPresenter.ParticipantCommittedToast(8, period, 37.5f, 10f),
                Is.EqualTo("Now logging P008   •   Period reset to 10.0 s   (was 37.5 s)"));
            Assert.That(PanelPresenter.RampFinishedToast(-1f), Is.EqualTo("Ramp finished at -1.00"));
        }

        [Test]
        public void NoticesDoNotClaimRecordingAfterTheLogStopped()
        {
            // SessionLogger が止まった後は、マーカーの行も新しいファイルも書かれない
            var period = ParameterDefs.Get(MenuItem.Period);
            foreach (string s in new[]
                     {
                         PanelPresenter.MarkerNotRecordedToast(true), PanelPresenter.MarkerNotRecordedToast(false),
                         PanelPresenter.ParticipantNotRecordingToast(8), PanelPresenter.ParticipantNotRecordingToast(8, period, 37.5f, 10f),
                     })
            {
                Assert.That(s, Does.Contain("Not recording").Or.Contain("not recorded"));
                Assert.That(s, Does.Not.Contain("logged").And.Not.Contain("Now logging").And.Not.Contain("new session file"));
            }
            Assert.That(PanelPresenter.ParticipantNotRecordingToast(8), Does.Contain("P008"));
            Assert.That(PanelPresenter.ParticipantNotRecordingToast(8, period, 37.5f, 10f), Does.Contain("P008").And.Contain("(was 37.5 s)"));
            Assert.That(PanelPresenter.MarkerNotRecordedToast(true), Does.Contain("log stopped"));
            Assert.That(PanelPresenter.MarkerNotRecordedToast(false), Does.Contain("not logging"));
        }

        // ---------------------------------------------------------------- 通知の選び方

        private static readonly ParameterDef PeriodDef = ParameterDefs.Get(MenuItem.Period);

        private static NoticeEvents Quiet() => new NoticeEvents { ParticipantId = 8, MarkerAt = double.NaN };

        private static NoticeEvents WithReset(NoticeEvents e)
        {
            e.ResetDef = PeriodDef;
            e.ResetBefore = 37.5f;
            e.ResetAfter = 10f;
            return e;
        }

        [Test]
        public void NothingHappenedShowsNothing()
        {
            Assert.That(PanelNotices.Choose(Quiet()).IsNone, Is.True);
            var e = Quiet();
            e.LogFailed = true; // 止まったままなのは新しい出来事ではない（ヘッダーとセッションカードに出ている）
            Assert.That(PanelNotices.Choose(e).IsNone, Is.True);
        }

        [Test]
        public void TheLogStoppingOutranksEverythingElseInTheFrame()
        {
            var e = WithReset(Quiet());
            e.LogJustFailed = e.LogFailed = true;
            e.Committed = true;
            e.MarkerPresses = 1;
            e.RampFinished = true;
            var n = PanelNotices.Choose(e);
            Assert.That(n.Kind, Is.EqualTo(NoticeKind.LogFailed));
            Assert.That(n.Message, Is.EqualTo(PanelPresenter.LogFailedToast));
            Assert.That(n.Tone, Is.EqualTo(StatusTone.Error));
            Assert.That(n.Seconds, Is.EqualTo(PanelNotices.LogFailedSec));
        }

        [Test]
        public void AResetThatCommitsTheParticipantKeepsTheOriginalValue()
        {
            // 被験者 ID を変えた後、最初のほかの操作が押し込みのリセットだと、そのリセットで ID が確定する
            var e = WithReset(Quiet());
            e.Committed = true;
            var n = PanelNotices.Choose(e);
            Assert.That(n.Kind, Is.EqualTo(NoticeKind.Participant));
            Assert.That(n.Tone, Is.EqualTo(StatusTone.Good));
            Assert.That(n.Message, Is.EqualTo("Now logging P008   •   Period reset to 10.0 s   (was 37.5 s)"));

            e.LogFailed = true;
            n = PanelNotices.Choose(e);
            Assert.That(n.Tone, Is.EqualTo(StatusTone.Error));
            Assert.That(n.Message, Is.EqualTo("Not recording P008   •   Period reset to 10.0 s   (was 37.5 s)"));
        }

        [Test]
        public void AMarkerThatCommitsTheParticipantShowsTheTimeWrittenToTheNewFile()
        {
            var e = Quiet();
            e.Committed = true;
            e.MarkerPresses = 1;
            e.MarkerAt = 99.94;
            var n = PanelNotices.Choose(e);
            Assert.That(n.Kind, Is.EqualTo(NoticeKind.Participant));
            Assert.That(n.Message, Is.EqualTo("Now logging P008   •   marker t = 99.9 s"));

            e.MarkerAt = double.NaN; // 行が書かれていない
            Assert.That(PanelNotices.Choose(e).Message, Is.EqualTo(PanelPresenter.ParticipantCommittedToast(8)));

            e.LogFailed = true;
            n = PanelNotices.Choose(e);
            Assert.That(n.Message, Is.EqualTo(PanelPresenter.ParticipantNotRecordingToast(8)));
            Assert.That(n.Tone, Is.EqualTo(StatusTone.Error));
        }

        [Test]
        public void ACommitOnItsOwnSaysANewFileWasStarted()
        {
            var e = Quiet();
            e.Committed = true;
            var n = PanelNotices.Choose(e);
            Assert.That(n.Message, Is.EqualTo("Now logging P008   •   new session file"));
            Assert.That(n.Seconds, Is.EqualTo(PanelNotices.ParticipantSec));
        }

        [Test]
        public void MarkersAreCountedWhileTheirNoticeIsShowing()
        {
            var e = Quiet();
            e.MarkerPresses = 1;
            e.MarkerAt = 12.34;
            var n = PanelNotices.Choose(e);
            Assert.That(n.Kind, Is.EqualTo(NoticeKind.Marker));
            Assert.That(n.Message, Is.EqualTo("Marker logged   •   t = 12.3 s"));
            Assert.That(n.MarkerRun, Is.EqualTo(1));

            e.VisibleMarkerRun = n.MarkerRun;
            e.MarkerAt = 13.0;
            n = PanelNotices.Choose(e);
            Assert.That(n.Message, Is.EqualTo("Marker ×2   •   t = 13.0 s"));
            Assert.That(n.MarkerRun, Is.EqualTo(2));
        }

        [Test]
        public void AMarkerThatWasNotWrittenIsNotCalledLogged()
        {
            var e = Quiet();
            e.MarkerPresses = 1;
            e.VisibleMarkerRun = 3;
            var n = PanelNotices.Choose(e); // 行が書かれていない（ログのファイルが無い）
            Assert.That(n.Message, Is.EqualTo(PanelPresenter.MarkerNotRecordedToast(false)));
            Assert.That(n.Tone, Is.EqualTo(StatusTone.Error));

            e.LogFailed = true;
            Assert.That(PanelNotices.Choose(e).Message, Is.EqualTo(PanelPresenter.MarkerNotRecordedToast(true)));
        }

        [Test]
        public void AMarkerOutranksAResetInTheSameFrame()
        {
            var e = WithReset(Quiet());
            e.MarkerPresses = 1;
            e.MarkerAt = 5.0;
            Assert.That(PanelNotices.Choose(e).Kind, Is.EqualTo(NoticeKind.Marker));
        }

        [Test]
        public void AResetIsShownEvenOverAWarning()
        {
            // 警告の通知が出ていても、元の値は出す（ログの停止はヘッダーとセッションカードに出たまま）
            var e = WithReset(Quiet());
            e.LogFailed = true;
            e.ToastVisible = true;
            var n = PanelNotices.Choose(e);
            Assert.That(n.Kind, Is.EqualTo(NoticeKind.Reset));
            Assert.That(n.Message, Is.EqualTo("Period reset to 10.0 s   (was 37.5 s)"));
            Assert.That(n.Tone, Is.EqualTo(StatusTone.Neutral));
        }

        [Test]
        public void TheEndOfARampDoesNotReplaceAnotherNotice()
        {
            // 波形カードの FINISHED に常に出ているので、マーカーの時刻などを消してまで出さない
            var e = Quiet();
            e.RampFinished = true;
            e.RampTo = -1f;
            e.ToastVisible = true;
            Assert.That(PanelNotices.Choose(e).IsNone, Is.True);
            e.ToastVisible = false;
            var n = PanelNotices.Choose(e);
            Assert.That(n.Kind, Is.EqualTo(NoticeKind.RampFinished));
            Assert.That(n.Message, Is.EqualTo("Ramp finished at -1.00"));
        }

        [Test]
        public void TheNoticeSamplesCoverEveryKindOfNotice()
        {
            var samples = PanelNotices.Samples().ToList();
            Assert.That(samples.All(x => !string.IsNullOrEmpty(x)), Is.True);
            foreach (var def in ParameterDefs.All)
                Assert.That(samples.Any(x => x.StartsWith(PanelPresenter.DisplayLabel(def) + " reset to", StringComparison.Ordinal)),
                    Is.EqualTo(def.Resettable), def.Item.ToString());
            Assert.That(samples.Any(x => x.StartsWith("Now logging", StringComparison.Ordinal) && x.Contains("(was")), Is.True);
            Assert.That(samples.Any(x => x.StartsWith("Not recording", StringComparison.Ordinal) && x.Contains("(was")), Is.True);
            Assert.That(samples, Has.Member(PanelPresenter.LogFailedToast));
        }

        // ---------------------------------------------------------------- 字

        [Test]
        public void TheStaticGlyphRuleMatchesTheAtlas()
        {
            foreach (char c in "AZaz09 ~±•…‹›–—·×é ") Assert.That(PanelPresenter.IsStaticGlyph(c), Is.True, "U+" + ((int)c).ToString("X4"));
            // 制御文字と、アトラスに無い記号（→ ● ► ☰ ▶ −）
            foreach (char c in "\u0000\u001F\u007F\u0085\u009F→●►☰▶−")
                Assert.That(PanelPresenter.IsStaticGlyph(c), Is.False, "U+" + ((int)c).ToString("X4"));
        }

        [Test]
        public void EveryPanelStringUsesOnlyStaticGlyphs()
        {
            foreach (var s in AllPanelStrings())
                Assert.That(PanelPresenter.UsesOnlyStaticGlyphs(s), Is.True, "\"" + s + "\" uses a glyph outside the static atlas");
        }

        private static IEnumerable<string> AllPanelStrings()
        {
            foreach (var def in ParameterDefs.All)
            {
                yield return PanelPresenter.Hint(def, FreeMode.Manual);
                yield return PanelPresenter.Hint(def, FreeMode.Auto);
                yield return PanelPresenter.DisplayLabel(def);
                yield return PanelPresenter.FormatValue(def, def.Min);
                yield return PanelPresenter.FormatValue(def, def.Max);
                yield return PanelPresenter.FormatValue(def, def.Default);
                yield return PanelPresenter.ResetToast(def, def.Max, def.Default);
                if (def.Options != null) foreach (var o in def.Options) yield return o;
            }
            foreach (FreeMode m in Enum.GetValues(typeof(FreeMode)))
            foreach (PlaybackStatus p in Enum.GetValues(typeof(PlaybackStatus)))
            {
                yield return PanelPresenter.TransportLabel(m, p);
                yield return PanelPresenter.StopHoldRelease(m, p);
            }
            foreach (PassthroughInit init in Enum.GetValues(typeof(PassthroughInit)))
            foreach (bool layer in new[] { false, true })
            foreach (bool on in new[] { false, true })
            foreach (bool visible in new[] { false, true })
                yield return PanelPresenter.PassthroughLabel(init, layer, on, visible);
            foreach (var e in PanelPresenter.Legend)
            {
                yield return e.Key;
                yield return e.Action;
            }
            yield return PanelPresenter.PositionLabel(0.5, 120f);
            yield return PanelPresenter.ManualTransportHint;
            yield return PanelPresenter.StopHoldTitle;
            yield return PanelPresenter.MenuHoldTitle;
            yield return PanelPresenter.MenuHoldRelease(true, 999);
            yield return PanelPresenter.MenuHoldRelease(false, -1);
            yield return PanelPresenter.BatteryLabel("HMD", 0.5f);
            yield return PanelPresenter.LogLabel("x.csv", false);
            yield return PanelPresenter.LogLabel("", false);
            yield return PanelPresenter.LogLabel("", true);
            yield return PanelPresenter.LogStartLabel("session_20261107T123045Z_P007.csv");
            yield return PanelPresenter.LogStoppedTitle;
            yield return PanelPresenter.LogStoppedDetail;
            yield return PanelPresenter.TrialsLabel(0);
            yield return PanelPresenter.TrialsLabel(5);
            yield return PanelPresenter.PendingLabel(8);
            yield return PanelPresenter.PendingTag;
            yield return PanelPresenter.AutoTag;
            yield return PanelPresenter.FadingTag;
            foreach (var s in PanelNotices.Samples()) yield return s;
            foreach (PlaybackStatus p in Enum.GetValues(typeof(PlaybackStatus))) yield return PanelPresenter.PlaybackLabel(p);
            // パネルの固定の文字（OperatorPanelView が使うもの）
            foreach (var s in new[] { "Free Control", "Output", "Waveform", "Session", "saturation", "Brightness", "Contrast", "+1", "0", "-1", "--:--" })
                yield return s;
        }

        // ---------------------------------------------------------------- 波形

        [Test]
        public void RampSamplesGoFromStartToEnd()
        {
            int n = PanelPresenter.SampleWaveform(Waveform.Ramp, 0.2f, -0.6f, 5, Xs, Ys);
            Assert.That(n, Is.EqualTo(2));
            Assert.That(Xs[0], Is.EqualTo(0f));
            Assert.That(Ys[0], Is.EqualTo(0.2f));
            Assert.That(Xs[1], Is.EqualTo(1f));
            Assert.That(Ys[1], Is.EqualTo(-0.6f));
        }

        [Test]
        public void PeriodicSamplesStartAtFromAndReachTo()
        {
            foreach (var w in new[] { Waveform.Sine, Waveform.LinearPingPong })
            {
                int n = PanelPresenter.SampleWaveform(w, 0f, -1f, 5, Xs, Ys);
                Assert.That(n, Is.GreaterThan(2), w.ToString());
                Assert.That(Xs[0], Is.EqualTo(0f), w.ToString());
                Assert.That(Ys[0], Is.EqualTo(0f).Within(1e-6f), w.ToString());
                Assert.That(Xs[n - 1], Is.EqualTo(1f), w.ToString());
                Assert.That(Ys[n - 1], Is.EqualTo(0f).Within(1e-6f), w.ToString());
                Assert.That(Ys.Take(n).Min(), Is.EqualTo(-1f).Within(1e-5f), w.ToString());
                for (int i = 1; i < n; i++) Assert.That(Xs[i], Is.GreaterThanOrEqualTo(Xs[i - 1]), w.ToString());
            }
        }

        [Test]
        public void StepSamplesMatchThePlayerAndHaveSquareCorners()
        {
            const int steps = 4;
            int n = PanelPresenter.SampleWaveform(Waveform.Step, -1f, 1f, steps, Xs, Ys);
            int segments = 2 * (steps - 1);
            Assert.That(n, Is.EqualTo(2 * segments));
            for (int k = 0; k < segments; k++)
            {
                // 各段は水平（始点と終点が同じ高さ）で、プレーヤーの値と一致する
                Assert.That(Ys[2 * k], Is.EqualTo(Ys[2 * k + 1]));
                double mid = (k + 0.5) / segments;
                Assert.That(Ys[2 * k], Is.EqualTo(WaveformMath.Evaluate(Waveform.Step, mid, steps, -1f, 1f)).Within(1e-6f));
            }
            Assert.That(Ys.Take(n).Max(), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(Ys[0], Is.EqualTo(-1f).Within(1e-6f));
        }

        [Test]
        public void SamplesFitInTheBufferForEveryStepCount()
        {
            for (int s = WaveformMath.MinSteps; s <= WaveformMath.MaxSteps; s++)
                Assert.That(PanelPresenter.SampleWaveform(Waveform.Step, 0f, 1f, s, Xs, Ys), Is.LessThanOrEqualTo(PanelPresenter.MaxSamples));
            Assert.Throws<ArgumentException>(() => PanelPresenter.SampleWaveform(Waveform.Sine, 0f, 1f, 5, new float[3], new float[3]));
        }

        [Test]
        public void StateCopiesTheModel()
        {
            var s = FreeControlSettings.CreateDefault();
            s.Saturation = -0.3f;
            s.Waveform = Waveform.Step;
            s.ParticipantId = 12;
            var m = new FreeControlModel(s);
            m.Enter(ColorAdjustment.Neutral);
            m.TogglePlayPause(); // Auto + 再生
            m.Tick(1f, out _);

            var state = new OperatorPanelState();
            state.CopyFrom(m, cursor: 3);
            Assert.That(state.Mode, Is.EqualTo(FreeMode.Auto));
            Assert.That(state.Playback, Is.EqualTo(PlaybackStatus.Playing));
            Assert.That(state.Cursor, Is.EqualTo(3));
            Assert.That(state.Get(MenuItem.Saturation), Is.EqualTo(-0.3f));
            Assert.That(state.Get(MenuItem.Mode), Is.EqualTo(1f));
            Assert.That(state.Waveform, Is.EqualTo(Waveform.Step));
            Assert.That(state.Get(MenuItem.ParticipantId), Is.EqualTo(12f));
            Assert.That(state.Phase, Is.EqualTo(m.Player.Phase));
            Assert.That(state.WaveValue, Is.EqualTo(m.Player.Value));
        }

        // ---------------------------------------------------------------- ログの形式

        [Test]
        public void TheCsvHeaderIsUnchanged()
        {
            // パネルの作業でログの列が変わっていないこと（解析スクリプトが列の名前と順に依存する）
            Assert.That(CsvFormat.Header, Is.EqualTo(
                "session_id,participant_id,wall_clock_utc,t_realtime_s,t_game_s,frame,unscaled_dt,state,trial_index," +
                "condition,trial_stage,trial_elapsed_s,target_saturation,passthrough_visible,saturation,brightness," +
                "contrast,free_mode,waveform,playing,active_controller,event,detail"));
        }
    }
}
