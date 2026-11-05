namespace QuestSaturation.Core
{
    public enum PressResult
    {
        None = 0,
        Tap = 1,  // しきい値より前に離した（離した時点で1回）
        Hold = 2, // しきい値に達した（押している間に1回だけ。そのあと離しても何も起きない）
    }

    /// <summary>
    /// ボタン1つのタップと長押しを見分ける。
    /// <see cref="Reset"/> のあとは、いったん離すまで押下を受け付けない
    /// （フォーカスが戻ったとき、押しっぱなしのボタンで長押しが成立しないように）。
    /// </summary>
    public sealed class TapOrHoldDetector
    {
        private bool down;
        private bool holdFired;
        private bool waitForRelease;
        private float downTime;

        public TapOrHoldDetector(float holdSec, bool tapEnabled = true)
        {
            HoldSec = holdSec;
            TapEnabled = tapEnabled;
        }

        public float HoldSec { get; set; }

        /// <summary>false なら、短く押して離しても Tap を返さない（長押し専用ボタン）。</summary>
        public bool TapEnabled { get; set; }

        public bool IsDown => down;

        public PressResult Update(bool pressed, float now)
        {
            if (waitForRelease)
            {
                if (!pressed) waitForRelease = false;
                return PressResult.None;
            }

            if (pressed)
            {
                if (!down)
                {
                    down = true;
                    holdFired = false;
                    downTime = now;
                }

                if (!holdFired && now - downTime >= HoldSec)
                {
                    holdFired = true;
                    return PressResult.Hold;
                }
                return PressResult.None;
            }

            if (!down) return PressResult.None;

            down = false;
            return !holdFired && TapEnabled ? PressResult.Tap : PressResult.None;
        }

        /// <summary>長押しの進み具合（0〜1）。押していなければ 0。</summary>
        public float HoldProgress(float now)
        {
            if (!down || holdFired || HoldSec <= 0f) return 0f;
            float p = (now - downTime) / HoldSec;
            return p < 0f ? 0f : p > 1f ? 1f : p;
        }

        /// <summary>押下状態を捨てる。次は、いったん離してからの押下だけを受け付ける。</summary>
        public void Reset()
        {
            down = false;
            holdFired = false;
            waitForRelease = true;
        }
    }
}
