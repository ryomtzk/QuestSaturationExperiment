using QuestSaturation.Core;

/// <summary>
/// 互換のために残している。中身は <see cref="ExperimentProfile.Canonical"/> に任せる。
/// </summary>
public static class SaturationCurve
{
    public const float HOLD_SEC = 3f;   // 選択後に 0 で固定する時間
    public const float RAMP_SEC = 10f;  // 0 から target までかける時間

    public static float TargetFor(Condition c) => ExperimentProfile.Canonical.TargetFor(c);

    /// <summary>選択からの経過秒を入れると、その時点の saturation を返す。</summary>
    public static float Evaluate(Condition c, float elapsedSec) => ExperimentProfile.Canonical.Evaluate(c, elapsedSec);
}
