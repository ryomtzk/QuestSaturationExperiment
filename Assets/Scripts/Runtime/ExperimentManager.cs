using UnityEngine;

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
    [SerializeField] private OVRInput.Button resetButton = OVRInput.Button.One; // 右手 A

    private enum Phase { Selection, Experiment }
    private Phase phase;
    private Condition selected;
    private float experimentStartTime;
    private float resetHoldStart = -1f;
    private CameraClearFlags originalClearFlags;
    private Color originalBackgroundColor;
    private bool cameraDefaultsSaved = false;
    void Start()
{
    if (mainCamera != null)
    {
        originalClearFlags = mainCamera.clearFlags;
        originalBackgroundColor = mainCamera.backgroundColor;
        cameraDefaultsSaved = true;
    }
    EnterSelection();
}

    void Update()
    {
        if (phase == Phase.Selection) UpdateSelection();
        else                          UpdateExperiment();
    }

    // ---------------- Selection ----------------
    void EnterSelection()
    {
        phase = Phase.Selection;
        if (passthroughLayer != null) passthroughLayer.enabled = false;
        if (mainCamera != null)
        {
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = Color.white;
        }
        if (selectionRoot != null) selectionRoot.SetActive(true);
        if (selector != null) selector.ResetSelection();
        if (saturationController != null) saturationController.SetSaturation(0f);
        resetHoldStart = -1f;

        Debug.Log("[Experiment] Selection phase");
    }

    void UpdateSelection()
    {
        if (selector != null && selector.HasSelected)
        {
            selected = selector.SelectedCondition;
            EnterExperiment();
        }
    }

    // ---------------- Experiment ----------------
   void EnterExperiment()
{
    phase = Phase.Experiment;
    if (passthroughLayer != null) passthroughLayer.enabled = true;

    // ★ 追加：選択フェーズで上書きしたカメラ設定を元に戻す
    if (mainCamera != null && cameraDefaultsSaved)
    {
        mainCamera.clearFlags = originalClearFlags;
        mainCamera.backgroundColor = originalBackgroundColor;
    }

    if (selectionRoot != null) selectionRoot.SetActive(false);
    experimentStartTime = Time.time;

    Debug.Log($"[Experiment] Started condition {selected} (target={SaturationCurve.TargetFor(selected)})");
}

    void UpdateExperiment()
    {
        // saturation の更新
        float elapsed = Time.time - experimentStartTime;
        float sat = SaturationCurve.Evaluate(selected, elapsed);
        if (saturationController != null) saturationController.SetSaturation(sat);

        // A 長押しでリセット
        if (OVRInput.Get(resetButton))
        {
            if (resetHoldStart < 0f)
            {
                resetHoldStart = Time.time;
            }
            else if (Time.time - resetHoldStart >= resetHoldSec)
            {
                Debug.Log("[Experiment] Reset by long-press");
                EnterSelection();
            }
        }
        else
        {
            resetHoldStart = -1f;
        }
    }
}