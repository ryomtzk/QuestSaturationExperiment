using System.Collections.Generic;

namespace QuestSaturation.Core
{
    /// <summary>通知の種類。</summary>
    public enum NoticeKind
    {
        None = 0,
        LogFailed = 1,
        Participant = 2,
        Marker = 3,
        Reset = 4,
        RampFinished = 5,
    }

    /// <summary>1フレームの間にパネルが受けた出来事。OperatorPanel が集めて <see cref="PanelNotices.Choose"/> に渡す。</summary>
    public struct NoticeEvents
    {
        /// <summary>このフレームでログが止まった。</summary>
        public bool LogJustFailed;
        /// <summary>ログが止まっている（再起動まで書き直さない）。</summary>
        public bool LogFailed;
        /// <summary>確定済みの被験者が替わった。</summary>
        public bool Committed;
        /// <summary>確定済みの被験者。</summary>
        public int ParticipantId;
        /// <summary>このフレームにマーカーを押した回数。</summary>
        public int MarkerPresses;
        /// <summary>SessionLogger が CSV に書いたマーカー行の t_realtime_s。書かれていなければ NaN。</summary>
        public double MarkerAt;
        /// <summary>いま出ているマーカーの通知にまとめた回数（出ていなければ 0）。</summary>
        public int VisibleMarkerRun;
        /// <summary>スティックの押し込みで既定値に戻した項目。なければ null。</summary>
        public ParameterDef ResetDef;
        public float ResetBefore;
        public float ResetAfter;
        /// <summary>Auto の Ramp が終わった。</summary>
        public bool RampFinished;
        public float RampTo;
        /// <summary>いま何かの通知が出ている。</summary>
        public bool ToastVisible;
    }

    /// <summary>出す通知（Kind が None なら出さない）。</summary>
    public readonly struct Notice
    {
        public readonly NoticeKind Kind;
        public readonly string Message;
        public readonly StatusTone Tone;
        public readonly float Seconds;
        /// <summary>マーカーの通知にまとめた回数（Kind が Marker のときだけ）。</summary>
        public readonly int MarkerRun;

        public Notice(NoticeKind kind, string message, StatusTone tone, float seconds, int markerRun = 0)
        {
            Kind = kind;
            Message = message;
            Tone = tone;
            Seconds = seconds;
            MarkerRun = markerRun;
        }

        public bool IsNone => Kind == NoticeKind.None;
    }

    /// <summary>
    /// パネルの通知の選び方（純粋な関数）。1フレームに出すのは1つだけで、新しい通知は出ている通知を置き換える。
    /// 優先の順：ログが止まった ＞ 被験者の確定 ＞ マーカー ＞ 値のリセット ＞ Ramp の終わり。
    /// - 被験者の確定は、それを起こした操作（マーカー、リセット）と1つの文にまとめる（どちらの情報も消さない）。
    /// - ログが止まった後は、記録したとは書かない（マーカーの行も新しいファイルも書かれない）。
    /// - Ramp の終わりは波形カードの FINISHED に常に出ているので、ほかの通知が出ている間は出さない。
    /// - 同じフレームのマーカーとリセット（ほぼ起きない）は、マーカーを出す。リセットの元の値は CSV の前の行にある。
    /// </summary>
    public static class PanelNotices
    {
        public const float LogFailedSec = 8f;
        public const float ParticipantSec = 3.5f;
        public const float ParticipantDetailSec = 4f; // 2つの情報をまとめた文は少し長く出す
        public const float MarkerSec = 2f;
        public const float MarkerNotRecordedSec = 3f;
        public const float ResetSec = 2.5f;
        public const float RampFinishedSec = 2f;

