using System;
using System.Collections.Generic;

namespace QuestSaturation.Core
{
    /// <summary>
    /// 被験者 ID の変更を、いつ新しい被験者として確定するかを決める。
    /// - ID を送っている途中では確定しない。ID は 1 つずつ送るので、途中の ID でファイルを作らないようにする。
    /// - 確定するのは、ID 以外の操作（パラメータの変更、再生・一時停止・停止）、マーカー、状態が変わったとき。
    /// - 確定する前に元の ID に戻したら、何も起きない。
    /// - この起動中に使った ID に戻ったら、その ID の試行数の続きから数える。
    /// </summary>
    public sealed class ParticipantSession
    {
        private readonly Dictionary<int, int> trialCounts = new Dictionary<int, int>();
        private int pendingId = -1;

        public ParticipantSession(int initialId)
        {
            CurrentId = initialId < 0 ? 0 : initialId;
        }

        /// <summary>今のファイルの被験者 ID。</summary>
        public int CurrentId { get; private set; }

        public bool HasPending => pendingId >= 0;

        /// <summary>まだ確定していない ID。なければ -1。</summary>
        public int PendingId => pendingId;

        /// <summary>FreeControl で ID が変わった。まだ確定しない。</summary>
        public void NoteIdEdited(int id)
        {
            if (id >= 0) pendingId = id;
        }

        /// <summary>
        /// 保留中の変更を確定する。被験者が替わったら true を返し、restoredTrialCount に新しい被験者の
        /// 試行数（初めての ID なら 0）を入れる。currentTrialCount は今の被験者のここまでの試行数。
        /// </summary>
        public bool Commit(int currentTrialCount, out int restoredTrialCount)
        {
            restoredTrialCount = currentTrialCount;
            if (pendingId < 0) return false;
            int id = pendingId;
            pendingId = -1;
            if (id == CurrentId) return false; // 元に戻しただけ

            trialCounts[CurrentId] = currentTrialCount < 0 ? 0 : currentTrialCount;
            CurrentId = id;
            restoredTrialCount = trialCounts.TryGetValue(id, out int n) ? n : 0;
            return true;
        }

        /// <summary>
        /// FreeControl のイベントが確定のきっかけになるか。ID の変更そのものと、自動で出る ramp_finished はならない。
        /// </summary>
        public static bool IsCommitTrigger(string freeControlEvent, string detail)
        {
            if (string.IsNullOrEmpty(freeControlEvent)) return false;
            if (freeControlEvent == FreeControlEvents.RampFinished) return false;
            if (freeControlEvent == FreeControlEvents.ParamChange && detail != null &&
                detail.StartsWith(FreeControlEvents.ItemPrefix(MenuItem.ParticipantId), StringComparison.Ordinal))
                return false;
            return true;
        }
    }
}
