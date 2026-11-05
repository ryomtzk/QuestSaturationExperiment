namespace QuestSaturation.Core
{
    public enum StickAxis
    {
        None = 0,
        Vertical = 1,
        Horizontal = 2,
    }

    /// <summary>
    /// スティックを倒し始めた軸を、中央に戻すまで固定する。
    /// 毎フレーム大きいほうの軸を選ぶと、斜めを通るたびに反対の軸へ 0 が入り、リピートが途切れたり、
    /// 戻したときにもう1回入力が出たりする。そのため、最初にしきい値を越えた軸だけを使い続ける。
    /// </summary>
    public sealed class StickAxisLock
    {
        public float PressThreshold = 0.6f;
        public float ReleaseThreshold = 0.35f;

        public StickAxis Locked { get; private set; }

        /// <summary>このフレームで使う軸。どちらもしきい値に届いていなければ None。</summary>
        public StickAxis Update(float x, float y)
        {
            float ax = float.IsNaN(x) ? 0f : System.Math.Abs(x);
            float ay = float.IsNaN(y) ? 0f : System.Math.Abs(y);

            if (Locked != StickAxis.None)
            {
                if (ax < ReleaseThreshold && ay < ReleaseThreshold) Locked = StickAxis.None;
                else return Locked;
            }

            if (ax >= PressThreshold || ay >= PressThreshold)
                Locked = ay >= ax ? StickAxis.Vertical : StickAxis.Horizontal;
            return Locked;
        }

        public void Reset() => Locked = StickAxis.None;
    }

    /// <summary>
    /// スティック1軸のアナログ値を、-1 / 0 / +1 の離散入力に変える。
    /// 倒した瞬間に1回出し、<see cref="Update"/> で repeat を許したときだけ、押し続けるとリピートする。
    /// スティックを押し込んでいる間（suppressed）は軸を無視し、そのあと中央に戻すまで入力を出さない。
    /// </summary>
    public sealed class StickRepeater
    {
        public float PressThreshold = 0.6f;
        public float ReleaseThreshold = 0.35f; // ヒステリシス。しきい値付近で震えて連打にならないように
        public float InitialDelaySec = 0.4f;
        public float RepeatIntervalSec = 0.1f;

        private int heldDir;
        private float nextRepeatTime;
        private bool waitForCenter;

        public int Update(float axis, bool suppressed, bool repeat, float now)
        {
            if (float.IsNaN(axis)) axis = 0f;

            int dir;
            if (axis >= PressThreshold) dir = 1;
            else if (axis <= -PressThreshold) dir = -1;
            else if (axis > -ReleaseThreshold && axis < ReleaseThreshold) dir = 0;
            else dir = heldDir; // ヒステリシス帯では前の状態を保つ

            if (suppressed)
            {
                waitForCenter = true;
                heldDir = 0;
                return 0;
            }

            if (dir == 0)
            {
                heldDir = 0;
                waitForCenter = false;
                return 0;
            }

            if (waitForCenter) return 0;

            if (dir != heldDir)
            {
                heldDir = dir;
                nextRepeatTime = now + InitialDelaySec;
                return dir;
            }

            if (repeat && now >= nextRepeatTime)
            {
                nextRepeatTime = now + RepeatIntervalSec;
                return dir;
            }

            return 0;
        }

        public void Reset()
        {
            heldDir = 0;
            waitForCenter = true;
        }
    }
}