        public static Notice Choose(in NoticeEvents e)
        {
            if (e.LogJustFailed)
                return new Notice(NoticeKind.LogFailed, PanelPresenter.LogFailedToast, StatusTone.Error, LogFailedSec);

            bool markerRecorded = e.MarkerPresses > 0 && !double.IsNaN(e.MarkerAt);

            if (e.Committed)
            {
                if (e.LogFailed)
                {
                    return e.ResetDef != null && e.MarkerPresses == 0
                        ? new Notice(NoticeKind.Participant,
                            PanelPresenter.ParticipantNotRecordingToast(e.ParticipantId, e.ResetDef, e.ResetBefore, e.ResetAfter),
                            StatusTone.Error, ParticipantDetailSec)
                        : new Notice(NoticeKind.Participant, PanelPresenter.ParticipantNotRecordingToast(e.ParticipantId), StatusTone.Error, ParticipantDetailSec);
                }
                if (markerRecorded)
                    return new Notice(NoticeKind.Participant, PanelPresenter.ParticipantCommittedToast(e.ParticipantId, e.MarkerAt), StatusTone.Good, ParticipantDetailSec);
                if (e.ResetDef != null && e.MarkerPresses == 0)
                    return new Notice(NoticeKind.Participant,
                        PanelPresenter.ParticipantCommittedToast(e.ParticipantId, e.ResetDef, e.ResetBefore, e.ResetAfter),
                        StatusTone.Good, ParticipantDetailSec);
                return new Notice(NoticeKind.Participant, PanelPresenter.ParticipantCommittedToast(e.ParticipantId), StatusTone.Good, ParticipantSec);
            }

            if (e.MarkerPresses > 0)
            {
                if (!markerRecorded)
                    return new Notice(NoticeKind.Marker, PanelPresenter.MarkerNotRecordedToast(e.LogFailed), StatusTone.Error, MarkerNotRecordedSec);
                // 続けて押したら、出ている通知の回数を増やす（時刻は最後のマーカーのもの）
                int run = (e.VisibleMarkerRun > 0 ? e.VisibleMarkerRun : 0) + e.MarkerPresses;
                return new Notice(NoticeKind.Marker, PanelPresenter.MarkerToast(run, e.MarkerAt), StatusTone.Good, MarkerSec, run);
            }

            if (e.ResetDef != null)
                return new Notice(NoticeKind.Reset, PanelPresenter.ResetToast(e.ResetDef, e.ResetBefore, e.ResetAfter), StatusTone.Neutral, ResetSec);

            if (e.RampFinished && !e.ToastVisible)
                return new Notice(NoticeKind.RampFinished, PanelPresenter.RampFinishedToast(e.RampTo), StatusTone.Neutral, RampFinishedSec);

            return default;
        }

        /// <summary>
        /// 通知の文の見本（いちばん長くなる組み合わせを含む）。テストで字を、プレビューで通知の欄に収まるかを調べる。
        /// </summary>
        public static IEnumerable<string> Samples()
        {
            const int id = 999;
            const double t = 99999.9;
            yield return PanelPresenter.LogFailedToast;
            yield return PanelPresenter.ParticipantCommittedToast(id);
            yield return PanelPresenter.ParticipantCommittedToast(id, t);
            yield return PanelPresenter.ParticipantNotRecordingToast(id);
            yield return PanelPresenter.MarkerToast(1, t);
            yield return PanelPresenter.MarkerToast(99, t);
            yield return PanelPresenter.MarkerNotRecordedToast(true);
            yield return PanelPresenter.MarkerNotRecordedToast(false);
            yield return PanelPresenter.RampFinishedToast(-1f);
            yield return PanelPresenter.RampFinishedToast(1f);
            foreach (var def in ParameterDefs.All)
            {
                if (!def.Resettable) continue;
                // リセットは既定値に戻す。元の値のうち、文がいちばん長くなるもの（数字の幅は文字数とほぼ比例する）
                var values = new List<float> { def.Min, def.Max };
                if (def.Kind == ParameterKind.Enum && def.Options != null)
                    for (int i = 0; i < def.Options.Length; i++) values.Add(i);
                string longest = null;
                float before = def.Default;
                foreach (float b in values)
                {
                    if (def.Sanitize(b).Equals(def.Sanitize(def.Default))) continue;
                    string s = PanelPresenter.ResetToast(def, b, def.Default);
                    if (longest != null && s.Length <= longest.Length) continue;
                    longest = s;
                    before = b;
                }
                if (longest == null) continue;
                yield return longest;
                yield return PanelPresenter.ParticipantCommittedToast(id, def, before, def.Default);
                yield return PanelPresenter.ParticipantNotRecordingToast(id, def, before, def.Default);
            }
        }
    }
}
