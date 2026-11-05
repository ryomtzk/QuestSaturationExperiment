namespace QuestSaturation.Core
{
    /// <summary>試行が実験カーブのどこにいるか。</summary>
    public enum TrialStage
    {
        Hold = 0,   // 選択直後、彩度 0 で保持
        Ramp = 1,   // 0 から目標値へ変化中
        Target = 2, // 目標値で保持
    }
}
