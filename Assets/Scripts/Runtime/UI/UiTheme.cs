using QuestSaturation.Core;
using UnityEngine;

namespace QuestSaturation.UI
{
    /// <summary>
    /// オペレーターパネルの色と文字の大きさ。
    /// - 色は無彩色だけ。オペレーターはパネルのすぐ横でパススルーの色を見て判断するので、
    ///   パネルに鮮やかな色があると、対比で色の見え方が変わるおそれがある。色を使うのは小さな状態表示だけ。
    /// - どの色も不透明（アルファ 1）。パススルーはアイバッファのアルファで合成されるので、
    ///   半透明の部品を重ねるとパネルの内側からパススルーが透けてしまう。
    /// - 灰色は sRGB の 8 ビット値で決め、暗い側でも 6 以上離す（Canvas.vertexColorAlwaysGammaSpace を
    ///   true にしているので、この値のまま表示される）。背景は真っ黒にせず 0.15 にして、横のパススルーの
    ///   明るさの見え方への影響を抑える。
    /// 単位は Canvas の 1 単位 = 0.5 mm（パネルは 0.8 m 先）。1 単位は 0.036° で、Quest 3 の
    /// アイバッファではおよそ 0.34〜0.79 ピクセル（動的解像度の下限〜上限）。
    /// </summary>
    public static class UiTheme
    {
        // 面
        public static readonly Color Background = Gray(0x26);
        public static readonly Color Border = Gray(0x3A);
        public static readonly Color Surface = Gray(0x30);        // カード、フッター
        public static readonly Color SurfaceRaised = Gray(0x4A);  // 選んでいる行、通知、長押しの帯
        public static readonly Color HoldFill = Gray(0x66);       // 長押しの進み具合
        public static readonly Color Keycap = Gray(0x4A);
        public static readonly Color Inverted = Gray(0xE6);       // 反転表示（ログ停止の警告）

        // 文字
        public static readonly Color TextPrimary = Gray(0xEB);    // 真っ白にしない（レンズのにじみを抑える）
        public static readonly Color TextSecondary = Gray(0xB4);
        public static readonly Color TextTertiary = Gray(0x8C);
        public static readonly Color TextDim = Gray(0x76);        // 効いていない項目
        public static readonly Color TextDimFocused = Gray(0xA6);
        public static readonly Color TextOnLight = Gray(0x1A);

        // 値のバー
        public static readonly Color Track = Gray(0x5C);
        public static readonly Color Fill = Gray(0xEB);
        public static readonly Color FillIdle = Gray(0xA8);
        public static readonly Color FillDim = Gray(0x6A);
        public static readonly Color Tick = Gray(0x9A);
        public static readonly Color Knob = Gray(0xF2);
        public static readonly Color Chevron = Gray(0xEB);
        public static readonly Color ChevronLimit = Gray(0x5C);

        // 選択肢
        public static readonly Color SegmentTrack = Gray(0x38);
        public static readonly Color Selection = Gray(0xE0);
        public static readonly Color SelectionIdle = Gray(0xA0);

        // グラフ
        public static readonly Color GraphGrid = Gray(0x40);
        public static readonly Color GraphZero = Gray(0x5C);
        public static readonly Color GraphArea = Gray(0x3E);
        public static readonly Color GraphAreaIdle = Gray(0x38);
        public static readonly Color GraphLine = Gray(0xE6);
        public static readonly Color GraphLineIdle = Gray(0x80);
        public static readonly Color Playhead = Gray(0xB0);

        // 状態（小さな点と文字だけに使う）
        public static readonly Color Good = Hex(0x4CB782);
        public static readonly Color Warning = Hex(0xE2A33C);
        public static readonly Color Error = Hex(0xE5574B);
        public static readonly Color ErrorText = Hex(0xFFB3AA);
        public static readonly Color WarningSurface = Hex(0x3D2E12);

        // 文字の大きさ（単位）。() 内は 0.8 m 先での em の高さ。32 単位より小さい文字は大文字の短い札だけ
        public const float SizeTitle = 40f;     // 20 mm, 1.43°
        public const float SizeLabel = 40f;     // 行の見出し
        public const float SizeValue = 44f;     // 22 mm, 1.58°（以前のパネルと同じ）
        public const float SizeBody = 36f;
        public const float SizeSecondary = 32f; // 16 mm, 1.15°
        public const float SizeSmall = 30f;     // カードの見出し、軸、凡例
        public const float SizeTag = 28f;       // 大文字の札（AUTO、REC）とキーの刻印
        public const float SizeLarge = 64f;     // 出力の彩度
        public const float SizeId = 60f;        // 被験者 ID

        public const float CornerPanel = 28f;
        public const float CornerCard = 18f;
        public const float CornerRow = 10f;
        public const float CornerChip = 10f;
        public const float CornerKey = 8f;

        public static Color ToneColor(StatusTone tone)
        {
            switch (tone)
            {
                case StatusTone.Good: return Good;
                case StatusTone.Warning: return Warning;
                case StatusTone.Error: return Error;
                default: return TextTertiary;
            }
        }

        /// <summary>状態の文字色。正常と中立はふつうの文字色にして、注意と異常だけ色を付ける。</summary>
        public static Color ToneText(StatusTone tone)
        {
            switch (tone)
            {
                case StatusTone.Warning: return Warning;
                case StatusTone.Error: return ErrorText;
                default: return TextSecondary;
            }
        }

        private static Color Gray(int v) => new Color32((byte)v, (byte)v, (byte)v, 255);

        private static Color Hex(int rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
    }
}
