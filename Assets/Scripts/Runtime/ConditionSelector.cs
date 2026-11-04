using UnityEngine;

public class ConditionSelector : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform rightControllerAnchor; // OVRCameraRig の RightControllerAnchor など
    [SerializeField] private SphereOption[] options;
    [SerializeField] private LineRenderer pointerLine;

    [Header("Settings")]
    [SerializeField] private float maxRayDistance = 5f;
    [SerializeField] private OVRInput.Button selectButton = OVRInput.Button.PrimaryIndexTrigger;

    public bool HasSelected { get; private set; }
    public Condition SelectedCondition { get; private set; }

    public void ResetSelection()
    {
        HasSelected = false;
        if (pointerLine != null) pointerLine.enabled = true;
    }

    void Update()
    {
        if (HasSelected || rightControllerAnchor == null) return;

        Vector3 origin = rightControllerAnchor.position;
        Vector3 dir    = rightControllerAnchor.forward;
        Vector3 endPoint = origin + dir * maxRayDistance;
        SphereOption hovered = null;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, maxRayDistance))
        {
            hovered = hit.collider.GetComponent<SphereOption>();
            endPoint = hit.point;
        }

        // ポインター描画
        if (pointerLine != null)
        {
            pointerLine.SetPosition(0, origin);
            pointerLine.SetPosition(1, endPoint);
        }

        // ハイライト更新
        foreach (var opt in options)
        {
            if (opt != null) opt.SetHovered(opt == hovered);
        }

        // トリガーで決定
        if (hovered != null && OVRInput.GetDown(selectButton))
        {
            SelectedCondition = hovered.condition;
            HasSelected = true;
            if (pointerLine != null) pointerLine.enabled = false;
            foreach (var opt in options)
                if (opt != null) opt.SetHovered(false);
        }
    }
}