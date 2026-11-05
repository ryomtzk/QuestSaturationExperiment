using System;
using System.Collections.Generic;
using QuestSaturation.Core;
using UnityEngine;

/// <summary>
/// 実験の進行役。状態遷移は Core の <see cref="ExperimentFlow"/> に任せ、
/// このクラスは Flow が返す命令を OVR とシーンに反映するだけにしている。
/// 実験のタイミングは以前と同じく、選んだフレームの Time.time から計る。
/// </summary>
[DefaultExecutionOrder(-10)]
public class ExperimentManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private OVRPassthroughLayer passthroughLayer;
    [SerializeField] private SaturationController saturationController;
    [SerializeField] private ConditionSelector selector;
    [SerializeField] private GameObject selectionRoot;   // 3つの球をぶら下げる空オブジェクト
    [SerializeField] private Camera mainCamera;          // OVRCameraRig の CenterEyeAnchor のカメラ

    [Header("Reset")]
    [SerializeField] private float resetHoldSec = 2f;
    [SerializeField] private OVRInput.Button resetButton = OVRInput.Button.One; // 右手 A（RTouch を指定して読む）

    private readonly ExperimentFlow flow = new ExperimentFlow();
    private readonly List<int> executionOrder = new List<int>();
    private TapOrHoldDetector resetDetector;
    private CameraClearFlags originalClearFlags;
    private Color originalBackgroundColor;
    private bool cameraDefaultsSaved;

    public ExperimentFlow Flow => flow;
    public FlowState State => flow.State;
    public OVRPassthroughLayer PassthroughLayer => passthroughLayer;
    public SaturationController Saturation => saturationController;
    public ConditionSelector Selector => selector;
    public GameObject SelectionRoot => selectionRoot;
    public Camera MainCamera => mainCamera;
    public float ResetHoldSec => resetHoldSec;
    public OVRInput.Button ResetButton => resetButton;

    /// <summary>Flow のイベント（状態遷移、試行の開始と終了など）。ログ係が購読する。</summary>
    public event Action<FlowEvent> FlowEventRaised;

    /// <summary>Flow の命令を実行したあと（パススルーを有効にした等）に通知する。</summary>
    public event Action<FlowCommand> CommandExecuted;

    /// <summary>リセットの長押しが成立した（振動などに使う）。</summary>
    public event Action ResetHoldFired;

    /// <summary>今の状態では受け付けない操作が来た。</summary>
    public event Action<Shortcut> InputIgnored;

    void Awake()
    {
        // 選択フェーズで書き換える前に、カメラの初期設定を保存する
        if (mainCamera != null)
        {
            originalClearFlags = mainCamera.clearFlags;
            originalBackgroundColor = mainCamera.backgroundColor;
            cameraDefaultsSaved = true;
        }
        resetDetector = new TapOrHoldDetector(resetHoldSec, tapEnabled: false);
    }

    void Start()
    {
        flow.Begin(Time.time);
        Execute();
    }

    void Update()
    {
        float now = Time.time;
        resetDetector.HoldSec = resetHoldSec;
        bool resetHeld = resetDetector.Update(OVRInput.Get(resetButton, OVRInput.Controller.RTouch), now) == PressResult.Hold;

        switch (flow.State)
        {
            case FlowState.Selection:
                if (selector != null && selector.HasSelected)
                {
                    flow.SelectCondition(selector.SelectedCondition, now);
                    // 決定と同時に A を押していても、離すまではリセットを受け付けない
                    resetDetector.Reset();
                    Execute();
                }
                else if (resetHeld)
                {
                    InputIgnored?.Invoke(Shortcut.ResetHold);
                }
                break;

            case FlowState.Trial:
                flow.Tick(now);
                Execute();
                if (resetHeld)
                {
                    Debug.Log("[QSE] reset by long-press");
                    ResetHoldFired?.Invoke();
                    flow.RequestReset(now);
                    Execute();
                }
                break;

            case FlowState.FreeControl:
                if (resetHeld) InputIgnored?.Invoke(Shortcut.ResetHold);
                break;
        }
    }

    /// <summary>左メニュー長押し。Selection ⇄ FreeControl。Trial 中は何もしない。</summary>
    public bool ToggleFreeControl()
    {
        bool changed = flow.ToggleFreeControl(Time.time);
        Execute();
        return changed;
    }

    /// <summary>試行中に起きた中断を記録する（trial_end に入る）。</summary>
    public void NoteInterruption(string kind) => flow.NoteInterruption(kind);

    /// <summary>これまでの試行数を設定する（被験者が替わったとき）。Trial 中は何もせず false。</summary>
    public bool SetTrialCount(int count)
    {
        if (!flow.SetTrialCount(count)) return false;
        Debug.Log("[QSE] trial count set to " + flow.TrialIndex);
        return true;
    }

    private void Execute()
    {
        // 順序は FlowExecution.Plan（Trial 中は色をイベントより先に。どちらでも色は EnablePassthrough より前）
        FlowExecution.Plan(flow.Actions, flow.ColorBeforeEvents, executionOrder);
        foreach (int i in executionOrder)
        {
            if (i == FlowExecution.Events)
            {
                foreach (var e in flow.Events)
                {
                    Debug.Log("[QSE] " + e);
                    FlowEventRaised?.Invoke(e);
                }
            }
            else
            {
                Run(flow.Actions[i]);
            }
        }

        // 球を選べるのは Selection の間だけ
        if (selector != null) selector.enabled = flow.State == FlowState.Selection;
    }

    private void Run(FlowAction a)
    {
        switch (a.Command)
        {
            case FlowCommand.ApplyColor:
                if (saturationController != null) saturationController.Apply(a.Color);
                break;

            case FlowCommand.EnablePassthrough:
                if (passthroughLayer != null) passthroughLayer.enabled = true;
                break;

            case FlowCommand.DisablePassthrough:
                if (passthroughLayer != null) passthroughLayer.enabled = false;
                break;

            case FlowCommand.UseSelectionBackground:
                if (mainCamera != null)
                {
                    mainCamera.clearFlags = CameraClearFlags.SolidColor;
                    mainCamera.backgroundColor = Color.white;
                }
                break;

            case FlowCommand.RestoreCameraBackground:
                if (mainCamera != null && cameraDefaultsSaved)
                {
                    mainCamera.clearFlags = originalClearFlags;
                    mainCamera.backgroundColor = originalBackgroundColor;
                }
                break;

            case FlowCommand.ShowSpheres:
                if (selectionRoot != null) selectionRoot.SetActive(true);
                break;

            case FlowCommand.HideSpheres:
                if (selectionRoot != null) selectionRoot.SetActive(false);
                break;

            case FlowCommand.ResetSelector:
                if (selector != null) selector.ResetSelection();
                break;
        }

        CommandExecuted?.Invoke(a.Command);
    }
}
