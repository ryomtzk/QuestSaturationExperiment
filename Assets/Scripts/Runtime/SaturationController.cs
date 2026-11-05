using QuestSaturation.Core;
using UnityEngine;

/// <summary>
/// パススルーの明るさ・コントラスト・彩度を OVRPassthroughLayer に送る唯一の窓口。
/// 値は [-1, 1] に収めてから、レイヤーが無効のときも含めてすぐに送る
/// （無効中のレイヤーは値を覚えていて、有効にした瞬間にその値で映るため）。
/// </summary>
[DefaultExecutionOrder(0)]
public class SaturationController : MonoBehaviour
{
    [SerializeField] private OVRPassthroughLayer passthroughLayer;
    [SerializeField] private float currentSaturation = 0f;
    [SerializeField] private float brightness = 0f;
    [SerializeField] private float contrast = 0f;

    public OVRPassthroughLayer Layer => passthroughLayer;

    public float CurrentSaturation => currentSaturation;

    /// <summary>最後にレイヤーへ送った値（クランプ後）。</summary>
    public ColorAdjustment Current => new ColorAdjustment(brightness, contrast, currentSaturation);

    public void SetSaturation(float value) => Apply(Current.WithSaturation(value));

    public void SetBrightness(float value) => Apply(new ColorAdjustment(value, contrast, currentSaturation));

    public void SetContrast(float value) => Apply(new ColorAdjustment(brightness, value, currentSaturation));

    public void Apply(ColorAdjustment value)
    {
        var c = value.Clamped();
        brightness = c.Brightness;
        contrast = c.Contrast;
        currentSaturation = c.Saturation;
        Push();
    }

    void OnEnable() => Apply(Current);

    void Update()
    {
        // SDK に毎フレーム上書きする処理はないが、他の書き込み元への保険として毎フレーム送り直す。
        // 値が同じなら SDK 側で何もしないので負荷はほぼない。
        Push();
    }

    void OnValidate()
    {
        var c = Current.Clamped();
        brightness = c.Brightness;
        contrast = c.Contrast;
        currentSaturation = c.Saturation;
    }

    private void Push()
    {
        if (passthroughLayer != null)
            passthroughLayer.SetBrightnessContrastSaturation(brightness, contrast, currentSaturation);
    }
}
