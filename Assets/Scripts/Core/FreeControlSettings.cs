namespace QuestSaturation.Core
{
    /// <summary>
    /// FreeControl のパラメータと被験者 ID。設定として保存するのはこのクラスの中身だけで、
    /// モード（Manual/Auto）と再生状態は保存しない。
    /// </summary>
    public sealed class FreeControlSettings
    {
        // Manual の値
        public float Brightness;
        public float Contrast;
        public float Saturation;

        // Auto の値
        public Waveform Waveform = Waveform.Sine;
        public float From;
        public float To = -1f;
        public float PeriodSec = 10f;
        public int Steps = 5;

        public int ParticipantId;

        public static FreeControlSettings CreateDefault()
        {
            var s = new FreeControlSettings();
            foreach (var def in ParameterDefs.All)
            {
                if (def.Item == MenuItem.Mode) continue;
                s.Set(def.Item, def.Default);
            }
            return s;
        }

        public FreeControlSettings Clone() => (FreeControlSettings)MemberwiseClone();

        public float Get(MenuItem item)
        {
            switch (item)
            {
                case MenuItem.Saturation: return Saturation;
                case MenuItem.Brightness: return Brightness;
                case MenuItem.Contrast: return Contrast;
                case MenuItem.Waveform: return (int)Waveform;
                case MenuItem.From: return From;
                case MenuItem.To: return To;
                case MenuItem.Period: return PeriodSec;
                case MenuItem.Steps: return Steps;
                case MenuItem.ParticipantId: return ParticipantId;
                default: return 0f;
            }
        }

        /// <summary>範囲外の値は定義の範囲に収めてから入れる。</summary>
        public void Set(MenuItem item, float value)
        {
            var def = ParameterDefs.Get(item);
            float v = def.Sanitize(value);
            switch (item)
            {
                case MenuItem.Saturation: Saturation = v; break;
                case MenuItem.Brightness: Brightness = v; break;
                case MenuItem.Contrast: Contrast = v; break;
                case MenuItem.Waveform: Waveform = (Waveform)(int)v; break;
                case MenuItem.From: From = v; break;
                case MenuItem.To: To = v; break;
                case MenuItem.Period: PeriodSec = v; break;
                case MenuItem.Steps: Steps = (int)v; break;
                case MenuItem.ParticipantId: ParticipantId = (int)v; break;
            }
        }

        public ColorAdjustment ManualColor => new ColorAdjustment(Brightness, Contrast, Saturation);
    }
}
