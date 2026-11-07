using System;
using System.Globalization;
using QuestSaturation.Core;
using QuestSaturation.UI;
using UnityEngine;

namespace QuestSaturation
{
    /// <summary>
    /// オペレーター用の表示専用パネル（FreeControl のときだけ見える）。見た目は <see cref="OperatorPanelView"/> が
    /// 実行時にコードで組み立て、このクラスは状態を集めて渡すことと、表示・配置だけを受け持つ。
    /// - 表示するのは FreeControl の間だけ（<see cref="PanelVisibility"/>）。Selection と Trial に入るたびに必ず、
    ///   その場で隠す（ExperimentManager の HidePanel 命令。念のため、FreeControl 以外で表示中なら LateUpdate でも隠す）。
    /// - 実験の処理を止めない：ほかの部品のイベント（命令、マーカー、FreeControl の変更）の中では、
    ///   印を付けるだけで、表示の処理はしない（同じイベントを SessionLogger も受けるので、ここで例外が出ると
    ///   ログの行が抜けるおそれがある）。表示の処理は LateUpdate で、例外が出たらパネルを止めて1回だけ報告する。
    /// - 背景は不透明にしている。半透明だとパススルーと混ざって読みにくく、色の評価にも影響するため。
    /// - 操作はコントローラーのボタンだけ（レイで押す部品は無い。Raycaster も付けない）。
    /// - ログには何も書かない（通知はパネルの中だけ）。Unity のログには "[QSE-Panel]" を付けて出す。
    /// </summary>
    public class OperatorPanel : MonoBehaviour
    {
        private const string LogTag = "[QSE-Panel] ";

        [SerializeField] private ExperimentManager experiment;
        [SerializeField] private FreeControlController freeControl;
        [SerializeField] private SessionLogger sessionLogger;
        [SerializeField] private ControllerShortcuts shortcuts;
        [SerializeField] private Transform head; // CenterEyeAnchor

        [Header("Layout")]
        [SerializeField] private float distance = 0.8f;
        [Tooltip("目の高さからの上下（m）。目の高さの帯を空けて、パネルの横のパススルーを見やすくする")]
        [SerializeField] private float heightOffset = -0.14f;
        [Tooltip("Canvas の 1 単位の長さ（m）。0.0005 でパネルは 0.64 × 0.48 m")]
        [SerializeField] private float metersPerUnit = 0.0005f;
        [Tooltip("X で呼び戻したときに、新しい位置へ動く時間（秒）")]
        [SerializeField] private float recallSec = 0.25f;

        private OperatorPanelView view;
        private readonly OperatorPanelState state = new OperatorPanelState();
        private readonly float[] previousValues = new float[ParameterDefs.All.Length];
        private bool placePending;
        private bool placed;
        private bool broken;
        private OVRDisplay hookedDisplay;

        // 置き直しのアニメーション
        private bool moving;
        private float moveT;
        private Vector3 moveFromPos, moveToPos;
        private Quaternion moveFromRot, moveToRot;

        // 遅く変わる値（電池、時計）
        private float nextSlowRefresh;
        private int lastMinute = -1;

        // 通知のきっかけ。イベントの中では数えるだけにして、LateUpdate で通知にする
        private int markerPresses;
        private int markerRun;
        private string markerMessage;
        private string resetDetail;
        private int lastCommittedParticipant = int.MinValue;
        private bool lastLogFailed;
        private PlaybackStatus lastPlayback;

        public bool Visible { get; private set; }

        /// <summary>表示中のパネル（プレビューやテスト用）。</summary>
        public OperatorPanelView View => view;

        void Awake() => Build();

        void OnEnable()
        {
            if (experiment != null) experiment.CommandExecuted += OnCommand;
            if (freeControl != null) freeControl.Changed += OnFreeControlChanged;
            if (shortcuts != null) shortcuts.MarkerPressed += OnMarker;
            OVRManager.TrackingAcquired += RequestPlace;
            HookRecenter();
        }

