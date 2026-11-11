using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using QuestSaturation.Core;
using UnityEngine;

namespace QuestSaturation
{
    /// <summary>
    /// 実験ログを CSV に書く。保存先は persistentDataPath/sessions/session_&lt;UTC時刻&gt;_P&lt;ID&gt;.csv。
    /// - サンプル行：Trial 中は毎フレーム、それ以外は 10Hz。書き溜めて 1 秒ごとに書き出す。
    /// - イベント行：その場で書き出す（異常終了しても失うのは最大 1 秒分のサンプルだけ）。
    /// 被験者 ID を変えたら新しいファイルにし、試行数をその被験者のものにする（初めての ID は 0、この起動中に
    /// 使った ID はその続き）。いつ切り替えるかは <see cref="ParticipantSession"/>。
    /// </summary>
    [DefaultExecutionOrder(90)] // LateUpdate を OVRPassthroughLayer(+100) より前に
    public class SessionLogger : MonoBehaviour
    {
        public const string FolderName = "sessions";
        public const string BuildInfoResource = "QuestSaturationBuildInfo";

        [SerializeField] private ExperimentManager experiment;
        [SerializeField] private FreeControlController freeControl;
        [SerializeField] private ControllerShortcuts shortcuts;
        [SerializeField] private SettingsStore settingsStore;

        [Header("Timing")]
        [SerializeField] private float idleSampleHz = 10f;
        [SerializeField] private float flushIntervalSec = 1f;
        [SerializeField] private float resumeTimeoutSec = 3f;

        private readonly CsvRow row = new CsvRow();
        private StreamWriter writer;
        private string sessionId = "";
        private int fileParticipantId;
        private ParticipantSession participants = new ParticipantSession(0);
        private readonly SampleClock sampleClock = new SampleClock();
        private bool failed;
        private float lastFlushTime;

        // パススルーが実際に映っているかの推定
        private bool resumedSinceEnable;
        private float enabledAt = -1f;
        private bool timeoutReported;

        private bool hasActiveController;
        private OVRInput.Controller lastActiveController;

        private OVRPassthroughLayer hookedLayer;
        private OVRDisplay hookedDisplay;
        private bool subscribed;

        public string FileName { get; private set; } = "";

        /// <summary>いまのファイルの被験者 ID（確定済み）。</summary>
        public int ParticipantId => participants.CurrentId;

        /// <summary>FreeControl で変えたが、まだ確定していない ID。なければ -1。</summary>
        public int PendingParticipantId => participants.PendingId;
        public string FilePath { get; private set; } = "";
        public bool Failed => failed;

        /// <summary>
        /// 最後に書いたマーカー行の t_realtime_s と、書いたフレーム（パネルの通知で、CSV と同じ時刻を出すため）。
        /// 行が書けなかったときは更新しない（フレームが今のフレームでなければ、そのマーカーは書かれていない）。
        /// </summary>
        public double LastMarkerRealtime { get; private set; } = double.NaN;
        public int LastMarkerFrame { get; private set; } = -1;

        private double filledRealtime;

        /// <summary>レイヤーが有効で、有効にしてから一度でも描画が再開された（passthroughLayerResumed が来た）。</summary>
        public bool PassthroughVisible
        {
            get
            {
                var layer = experiment != null ? experiment.PassthroughLayer : null;
                return layer != null && layer.enabled && resumedSinceEnable && OVRManager.IsInsightPassthroughInitialized();
            }
        }

        void Awake()
        {
            int id = settingsStore != null ? settingsStore.EnsureLoaded().ParticipantId : 0;
            participants = new ParticipantSession(id);
            Open(participants.CurrentId, "startup");
        }

        void OnEnable() => Subscribe();

        void OnDisable()
        {
            Unsubscribe();
            Close("disabled");
        }

        void OnApplicationQuit() => Close("quit");

        void OnApplicationPause(bool paused)
        {
            if (paused && experiment != null) experiment.NoteInterruption("app_pause");
            WriteEvent(paused ? "app_pause" : "app_resume", "", echo: true);
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused) Flush();
        }

