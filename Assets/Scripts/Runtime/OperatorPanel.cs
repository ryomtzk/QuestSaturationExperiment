using System.Globalization;
using System.Text;
using QuestSaturation.Core;
using UnityEngine;
using UnityEngine.UI;

namespace QuestSaturation
{
    /// <summary>
    /// オペレーター用の表示専用パネル。Canvas は実行時にコードで組み立てる
    /// （TMP の Essentials が入っていないので UI.Text と組み込みフォントを使う。Raycaster は付けない）。
    /// 背景は不透明にしている。半透明だとパススルーと混ざって読みにくく、色の評価にも影響するため。
    /// Selection と Trial に入るたびに必ず隠す（ExperimentManager の HidePanel 命令）。
    /// </summary>
    public class OperatorPanel : MonoBehaviour
    {
        [SerializeField] private ExperimentManager experiment;
        [SerializeField] private FreeControlController freeControl;
        [SerializeField] private SessionLogger sessionLogger;
        [SerializeField] private Transform head; // CenterEyeAnchor

        [Header("Layout")]
        [SerializeField] private float distance = 0.8f;
        [SerializeField] private float verticalOffset = -0.05f;
        [SerializeField] private Vector2 sizePx = new Vector2(640f, 520f);
        [SerializeField] private float metersPerPixel = 0.001f; // 0.8m 先で 1文字の高さが約 1.6 度
        [SerializeField] private int fontSize = 22;
        [SerializeField] private float refreshHz = 10f;

        private Canvas canvas;
        private Text text;
        private string lastText = "";
        private float nextRefresh;
        private bool placePending;
        private OVRDisplay hookedDisplay;
        private readonly StringBuilder sb = new StringBuilder(1024);

        public bool Visible { get; private set; }

        void Awake() => Build();

        void OnEnable()
        {
            if (experiment != null) experiment.CommandExecuted += OnCommand;
            OVRManager.TrackingAcquired += RequestPlace;
            HookRecenter();
        }

        void OnDisable()
        {
            if (experiment != null) experiment.CommandExecuted -= OnCommand;
            OVRManager.TrackingAcquired -= RequestPlace;
            if (hookedDisplay != null) hookedDisplay.RecenteredPose -= RequestPlace;
            hookedDisplay = null;
        }

        // OVRManager.display は OVRManager の初期化後にできるので、できたところで購読する
        private void HookRecenter()
        {
            if (hookedDisplay != null || OVRManager.display == null) return;
            hookedDisplay = OVRManager.display;
            hookedDisplay.RecenteredPose += RequestPlace;
        }

        public void Show()
        {
            SetVisible(true);
            RequestPlace();
        }

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

            if (placePending && TryPlace()) placePending = false;

            float now = Time.unscaledTime;
            if (now < nextRefresh) return;
            nextRefresh = now + 1f / Mathf.Max(1f, refreshHz);

            string s = Compose();
            if (s != lastText)
            {
                lastText = s;
                text.text = s;
            }
        }

        private void OnCommand(FlowCommand c)
        {
            if (c == FlowCommand.ShowPanel) Show();
            else if (c == FlowCommand.HidePanel) Hide();
        }

        private void RequestPlace()
        {
            if (Visible) placePending = true;
        }

        private void SetVisible(bool v)
        {
            Visible = v;
            if (canvas != null) canvas.gameObject.SetActive(v);
            if (v) nextRefresh = 0f;
        }

        /// <summary>頭の位置が有効になってから、頭の前に水平に置く。</summary>
        private bool TryPlace()
        {
            if (head == null) return false;
            if (!Application.isEditor && !HeadPoseValid()) return false;

            Vector3 forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            forward.Normalize();

            var t = canvas.transform;
            t.position = head.position + forward * distance + Vector3.up * verticalOffset;
            t.rotation = Quaternion.LookRotation(forward, Vector3.up);
            return true;
        }

        private static bool HeadPoseValid() =>
            OVRManager.OVRManagerinitialized && OVRManager.isHmdPresent &&
            OVRPlugin.GetNodePositionValid(OVRPlugin.Node.Head);