        void OnDisable()
        {
            if (experiment != null) experiment.CommandExecuted -= OnCommand;
            if (freeControl != null) freeControl.Changed -= OnFreeControlChanged;
            if (shortcuts != null) shortcuts.MarkerPressed -= OnMarker;
            OVRManager.TrackingAcquired -= RequestPlace;
            if (hookedDisplay != null) hookedDisplay.RecenteredPose -= RequestPlace;
            hookedDisplay = null;
            SetVisible(false);
        }

        // OVRManager.display は OVRManager の初期化後にできるので、できたところで購読する
        private void HookRecenter()
        {
            if (hookedDisplay != null || OVRManager.display == null) return;
            hookedDisplay = OVRManager.display;
            hookedDisplay.RecenteredPose += RequestPlace;
        }

        /// <summary>FreeControl のときだけ表示する。ほかの状態では何もしない。</summary>
        public void Show()
        {
            if (view == null || broken) return;
            if (experiment != null && !PanelVisibility.Allowed(experiment.State)) return;
            if (!Visible)
            {
                SetVisible(true);
                placed = false; // 隠していた間の位置は使わず、目の前に置き直す
                try
                {
                    view.ClearTransient();
                    Refresh(force: true);
                    view.PlayAppear();
                }
                catch (Exception e)
                {
                    Fail(e);
                    return;
                }
            }
            RequestPlace();
        }

        /// <summary>すぐに隠す。どの状態からでも、例外を出さずに隠せるよう最小限のことだけをする。</summary>
        public void Hide() => SetVisible(false);

        public void Toggle()
        {
            if (Visible) Hide();
            else Show();
        }

        /// <summary>目の前に置き直す（表示していなければ表示する）。</summary>
        public void Recall() => Show();

