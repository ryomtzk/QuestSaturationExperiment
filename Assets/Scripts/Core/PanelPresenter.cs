using System;
using System.Globalization;

namespace QuestSaturation.Core
{
    /// <summary>値のバーの位置。どれも [0, 1] で、0 がトラックの左端（最小値）。</summary>
    public readonly struct BarGeometry
    {
        public readonly float Origin;  // 塗りの起点（両側に振れる項目は 0 の位置、それ以外は左端）
        public readonly float Value;   // いまの値
        public readonly float Default; // 既定値の目盛り

        public BarGeometry(float origin, float value, float @default)
        {
            Origin = origin;
            Value = value;
            Default = @default;
        }

        public float FillMin => Origin < Value ? Origin : Value;
        public float FillMax => Origin < Value ? Value : Origin;
    }

    /// <summary>フッターのボタン凡例の1項目。MenuIcon のときはキーの前にメニューボタンの絵を描く。</summary>
    public readonly struct LegendEntry
    {
        public readonly string Key;
        public readonly bool MenuIcon;
        public readonly string Action;

        public LegendEntry(string key, string action, bool menuIcon = false)
        {
            Key = key;
            Action = action;
            MenuIcon = menuIcon;
        }
    }

    /// <summary>
    /// オペレーターパネルの表示内容を決める純粋な関数。文字列とバーの位置はここで作り、表示側は並べるだけにする。
    /// 文字は TMP の LiberationSans SDF の固定アトラスにある字だけを使う（<see cref="IsStaticGlyph"/>）。
    /// アトラスに無い字は実行時に別のフォントから作られ、描画が増えるうえ、色の指定も効かないため。
    /// </summary>
    public static class PanelPresenter
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ---------------------------------------------------------------- 文字

        /// <summary>
        /// LiberationSans SDF の固定アトラスにある字か（ASCII、Latin-1、• · ± × … – — ‹ › « » と数字幅の空白）。
        /// アトラスの characterSequence は "32 - 126, 160 - 255, 8192 - 8303, 8364, 8482, 9633" だが、
        /// 8192〜8303 のうち実際に使ってよいものだけを挙げる。
        /// </summary>
        public static bool IsStaticGlyph(char c)
        {
            if (c >= 0x20 && c <= 0x7E) return true;
            if (c >= 0xA0 && c <= 0xFF) return true;
            switch (c)
            {
                case ' ': // 数字幅の空白
                case '–': // –
                case '—': // —
                case '•': // •
                case '…': // …
                case '‹': // ‹
                case '›': // ›
                    return true;
                default:
                    return false;
            }
        }

        public static bool UsesOnlyStaticGlyphs(string s)
        {
            if (s == null) return true;
            foreach (char c in s)
                if (!IsStaticGlyph(c)) return false;
            return true;
        }

        // ---------------------------------------------------------------- 再生

        /// <summary>波形の再生状態。以前のテキスト表示と同じ判定（再生中 → 終了 → 途中 → 停止）。</summary>
        public static PlaybackStatus PlaybackOf(WaveformPlayer player)
        {
            if (player == null) return PlaybackStatus.Stopped;
            if (player.IsPlaying) return PlaybackStatus.Playing;
            if (player.IsFinished) return PlaybackStatus.Finished;
            return player.Phase > 0.0 ? PlaybackStatus.Paused : PlaybackStatus.Stopped;
        }

        public static string PlaybackLabel(PlaybackStatus s)
        {
            switch (s)
            {
                case PlaybackStatus.Playing: return "PLAYING";
                case PlaybackStatus.Paused: return "PAUSED";
                case PlaybackStatus.Finished: return "FINISHED";
                default: return "STOPPED";
            }
        }

        /// <summary>波形カードの再生チップ。Manual では波形は動かないので「MANUAL」。</summary>
        public static string TransportLabel(FreeMode mode, PlaybackStatus s) =>
            mode == FreeMode.Manual ? "MANUAL" : PlaybackLabel(s);

        /// <summary>再生の位置（0〜1）。NaN と範囲外は端にする。</summary>
        public static float ClampPhase(double phase)
        {
            if (double.IsNaN(phase) || phase < 0.0) return 0f;
            return phase > 1.0 ? 1f : (float)phase;
        }

