using UnityEngine;

public class SaturationController : MonoBehaviour
{
    [SerializeField] private OVRPassthroughLayer passthroughLayer;
    [SerializeField] private float currentSaturation = 0f;
    [SerializeField] private float brightness = 0f;
    [SerializeField] private float contrast = 0f;

    public void SetSaturation(float value) => currentSaturation = value;
    public float CurrentSaturation => currentSaturation;

    void Update()
    {
        // Building Blocks の Passthrough は毎フレーム上書きしてくる可能性があるため
        // 毎フレーム適用する（既存のハマりどころ #2）
        if (passthroughLayer != null && passthroughLayer.enabled)
        {
            passthroughLayer.SetBrightnessContrastSaturation(brightness, contrast, currentSaturation);
        }
    }
}