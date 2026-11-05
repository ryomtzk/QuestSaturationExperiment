using System.Collections.Generic;
using System.Globalization;

namespace QuestSaturation.Core
{
    public enum FlowState
    {
        Selection = 0,   // 白背景で3つの球から条件を選ぶ
        Trial = 1,       // パススルーで実験カーブを再生
        FreeControl = 2, // オペレーターが自由に色を調整
    }

    /// <summary>ExperimentManager が実行する命令。順序（ApplyColor を先に送る場合や、イベントとの前後）は <see cref="FlowExecution.Plan"/>。</summary>
    public enum FlowCommand
    {
        ApplyColor,              // Color の値を SaturationController に送る
        EnablePassthrough,
        DisablePassthrough,
        UseSelectionBackground,  // カメラを白の単色背景にする
        RestoreCameraBackground, // カメラを起動時の設定に戻す
        ShowSpheres,
        HideSpheres,
        ResetSelector,
        HidePanel,
        ShowPanel,
        EnterFreeControl,        // FreeControl を Manual・停止の状態で始める
        ExitFreeControl,
    }

    public readonly struct FlowAction
    {
        public readonly FlowCommand Command;
        public readonly ColorAdjustment Color;

        public FlowAction(FlowCommand command, ColorAdjustment color = default)
        {
            Command = command;
            Color = color;
        }

        public override string ToString() => Command == FlowCommand.ApplyColor ? $"ApplyColor{Color}" : Command.ToString();
    }

    public readonly struct FlowEvent
    {
        public readonly string Name;
        public readonly string Detail;

        public FlowEvent(string name, string detail = "")
        {
            Name = name;
            Detail = detail ?? "";
        }

        public override string ToString() => string.IsNullOrEmpty(Detail) ? Name : Name + "(" + Detail + ")";
    }

    /// <summary>
    /// Selection / Trial / FreeControl の状態機械。OVR に触れず、命令とログイベントの列を返すだけにしている。
    /// 呼び出しのたびに <see cref="Actions"/> と <see cref="Events"/> を作り直すので、呼んだ側はすぐに処理する。
    /// 守っている順序：パススルーを有効にする前に、必ず ApplyColor で次に見せる値を送る
    /// （無効中のレイヤーには前の試行の値が残っていて、有効にした瞬間それが映るため）。
    /// </summary>
    public sealed class ExperimentFlow
    {
        private readonly ExperimentProfile profile;
        private readonly List<FlowAction> actions = new List<FlowAction>();
        private readonly List<FlowEvent> events = new List<FlowEvent>();
        private readonly List<string> interruptions = new List<string>();

        private float trialStartTime;
        private TrialStage lastStage;

        public ExperimentFlow(ExperimentProfile profile = null)
        {
            this.profile = profile ?? ExperimentProfile.Canonical;
        }

        public ExperimentProfile Profile => profile;
        public FlowState State { get; private set; } = FlowState.Selection;
        public bool Started { get; private set; }

        /// <summary>Trial 中の条件。それ以外では最後に選ばれた条件。</summary>
        public Condition Condition { get; private set; }

        /// <summary>
        /// これまでに始めた試行の数。Trial 中は今の試行の番号（1始まり）。被験者が替わると、その被験者の試行数になる
        /// （初めての ID なら 0、この起動中に使った ID ならその続き。<see cref="SetTrialCount"/>）。
        /// </summary>
        public int TrialIndex { get; private set; }

        public IReadOnlyList<FlowAction> Actions => actions;
        public IReadOnlyList<FlowEvent> Events => events;

        public float TrialElapsed(float now) => State == FlowState.Trial ? now - trialStartTime : 0f;

        public TrialStage StageAt(float now) => profile.StageAt(TrialElapsed(now));

        public float TargetSaturation => State == FlowState.Trial ? profile.TargetFor(Condition) : 0f;

