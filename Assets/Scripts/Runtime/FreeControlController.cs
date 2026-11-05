using System;
using System.Globalization;
using QuestSaturation.Core;
using UnityEngine;

namespace QuestSaturation
{
    /// <summary>
    /// FreeControl 中に <see cref="FreeControlModel"/> を動かし、その出力を SaturationController に送る。
    /// 入る・出るのは ExperimentManager の命令（EnterFreeControl / ExitFreeControl）に従う。
    /// </summary>
    [DefaultExecutionOrder(-9)] // ExperimentManager(-10) の後、SaturationController(0) の前
    public class FreeControlController : MonoBehaviour
    {
        [SerializeField] private ExperimentManager experiment;
        [SerializeField] private SaturationController saturationController;
        [SerializeField] private SettingsStore settingsStore;

        private FreeControlModel model;
        private readonly ParameterMenu menu = new ParameterMenu();

        public bool Active { get; private set; }
        public FreeControlModel Model => EnsureModel();
        public ParameterMenu Menu => menu;

        /// <summary>ログ用の通知（名前, 詳細）。play / pause / stop / ramp_finished / param_change / free_enter / free_exit。</summary>
        public event Action<string, string> Changed;

        /// <summary>被験者 ID が変わった（新しい値）。</summary>
        public event Action<int> ParticipantIdChanged;

        void OnEnable()
        {
            if (experiment != null) experiment.CommandExecuted += OnCommand;
        }

        void OnDisable()
        {
            if (experiment != null) experiment.CommandExecuted -= OnCommand;
        }

        void Update()
        {
            if (!Active) return;
            var output = EnsureModel().Tick(Time.deltaTime, out bool rampFinished);
            if (saturationController != null) saturationController.Apply(output);
            if (rampFinished) Raise(FreeControlEvents.RampFinished, "");
        }

        public void MoveCursor(int delta) => menu.MoveCursor(delta);

        public ParameterDef CurrentDef => menu.CurrentDef;

        public void AdjustCurrent(int direction)
        {
            var item = menu.Current;
            int idBefore = Model.Settings.ParticipantId;
            if (!Model.Adjust(item, direction)) return;
            OnParameterChanged(item, "adjust", idBefore);
        }

        public void ResetCurrent()
        {
            var item = menu.Current;
            int idBefore = Model.Settings.ParticipantId;
            if (!Model.ResetToDefault(item)) return;
            OnParameterChanged(item, "reset", idBefore);
        }

        public void TogglePlayPause()
        {
            var before = Model.Mode;
            bool playing = Model.TogglePlayPause();
            if (before != Model.Mode)
                Raise(FreeControlEvents.ParamChange, FreeControlEvents.ParamChangeDetail(MenuItem.Mode, Model.Mode.ToString(), "play"));
            Raise(playing ? "play" : "pause", Describe());
        }

        public void Stop()
        {
            Model.Stop();
            Raise("stop", Describe());
        }

        private void OnCommand(FlowCommand c)
        {
            switch (c)
            {
                case FlowCommand.EnterFreeControl:
                    var current = saturationController != null ? saturationController.Current : ColorAdjustment.Neutral;
                    EnsureModel().Enter(current);
                    menu.ResetCursor(); // 毎回いちばん上（Mode）から。前回の Participant の行に残ったまま ID を触らないように
                    Active = true;
                    Raise("free_enter", Describe());
                    break;

                case FlowCommand.ExitFreeControl:
                    if (!Active) break;
                    EnsureModel().Exit();
                    Active = false;
                    Raise("free_exit", "");
                    break;
            }
        }

        private void OnParameterChanged(MenuItem item, string how, int idBefore)
        {
            var def = ParameterDefs.Get(item);
            Raise(FreeControlEvents.ParamChange, FreeControlEvents.ParamChangeDetail(item, def.Format(Model.GetValue(item)), how));
            if (item != MenuItem.Mode && settingsStore != null) settingsStore.MarkDirty();
            if (Model.Settings.ParticipantId != idBefore) ParticipantIdChanged?.Invoke(Model.Settings.ParticipantId);
        }

        private string Describe()
        {
            var m = EnsureModel();
            var s = m.Settings;
            return string.Format(CultureInfo.InvariantCulture,
                "mode={0};waveform={1};from={2:0.##};to={3:0.##};period={4:0.#};steps={5};phase={6:0.###}",
                m.Mode, s.Waveform, s.From, s.To, s.PeriodSec, s.Steps, m.Player.Phase);
        }

        private FreeControlModel EnsureModel()
        {
            if (model != null) return model;
            var settings = settingsStore != null ? settingsStore.EnsureLoaded() : FreeControlSettings.CreateDefault();
            model = new FreeControlModel(settings);
            return model;
        }

        private void Raise(string name, string detail)
        {
            Debug.Log("[QSE] " + name + (string.IsNullOrEmpty(detail) ? "" : " " + detail));
            Changed?.Invoke(name, detail);
        }
    }
}
