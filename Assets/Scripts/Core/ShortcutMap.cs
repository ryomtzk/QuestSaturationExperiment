namespace QuestSaturation.Core
{
    /// <summary>コントローラーの操作。左右はすべて名前で明示する。</summary>
    public enum Shortcut
    {
        SelectRightTrigger = 0, // 右トリガー：球を決定
        SelectLeftTrigger = 1,  // 左トリガー：球を決定（旧ビルドの手順を壊さないため）
        MenuHold = 2,           // 左☰ 長押し：Selection ⇄ FreeControl
        ResetHold = 3,          // 右A 長押し：Trial → Selection
        Marker = 4,             // 左グリップ：ログにマーカー
        TogglePanel = 5,        // 左Y：パネルの表示切替
        RecallPanel = 6,        // 左X：パネルを目の前に呼び戻す
        StickUp = 7,            // 右スティック上：前の項目
        StickDown = 8,          // 右スティック下：次の項目
        StickLeft = 9,          // 右スティック左：値を減らす
        StickRight = 10,        // 右スティック右：値を増やす
        StickClick = 11,        // 右スティック押し込み：既定値に戻す
        PlayPause = 12,         // 右B タップ：再生/一時停止
        Stop = 13,              // 右B 長押し：停止
    }

    /// <summary>
    /// 状態ごとに受け付ける操作。Trial 中は実験を乱さないよう、リセットとマーカー以外を受け付けない。
    /// </summary>
    public static class ShortcutMap
    {
        public static bool IsAllowed(FlowState state, Shortcut s)
        {
            switch (state)
            {
                case FlowState.Selection:
                    return s == Shortcut.SelectRightTrigger || s == Shortcut.SelectLeftTrigger ||
                           s == Shortcut.MenuHold || s == Shortcut.Marker;

                case FlowState.Trial:
                    return s == Shortcut.ResetHold || s == Shortcut.Marker;

                case FlowState.FreeControl:
                    return s == Shortcut.MenuHold || s == Shortcut.Marker ||
                           s == Shortcut.TogglePanel || s == Shortcut.RecallPanel ||
                           s == Shortcut.StickUp || s == Shortcut.StickDown ||
                           s == Shortcut.StickLeft || s == Shortcut.StickRight ||
                           s == Shortcut.StickClick || s == Shortcut.PlayPause || s == Shortcut.Stop;

                default:
                    return false;
            }
        }
    }
}