        /// <summary>起動直後に1回呼ぶ。必ず Selection・(0,0,0) から始める。</summary>
        public void Begin(float now)
        {
            Clear();
            Started = true;
            EnterSelection("startup", now);
        }

        /// <summary>球が選ばれた。Selection 以外では無視して false。</summary>
        public bool SelectCondition(Condition condition, float now)
        {
            Clear();
            if (!Started || State != FlowState.Selection) return false;

            Condition = condition;
            TrialIndex++;
            trialStartTime = now;
            lastStage = TrialStage.Hold;
            interruptions.Clear();

            float target = profile.TargetFor(condition);
            ChangeState(FlowState.Trial, "selected");
            Emit("trial_start", Kv("condition", condition.ToString()) + ";" + Kv("trial", TrialIndex) + ";" + Kv("target", target));

            // 実験の時刻 0 の値を、レイヤーを有効にする前に送る
            Do(FlowCommand.ApplyColor, ColorAdjustment.SaturationOnly(profile.Evaluate(condition, 0f)));
            Do(FlowCommand.EnablePassthrough);
            Do(FlowCommand.RestoreCameraBackground);
            Do(FlowCommand.HideSpheres);
            Do(FlowCommand.HidePanel);
            return true;
        }

        /// <summary>Trial 中のフレーム更新。実験カーブの値を送り、段階が変わったらイベントを出す。</summary>
        public void Tick(float now)
        {
            Clear();
            if (State != FlowState.Trial) return;

            float elapsed = now - trialStartTime;
            var stage = profile.StageAt(elapsed);
            if (stage != lastStage)
            {
                // 1フレームで Hold から Target まで飛んだ場合も、ramp_start と ramp_end を両方出す
                if (lastStage == TrialStage.Hold && stage != TrialStage.Hold)
                    Emit("ramp_start", Kv("elapsed", elapsed));
                if (stage == TrialStage.Target)
                    Emit("ramp_end", Kv("elapsed", elapsed));
                lastStage = stage;
            }

            Do(FlowCommand.ApplyColor, ColorAdjustment.SaturationOnly(profile.Evaluate(Condition, elapsed)));
        }

        /// <summary>右A 長押し。Trial を終えて Selection に戻る。Trial 以外では false。</summary>
        public bool RequestReset(float now)
        {
            Clear();
            if (State != FlowState.Trial) return false;
            EndTrial("reset", now);
            EnterSelection("reset", now);
            return true;
        }