        /// <summary>
        /// 経過時間を 0.1 秒単位の整数にする（切り捨て：先の時刻を出さない）。波形カードはこれを
        /// TMP の SetText("{0:1} / {1:1} s", tenths / 10f, periodSec) で書く（毎回の文字列の割り当てを避けるため）。
        /// </summary>
        public static int PositionTenths(double phase, float periodSec)
        {
            if (!(periodSec > 0f)) return 0;
            return (int)Math.Floor(ClampPhase(phase) * periodSec * 10f + 1e-4f);
        }

        /// <summary>
        /// 再生位置。周期波形は「経過 / 周期」、Ramp は「経過 / 長さ」（例 "4.2 / 10.0 s"）。
        /// 波形カードが TMP の書式で書くものと同じ文（テストと、字の確認用）。
        /// </summary>
        public static string PositionLabel(double phase, float periodSec) =>
            (PositionTenths(phase, periodSec) / 10.0).ToString("0.0", Inv) + " / " + periodSec.ToString("0.0", Inv) + " s";

        /// <summary>Manual のとき、再生位置の代わりに出す説明。</summary>
        public const string ManualTransportHint = "Press B to play in Auto";

        // ---------------------------------------------------------------- 項目

        /// <summary>v を定義の範囲で [0, 1] にした位置。</summary>
        public static float Fraction(ParameterDef def, float v)
        {
            float span = def.Max - def.Min;
            if (!(span > 0f)) return 0f;
            float f = (def.Sanitize(v) - def.Min) / span;
            return f < 0f ? 0f : f > 1f ? 1f : f;
        }

        /// <summary>バーの塗りの起点・値・既定値の位置。範囲が 0 をまたぐ項目は 0 から塗る。</summary>
        public static BarGeometry Bar(ParameterDef def, float value)
        {
            float origin = def.Min < 0f && def.Max > 0f ? Fraction(def, 0f) : 0f;
            return new BarGeometry(origin, Fraction(def, value), Fraction(def, def.Default));
        }

        /// <summary>これ以上下げられない（左の矢印を薄くする）。Enum は端で折り返すので常に false。</summary>
        public static bool AtMin(ParameterDef def, float v) => def.Kind != ParameterKind.Enum && def.Sanitize(v) <= def.Min;

        /// <summary>これ以上上げられない（右の矢印を薄くする）。</summary>
        public static bool AtMax(ParameterDef def, float v) => def.Kind != ParameterKind.Enum && def.Sanitize(v) >= def.Max;

        /// <summary>
        /// 項目がいまの設定で効いているか。Steps は波形が Step のときだけ効く。
        /// 効いていない項目も選べるし変えられる（表示を薄くするだけ）。
        /// </summary>
        public static bool IsRelevant(MenuItem item, Waveform waveform) =>
            item != MenuItem.Steps || waveform == Waveform.Step;

        /// <summary>Auto の間、Manual の彩度は使われない（Manual に戻すとこの値に戻る）。</summary>
        public static bool IsOverriddenByAuto(MenuItem item, FreeMode mode) =>
            mode == FreeMode.Auto && item == MenuItem.Saturation;

        /// <summary>行の見出し。ログの項目名（ParameterDef.Label）から単位を外したもの。</summary>
        public static string DisplayLabel(ParameterDef def) => def.Item == MenuItem.Period ? "Period" : def.Label;

        /// <summary>行に出す値。ログと同じ書式に、単位と ID の書式を足す。</summary>
        public static string FormatValue(ParameterDef def, float v)
        {
            switch (def.Item)
            {
                case MenuItem.Period: return def.Format(v) + " s";
                case MenuItem.ParticipantId: return ParticipantLabel((int)def.Sanitize(v));
                default: return def.Format(v);
            }
        }

        public static string ParticipantLabel(int id) => "P" + (id < 0 ? 0 : id).ToString("000", Inv);

