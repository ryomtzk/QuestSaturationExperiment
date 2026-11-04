namespace QuestSaturation.Core
{
    /// <summary>
    /// 値が不連続になる場面（Auto⇄Manual、波形の変更、Stop、既定値へのリセット、FreeControl に入るとき）で、
    /// いま表示している値から新しい目標値へ短時間でクロスフェードする。
    /// </summary>
    public sealed class ColorTransition
    {
        public const float DefaultDurationSec = 0.5f;

        private ColorAdjustment from;
        private float elapsed;
        private float duration = DefaultDurationSec;

        public bool IsActive { get; private set; }

        /// <summary>
        /// クロスフェードを始める。<paramref name="currentOutput"/> には、いま実際に出している値を渡す。
        /// フェード中に呼び直しても、出力は連続したままになる。
        /// </summary>
        public void Begin(ColorAdjustment currentOutput, float durationSec = DefaultDurationSec)
        {
            from = currentOutput;
            elapsed = 0f;
            duration = durationSec > 0f ? durationSec : 0f;
            IsActive = duration > 0f;
        }

        public void Cancel() => IsActive = false;

        /// <summary>時間を dt 進め、目標値とブレンドした出力を返す。</summary>
        public ColorAdjustment Step(ColorAdjustment target, float dt)
        {
            if (!IsActive) return target;

            if (dt > 0f) elapsed += dt;
            float t = elapsed / duration;
            if (t >= 1f)
            {
                IsActive = false;
                return target;
            }

            // smoothstep で始まりと終わりをなめらかにする
            float k = t * t * (3f - 2f * t);
            return ColorAdjustment.Lerp(from, target, k);
        }
    }
}