        /// <summary>
        /// 左メニュー長押し。Selection → FreeControl、FreeControl → Selection。
        /// Trial 中は切り替えない（false）。
        /// </summary>
        public bool ToggleFreeControl(float now)
        {
            Clear();
            if (!Started) return false;

            switch (State)
            {
                case FlowState.Selection:
                    ChangeState(FlowState.FreeControl, "menu_hold");
                    Do(FlowCommand.ApplyColor, ColorAdjustment.Neutral);
                    Do(FlowCommand.EnablePassthrough);
                    Do(FlowCommand.RestoreCameraBackground);
                    Do(FlowCommand.HideSpheres);
                    Do(FlowCommand.EnterFreeControl);
                    Do(FlowCommand.ShowPanel);
                    return true;

                case FlowState.FreeControl:
                    Do(FlowCommand.ExitFreeControl);
                    EnterSelection("menu_hold", now);
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// これまでの試行数を設定する。被験者が替わったときに呼ぶ（新しい被験者は 0、この起動中に使った ID に
        /// 戻ったときはその続き）。被験者 ID は FreeControl で変える。Trial 中は何もせず false。
        /// 命令とイベントの列は消さない（ExperimentManager が実行している途中に呼ばれることがあるため）。
        /// </summary>
        public bool SetTrialCount(int count)
        {
            if (State == FlowState.Trial) return false;
            TrialIndex = count < 0 ? 0 : count;
            return true;
        }

        /// <summary>
        /// ExperimentManager がイベントより先に色を送るべきか。Trial 中（試行の開始と毎フレームの更新）は先に送り、
        /// trial_start や ramp_start の行に、そのフレームの経過時間と同じフレームの色が並ぶようにする。
        /// それ以外（リセット、FreeControl の出入り）はイベントを先に出し、trial_end や state_change の行には
        /// それまで見えていた色を残す。どちらの場合も、色は EnablePassthrough より先に送られる。
        /// </summary>
        public bool ColorBeforeEvents => State == FlowState.Trial;

        /// <summary>
        /// 試行中に起きた中断（HMD を外した、フォーカスを失った、アプリが一時停止した等）を記録する。
        /// trial_end の detail に含める。
        /// </summary>
        public void NoteInterruption(string kind)
        {
            if (State != FlowState.Trial || string.IsNullOrEmpty(kind)) return;
            if (!interruptions.Contains(kind)) interruptions.Add(kind);
        }

        private void EndTrial(string reason, float now)
        {
            float elapsed = now - trialStartTime;
            string detail = Kv("reason", reason) + ";" + Kv("condition", Condition.ToString()) + ";" +
                            Kv("trial", TrialIndex) + ";" + Kv("elapsed", elapsed) + ";" +
                            Kv("stage", profile.StageAt(elapsed).ToString()) + ";" +
                            Kv("interrupted", interruptions.Count > 0 ? "true" : "false");
            if (interruptions.Count > 0) detail += ";" + Kv("interruptions", string.Join("|", interruptions));
            Emit("trial_end", detail);
            interruptions.Clear();
        }

        private void EnterSelection(string reason, float now)
        {
            ChangeState(FlowState.Selection, reason);
            Do(FlowCommand.HidePanel);
            Do(FlowCommand.ApplyColor, ColorAdjustment.Neutral);
            Do(FlowCommand.DisablePassthrough);
            Do(FlowCommand.UseSelectionBackground);
            Do(FlowCommand.ShowSpheres);
            Do(FlowCommand.ResetSelector);
        }

        private void ChangeState(FlowState next, string reason)
        {
            var prev = State;
            State = next;
            Emit("state_change", Kv("from", prev.ToString()) + ";" + Kv("to", next.ToString()) + ";" + Kv("reason", reason));
        }

        private void Clear()
        {
            actions.Clear();
            events.Clear();
        }

        private void Do(FlowCommand c, ColorAdjustment color = default) => actions.Add(new FlowAction(c, color));

        private void Emit(string name, string detail) => events.Add(new FlowEvent(name, detail));

        private static string Kv(string k, string v) => k + "=" + v;
        private static string Kv(string k, int v) => k + "=" + v.ToString(CultureInfo.InvariantCulture);
        private static string Kv(string k, float v) => k + "=" + v.ToString("0.####", CultureInfo.InvariantCulture);
    }

    /// <summary>ExperimentManager が命令とイベントを処理する順序。</summary>
    public static class FlowExecution
    {
        /// <summary>順序の列の中で「ここでイベントをすべて出す」を表す値。</summary>
        public const int Events = -1;

        /// <summary>
        /// 命令の番号を処理する順に order へ入れる。<see cref="Events"/> の位置でイベントをすべて出す。
        /// colorFirst なら ApplyColor をすべて先に、次にイベント、次に残りの命令を元の順で。
        /// そうでなければイベント、次にすべての命令を元の順で。どちらでも、ApplyColor どうし、ApplyColor 以外どうしの
        /// 順序は変わらない（colorFirst のときは ApplyColor が他の命令より前に出る）。
        /// </summary>
        public static void Plan(IReadOnlyList<FlowAction> actions, bool colorFirst, List<int> order)
        {
            order.Clear();
            if (colorFirst)
            {
                for (int i = 0; i < actions.Count; i++)
                    if (actions[i].Command == FlowCommand.ApplyColor) order.Add(i);
            }
            order.Add(Events);
            for (int i = 0; i < actions.Count; i++)
                if (!colorFirst || actions[i].Command != FlowCommand.ApplyColor) order.Add(i);
        }
    }
}