        /// <summary>
        /// 選んでいる行の操作説明（フッターに出す）。被験者 ID は README と同じ条件を書く
        /// （別の項目の変更、再生・一時停止・停止、マーカー、FreeControl を出たときに確定する）。
        /// Mode の行は、B が Manual では Auto に切り替えて再生、Auto では再生/一時停止になるので、モードで書き分ける。
        /// </summary>
        public static string Hint(ParameterDef def, FreeMode mode)
        {
            const string Sep = "   •   ";
            string reset = "Click  reset to " + FormatValue(def, def.Default);
            switch (def.Item)
            {
                case MenuItem.Mode:
                    return "Left / Right  Manual or Auto" + Sep + (mode == FreeMode.Auto ? "B  play / pause" : "B  play (switches to Auto)");
                case MenuItem.Waveform:
                    return "Left / Right  choose the shape" + Sep + reset;
                case MenuItem.ParticipantId:
                    return "Left / Right  ±1" + Sep + "Applies at your next other change, B, marker or exit";
                case MenuItem.Steps:
                    return "Left / Right  ±1 (Step waveform only)" + Sep + reset;
            }

            string step = def.Item == MenuItem.Period
                ? def.Step.ToString("0.0", Inv) + " s"
                : def.Step.ToString("0.00", Inv);
            return "Left / Right  ±" + step + ", hold to repeat" + Sep + reset;
        }

        // ---------------------------------------------------------------- 長押し

        /// <summary>右B の長押しを表示し始める進み具合。ふつうのタップ（再生/一時停止）で一瞬出ないように 0.3 秒ぶん待つ。</summary>
        public const float StopHoldShowAt = 0.3f;

        /// <summary>左メニューの長押しを表示し始める進み具合（1.5 秒の長押しで約 0.1 秒）。</summary>
        public const float MenuHoldShowAt = 0.07f;

        public const string StopHoldTitle = "Hold to stop and rewind";
        public const string MenuHoldTitle = "Hold to go back to Selection";

        /// <summary>
        /// 右B を途中で離したときに起きること。B はタップで再生/一時停止なので、
        /// 長押しをやめても「取り消し」にはならない。Manual では Auto に切り替わって再生が始まる。
        /// </summary>
        public static string StopHoldRelease(FreeMode mode, PlaybackStatus playback)
        {
            if (mode == FreeMode.Manual) return "Release now: play (switches to Auto)";
            switch (playback)
            {
                case PlaybackStatus.Playing: return "Release now: pause";
                case PlaybackStatus.Finished: return "Release now: play again";
                default: return "Release now: play";
            }
        }

        /// <summary>左メニューを途中で離したときの説明。メニューはタップに何も割り当てていないので取り消しになる。</summary>
        public static string MenuHoldRelease(bool hasPendingParticipant, int pendingId) =>
            hasPendingParticipant
                ? "Release to cancel   •   " + ParticipantLabel(pendingId) + " starts on exit"
                : "Release to cancel";

        // ---------------------------------------------------------------- 状態表示

        public static string BatteryLabel(string prefix, float level)
        {
            if (!(level >= 0f)) return "";
            int pct = (int)Math.Round(Math.Min(1f, level) * 100f, MidpointRounding.AwayFromZero);
            return prefix + " " + pct.ToString(Inv) + "%";
        }

        public static StatusTone BatteryTone(float level, bool charging)
        {
            if (!(level >= 0f) || charging) return StatusTone.Neutral;
            if (level <= 0.10f) return StatusTone.Error;
            if (level <= 0.20f) return StatusTone.Warning;
            return StatusTone.Neutral;
        }

        /// <summary>
        /// パススルーの状態。FreeControl では、レイヤーが有効で、実際に描画が再開されている（visible）のが正常。
        /// 初期化が済んでいても、まだ映っていなければ「待ち」として注意の色にする。
        /// </summary>
        public static string PassthroughLabel(PassthroughInit init, bool hasLayer, bool layerEnabled, bool visible)
        {
            if (!hasLayer) return "No passthrough layer";
            switch (init)
            {
                case PassthroughInit.Unsupported: return "No passthrough";
                case PassthroughInit.Failed: return "Passthrough failed";
                case PassthroughInit.Starting: return "Passthrough starting…";
                case PassthroughInit.Ready:
                    if (!layerEnabled) return "Passthrough off";
                    return visible ? "Passthrough" : "Passthrough waiting…";
                default: return "Passthrough off";
            }
        }