        private void Build()
        {
            if (canvas != null) return;

            var root = new GameObject("OperatorPanelCanvas", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 3f;
            var rt = (RectTransform)root.transform;
            rt.sizeDelta = sizePx;
            rt.localScale = Vector3.one * metersPerPixel;

            var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(root.transform, false);
            Stretch((RectTransform)bg.transform, 0f);
            var image = bg.GetComponent<Image>();
            image.color = new Color(0.08f, 0.08f, 0.08f, 1f);
            image.raycastTarget = false;

            var label = new GameObject("Text", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(root.transform, false);
            Stretch((RectTransform)label.transform, 16f);
            text = label.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.lineSpacing = 1f;

            SetVisible(false);
        }

        private static void Stretch(RectTransform r, float margin)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(margin, margin);
            r.offsetMax = new Vector2(-margin, -margin);
        }

        /// <summary>表示文字列。ASCII だけを使う（組み込みフォントで確実に出るように）。</summary>
        private string Compose()
        {
            var inv = CultureInfo.InvariantCulture;
            sb.Clear();
            var state = experiment != null ? experiment.State : FlowState.Selection;
            sb.Append("QSE v").Append(Application.version).Append("   State: ").Append(state).Append('\n');

            if (freeControl != null)
            {
                var m = freeControl.Model;
                sb.Append("Mode: ").Append(m.Mode);
                if (m.Mode == FreeMode.Auto)
                {
                    string play = m.Player.IsPlaying ? "PLAYING" : m.Player.IsFinished ? "FINISHED" : m.Player.Phase > 0 ? "PAUSED" : "STOPPED";
                    sb.Append("  [").Append(play).Append("] ").Append((m.Player.IsFinished ? 1.0 : m.Player.Phase).ToString("0.00", inv));
                }
                sb.Append('\n');

                var menu = freeControl.Menu;
                foreach (var def in ParameterDefs.All)
                {
                    sb.Append(menu.Current == def.Item ? "> " : "  ");
                    sb.Append(def.Label.PadRight(12)).Append(def.Format(m.GetValue(def.Item))).Append('\n');
                }
            }

            if (experiment != null && experiment.Saturation != null)
            {
                var c = experiment.Saturation.Current;
                sb.Append(string.Format(inv, "Out  b {0:+0.00;-0.00;0.00}  c {1:+0.00;-0.00;0.00}  s {2:+0.00;-0.00;0.00}\n",
                    c.Brightness, c.Contrast, c.Saturation));
            }

            sb.Append("Passthrough: ").Append(PassthroughStatus()).Append('\n');

            if (experiment != null)
            {
                // 試行数は確定済みの被験者のもの。変えたばかりで未確定の ID は別に示す
                int current = sessionLogger != null ? sessionLogger.ParticipantId : CurrentParticipant();
                int pending = sessionLogger != null ? sessionLogger.PendingParticipantId : -1;
                sb.Append("Participant P").Append(current.ToString("000", inv));
                if (pending >= 0 && pending != current) sb.Append(" -> P").Append(pending.ToString("000", inv)).Append(" (pending)");
                sb.Append("   Trials ").Append(experiment.Flow.TrialIndex.ToString(inv)).Append('\n');
            }

            if (sessionLogger != null)
                sb.Append("Log: ").Append(string.IsNullOrEmpty(sessionLogger.FileName) ? "(off)" : sessionLogger.FileName).Append('\n');

            sb.Append("Stick: item/value  Click: default  B: play, hold stop\n");
            sb.Append("Y: hide  X: recall  Menu hold: back to selection");
            return sb.ToString();
        }

        private int CurrentParticipant() => freeControl != null ? freeControl.Model.Settings.ParticipantId : 0;

        private string PassthroughStatus()
        {
            string init;
            if (!OVRManager.IsInsightPassthroughSupported()) init = "unsupported";
            else if (OVRManager.HasInsightPassthroughInitFailed()) init = "init FAILED";
            else if (OVRManager.IsInsightPassthroughInitPending()) init = "starting";
            else if (OVRManager.IsInsightPassthroughInitialized()) init = "ready";
            else init = "off";

            var layer = experiment != null ? experiment.PassthroughLayer : null;
            string layerState = layer == null ? "no layer" : layer.enabled ? "layer on" : "layer off";
            return init + ", " + layerState;
        }
    }
}