        void LateUpdate()
        {
            float real = Time.realtimeSinceStartup;

            // 普通は FreeControl を出たときの state_change で確定済み。念のため Selection でも確定する。
            // ログが書けなくなっていても試行数は切り替えるので、writer の確認より前に置く
            if (participants.HasPending && experiment != null && experiment.State == FlowState.Selection)
                CommitParticipant();

            if (writer == null) return;
            HookLateTargets();

            var active = OVRInput.GetActiveController();
            if (!hasActiveController || active != lastActiveController)
            {
                WriteEvent("active_controller_changed",
                    "from=" + (hasActiveController ? lastActiveController.ToString() : "none") + ";to=" + active, echo: true);
                lastActiveController = active;
                hasActiveController = true;
            }

            var layer = experiment != null ? experiment.PassthroughLayer : null;
            if (layer != null && layer.enabled && !resumedSinceEnable && enabledAt >= 0f && !timeoutReported &&
                real - enabledAt > resumeTimeoutSec)
            {
                timeoutReported = true;
                WriteEvent("passthrough_resume_timeout", Kv("waited_s", real - enabledAt) + ";" + Kv("init", InitState()), echo: true);
            }

            // Trial 中は毎フレーム。それ以外は 1/idleSampleHz 秒の格子に乗せる
            bool trial = experiment != null && experiment.State == FlowState.Trial;
            if (sampleClock.Due(Time.realtimeSinceStartupAsDouble, 1.0 / Mathf.Max(0.1f, idleSampleHz), everyFrame: trial))
            {
                Fill("", "");
                WriteLine(row.ToLine());
            }

            if (real - lastFlushTime >= flushIntervalSec) Flush();
        }

        // ---------------- ファイル ----------------

        private void Open(int participantId, string reason)
        {
            if (failed) return;
            fileParticipantId = participantId;
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            sessionId = stamp + "_P" + participantId.ToString("000", CultureInfo.InvariantCulture);

            try
            {
                string dir = Path.Combine(Application.persistentDataPath, FolderName);
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "session_" + sessionId + ".csv");
                for (int n = 2; File.Exists(path); n++)
                    path = Path.Combine(dir, "session_" + sessionId + "_" + n.ToString(CultureInfo.InvariantCulture) + ".csv");

                var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536);
                writer = new StreamWriter(stream, new UTF8Encoding(false), 65536) { NewLine = "\n", AutoFlush = false };
                FilePath = path;
                FileName = Path.GetFileName(path);
                writer.WriteLine(CsvFormat.Header);
                Debug.Log("[QSE] logging to " + path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                Fail(e);
                return;
            }

