using UnityEngine;

/// <summary>
/// 条件を表す球。ホバー中は少し大きくする（表示の変化は拡大だけ）。
/// 以前は発光も切り替えていたが、実機ビルドではそのシェーダーバリアントが落ちていて見えていなかった。
/// 被験者がこれまで見ていた表示と同じにするため、発光の処理は外した。
/// </summary>
[RequireComponent(typeof(MeshRenderer), typeof(SphereCollider))]
public class SphereOption : MonoBehaviour
{
    public Condition condition;

    [SerializeField] private float normalScale = 0.2f;     // 直径20cm
    [SerializeField] private float hoveredScale = 0.24f;

    private bool hovered;
    private bool applied;

    public bool IsHovered => hovered;

    /// <summary>ログ用のマテリアル名（例：Mat_Sphere_Red）。マテリアルを複製しないよう sharedMaterial を読む。</summary>
    public string MaterialName
    {
        get
        {
            var r = GetComponent<MeshRenderer>();
            return r != null && r.sharedMaterial != null ? r.sharedMaterial.name : "";
        }
    }

    void Awake() => SetHovered(false);

    public void SetHovered(bool value)
    {
        if (applied && value == hovered) return;
        hovered = value;
        applied = true;
        transform.localScale = Vector3.one * (value ? hoveredScale : normalScale);
    }
}
