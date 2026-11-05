namespace QuestSaturation.Core
{
    /// <summary>
    /// サンプル行を書くかどうかを決める。毎フレーム書くとき（Trial 中）は必ず書く。
    /// それ以外は interval 秒の格子に乗せる。前回書いた時刻から数えると、フレームの端数のぶん間隔が延びて、
    /// 平均が指定より少なくなる（72Hz で 10Hz を指定すると約 9Hz になる）ため。
    /// </summary>
    public sealed class SampleClock
    {
        private double next = double.NegativeInfinity;

        public bool Due(double now, double interval, bool everyFrame)
        {
            if (!(interval > 0.0)) interval = 0.1; // NaN と 0 以下
            if (everyFrame)
            {
                next = now + interval;
                return true;
            }
            if (now < next) return false;
            next += interval;
            if (next <= now) next = now + interval; // 長く止まっていた後に、まとめて追いつこうとしない
            return true;
        }

        public void Reset() => next = double.NegativeInfinity;
    }
}
