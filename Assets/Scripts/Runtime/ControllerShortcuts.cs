using System;
using QuestSaturation.Core;
using UnityEngine;

namespace QuestSaturation
{
    /// <summary>
    /// コントローラーのボタンを読み、状態ごとに許された操作（<see cref="ShortcutMap"/>）だけを実行する。
    /// 許されていない操作は <see cref="InputIgnored"/> で知らせる（ログに input_ignored として残る）。
    /// 左右は RawButton で明示して読む。右A の長押しリセットは従来どおり ExperimentManager が読む。
    /// </summary>
    [DefaultExecutionOrder(-20)] // ExperimentManager(-10) より先に、同じフレームの入力を反映する
    public class ControllerShortcuts : MonoBehaviour
    {
        [SerializeField] private ExperimentManager experiment;
        [SerializeField] private FreeControlController freeControl;
        [SerializeField] private OperatorPanel panel;

        [Header("Timing")]
        [SerializeField] private float menuHoldSec = 1.5f;
        [SerializeField] private float stopHoldSec = 1f;
        [SerializeField] private float hapticSec = 0.1f;
        [SerializeField, Range(0f, 1f)] private float hapticAmplitude = 0.6f;

        private TapOrHoldDetector menuButton;
        private TapOrHoldDetector bButton;
        private readonly StickAxisLock stickAxis = new StickAxisLock();
        private readonly StickRepeater stickVertical = new StickRepeater();
        private readonly StickRepeater stickHorizontal = new StickRepeater();
        private float leftHapticUntil = -1f;
        private float rightHapticUntil = -1f;

        /// <summary>今の状態では受け付けない操作が来た（操作, その時の状態）。</summary>
        public event Action<Shortcut, FlowState> InputIgnored;

        /// <summary>左グリップのマーカー。</summary>
        public event Action MarkerPressed;

        public float MenuHoldProgress => menuButton != null ? menuButton.HoldProgress(Time.unscaledTime) : 0f;

        /// <summary>右B 長押し（停止）の進み具合（0〜1）。パネルの表示用。</summary>
        public float StopHoldProgress => bButton != null ? bButton.HoldProgress(Time.unscaledTime) : 0f;

        void Awake()
        {
            menuButton = new TapOrHoldDetector(menuHoldSec, tapEnabled: false);
            bButton = new TapOrHoldDetector(stopHoldSec, tapEnabled: true);
        }

        void OnEnable()
        {
            OVRManager.InputFocusLost += ResetInputs;
            if (experiment != null)
            {
                experiment.ResetHoldFired += OnResetHold;
                experiment.InputIgnored += OnManagerIgnored;
            }
        }

        void OnDisable()
        {
            OVRManager.InputFocusLost -= ResetInputs;
            if (experiment != null)
            {
                experiment.ResetHoldFired -= OnResetHold;
                experiment.InputIgnored -= OnManagerIgnored;
            }
            StopHaptics();
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused) ResetInputs();
        }