        void LateUpdate()
        {
            HookRecenter();
            if (!Visible) return;

            // パネルは FreeControl 専用。被験者が見る状態では、命令の順序にかかわらず表示しない
            if (experiment != null && !PanelVisibility.Allowed(experiment.State))
            {
                Hide();
                return;
            }

            try
            {
                if (placePending && TryPlace()) placePending = false;
                UpdateMove(Time.unscaledDeltaTime);
                Refresh(force: false);
                view.Animate(Time.unscaledDeltaTime);
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        /// <summary>表示の処理で例外が出た。パネルを止めて隠し、1回だけ報告する（実験の処理は続ける）。</summary>
        private void Fail(Exception e)
        {
            SetVisible(false);
            if (broken) return;
            broken = true;
            Debug.LogError(LogTag + "the operator panel stopped after an error and stays hidden: " + e);
        }

        // ---------------------------------------------------------------- イベント（印を付けるだけ）

        private void OnCommand(FlowCommand c)
        {
            try
            {
                if (c == FlowCommand.ShowPanel) Show();
                else if (c == FlowCommand.HidePanel) Hide();
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        private void OnMarker()
        {
            // 時刻は、SessionLogger が行に書いたものを LateUpdate で読む（どちらのハンドラーが先に呼ばれても同じ）
            markerPresses++;
        }

        private void OnFreeControlChanged(string name, string detail)
        {
            try
            {
                if (name == FreeControlEvents.ParamChange && detail != null &&
                    detail.EndsWith(";how=reset", StringComparison.Ordinal))
                    resetDetail = detail;
            }
            catch (Exception)
            {
                // 通知が出ないだけ
            }
        }

        // ---------------------------------------------------------------- 表示と配置

        private void RequestPlace()
        {
            if (Visible) placePending = true;
        }

        private void SetVisible(bool v)
        {
            Visible = v;
            if (!v)
            {
                moving = false;
                placePending = false;
            }
            if (view == null) return;
            if (view.gameObject.activeSelf != v) view.gameObject.SetActive(v);
            if (!v)
            {
                try
                {
                    view.ClearTransient();
                }
                catch (Exception)
                {
                    // 隠すことはもう済んでいる
                }
            }
        }

        /// <summary>
        /// 頭の位置が有効になってから、頭の前の少し下に、頭のほうへ向けて置く。表示中に置き直すときは滑らかに動かす。
        /// </summary>
        private bool TryPlace()
        {
            if (head == null) return false;
            if (!Application.isEditor && !HeadPoseValid()) return false;

            Vector3 forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 pos = head.position + forward * distance + Vector3.up * heightOffset;
            Vector3 facing = pos - head.position;
            Quaternion rot = facing.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(facing, Vector3.up) : Quaternion.LookRotation(forward, Vector3.up);
            var t = view.transform;

            if (!placed || recallSec <= 0f)
            {
                t.SetPositionAndRotation(pos, rot);
                moving = false;
            }
            else
            {
                moveFromPos = t.position;
                moveFromRot = t.rotation;
                moveToPos = pos;
                moveToRot = rot;
                moveT = 0f;
                moving = true;
            }
            placed = true;
            return true;
        }

        private void UpdateMove(float dt)
        {
            if (!moving) return;
            moveT = Mathf.Min(1f, moveT + dt / Mathf.Max(0.01f, recallSec));
            float e = moveT * moveT * (3f - 2f * moveT);
            view.transform.SetPositionAndRotation(Vector3.Lerp(moveFromPos, moveToPos, e), Quaternion.Slerp(moveFromRot, moveToRot, e));
            if (moveT >= 1f) moving = false;
        }

        private static bool HeadPoseValid() =>
            OVRManager.OVRManagerinitialized && OVRManager.isHmdPresent &&
            OVRPlugin.GetNodePositionValid(OVRPlugin.Node.Head);

        private void Build()
        {
            if (view != null) return;
            try
            {
                view = OperatorPanelView.Create(transform, metersPerUnit);
                view.SetVersion("v" + Application.version);
            }
            catch (Exception e)
            {
                view = null;
                broken = true;
                Debug.LogError(LogTag + "could not build the operator panel; it will stay hidden: " + e);
            }
            SetVisible(false);
        }

        // ---------------------------------------------------------------- 状態を集める

        private void Refresh(bool force)
        {
            Gather(force);
            view.Apply(state);
            Notify(force);
        }

        private void Gather(bool force)
        {
            // 前のフレームの値（リセットの通知で、元の値を出すため）
            Array.Copy(state.Values, previousValues, previousValues.Length);

            state.State = experiment != null ? experiment.State : FlowState.FreeControl;

            if (freeControl != null) state.CopyFrom(freeControl.Model, freeControl.Menu.Cursor);

            var saturation = experiment != null ? experiment.Saturation : null;
            state.Output = saturation != null ? saturation.Current : ColorAdjustment.Neutral;

            state.Passthrough = PassthroughInitState();
            var layer = experiment != null ? experiment.PassthroughLayer : null;
            state.HasLayer = layer != null;
            state.LayerEnabled = layer != null && layer.enabled;
            state.PassthroughVisible = sessionLogger != null ? sessionLogger.PassthroughVisible : state.LayerEnabled;

            // 試行数と被験者は、確定済みの被験者のもの。変えたばかりで未確定の ID は別に持つ
            int settingsId = freeControl != null ? freeControl.Model.Settings.ParticipantId : 0;
            state.ParticipantId = sessionLogger != null ? sessionLogger.ParticipantId : settingsId;
            state.PendingParticipantId = sessionLogger != null ? sessionLogger.PendingParticipantId : -1;
            state.Trials = experiment != null ? experiment.Flow.TrialIndex : 0;
            state.LogFile = sessionLogger != null ? sessionLogger.FileName : "";
            state.LogFailed = sessionLogger != null && sessionLogger.Failed;

            state.MenuHoldProgress = shortcuts != null ? shortcuts.MenuHoldProgress : 0f;
            state.StopHoldProgress = shortcuts != null ? shortcuts.StopHoldProgress : 0f;

            float now = Time.unscaledTime;
            if (force || now >= nextSlowRefresh)
            {
                nextSlowRefresh = now + 1f;
                // Android では電池の問い合わせが JNI 呼び出しになるので、1 秒に 1 回だけ読む
                state.BatteryLevel = SystemInfo.batteryLevel;
                var bs = SystemInfo.batteryStatus;
                state.Charging = bs == BatteryStatus.Charging || bs == BatteryStatus.Full;
                var clock = DateTime.Now;
                int minute = clock.Hour * 60 + clock.Minute;
                if (minute != lastMinute)
                {
                    lastMinute = minute;
                    state.Clock = clock.ToString("HH:mm", CultureInfo.InvariantCulture);
                }
            }
        }

        private static PassthroughInit PassthroughInitState()
        {
            if (!OVRManager.IsInsightPassthroughSupported()) return PassthroughInit.Unsupported;
            if (OVRManager.HasInsightPassthroughInitFailed()) return PassthroughInit.Failed;
            if (OVRManager.IsInsightPassthroughInitPending()) return PassthroughInit.Starting;
            if (OVRManager.IsInsightPassthroughInitialized()) return PassthroughInit.Ready;
            return PassthroughInit.Off;
        }

        // ---------------------------------------------------------------- 通知

        /// <summary>前のフレームとの違いから通知を出す。再生・一時停止・停止は波形カードに常に出ているので通知しない。</summary>
        private void Notify(bool reset)
        {
            if (reset)
            {
                lastCommittedParticipant = state.ParticipantId;
                lastLogFailed = state.LogFailed;
                lastPlayback = state.Playback;
                markerPresses = 0;
                markerRun = 0;
                resetDetail = null;
                return;
            }

            // このフレームに起きたことを集めてから、いちばん大事な1つだけを出す（選び方は PanelNotices）
            var e = new NoticeEvents
            {
                LogJustFailed = state.LogFailed && !lastLogFailed,
                LogFailed = state.LogFailed,
                Committed = state.ParticipantId != lastCommittedParticipant,
                ParticipantId = state.ParticipantId,
                MarkerPresses = markerPresses,
                MarkerAt = double.NaN,
                RampFinished = state.Playback == PlaybackStatus.Finished && lastPlayback != PlaybackStatus.Finished && state.Mode == FreeMode.Auto,
                RampTo = state.Get(MenuItem.To),
                ToastVisible = view.ToastMessage != null,
            };
            lastLogFailed = state.LogFailed;
            lastCommittedParticipant = state.ParticipantId;
            lastPlayback = state.Playback;
            markerPresses = 0;

            // マーカーは、SessionLogger がこのフレームに行を書いたときだけ「記録した」と出す（時刻も CSV と同じ）
            if (e.MarkerPresses > 0 && sessionLogger != null && sessionLogger.LastMarkerFrame == Time.frameCount)
                e.MarkerAt = sessionLogger.LastMarkerRealtime;
            if (markerMessage != null && string.Equals(view.ToastMessage, markerMessage)) e.VisibleMarkerRun = markerRun;

            if (resetDetail != null)
            {
                e.ResetDef = ResetItem(resetDetail);
                resetDetail = null;
                if (e.ResetDef != null)
                {
                    e.ResetBefore = previousValues[(int)e.ResetDef.Item];
                    e.ResetAfter = state.Get(e.ResetDef.Item);
                }
            }

            var notice = PanelNotices.Choose(e);
            if (notice.IsNone) return;
            view.ShowNotice(notice);
            if (notice.Kind == NoticeKind.Marker)
            {
                markerRun = notice.MarkerRun;
                markerMessage = notice.Message;
            }
        }

        /// <summary>param_change の detail（item=Period;value=...;how=reset）から項目を読む。</summary>
        private static ParameterDef ResetItem(string detail)
        {
            foreach (var def in ParameterDefs.All)
                if (detail.StartsWith(FreeControlEvents.ItemPrefix(def.Item), StringComparison.Ordinal)) return def;
            return null;
        }
    }
}