        public static StatusTone PassthroughTone(PassthroughInit init, bool hasLayer, bool layerEnabled, bool visible)
        {
            if (!hasLayer) return StatusTone.Error;
            switch (init)
            {
                case PassthroughInit.Unsupported:
                case PassthroughInit.Failed: return StatusTone.Error;
                case PassthroughInit.Ready: return layerEnabled && visible ? StatusTone.Good : StatusTone.Warning;
                default: return StatusTone.Warning;
            }
        }

        /// <summary>ログの状態（ヘッダーとセッションカードのチップ）。</summary>
        public static string LogLabel(string fileName, bool failed)
        {
            if (failed) return "LOG STOPPED";
            return string.IsNullOrEmpty(fileName) ? "NOT LOGGING" : "REC";
        }

        public static StatusTone LogTone(string fileName, bool failed) =>
            failed ? StatusTone.Error : string.IsNullOrEmpty(fileName) ? StatusTone.Warning : StatusTone.Good;

        /// <summary>
        /// ファイル名（session_20261107T123045Z_P007.csv）から記録の開始時刻を読み、"Since 12:30 UTC" にする（REC の横に出すので「記録」は書かない）。
        /// 読めなければ "Recording"。ファイル名そのものは長いので出さない。
        /// </summary>
        public static string LogStartLabel(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "";
            const string prefix = "session_";
            if (fileName.StartsWith(prefix, StringComparison.Ordinal) && fileName.Length >= prefix.Length + 16)
            {
                string stamp = fileName.Substring(prefix.Length, 16); // yyyyMMddTHHmmssZ
                if (DateTime.TryParseExact(stamp, "yyyyMMdd'T'HHmmss'Z'", Inv,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t))
                    return "Since " + t.ToString("HH:mm", Inv) + " UTC";
            }
            return "Recording";
        }

        /// <summary>LOG STOPPED のときの説明。SessionLogger は書けなくなったら再起動まで書き直さない。</summary>
        public const string LogStoppedTitle = "Not recording";
        public const string LogStoppedDetail = "Restart the app to log again";

        public static string TrialsLabel(int trials)
        {
            if (trials <= 0) return "No trials yet";
            return trials == 1 ? "1 trial" : trials.ToString(Inv) + " trials";
        }

        /// <summary>確定していない被験者 ID の表示（セッションカード）。</summary>
        public static string PendingLabel(int pendingId) => "Next: " + ParticipantLabel(pendingId) + "   •   pending";

        public const string PendingTag = "PENDING";
        public const string AutoTag = "AUTO";
        public const string FadingTag = "Fading";

        // ---------------------------------------------------------------- 通知

        /// <summary>マーカーの通知。CSV の t_realtime_s で行を探せるよう、記録した時刻を出す。続けて押したら回数をまとめる。</summary>
        public static string MarkerToast(int count, double realtimeSec)
        {
            string t = "t = " + realtimeSec.ToString("0.0", Inv) + " s";
            return (count > 1 ? "Marker ×" + count.ToString(Inv) : "Marker logged") + "   •   " + t;
        }

        /// <summary>スティック押し込みで既定値に戻したときの通知。誤って押したときに元の値が分かるようにする。</summary>
        public static string ResetToast(ParameterDef def, float before, float after) =>
            DisplayLabel(def) + " reset to " + FormatValue(def, after) + "   (was " + FormatValue(def, before) + ")";

        public static string ParticipantCommittedToast(int id) => "Now logging " + ParticipantLabel(id) + "   •   new session file";

        /// <summary>マーカーで新しい被験者に切り替わったとき（マーカーは新しいファイルの最初の行になる）。</summary>
        public static string ParticipantCommittedToast(int id, double markerRealtimeSec) =>
            "Now logging " + ParticipantLabel(id) + "   •   marker t = " + markerRealtimeSec.ToString("0.0", Inv) + " s";

        /// <summary>値のリセットで新しい被験者に切り替わったとき。リセットの元の値も消さずに出す。</summary>
        public static string ParticipantCommittedToast(int id, ParameterDef reset, float before, float after) =>
            "Now logging " + ParticipantLabel(id) + "   •   " + ResetToast(reset, before, after);

