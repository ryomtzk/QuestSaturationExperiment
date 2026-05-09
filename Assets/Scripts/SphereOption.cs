using UnityEngine;

[RequireComponent(typeof(Renderer), typeof(SphereCollider))]
public class SphereOption : MonoBehaviour
{
    public Condition condition;

    [SerializeField] private float normalScale = 0.2f;     // 直径20cm
    [SerializeField] private float hoveredScale = 0.24f;
    [SerializeField] private float emissionStrength = 0.6f;

    private Renderer rend;
    private Color baseColor;

    void Awake()
    {
        rend = GetComponent<Renderer>();
        baseColor = rend.material.color;
        rend.material.EnableKeyword("_EMISSION");
        SetHovered(false);
    }

    public void SetHovered(bool hovered)
    {
        transform.localScale = Vector3.one * (hovered ? hoveredScale : normalScale);
        Color emission = hovered ? baseColor * emissionStrength : Color.black;
        rend.material.SetColor("_EmissionColor", emission);
    }
}