        void Update()
        {
            UpdateHaptics();
            if (experiment == null) return;

            float now = Time.unscaledTime;
            menuButton.HoldSec = menuHoldSec;
            bButton.HoldSec = stopHoldSec;

            // 左☰ 長押し：Selection ⇄ FreeControl
            if (menuButton.Update(OVRInput.Get(OVRInput.RawButton.Start), now) == PressResult.Hold)
            {
                if (Run(Shortcut.MenuHold))
                {
                    experiment.ToggleFreeControl();
                    Pulse(OVRInput.Controller.LTouch);
                }
            }

            // 左グリップ：マーカー（どの状態でも記録する）。
            // 試行中は振動させない。被験者がコントローラーを持っていると、刺激以外の手がかりになるため
            if (OVRInput.GetDown(OVRInput.RawButton.LHandTrigger) && Run(Shortcut.Marker))
            {
                MarkerPressed?.Invoke();
                if (experiment.State != FlowState.Trial) Pulse(OVRInput.Controller.LTouch);
            }

            // 左Y：パネル表示の切替、左X：パネルを呼び戻す
            if (OVRInput.GetDown(OVRInput.RawButton.Y) && Run(Shortcut.TogglePanel) && panel != null) panel.Toggle();
            if (OVRInput.GetDown(OVRInput.RawButton.X) && Run(Shortcut.RecallPanel) && panel != null) panel.Recall();

            // 右B：タップで再生/一時停止、長押しで停止
            switch (bButton.Update(OVRInput.Get(OVRInput.RawButton.B), now))
            {
                case PressResult.Tap:
                    if (Run(Shortcut.PlayPause) && freeControl != null) freeControl.TogglePlayPause();
                    break;
                case PressResult.Hold:
                    if (Run(Shortcut.Stop) && freeControl != null)
                    {
                        freeControl.Stop();
                        Pulse(OVRInput.Controller.RTouch);
                    }
                    break;
            }

            // 右スティック
            bool clickDown = OVRInput.GetDown(OVRInput.RawButton.RThumbstick);
            bool clickHeld = OVRInput.Get(OVRInput.RawButton.RThumbstick);
            if (clickDown && Run(Shortcut.StickClick) && freeControl != null) freeControl.ResetCurrent();

            // 倒し始めた軸を中央に戻すまで使い続ける（斜めを通ってもリピートが切れない）。
            // リピートは FreeControl の小数の項目だけ。ほかの状態では、押し続けても input_ignored は1回だけ
            Vector2 axis = OVRInput.Get(OVRInput.RawAxis2D.RThumbstick);
            var locked = stickAxis.Update(axis.x, axis.y);
            bool repeat = experiment.State == FlowState.FreeControl && freeControl != null && freeControl.CurrentDef.Repeats;
            int v = stickVertical.Update(locked == StickAxis.Vertical ? axis.y : 0f, clickHeld, repeat: false, now);
            int h = stickHorizontal.Update(locked == StickAxis.Horizontal ? axis.x : 0f, clickHeld, repeat, now);

            if (v > 0 && Run(Shortcut.StickUp) && freeControl != null) freeControl.MoveCursor(-1);
            if (v < 0 && Run(Shortcut.StickDown) && freeControl != null) freeControl.MoveCursor(+1);
            if (h > 0 && Run(Shortcut.StickRight) && freeControl != null) freeControl.AdjustCurrent(+1);
            if (h < 0 && Run(Shortcut.StickLeft) && freeControl != null) freeControl.AdjustCurrent(-1);

            // 決定トリガーは ConditionSelector が読む。Selection 以外で引かれたら記録だけする
            if (experiment.State != FlowState.Selection)
            {
                if (OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger)) Run(Shortcut.SelectRightTrigger);
                if (OVRInput.GetDown(OVRInput.RawButton.LIndexTrigger)) Run(Shortcut.SelectLeftTrigger);
            }
        }

        /// <summary>コントローラーを短く振動させる。</summary>
        public void Pulse(OVRInput.Controller controller)
        {
            float until = Time.unscaledTime + hapticSec;
            if ((controller & OVRInput.Controller.LTouch) != 0) leftHapticUntil = until;
            if ((controller & OVRInput.Controller.RTouch) != 0) rightHapticUntil = until;
            OVRInput.SetControllerVibration(1f, hapticAmplitude, controller);
        }

        /// <summary>今の状態で許されていれば true。許されていなければ InputIgnored を出して false。</summary>
        private bool Run(Shortcut s)
        {
            var state = experiment.State;
            if (ShortcutMap.IsAllowed(state, s)) return true;
            InputIgnored?.Invoke(s, state);
            return false;
        }

        private void OnResetHold() => Pulse(OVRInput.Controller.RTouch);

        private void OnManagerIgnored(Shortcut s) => InputIgnored?.Invoke(s, experiment.State);

        private void ResetInputs()
        {
            menuButton?.Reset();
            bButton?.Reset();
            stickAxis.Reset();
            stickVertical.Reset();
            stickHorizontal.Reset();
        }

        private void UpdateHaptics()
        {
            float now = Time.unscaledTime;
            if (leftHapticUntil >= 0f && now >= leftHapticUntil)
            {
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
                leftHapticUntil = -1f;
            }
            if (rightHapticUntil >= 0f && now >= rightHapticUntil)
            {
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
                rightHapticUntil = -1f;
            }
        }

        private void StopHaptics()
        {
            if (leftHapticUntil >= 0f) OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
            if (rightHapticUntil >= 0f) OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
            leftHapticUntil = rightHapticUntil = -1f;
        }
    }
}
