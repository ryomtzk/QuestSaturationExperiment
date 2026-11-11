namespace QuestSaturation.Core
{
    /// <summary>波形の再生状態（パネルの表示用）。</summary>
    public enum PlaybackStatus
    {
        Stopped = 0,  // 位相 0 で止まっている
        Playing = 1,
        Paused = 2,   // 途中で止まっている
        Finished = 3, // Ramp が終了値に達した
    }

    /// <summary>パススルーの初期化の状態（OVRManager の問い合わせ結果）。</summary>
    public enum PassthroughInit
    {
        Off = 0,
        Starting = 1,
        Ready = 2,
        Failed = 3,
        Unsupported = 4,
    }

    /// <summary>状態表示の色分け。パネルは無彩色で、色を使うのはこの分類だけ。</summary>
    public enum StatusTone
    {
        Neutral = 0,
        Good = 1,
        Warning = 2,
        Error = 3,
    }

    /// <summary>
    /// オペレーターパネルに出す値の、ある瞬間のスナップショット。OVR にもシーンにも触れない。
    /// OperatorPanel が毎回同じインスタンスに書き込み、表示側はこれだけを見て描く（エディタのプレビューも同じ）。
    /// </summary>
    public sealed class OperatorPanelState
    {
        public FlowState State = FlowState.FreeControl;

        // FreeControl
        public FreeMode Mode = FreeMode.Manual;
        public PlaybackStatus Playback = PlaybackStatus.Stopped;
        public double Phase;        // 周期波形は [0, 1)、Ramp は [0, 1]
        public float WaveValue;     // 波形がいま指している彩度（フェードの前）
        public int Cursor;          // ParameterDefs.All の番号
        public readonly float[] Values = new float[ParameterDefs.All.Length]; // MenuItem の順

        // レイヤーに実際に送った値
        public ColorAdjustment Output;
        public bool Transitioning;  // クロスフェード中

        // パススルー
        public PassthroughInit Passthrough = PassthroughInit.Off;
        public bool HasLayer;
        public bool LayerEnabled;
        public bool PassthroughVisible; // レイヤーが有効になってから実際に描画が再開された（SessionLogger の推定）

        // セッション
        public int ParticipantId;
        public int PendingParticipantId = -1; // 確定していない ID。なければ -1
        public int Trials;
        public string LogFile = "";
        public bool LogFailed;

        // 長押しの進み具合（0〜1）
        public float MenuHoldProgress;
        public float StopHoldProgress;

        // 電池（0〜1。分からなければ負）
        public float BatteryLevel = -1f;     // ヘッドセット
        public bool Charging;
        public string Clock = "";        // 例 21:34

        public float Get(MenuItem item) => Values[(int)item];

        public void Set(MenuItem item, float value) => Values[(int)item] = value;

        public Waveform Waveform => (Waveform)(int)Get(MenuItem.Waveform);

        public float PeriodSec => Get(MenuItem.Period);

        public int Steps => (int)Get(MenuItem.Steps);

        public bool HasPendingParticipant => PendingParticipantId >= 0 && PendingParticipantId != ParticipantId;

        /// <summary>FreeControl のモデルから、モードと各項目の値と再生状態を写す。</summary>
        public void CopyFrom(FreeControlModel model, int cursor)
        {
            Mode = model.Mode;
            Cursor = cursor;
            for (int i = 0; i < ParameterDefs.All.Length; i++)
                Values[i] = model.GetValue(ParameterDefs.All[i].Item);
            Playback = PanelPresenter.PlaybackOf(model.Player);
            Phase = model.Player.IsFinished ? 1.0 : model.Player.Phase;
            WaveValue = model.Player.Value;
            Transitioning = model.IsTransitioning;
        }
    }
}