        /// <summary>ログが止まった後に被験者が切り替わったとき。新しいファイルは作られない。</summary>
        public static string ParticipantNotRecordingToast(int id) => "Not recording " + ParticipantLabel(id) + "   •   log stopped";

        /// <summary>ログが止まった後に、値のリセットで被験者が切り替わったとき。</summary>
        public static string ParticipantNotRecordingToast(int id, ParameterDef reset, float before, float after) =>
            "Not recording " + ParticipantLabel(id) + "   •   " + ResetToast(reset, before, after);

        /// <summary>マーカーの行が書かれなかったとき（ログが止まった後か、ログのファイルが無いとき）。</summary>
        public static string MarkerNotRecordedToast(bool logFailed) =>
            "Marker not recorded   •   " + (logFailed ? "log stopped" : "not logging");

        public static string RampFinishedToast(float to) => "Ramp finished at " + ParameterDefs.Get(MenuItem.To).Format(to);

        public const string LogFailedToast = "Log stopped   •   this session is not being recorded";

        // ---------------------------------------------------------------- 凡例

        /// <summary>フッターのボタン凡例（ShortcutMap で FreeControl に許されている操作）。</summary>
        public static readonly LegendEntry[] Legend =
        {
            new LegendEntry("B", "Play / pause (hold: stop)"),
            new LegendEntry("Y", "Hide"),
            new LegendEntry("X", "Recall"),
            new LegendEntry("L grip", "Marker"),
            new LegendEntry("hold", "Selection", menuIcon: true),
        };

        // ---------------------------------------------------------------- 波形

        /// <summary>
        /// プレビュー用に、1周期（Ramp は片道）の波形を折れ線の頂点にする。x は [0, 1]、y は彩度。
        /// Step は段の角を正確に出す（各段の始点と終点）。書き込んだ頂点の数を返す。
        /// 配列には <see cref="MaxSamples"/> 以上の長さが必要。
        /// </summary>
        public static int SampleWaveform(Waveform w, float from, float to, int steps, float[] xs, float[] ys)
        {
            if (xs == null || ys == null) throw new ArgumentNullException(xs == null ? nameof(xs) : nameof(ys));
            int capacity = Math.Min(xs.Length, ys.Length);
            if (capacity < MaxSamples) throw new ArgumentException("buffers must hold at least " + MaxSamples + " points");

            int n = 0;
            switch (w)
            {
                case Waveform.Ramp:
                    Put(xs, ys, ref n, 0f, from);
                    Put(xs, ys, ref n, 1f, to);
                    break;

                case Waveform.LinearPingPong:
                    Put(xs, ys, ref n, 0f, from);
                    Put(xs, ys, ref n, 0.5f, to);
                    Put(xs, ys, ref n, 1f, from);
                    break;

                case Waveform.Step:
                {
                    int levels = WaveformMath.ClampSteps(steps);
                    int segments = 2 * (levels - 1);
                    for (int k = 0; k < segments; k++)
                    {
                        // 区間の中央で値を取れば、区間全体の段の高さになる
                        double mid = (k + 0.5) / segments;
                        float y = WaveformMath.Evaluate(w, mid, levels, from, to);
                        Put(xs, ys, ref n, (float)k / segments, y);
                        Put(xs, ys, ref n, (float)(k + 1) / segments, y);
                    }
                    break;
                }

                default:
                {
                    int count = SineSamples;
                    for (int i = 0; i <= count; i++)
                    {
                        double p = (double)i / count;
                        float y = i == count ? from : WaveformMath.Evaluate(w, p, steps, from, to);
                        Put(xs, ys, ref n, (float)p, y);
                    }
                    break;
                }
            }
            return n;
        }

        /// <summary>サイン波の分割数。</summary>
        public const int SineSamples = 96;

        /// <summary><see cref="SampleWaveform"/> が書く頂点数の上限（Step 20 段で 76、サイン波で 97）。</summary>
        public const int MaxSamples = 2 * 2 * (WaveformMath.MaxSteps - 1) > SineSamples + 1 ? 2 * 2 * (WaveformMath.MaxSteps - 1) : SineSamples + 1;

        private static void Put(float[] xs, float[] ys, ref int n, float x, float y)
        {
            xs[n] = x;
            ys[n] = y;
            n++;
        }
    }
}