            lastFlushTime = Time.realtimeSinceStartup;
            WriteEvent("session_start", SessionInfo(reason), echo: true);
        }

        private void Close(string reason)
        {
            if (writer == null) return;
            WriteEvent("session_end", Kv("reason", reason) + ";" + Kv("trials", experiment != null ? experiment.Flow.TrialIndex : 0), echo: true);
            if (writer == null) return; // 書き込みに失敗すると Fail が writer を閉じている
            try
            {
                writer.Flush();
                writer.Dispose();
            }
            catch (Exception e) when (e is IOException || e is ObjectDisposedException)
            {
                Debug.LogError("[QSE] closing log failed: " + e.Message);
            }
            writer = null;
        }

        /// <summary>
        /// 保留中の被験者 ID の変更を確定する。被験者が替わったら、古いファイルを閉じ、試行数を新しい被験者のもの
        /// （初めてなら 0）にして、新しいファイルを開く。Trial 中は確定しない（ID は FreeControl でしか変えられない
        /// ので、普通は起きない）。
        /// </summary>
        private void CommitParticipant()
        {
            if (!participants.HasPending) return;
            if (experiment != null && experiment.State == FlowState.Trial) return;
            int trials = experiment != null ? experiment.Flow.TrialIndex : 0;
            if (!participants.Commit(trials, out int restored)) return; // 元の ID に戻しただけ

            Close("participant_changed"); // session_end には前の被験者の試行数が入る
            if (experiment != null) experiment.SetTrialCount(restored);
            fileParticipantId = participants.CurrentId; // ログが書けなくても、ID は切り替える
            Open(participants.CurrentId, "participant_changed");
        }

        private void Flush()
        {
            if (writer == null) return;
            lastFlushTime = Time.realtimeSinceStartup;
            try
            {
                writer.Flush();
            }
            catch (IOException e)
            {
                Fail(e);
            }
        }

        private void WriteLine(string line)
        {
            if (writer == null) return;
            try
            {
                writer.WriteLine(line);
            }
            catch (IOException e)
            {
                Fail(e);
            }
        }

        private void Fail(Exception e)
        {
            if (failed) return;
            failed = true;
            Debug.LogError("[QSE] session log disabled: " + e.GetType().Name + ": " + e.Message);
            try { writer?.Dispose(); } catch (Exception) { /* 既に壊れているので無視 */ }
            writer = null;
            FileName = "";
        }

        /// <summary>イベント行を書いてすぐに書き出す。echo なら logcat にも出す。</summary>
        public void WriteEvent(string name, string detail, bool echo = false)
        {
            if (echo) Debug.Log("[QSE] " + name + (string.IsNullOrEmpty(detail) ? "" : " " + detail));
            if (writer == null) return;
            Fill(name, detail);
            WriteLine(row.ToLine());
            Flush();
        }

        private void Fill(string evt, string detail)
        {
            row.Clear();
            float now = Time.time;
            filledRealtime = Time.realtimeSinceStartupAsDouble;
            var flow = experiment != null ? experiment.Flow : null;
            var state = flow != null ? flow.State : FlowState.Selection;
            bool trial = state == FlowState.Trial;

            row.Set(CsvColumn.SessionId, sessionId)
               .Set(CsvColumn.ParticipantId, fileParticipantId)
               .Set(CsvColumn.WallClockUtc, CsvFormat.Utc(DateTime.UtcNow))
               .Set(CsvColumn.TRealtimeSec, filledRealtime)
               .Set(CsvColumn.TGameSec, now)
               .Set(CsvColumn.Frame, Time.frameCount)
               .Set(CsvColumn.UnscaledDt, Time.unscaledDeltaTime)
               .Set(CsvColumn.State, state.ToString())
               .Set(CsvColumn.PassthroughVisible, PassthroughVisible)
               .Set(CsvColumn.ActiveController, OVRInput.GetActiveController().ToString())
               .Set(CsvColumn.Event, evt)
               .Set(CsvColumn.Detail, detail);

            if (flow != null) row.Set(CsvColumn.TrialIndex, flow.TrialIndex);

            if (trial)
            {
                float elapsed = flow.TrialElapsed(now); // ExperimentFlow.Tick が Evaluate に渡す値と同じ計算
                row.Set(CsvColumn.Condition, flow.Condition.ToString())
                   .Set(CsvColumn.TrialStage, flow.StageAt(now).ToString().ToLowerInvariant())
                   .Set(CsvColumn.TrialElapsedSec, elapsed)
                   .Set(CsvColumn.TargetSaturation, flow.TargetSaturation);
            }

            var sat = experiment != null ? experiment.Saturation : null;
            if (sat != null)
            {
                var c = sat.Current; // クランプ後に実際に送った値
                row.Set(CsvColumn.Saturation, c.Saturation)
                   .Set(CsvColumn.Brightness, c.Brightness)
                   .Set(CsvColumn.Contrast, c.Contrast);
            }

            if (state == FlowState.FreeControl && freeControl != null)
            {
                var m = freeControl.Model;
                row.Set(CsvColumn.FreeMode, m.Mode.ToString())
                   .Set(CsvColumn.Waveform, m.Settings.Waveform.ToString())
                   .Set(CsvColumn.Playing, m.Player.IsPlaying);
            }
        }

        // ---------------- イベントの購読 ----------------

        private void Subscribe()
        {
            if (subscribed) return;
            subscribed = true;

            if (experiment != null)
            {
                experiment.FlowEventRaised += OnFlowEvent;
                experiment.CommandExecuted += OnCommand;
            }
            if (freeControl != null)
            {
                freeControl.Changed += OnFreeControlChanged;
                freeControl.ParticipantIdChanged += OnParticipantIdChanged;
            }
            if (shortcuts != null)
            {
                shortcuts.InputIgnored += OnInputIgnored;
                shortcuts.MarkerPressed += OnMarker;
            }

            OVRManager.HMDMounted += OnHmdMounted;
            OVRManager.HMDUnmounted += OnHmdUnmounted;
            OVRManager.InputFocusAcquired += OnFocusAcquired;
            OVRManager.InputFocusLost += OnFocusLost;
            OVRManager.TrackingAcquired += OnTrackingAcquired;
            OVRManager.TrackingLost += OnTrackingLost;
            OVRManager.OnPassthroughInitializedStateChange += OnPassthroughInitChanged;
            HookLateTargets();
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            subscribed = false;

            if (experiment != null)
            {
                experiment.FlowEventRaised -= OnFlowEvent;
                experiment.CommandExecuted -= OnCommand;
            }
            if (freeControl != null)
            {
                freeControl.Changed -= OnFreeControlChanged;
                freeControl.ParticipantIdChanged -= OnParticipantIdChanged;
            }
            if (shortcuts != null)
            {
                shortcuts.InputIgnored -= OnInputIgnored;
                shortcuts.MarkerPressed -= OnMarker;
            }

            OVRManager.HMDMounted -= OnHmdMounted;
            OVRManager.HMDUnmounted -= OnHmdUnmounted;
            OVRManager.InputFocusAcquired -= OnFocusAcquired;
            OVRManager.InputFocusLost -= OnFocusLost;
            OVRManager.TrackingAcquired -= OnTrackingAcquired;
            OVRManager.TrackingLost -= OnTrackingLost;
            OVRManager.OnPassthroughInitializedStateChange -= OnPassthroughInitChanged;

            if (hookedLayer != null) hookedLayer.passthroughLayerResumed.RemoveListener(OnPassthroughResumed);
            hookedLayer = null;
            if (hookedDisplay != null) hookedDisplay.RecenteredPose -= OnRecentered;
            hookedDisplay = null;
        }

        // レイヤーと display は後からできることがあるので、見つかったところで購読する
        private void HookLateTargets()
        {
            if (!subscribed) return;
            var layer = experiment != null ? experiment.PassthroughLayer : null;
            if (hookedLayer == null && layer != null)
            {
                hookedLayer = layer;
                hookedLayer.passthroughLayerResumed.AddListener(OnPassthroughResumed);
            }
            if (hookedDisplay == null && OVRManager.display != null)
            {
                hookedDisplay = OVRManager.display;
                hookedDisplay.RecenteredPose += OnRecentered;
            }
        }

        private void OnFlowEvent(FlowEvent e)
        {
            CommitParticipant(); // flow.State はもう新しい状態。この行（state_change など）を書く前に ID の変更を確定する
            if (e.Name == "trial_start") WriteEvent("condition_selected", SelectionInfo());
            WriteEvent(e.Name, e.Detail);

            if (e.Name == "ramp_start" && !PassthroughVisible)
                WriteEvent("passthrough_not_visible_at_ramp_start", Kv("init", InitState()), echo: true);

        }

        private void OnCommand(FlowCommand c)
        {
            switch (c)
            {
                case FlowCommand.EnablePassthrough:
                    resumedSinceEnable = false;
                    timeoutReported = false;
                    enabledAt = Time.realtimeSinceStartup;
                    WriteEvent("layer_enabled", Kv("init", InitState()));
                    break;

                case FlowCommand.DisablePassthrough:
                    resumedSinceEnable = false;
                    enabledAt = -1f;
                    WriteEvent("layer_disabled", "");
                    break;
            }
        }

        private void OnPassthroughResumed(OVRPassthroughLayer _)
        {
            string detail;
            if (!resumedSinceEnable && enabledAt >= 0f)
                detail = Kv("latency_s", Time.realtimeSinceStartup - enabledAt); // 有効にしてから映るまで
            else
                detail = "after_enable=false";
            resumedSinceEnable = true;
            WriteEvent("passthrough_resumed", detail, echo: true);
        }

        private void OnFreeControlChanged(string name, string detail)
        {
            // ID を送っている途中の行は前のファイルへ。ID 以外の操作をしたら、その行から新しい被験者のファイルへ
            if (ParticipantSession.IsCommitTrigger(name, detail)) CommitParticipant();
            WriteEvent(name, detail);
        }

        private void OnParticipantIdChanged(int id) => participants.NoteIdEdited(id);

        private void OnInputIgnored(Shortcut s, FlowState state) =>
            WriteEvent("input_ignored", Kv("input", s.ToString()) + ";" + Kv("state", state.ToString()), echo: true);

        private void OnMarker()
        {
            CommitParticipant();
            bool open = writer != null;
            WriteEvent("marker", "", echo: true);
            if (!open || writer == null) return; // 書く前から閉じていたか、書けずに止まった
            LastMarkerRealtime = filledRealtime;
            LastMarkerFrame = Time.frameCount;
        }

        private void OnHmdMounted() => WriteEvent("hmd_mounted", "", echo: true);

        private void OnHmdUnmounted()
        {
            if (experiment != null) experiment.NoteInterruption("hmd_unmounted");
            WriteEvent("hmd_unmounted", "", echo: true);
        }

        private void OnFocusAcquired() => WriteEvent("input_focus_acquired", "", echo: true);

        private void OnFocusLost()
        {
            if (experiment != null) experiment.NoteInterruption("input_focus_lost");
            WriteEvent("input_focus_lost", "", echo: true);
        }

        private void OnTrackingAcquired() => WriteEvent("tracking_acquired", "", echo: true);

        private void OnTrackingLost()
        {
            if (experiment != null) experiment.NoteInterruption("tracking_lost");
            WriteEvent("tracking_lost", "", echo: true);
        }

        private void OnRecentered() => WriteEvent("recentered", "", echo: true);

        private void OnPassthroughInitChanged(bool initialized) =>
            WriteEvent("passthrough_init", Kv("initialized", initialized ? "true" : "false") + ";" + Kv("init", InitState()), echo: true);

        // ---------------- 詳細の文字列 ----------------

        private string SelectionInfo()
        {
            var sel = experiment != null ? experiment.Selector : null;
            var opt = sel != null ? sel.SelectedOption : null;
            if (opt == null) return "";

            // 左/中/右は、選択肢を x 座標（SelectionRoot のローカル）で並べたときの位置
            string position = "";
            var opts = sel.Options;
            if (opts != null && opts.Length > 0)
            {
                var sorted = opts.Where(o => o != null).OrderBy(o => o.transform.localPosition.x).ToList();
                int i = sorted.IndexOf(opt);
                position = sorted.Count == 3 && i >= 0 ? new[] { "left", "center", "right" }[i] : i.ToString(CultureInfo.InvariantCulture);
            }

            return Kv("condition", opt.condition.ToString()) + ";" + Kv("position", position) + ";" +
                   Kv("material", opt.MaterialName) + ";" + Kv("button", sel.SelectedWith);
        }

        private string SessionInfo(string reason)
        {
            string build = "editor";
            var info = Resources.Load<TextAsset>(BuildInfoResource);
            if (info != null) build = info.text.Trim().Replace("\n", ";").Replace("\r", "");

            string refresh = "unknown";
            try
            {
                if (OVRManager.display != null)
                    refresh = OVRManager.display.displayFrequency.ToString("0.#", CultureInfo.InvariantCulture);
            }
            catch (Exception) { /* XR が動いていないエディタなど */ }

            var tz = TimeZoneInfo.Local;
            return string.Join(";", new[]
            {
                Kv("reason", reason),
                Kv("app_version", Application.version),
                build,
                Kv("build_guid", Application.buildGUID),
                Kv("unity", Application.unityVersion),
                Kv("device", SystemInfo.deviceModel),
                Kv("os", SystemInfo.operatingSystem),
                Kv("refresh_hz", refresh),
                Kv("color_space", QualitySettings.activeColorSpace.ToString()),
                Kv("timezone", tz.Id),
                Kv("utc_offset", tz.GetUtcOffset(DateTime.Now).ToString()),
                Kv("profile", ExperimentProfile.Canonical.Describe()),
                Kv("idle_sample_hz", idleSampleHz),
                Kv("flush_s", flushIntervalSec),
                Kv("settings_warning", settingsStore != null && settingsStore.LoadWarning != null ? settingsStore.LoadWarning : "none"),
            });
        }

        private static string InitState()
        {
            if (!OVRManager.IsInsightPassthroughSupported()) return "unsupported";
            if (OVRManager.HasInsightPassthroughInitFailed()) return "failed";
            if (OVRManager.IsInsightPassthroughInitPending()) return "pending";
            if (OVRManager.IsInsightPassthroughInitialized()) return "initialized";
            return "off";
        }

        private static string Kv(string k, string v) => k + "=" + v;
        private static string Kv(string k, int v) => k + "=" + v.ToString(CultureInfo.InvariantCulture);
        private static string Kv(string k, float v) => k + "=" + v.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
