using UnityEngine;

/// <summary>
/// 右コントローラーのレイで球を指し、左右どちらかのトリガーで条件を決定する。
/// レイは右手からだけ出す。どちらのトリガーで決めたかは <see cref="SelectedWith"/> に残す。
/// </summary>
[DefaultExecutionOrder(-30)] // OVRCameraRig(-50) の後、ExperimentManager(-10) の前
public class ConditionSelector : MonoBehaviour
{
    public const string RightTrigger = "right_trigger";
    public const string LeftTrigger = "left_trigger";
    public const string BothTriggers = "both_triggers";

    [Header("References")]
    [SerializeField] private Transform rightControllerAnchor; // OVRCameraRig の RightControllerAnchor など
    [SerializeField] private SphereOption[] options;
    [SerializeField] private LineRenderer pointerLine;

    [Header("Settings")]
    [SerializeField] private float maxRayDistance = 5f;
    // 型は OVRInput.Button のまま。シーンに保存された 8192 は Button.PrimaryIndexTrigger を指す。
    // RawButton に変えると同じ 8192 が RThumbstickDown になってしまうので変えないこと。
    [SerializeField] private OVRInput.Button selectButton = OVRInput.Button.PrimaryIndexTrigger;
    [SerializeField] private LayerMask raycastMask = Physics.DefaultRaycastLayers;

    private SphereOption hovered;
    private OVRCameraRig rig;

    public bool HasSelected { get; private set; }
    public Condition SelectedCondition { get; private set; }
    public SphereOption SelectedOption { get; private set; }

    /// <summary>決定に使ったトリガー（right_trigger / left_trigger / both_triggers）。</summary>
    public string SelectedWith { get; private set; } = "";

    public SphereOption[] Options => options;
    public OVRInput.Button SelectButton => selectButton;
    public LayerMask RaycastMask => raycastMask;

    public void ResetSelection()
    {
        HasSelected = false;
        SelectedOption = null;
        SelectedWith = "";
        SetHovered(null);
    }

    void OnEnable()
    {
        if (pointerLine != null) pointerLine.useWorldSpace = false; // 手のアンカーに付いて動くので、描画が遅れない

        rig = rightControllerAnchor != null ? rightControllerAnchor.GetComponentInParent<OVRCameraRig>() : null;
        if (rig == null) rig = FindFirstObjectByType<OVRCameraRig>();
        if (rig != null) rig.UpdatedAnchors += OnUpdatedAnchors;
    }

    void OnDisable()
    {
        if (rig != null) rig.UpdatedAnchors -= OnUpdatedAnchors;
        rig = null;
        SetHovered(null);
        SetPointerVisible(false);
    }

    void Update()
    {
        if (HasSelected || !CanPoint())
        {
            SetHovered(null);
            SetPointerVisible(false);
            return;
        }

        SetHovered(Cast(out _));

        if (hovered == null) return;

        bool right = OVRInput.GetDown(selectButton, OVRInput.Controller.RTouch);
        bool left = OVRInput.GetDown(selectButton, OVRInput.Controller.LTouch);
        if (!right && !left) return;

        SelectedCondition = hovered.condition;
        SelectedOption = hovered;
        SelectedWith = right && left ? BothTriggers : right ? RightTrigger : LeftTrigger;
        HasSelected = true;
        SetHovered(null);
        SetPointerVisible(false);
    }

    void LateUpdate()
    {
        // OVRCameraRig が見つからないときの予備。見つかっていれば OnUpdatedAnchors が描く
        if (rig == null) DrawPointer();
    }

    private void OnUpdatedAnchors(OVRCameraRig _) => DrawPointer();

    private void DrawPointer()
    {
        if (!isActiveAndEnabled || HasSelected || !CanPoint())
        {
            SetPointerVisible(false);
            return;
        }

        Cast(out Vector3 end);
        if (pointerLine == null) return;

        var t = pointerLine.transform;
        pointerLine.SetPosition(0, t.InverseTransformPoint(rightControllerAnchor.position));
        pointerLine.SetPosition(1, t.InverseTransformPoint(end));
        SetPointerVisible(true);
    }

    private bool CanPoint() =>
        rightControllerAnchor != null && OVRInput.IsControllerConnected(OVRInput.Controller.RTouch);

    private SphereOption Cast(out Vector3 end)
    {
        Vector3 origin = rightControllerAnchor.position;
        Vector3 dir = rightControllerAnchor.forward;
        end = origin + dir * maxRayDistance;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, maxRayDistance, raycastMask, QueryTriggerInteraction.Ignore))
        {
            end = hit.point;
            return hit.collider.GetComponentInParent<SphereOption>();
        }
        return null;
    }

    private void SetHovered(SphereOption next)
    {
        if (next == hovered) return;
        if (hovered != null) hovered.SetHovered(false);
        hovered = next;
        if (hovered != null) hovered.SetHovered(true);
    }

    private void SetPointerVisible(bool visible)
    {
        if (pointerLine != null && pointerLine.enabled != visible) pointerLine.enabled = visible;
    }
}
