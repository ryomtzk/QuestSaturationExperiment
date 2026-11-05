using System.Globalization;
using UnityEngine;

namespace QuestSaturation.Core
{
    /// <summary>
    /// 3条件実験のタイミングと目標値。
    /// 正規プロファイルはあえて static readonly にしている。シリアライズされないので、
    /// インスペクターから実験条件を誤って変えることができない。
    /// </summary>
    public sealed class ExperimentProfile
    {
        public static readonly ExperimentProfile Canonical =
            new ExperimentProfile("canonical-v1", holdSec: 3f, rampSec: 10f, targetA: 0f, targetB: -0.8f, targetC: 0.8f);

        public readonly string Id;
        public readonly float HoldSec;
        public readonly float RampSec;
        private readonly float targetA;
        private readonly float targetB;
        private readonly float targetC;

        public ExperimentProfile(string id, float holdSec, float rampSec, float targetA, float targetB, float targetC)
        {
            Id = id;
            HoldSec = holdSec;
            RampSec = rampSec;
            this.targetA = targetA;
            this.targetB = targetB;
            this.targetC = targetC;
        }

        public float TargetFor(Condition c)
        {
            switch (c)
            {
                case Condition.A: return targetA;
                case Condition.B: return targetB;
                case Condition.C: return targetC;
                default: return 0f;
            }
        }

        /// <summary>
        /// 選択から <paramref name="elapsedSec"/> 秒後の彩度。
        /// 元の SaturationCurve.Evaluate と同じ式で、結果もビット単位で一致する。
        /// </summary>
        public float Evaluate(Condition c, float elapsedSec)
        {
            float target = TargetFor(c);
            if (elapsedSec < HoldSec) return 0f;

            float rampElapsed = elapsedSec - HoldSec;
            if (rampElapsed >= RampSec) return target;

            return Mathf.Lerp(0f, target, rampElapsed / RampSec);
        }

        public TrialStage StageAt(float elapsedSec)
        {
            if (elapsedSec < HoldSec) return TrialStage.Hold;
            if (elapsedSec - HoldSec >= RampSec) return TrialStage.Target;
            return TrialStage.Ramp;
        }

        /// <summary>セッションログ用の短い説明。</summary>
        public string Describe() => string.Format(CultureInfo.InvariantCulture,
            "{0};hold={1};ramp={2};A={3};B={4};C={5}", Id, HoldSec, RampSec, targetA, targetB, targetC);
    }
}
