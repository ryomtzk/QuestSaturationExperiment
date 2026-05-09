using UnityEngine;

public enum Condition { A, B, C }

public static class SaturationCurve
{
    public const float HOLD_SEC = 3f;   // 選択後に 0 で固定する時間
    public const float RAMP_SEC = 10f;  // 0 から target までかける時間

    public static float TargetFor(Condition c)
    {
        switch (c)
        {
            case Condition.A: return  0.0f;
            case Condition.B: return -0.8f;
            case Condition.C: return  0.8f;
            default:          return  0.0f;
        }
    }

    /// <summary>選択からの経過秒を入れると、その時点の saturation を返す。</summary>
    public static float Evaluate(Condition c, float elapsedSec)
    {
        float target = TargetFor(c);
        if (elapsedSec < HOLD_SEC) return 0f;

        float rampElapsed = elapsedSec - HOLD_SEC;
        if (rampElapsed >= RAMP_SEC) return target;

        return Mathf.Lerp(0f, target, rampElapsed / RAMP_SEC);
    }
}