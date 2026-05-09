using UnityEngine;

public class SaturationController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private OVRPassthroughLayer passthroughLayer;

    [Header("Saturation cycle")]
    [SerializeField] private float[] saturationValues = { -1f, 0f, 1f };
    [SerializeField] private float secondsPerStep = 3f;

    private int currentIndex = 0;
    private float timer = 0f;

    void Update()
    {
        if (passthroughLayer == null) return;
        if (saturationValues == null || saturationValues.Length == 0) return;

        // 毎フレーム適用(Building Blocks の上書きに対抗)
        float saturation = saturationValues[currentIndex];
        passthroughLayer.SetBrightnessContrastSaturation(
            brightness: 0f,
            contrast: 0f,
            saturation: saturation
        );

        // タイマーで値を切り替え
        timer += Time.deltaTime;
        if (timer >= secondsPerStep)
        {
            timer = 0f;
            currentIndex = (currentIndex + 1) % saturationValues.Length;
            Debug.Log($"[Saturation] {saturationValues[currentIndex]:F2}");
        }
    }
}