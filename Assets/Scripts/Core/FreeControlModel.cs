namespace QuestSaturation.Core
{
    public enum FreeMode
    {
        Manual = 0,
        Auto = 1,
    }

    /// <summary>
    /// FreeControl（オペレーターが自由に色を調整するモード）の中身。
    /// Auto は彩度にだけ効き、明るさとコントラストは Manual の値をそのまま使う。
    /// Auto から Manual に戻すと、彩度は Manual の値に戻る。
    /// 値が飛ぶ操作では、いま出している値からクロスフェードする。
    /// </summary>
    public sealed class FreeControlModel
    {
        private readonly WaveformPlayer player = new WaveformPlayer();
        private readonly ColorTransition transition = new ColorTransition();

        public FreeControlModel(FreeControlSettings settings)
        {
            Settings = settings ?? FreeControlSettings.CreateDefault();
            SyncPlayer();
        }

        public FreeControlSettings Settings { get; }

        public FreeMode Mode { get; private set; } = FreeMode.Manual;

        public WaveformPlayer Player => player;

        public bool IsTransitioning => transition.IsActive;

        /// <summary>直近の <see cref="Tick"/> で出した値（クランプ前）。</summary>
        public ColorAdjustment Output { get; private set; }

        /// <summary>フェードを除いた、いま目指している値。</summary>
        public ColorAdjustment Target
        {
            get
            {
                var manual = Settings.ManualColor;
                return Mode == FreeMode.Manual ? manual : manual.WithSaturation(player.Value);
            }
        }

        /// <summary>FreeControl に入る。必ず Manual・停止の状態から始め、<paramref name="currentOutput"/> からフェードする。</summary>
        public void Enter(ColorAdjustment currentOutput)
        {
            Mode = FreeMode.Manual;
            player.Stop();
            SyncPlayer();
            Output = currentOutput;
            transition.Begin(currentOutput);
        }

        /// <summary>FreeControl を出る。再生は止める。</summary>
        public void Exit()
        {
            player.Stop();
            transition.Cancel();
        }

        /// <summary>時間を進めて出力を返す。Ramp がこのフレームで終わったら rampFinished が true になる。</summary>
        public ColorAdjustment Tick(float dt, out bool rampFinished)
        {
            SyncPlayer();
            rampFinished = player.Tick(dt);
            Output = transition.Step(Target, dt);
            return Output;
        }

        public void SetMode(FreeMode mode)
        {
            if (mode == Mode) return;
            Mode = mode;
            player.Stop();
            BeginFade();
        }

        /// <summary>
        /// 右B タップ。Manual なら Auto にして再生、Auto なら再生と一時停止を切り替える。
        /// 戻り値は再生中かどうか。
        /// </summary>
        public bool TogglePlayPause()
        {
            if (Mode == FreeMode.Manual)
            {
                SetMode(FreeMode.Auto);
                player.Play();
                return true;
            }

            if (player.IsPlaying)
            {
                player.Pause();
                return false;
            }

            if (player.IsFinished) BeginFade(); // 終わった Ramp は頭に戻るので値が飛ぶ
            player.Play();
            return true;
        }

        /// <summary>右B 長押し。停止して位相を頭に戻す。</summary>
        public void Stop()
        {
            bool jumps = Mode == FreeMode.Auto && player.Phase != 0.0;
            player.Stop();
            if (jumps) BeginFade();
        }

        public float GetValue(MenuItem item) => item == MenuItem.Mode ? (float)(int)Mode : Settings.Get(item);

        /// <summary>メニューから値を1刻み動かす。値が変わったら true。</summary>
        public bool Adjust(MenuItem item, int direction)
        {
            var def = ParameterDefs.Get(item);
            return SetValue(item, def.Adjust(GetValue(item), direction), isReset: false);
        }

        /// <summary>スティック押し込み。その項目を既定値に戻す（戻せない項目は何もしない）。値が変わったら true。</summary>
        public bool ResetToDefault(MenuItem item)
        {
            var def = ParameterDefs.Get(item);
            if (!def.Resettable) return false;
            return SetValue(item, def.Default, isReset: true);
        }

        private bool SetValue(MenuItem item, float value, bool isReset)
        {
            var def = ParameterDefs.Get(item);
            float before = GetValue(item);
            float v = def.Sanitize(value);
            if (v.Equals(before)) return false;

            if (item == MenuItem.Mode)
            {
                SetMode((FreeMode)(int)v);
                return true;
            }

            var targetBefore = Target;
            Settings.Set(item, v);
            SyncPlayer();

            // 値が大きく飛ぶ変更だけフェードする。刻み1つ分の手動調整はすぐ反映する。
            bool fade = isReset || item == MenuItem.Waveform || item == MenuItem.Steps;
            if (fade && !Target.Equals(targetBefore)) BeginFade();
            return true;
        }

        private void BeginFade() => transition.Begin(Output);

        private void SyncPlayer()
        {
            player.Waveform = Settings.Waveform;
            player.From = Settings.From;
            player.To = Settings.To;
            player.PeriodSec = Settings.PeriodSec;
            player.Steps = Settings.Steps;
        }
    }

    /// <summary>
    /// FreeControl がログに出すイベントの名前と、param_change の detail の書式。
    /// FreeControlController と <see cref="ParticipantSession.IsCommitTrigger"/> が同じ書式を使うように、ここにまとめる。
    /// </summary>
    public static class FreeControlEvents
    {
        public const string ParamChange = "param_change";
        public const string RampFinished = "ramp_finished";

        public static string ParamChangeDetail(MenuItem item, string value, string how) =>
            ItemPrefix(item) + "value=" + value + ";how=" + how;

        public static string ItemPrefix(MenuItem item) => "item=" + item + ";";
    }
}
