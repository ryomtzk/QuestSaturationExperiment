using System.Collections.Generic;
using System.Globalization;
using QuestSaturation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static QuestSaturation.UI.UiFactory;

namespace QuestSaturation.UI
{
    /// <summary>
    /// オペレーターパネルの見た目。Canvas と部品をコードで組み立て、<see cref="OperatorPanelState"/> を描く。
    /// OVR にもシーンにも触れず、Update も使わないので、エディタのプレビューからも同じものを作れる。
    /// - <see cref="Apply"/>：状態を部品に反映する（変わった部品だけを書き換える。毎フレーム呼んでよい）。
    /// - <see cref="Animate"/>：選択の移動、値のバー、通知、長押しの帯、出現のアニメーションを進める。
    /// 配置（単位。1 単位 = 0.5 mm）:
    ///   ヘッダー（題名と状態） / 左：項目の一覧 / 右：出力・波形・セッションのカード / フッター（操作説明と凡例）
    /// 変わる頻度ごとに入れ子の Canvas に分け、ある部分が変わっても、ほかの部分を組み直さないようにしている。
    /// </summary>
    public sealed class OperatorPanelView : MonoBehaviour
    {
        // パネルの寸法
        public const float Width = 1280f;
        public const float Height = 960f;
        public const float CornerRadius = UiTheme.CornerPanel;
        private const float Pad = 32f;
        private const float HeaderH = 84f;
        private const float BodyTop = 92f;
        private const float BodyH = 708f;
        private const float FooterTop = 816f;
        private const float FooterH = 120f;
        private const float ListW = 728f;
        private const float RightX = Pad + ListW + 16f;
        private const float RightW = Width - Pad - RightX;
        private const float RowH = 66f;
        private const float GroupGap = 16f;
        private const float CardPad = 24f;

        // 行の中の列（行の左端から）
        private const float LabelX = 24f, LabelW = 212f;
        private const float BarX = 240f, BarW = 152f;
        private const float FieldX = 400f, FieldW = 204f;
        private const float ChevronW = 18f, ChevronH = 24f, ChevronGap = 8f;
        private const float LessGap = 14f; // 左の矢印と数字の間（数字は右端を中心に 7% 大きくなるので、右より広く）
        private const float ValueRight = FieldX + FieldW - ChevronW - ChevronGap;
        private const float TagX = 612f;
        private const float WideX = BarX, WideW = ListW - 12f - BarX;

        // 右のカード（本文の上端から）
        private const float OutputY = 0f, OutputH = 168f;
        private const float WaveY = 182f, WaveH = 320f;
        private const float SessionY = 516f, SessionH = 192f;
        private const float GraphX = 64f, GraphY = 66f, GraphW = RightW - CardPad - GraphX, GraphH = 180f;

        // 動き
        private const float FocusRate = 22f;   // 時定数 45 ms
        private const float FocusSnap = 0.5f;
        private const float AppearSec = 0.18f;
        private const float EmphasisHold = 0.3f;
        private const float TextRefreshSec = 0.1f; // 毎フレーム変わる数字は 10 Hz で書き換える

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private sealed class Row
        {
            public ParameterDef Def;
            public float Y;
            public TextMeshProUGUI Label;
            public TextMeshProUGUI Value;
            public UiShape Less;
            public UiShape More;
            public ValueBar Bar;
            public SegmentedControl Segmented;
            public Chip Tag;                 // Saturation の AUTO、Participant の PENDING
            public float LastValue = float.NaN;
            public float ValueWidth;         // 数字の幅（左の矢印を数字に寄せる）
            public bool LastFocused;
            public bool LastDim;
            public bool Styled;
            public float Emphasis;           // 0〜1。値を変えた直後に少し大きく見せる
            public float SinceChange = 99f;
        }

        private Canvas canvas;
        private RectTransform root;
        private Row[] rows;
        private string[] hintsManual;
        private string[] hintsAuto;      // Mode の行だけ違う（B の働きがモードで変わる）
        private readonly List<TextMeshProUGUI> texts = new List<TextMeshProUGUI>();

        private RectTransform focusRt;
        private float focusY = -1f;
        private int lastCursor = -1;
        private TextMeshProUGUI hint;

        // ヘッダー
        private TextMeshProUGUI version;
        private float versionRight;      // 版の文字の右端。状態の列がここまで伸びたら版を隠す
        private bool? lastVersionFits;
        private StatusStrip status;
        private int lastHmdPct = int.MinValue;
        private string hmdLabel = "";

        // 出力
        private TextMeshProUGUI outSaturation;
        private TextMeshProUGUI outBrightness;
        private TextMeshProUGUI outContrast;
        private Chip fadeChip;
        private float outputNext;

        // 波形
        private WaveformGraph graph;
        private RectTransform graphRt;
        private RectTransform playhead;
        private UiShape playheadShape;
        private RectTransform waveDot;
        private UiShape waveDotShape;
        private Chip transport;
        private TextMeshProUGUI position;
        private readonly float[] sampleX = new float[PanelPresenter.MaxSamples];
        private readonly float[] sampleY = new float[PanelPresenter.MaxSamples];
        private PlaybackStatus lastPlayback = (PlaybackStatus)(-1);
        private FreeMode lastMode = (FreeMode)(-1);
        private int lastPositionKey = int.MinValue;
        private float lastPositionPeriod = float.NaN;

        // セッション
        private TextMeshProUGUI participant;
        private TextMeshProUGUI trials;
        private TextMeshProUGUI since;
        private Chip recChip;
        private GameObject pendingStrip;
        private TextMeshProUGUI pendingText;
        private GameObject banner;
        private int lastParticipant = int.MinValue;
        private int lastPending = int.MinValue;
        private int lastTrials = int.MinValue;
        private string lastLogFile;
        private bool lastLogFailed;

        // 重ねる表示
        private HoldBar hold;
        private ToastView toast;
        private int lastMenuCaptionId = int.MinValue;
        private string menuCaption = "";

        private float appear = 1f;
        private bool hasApplied;
        private float clock;
        private Vector3 baseScale = Vector3.one;

        // 文字列のキャッシュ（毎フレームの割り当てを避ける）
        private static string[] signedCache;

        public Canvas Canvas => canvas;
        public RectTransform Root => root;

        /// <summary>パネルの文字すべて（プレビューで、はみ出しと字の有無を調べる）。</summary>
        public IReadOnlyList<TextMeshProUGUI> Texts => texts;

        /// <summary>
        /// パネルを組み立てる。parent の子に "OperatorPanelCanvas" を作り、非表示の状態で返す。
        /// 組み立ての途中で文字のメッシュも一度作っておく（最初に表示したときに引っかからないように）。
        /// 失敗したら作りかけのものを消して例外を投げる。
        /// </summary>
        public static OperatorPanelView Create(Transform parent, float metersPerUnit)
        {
            var go = new GameObject("OperatorPanelCanvas", typeof(RectTransform));
            if (parent != null) go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            try
            {
                var view = go.AddComponent<OperatorPanelView>();
                view.Build(metersPerUnit);
                foreach (var t in view.texts) t.ForceMeshUpdate();
                go.SetActive(false);
                return view;
            }
            catch
            {
                go.SetActive(false);
                if (Application.isPlaying) Destroy(go);
                else DestroyImmediate(go);
                throw;
            }
        }

        private void Build(float metersPerUnit)
        {
            if (DefaultFont == null)
                Debug.LogError("[QSE-Panel] TextMeshPro Essentials are missing; the operator panel has no font. Run QuestSaturation > Import TextMeshPro Essentials.");

            root = (RectTransform)transform;
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ConfigureCanvas(canvas);
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 1f;
            scaler.referencePixelsPerUnit = 100f;
            root.sizeDelta = new Vector2(Width, Height);
            baseScale = Vector3.one * metersPerUnit;
            root.localScale = baseScale;

            // 外枠と背景（どちらも不透明。いちばん下の面なので、テクスチャを使わない図形で描く）
            Stretch(CreatePanel("Border", root, UiTheme.Border, UiTheme.CornerPanel + 2f).rectTransform);
            Stretch(CreatePanel("Background", root, UiTheme.Background, UiTheme.CornerPanel).rectTransform, 2f, 2f, 2f, 2f);

            BuildHeader();
            BuildList();
            BuildCards();
            BuildFooter();

            var overlay = Stretch(CreateRect("Overlay", root));
            AddSubCanvas(overlay.gameObject);
            hold = new HoldBar(overlay, 16f, 10f, Width - 32f, HeaderH - 20f);
            toast = new ToastView(overlay, Pad + 12f, FooterTop + 9f, Width - Pad * 2f - 24f, 52f);

            texts.Clear();
            GetComponentsInChildren(true, texts);
        }

        // ---------------------------------------------------------------- 組み立て

        /// <summary>文字の基準線を baseline（親の上端から）に合わせて置く。TMP の Baseline 揃えは、矩形の縦の中央に基準線を置く。</summary>
        private static RectTransform PlaceBaseline(TextMeshProUGUI t, float x, float baseline, float w, float h = 80f) =>
            Place(t.rectTransform, x, baseline - h * 0.5f, w, h);

        /// <summary>左の矢印を数字の左端に寄せる（右揃えの数字は幅が値ごとに違うので、欄の左端に置くと離れて見える）。</summary>
        private static void PlaceLess(Row r)
        {
            float x = Mathf.Max(FieldX, ValueRight - r.ValueWidth - LessGap - ChevronW);
            var rt = r.Less.rectTransform;
            var p = new Vector2(x, rt.anchoredPosition.y);
            if (rt.anchoredPosition != p) rt.anchoredPosition = p;
        }

        private void BuildHeader()
        {
            var header = Place(CreateRect("Header", root), 0f, 0f, Width, HeaderH);
            AddSubCanvas(header.gameObject);

            const float baseline = 56f;
            var title = CreateText("Title", header, "Free Control", UiTheme.SizeTitle, UiTheme.TextPrimary, TextAlignmentOptions.BaselineLeft, FontStyles.Bold);
            float tw = Measure(title, title.text) + 4f;
            PlaceBaseline(title, Pad, baseline, tw);
            version = CreateText("Version", header, "", UiTheme.SizeSmall, UiTheme.TextTertiary, TextAlignmentOptions.BaselineLeft);
            PlaceBaseline(version, Pad + tw + 18f, baseline, 220f);

            // 左から：パススルー、ログ、ヘッドセットの電池、右コントローラーの電池、時計
            status = new StatusStrip(header, Width - Pad, 0f, HeaderH, UiTheme.SizeSmall, true, true, false, false);
        }

        private void BuildList()
        {
            var list = Place(CreateRect("List", root), Pad, BodyTop, ListW, BodyH);

            // 選択の帯は行より奥に描く。動かしても行の Canvas を組み直さないよう、別の Canvas にする
            var focusLayer = Stretch(CreateRect("Focus", list));
            AddSubCanvas(focusLayer.gameObject);
            var focus = CreatePanel("Highlight", focusLayer, UiTheme.SurfaceRaised, UiTheme.CornerRow);
            focusRt = Place(focus.rectTransform, 0f, 0f, ListW, RowH - 6f);
            var accent = CreatePanel("Accent", focusRt, UiTheme.Fill, 4f);
            Place(accent.rectTransform, 0f, 0f, 8f, RowH - 6f);

            var rowsLayer = Stretch(CreateRect("Rows", list));
            AddSubCanvas(rowsLayer.gameObject);

            rows = new Row[ParameterDefs.All.Length];
            hintsManual = new string[ParameterDefs.All.Length];
            hintsAuto = new string[ParameterDefs.All.Length];
            float y = 0f;
            for (int i = 0; i < ParameterDefs.All.Length; i++)
            {
                var def = ParameterDefs.All[i];
                if (i > 0 && StartsGroup(def.Item)) y += GroupGap;
                rows[i] = BuildRow(rowsLayer, def, y);
                hintsManual[i] = PanelPresenter.Hint(def, FreeMode.Manual);
                hintsAuto[i] = PanelPresenter.Hint(def, FreeMode.Auto);
                y += RowH;
            }
        }

        /// <summary>見出しの代わりに間を空ける位置（モード / 色 / 波形 / 被験者）。</summary>
        private static bool StartsGroup(MenuItem item) =>
            item == MenuItem.Saturation || item == MenuItem.Waveform || item == MenuItem.ParticipantId;

        private Row BuildRow(RectTransform parent, ParameterDef def, float y)
        {
            var row = new Row { Def = def, Y = y };
            var rt = Place(CreateRect("Row " + def.Label, parent), 0f, y, ListW, RowH);

            row.Label = CreateText("Label", rt, PanelPresenter.DisplayLabel(def), UiTheme.SizeLabel, UiTheme.TextSecondary);
            Place(row.Label.rectTransform, LabelX, 0f, LabelW, RowH);

            if (def.Kind == ParameterKind.Enum)
            {
                row.Segmented = new SegmentedControl(rt, WideX, 8f, WideW, RowH - 16f, def.Options);
            }
            else
            {
                if (def.Item != MenuItem.ParticipantId) row.Bar = new ValueBar(rt, BarX, 0f, BarW, RowH);

                // 値の欄：左右の矢印（選んでいる行だけ）と、右揃えの数字（桁の位置が動かない）
                float cy = (RowH - ChevronH) * 0.5f;
                row.Less = CreateShape("Less", rt, ShapeKind.ChevronLeft, UiTheme.Chevron);
                Place(row.Less.rectTransform, FieldX, cy, ChevronW, ChevronH);
                row.More = CreateShape("More", rt, ShapeKind.ChevronRight, UiTheme.Chevron);
                Place(row.More.rectTransform, FieldX + FieldW - ChevronW, cy, ChevronW, ChevronH);
                SetActive(row.Less, false);
                SetActive(row.More, false);

                row.Value = CreateText("Value", rt, "", UiTheme.SizeValue, UiTheme.TextPrimary, TextAlignmentOptions.MidlineRight);
                float vx = FieldX + ChevronW + LessGap;
                float vw = ValueRight - vx;
                var vrt = Place(row.Value.rectTransform, vx, 0f, vw, RowH);
                // 大きくするときは右端の中央を中心にする（数字の右端が動かないように）
                vrt.pivot = new Vector2(1f, 0.5f);
                vrt.anchoredPosition = new Vector2(ValueRight, -RowH * 0.5f);
            }

            if (def.Item == MenuItem.Saturation)
            {
                row.Tag = new Chip(rt, TagX, (RowH - 36f) * 0.5f, 36f, false, UiTheme.SizeTag);
                row.Tag.Set(PanelPresenter.AutoTag, UiTheme.Keycap, UiTheme.TextPrimary);
                row.Tag.SetVisible(false);
            }
            else if (def.Item == MenuItem.ParticipantId)
            {
                // 変えたがまだ確定していない ID。値の横（バーの位置）に出す
                row.Tag = new Chip(rt, BarX, (RowH - 36f) * 0.5f, 36f, false, UiTheme.SizeTag, 0f, FontStyles.Normal);
                row.Tag.Set(PanelPresenter.PendingTag, UiTheme.WarningSurface, UiTheme.Warning);
                row.Tag.SetVisible(false);
            }
            return row;
        }

        private void BuildCards()
        {
            var body = Place(CreateRect("Cards", root), RightX, BodyTop, RightW, BodyH);
            BuildOutput(body);
            BuildWave(body);
            BuildSession(body);
        }

        private static RectTransform Card(RectTransform parent, string title, float y, float h)
        {
            var card = Place(CreateRect("Card " + title, parent), 0f, y, RightW, h);
            AddSubCanvas(card.gameObject);
            Stretch(CreatePanel("Bg", card, UiTheme.Surface, UiTheme.CornerCard).rectTransform);
            var t = CreateText("Title", card, title, UiTheme.SizeSmall, UiTheme.TextTertiary);
            Place(t.rectTransform, CardPad, 12f, 200f, 40f);
            return card;
        }

        private void BuildOutput(RectTransform body)
        {
            // 読むだけの表示なので、編集できるバー（つまみ、トラック）とは違う見た目にする
            var card = Card(body, "Output", OutputY, OutputH);
            fadeChip = new Chip(card, RightW - 20f, 12f, 38f, true, UiTheme.SizeTag);
            fadeChip.Set(PanelPresenter.FadingTag, UiTheme.Keycap, UiTheme.TextPrimary);
            fadeChip.SetVisible(false);

            // 左：彩度を大きく（下に名前）。右：明るさとコントラストを2行で（名前は左揃え、数字は右揃え）
            const float line1 = 106f, line2 = 146f;
            outSaturation = CreateText("Saturation", card, "", UiTheme.SizeLarge, UiTheme.TextPrimary, TextAlignmentOptions.BaselineLeft, FontStyles.Bold);
            PlaceBaseline(outSaturation, CardPad, line1, 172f, 100f);
            var satLabel = CreateText("SaturationLabel", card, "saturation", UiTheme.SizeSmall, UiTheme.TextTertiary, TextAlignmentOptions.BaselineLeft);
            PlaceBaseline(satLabel, CardPad, line2, 172f);

            const float valueW = 84f, labelW = 144f;
            float valueX = RightW - CardPad - valueW, labelX = valueX - 8f - labelW;
            var bl = CreateText("BrightnessLabel", card, "Brightness", UiTheme.SizeSmall, UiTheme.TextTertiary, TextAlignmentOptions.BaselineLeft);
            PlaceBaseline(bl, labelX, line1, labelW);
            outBrightness = CreateText("Brightness", card, "", UiTheme.SizeSecondary, UiTheme.TextPrimary, TextAlignmentOptions.BaselineRight);
            PlaceBaseline(outBrightness, valueX, line1, valueW);
            var cl = CreateText("ContrastLabel", card, "Contrast", UiTheme.SizeSmall, UiTheme.TextTertiary, TextAlignmentOptions.BaselineLeft);
            PlaceBaseline(cl, labelX, line2, labelW);
            outContrast = CreateText("Contrast", card, "", UiTheme.SizeSecondary, UiTheme.TextPrimary, TextAlignmentOptions.BaselineRight);
            PlaceBaseline(outContrast, valueX, line2, valueW);
        }

        private void BuildWave(RectTransform body)
        {
            var card = Card(body, "Waveform", WaveY, WaveH);
            // 波形の名前は左の Waveform の行（選んでいる形が反転している）で分かるので、ここには出さない。
            // 題の横に置くと、再生状態のチップ（STOPPED、FINISHED など）と重なる
            transport = new Chip(card, RightW - 20f, 12f, 40f, true, UiTheme.SizeTag, 24f);

            for (int i = 0; i < 3; i++)
            {
                float ly = GraphY + GraphH * i * 0.5f;
                bool zero = i == 1;
                float th = zero ? 4f : 3f;
                Place(CreateSolid("Grid", card, zero ? UiTheme.GraphZero : UiTheme.GraphGrid).rectTransform, GraphX, ly - th * 0.5f, GraphW, th);
                var t = CreateText("Axis", card, i == 0 ? "+1" : zero ? "0" : "-1", UiTheme.SizeSmall, UiTheme.TextTertiary, TextAlignmentOptions.MidlineRight);
                // 右端を GraphX - 22 にして、左端（位相 0）にある再生位置の点（半径 11）と離す
                Place(t.rectTransform, 4f, ly - 20f, GraphX - 26f, 40f);
            }

            graph = CreateRect("Graph", card).gameObject.AddComponent<WaveformGraph>();
            graph.raycastTarget = false;
            graph.maskable = false;
            graphRt = Place(graph.rectTransform, GraphX, GraphY, GraphW, GraphH);

            // 再生位置は毎フレーム動くので、さらに別の Canvas にする
            var live = Place(CreateRect("Playhead", card), GraphX, GraphY, GraphW, GraphH);
            AddSubCanvas(live.gameObject);
            playheadShape = CreateSolid("Line", live, UiTheme.Playhead);
            playhead = playheadShape.rectTransform;
            playhead.anchorMin = new Vector2(0f, 0f);
            playhead.anchorMax = new Vector2(0f, 1f);
            playhead.pivot = new Vector2(0.5f, 0.5f);
            playhead.sizeDelta = new Vector2(4f, 0f);
            waveDotShape = CreateShape("Dot", live, ShapeKind.Circle, UiTheme.Knob);
            waveDot = waveDotShape.rectTransform;
            waveDot.anchorMin = waveDot.anchorMax = new Vector2(0f, 0f);
            waveDot.sizeDelta = new Vector2(22f, 22f);

            position = CreateText("Position", card, "", UiTheme.SizeSecondary, UiTheme.TextPrimary);
            Place(position.rectTransform, CardPad, 264f, RightW - CardPad * 2f, 44f);
        }

        private void BuildSession(RectTransform body)
        {
            var card = Card(body, "Session", SessionY, SessionH);
            recChip = new Chip(card, RightW - 20f, 12f, 38f, true, UiTheme.SizeTag, 12f);

            // ID を大きく、その右に試行数と記録の開始時刻。下の帯は、確定待ちの ID かログの停止を出す
            const float idBaseline = 116f, sideX = 200f;
            participant = CreateText("Id", card, "", UiTheme.SizeId, UiTheme.TextPrimary, TextAlignmentOptions.BaselineLeft, FontStyles.Bold);
            PlaceBaseline(participant, CardPad, idBaseline, sideX - CardPad - 8f, 90f);
            trials = CreateText("Trials", card, "", UiTheme.SizeSecondary, UiTheme.TextSecondary, TextAlignmentOptions.BaselineLeft);
            PlaceBaseline(trials, sideX, idBaseline - 36f, RightW - CardPad - sideX);
            since = CreateText("Since", card, "", UiTheme.SizeSmall, UiTheme.TextTertiary, TextAlignmentOptions.BaselineLeft);
            PlaceBaseline(since, sideX, idBaseline, RightW - CardPad - sideX);

            const float stripY = 134f, stripH = 42f;
            var strip = Place(CreateRect("Pending", card), 20f, stripY, RightW - 40f, stripH);
            Stretch(CreatePanel("Bg", strip, UiTheme.WarningSurface, UiTheme.CornerChip).rectTransform);
            var dot = CreateShape("Dot", strip, ShapeKind.Circle, UiTheme.Warning);
            Place(dot.rectTransform, 16f, (stripH - 14f) * 0.5f, 14f, 14f);
            pendingText = CreateText("Text", strip, "", UiTheme.SizeSmall, UiTheme.Warning);
            Place(pendingText.rectTransform, 40f, 0f, RightW - 40f - 52f, stripH);
            pendingStrip = strip.gameObject;
            SetActive(pendingStrip, false);

            // ログが止まったときの警告。反転させて目立たせる（色は小さな点だけ）
            // （"Not recording" は ID の右の行に出す。帯には何をすればよいかだけを書く）
            var b = Place(CreateRect("LogStopped", card), 20f, stripY, RightW - 40f, stripH);
            Stretch(CreatePanel("Bg", b, UiTheme.Inverted, UiTheme.CornerChip).rectTransform);
            var bdot = CreateShape("Dot", b, ShapeKind.Circle, UiTheme.Error);
            Place(bdot.rectTransform, 16f, (stripH - 14f) * 0.5f, 14f, 14f);
            var bd = CreateText("Detail", b, PanelPresenter.LogStoppedDetail, UiTheme.SizeSmall, UiTheme.TextOnLight);
            Place(bd.rectTransform, 40f, 0f, RightW - 40f - 52f, stripH);
            banner = b.gameObject;
            SetActive(banner, false);
        }

        private void BuildFooter()
        {
            var footer = Place(CreateRect("Footer", root), Pad, FooterTop, Width - Pad * 2f, FooterH);
            AddSubCanvas(footer.gameObject);
            float w = Width - Pad * 2f;
            Stretch(CreatePanel("Bg", footer, UiTheme.Surface, UiTheme.CornerCard).rectTransform);

            hint = CreateText("Hint", footer, "", UiTheme.SizeSecondary, UiTheme.TextPrimary);
            Place(hint.rectTransform, CardPad, 12f, w - CardPad * 2f, 46f);

            // ボタンの凡例。幅を測って並べ、入りきらなければ間隔を詰める
            const float ky = 68f, kh = 38f, keyGap = 10f, iconW = 28f, iconH = 26f, iconGap = 8f;
            var legend = PanelPresenter.Legend;
            var caps = new RectTransform[legend.Length];
            var actions = new TextMeshProUGUI[legend.Length];
            var capW = new float[legend.Length];
            var actW = new float[legend.Length];
            float total = 0f;
            for (int i = 0; i < legend.Length; i++)
            {
                var e = legend[i];
                var cap = Place(CreateRect("Key " + e.Key, footer), 0f, ky, 10f, kh);
                Stretch(CreatePanel("Bg", cap, UiTheme.Keycap, UiTheme.CornerKey).rectTransform);
                var kt = CreateText("Key", cap, e.Key, UiTheme.SizeTag, UiTheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                float textW = Measure(kt, e.Key) + 2f;
                float inner = textW + (e.MenuIcon ? iconW + iconGap : 0f);
                float cw = Mathf.Max(kh, inner + 24f);
                float x0 = (cw - inner) * 0.5f;
                if (e.MenuIcon)
                {
                    var icon = CreateShape("Menu", cap, ShapeKind.Menu, UiTheme.TextPrimary);
                    Place(icon.rectTransform, x0, (kh - iconH) * 0.5f, iconW, iconH);
                    x0 += iconW + iconGap;
                }
                Place(kt.rectTransform, x0, 0f, textW, kh);
                caps[i] = cap;
                capW[i] = cw;

                var at = CreateText("Action", footer, e.Action, UiTheme.SizeSmall, UiTheme.TextSecondary);
                actions[i] = at;
                actW[i] = Measure(at, e.Action) + 2f;
                total += cw + keyGap + actW[i];
            }

            float avail = w - CardPad * 2f;
            float gap = legend.Length > 1 ? Mathf.Clamp((avail - total) / (legend.Length - 1), 14f, 40f) : 0f;
            float x = CardPad;
            for (int i = 0; i < legend.Length; i++)
            {
                caps[i].sizeDelta = new Vector2(capW[i], kh);
                caps[i].anchoredPosition = new Vector2(x, -ky);
                x += capW[i] + keyGap;
                Place(actions[i].rectTransform, x, ky, actW[i], kh);
                x += actW[i] + gap;
            }
        }

        // ---------------------------------------------------------------- 状態の反映

        /// <summary>状態を描く。変わったところだけ書き換える。</summary>
        public void Apply(OperatorPanelState s)
        {
            bool snap = !hasApplied;
            ApplyHeader(s);
            ApplyList(s, snap);
            ApplyOutput(s, snap);
            ApplyWave(s);
            ApplySession(s);
            ApplyHold(s);
            hasApplied = true;
        }

        public void SetVersion(string text)
        {
            SetText(version, text);
            versionRight = version.rectTransform.anchoredPosition.x + Measure(version, text ?? "");
            lastVersionFits = null;
        }

        private void ApplyHeader(OperatorPanelState s)
        {
            var ptTone = PanelPresenter.PassthroughTone(s.Passthrough, s.HasLayer, s.LayerEnabled, s.PassthroughVisible);
            status.Set(0, PanelPresenter.PassthroughLabel(s.Passthrough, s.HasLayer, s.LayerEnabled, s.PassthroughVisible),
                UiTheme.ToneText(ptTone), UiTheme.ToneColor(ptTone));

            var logTone = PanelPresenter.LogTone(s.LogFile, s.LogFailed);
            status.Set(1, PanelPresenter.LogLabel(s.LogFile, s.LogFailed), UiTheme.ToneText(logTone), UiTheme.ToneColor(logTone));

            int hmd = Percent(s.BatteryLevel);
            if (hmd != lastHmdPct)
            {
                lastHmdPct = hmd;
                hmdLabel = PanelPresenter.BatteryLabel("HMD", s.BatteryLevel);
            }
            status.Set(2, hmdLabel, UiTheme.ToneText(PanelPresenter.BatteryTone(s.BatteryLevel, s.Charging)), default);

            status.Set(3, string.IsNullOrEmpty(s.Clock) ? "--:--" : s.Clock, UiTheme.TextSecondary, default);

            // 警告が重なって状態の列が長くなったら、版の表示をやめて場所を空ける（重ねて描かない）
            bool fits = status.Left >= versionRight + 18f;
            if (fits != lastVersionFits)
            {
                lastVersionFits = fits;
                SetActive(version, fits);
            }
        }

        private static int Percent(float level) => level >= 0f ? Mathf.RoundToInt(Mathf.Min(1f, level) * 100f) : -1;

        private void ApplyList(OperatorPanelState s, bool snap)
        {
            int cursor = Mathf.Clamp(s.Cursor, 0, rows.Length - 1);
            if (cursor != lastCursor)
            {
                // 上下の端で折り返したとき（Mode ⇄ Participant）は滑らせずに飛ぶ
                bool wrap = lastCursor >= 0 && Mathf.Abs(cursor - lastCursor) > 1;
                lastCursor = cursor;
                if (snap || wrap || focusY < 0f) MoveFocus(rows[cursor].Y);
            }

            // 同じ文字列なら SetText は何もしない（配列の要素をそのまま渡すので、比較は参照だけで済む）
            SetText(hint, (s.Mode == FreeMode.Auto ? hintsAuto : hintsManual)[cursor]);

            var waveform = s.Waveform;
            for (int i = 0; i < rows.Length; i++)
            {
                var r = rows[i];
                var item = r.Def.Item;
                float v = s.Get(item);
                bool focused = i == cursor;
                bool dim = !PanelPresenter.IsRelevant(item, waveform);

                if (!v.Equals(r.LastValue))
                {
                    bool first = float.IsNaN(r.LastValue);
                    r.LastValue = v;
                    if (r.Value != null)
                    {
                        string text = PanelPresenter.FormatValue(r.Def, v);
                        SetText(r.Value, text);
                        r.ValueWidth = Measure(r.Value, text);
                        PlaceLess(r);
                    }
                    if (r.Bar != null) r.Bar.Set(PanelPresenter.Bar(r.Def, v), snap);
                    if (r.Segmented != null) r.Segmented.Set((int)r.Def.Sanitize(v) - (int)r.Def.Min, snap);
                    if (!first && !snap) r.SinceChange = 0f;
                }

                if (!r.Styled || focused != r.LastFocused || dim != r.LastDim)
                {
                    r.Styled = true;
                    r.LastFocused = focused;
                    r.LastDim = dim;
                    SetColor(r.Label, dim ? (focused ? UiTheme.TextDimFocused : UiTheme.TextDim) : focused ? UiTheme.TextPrimary : UiTheme.TextSecondary);
                    if (r.Value != null) SetColor(r.Value, dim ? (focused ? UiTheme.TextDimFocused : UiTheme.TextDim) : UiTheme.TextPrimary);
                    r.Bar?.SetStyle(focused, dim);
                    r.Segmented?.SetStyle(focused, dim);
                    if (r.Less != null) SetActive(r.Less, focused);
                    if (r.More != null) SetActive(r.More, focused);
                }

                if (focused && r.Less != null)
                {
                    // 端まで来た側の矢印を薄くする（押しても変わらないことが分かるように）
                    SetColor(r.Less, PanelPresenter.AtMin(r.Def, v) ? UiTheme.ChevronLimit : UiTheme.Chevron);
                    SetColor(r.More, PanelPresenter.AtMax(r.Def, v) ? UiTheme.ChevronLimit : UiTheme.Chevron);
                }

                if (r.Tag != null)
                {
                    bool show = item == MenuItem.ParticipantId ? s.HasPendingParticipant : PanelPresenter.IsOverriddenByAuto(item, s.Mode);
                    r.Tag.SetVisible(show);
                }
            }
        }

        private void ApplyOutput(OperatorPanelState s, bool snap)
        {
            fadeChip.SetVisible(s.Transitioning);
            // Auto では毎フレーム値が変わる。数字は 10 Hz で書き換える（それより速くても読めない）
            if (!snap && clock < outputNext) return;
            outputNext = clock + TextRefreshSec;
            SetText(outSaturation, Signed(s.Output.Saturation));
            SetText(outBrightness, Signed(s.Output.Brightness));
            SetText(outContrast, Signed(s.Output.Contrast));
        }

        private void ApplyWave(OperatorPanelState s)
        {
            var w = s.Waveform;
            float from = s.Get(MenuItem.From), to = s.Get(MenuItem.To);
            int n = PanelPresenter.SampleWaveform(w, from, to, s.Steps, sampleX, sampleY);
            graph.SetCurve(sampleX, sampleY, n, -1f, 1f);

            bool auto = s.Mode == FreeMode.Auto;
            bool playing = auto && s.Playback == PlaybackStatus.Playing;
            graph.SetStyle(auto ? UiTheme.GraphLine : UiTheme.GraphLineIdle, auto ? UiTheme.GraphArea : UiTheme.GraphAreaIdle, 6f);

            if (s.Playback != lastPlayback || s.Mode != lastMode)
            {
                lastPlayback = s.Playback;
                lastMode = s.Mode;
                ShapeKind icon = !auto ? ShapeKind.Stop
                    : s.Playback == PlaybackStatus.Playing ? ShapeKind.Play
                    : s.Playback == PlaybackStatus.Paused ? ShapeKind.Pause
                    : ShapeKind.Stop;
                Color bg = playing ? UiTheme.Selection : UiTheme.Keycap;
                Color fg = playing ? UiTheme.TextOnLight : UiTheme.TextPrimary;
                transport.Set(PanelPresenter.TransportLabel(s.Mode, s.Playback), bg, fg);
                transport.SetMark(icon, fg);
                lastPositionKey = int.MinValue;
            }

            SetActive(playhead, auto);
            SetActive(waveDot, auto);
            if (auto)
            {
                // 線と点は毎フレーム動かす（Canvas が小さいので軽い）。文字は 0.1 秒ごと
                float x = PanelPresenter.ClampPhase(s.Phase);
                Vector2 p = graph.ToLocal(x, s.WaveValue) - graphRt.rect.min;
                if (playhead.anchoredPosition.x != p.x) playhead.anchoredPosition = new Vector2(p.x, 0f);
                if (waveDot.anchoredPosition != p) waveDot.anchoredPosition = p;
                SetColor(playheadShape, playing ? UiTheme.Playhead : UiTheme.FillDim);
                SetColor(waveDotShape, playing ? UiTheme.Knob : UiTheme.FillIdle);

                int key = PanelPresenter.PositionTenths(s.Phase, s.PeriodSec);
                // 周期も文に入っているので、止まっている（位置が同じ）ときに周期を変えても書き直す
                if (key != lastPositionKey || !s.PeriodSec.Equals(lastPositionPeriod))
                {
                    lastPositionKey = key;
                    lastPositionPeriod = s.PeriodSec;
                    // SetText の書式指定は文字列を作らない（"{0:1}" は小数1桁）。文は PanelPresenter.PositionLabel と同じ
                    position.SetText("{0:1} / {1:1} s", key / 10f, s.PeriodSec);
                    SetColor(position, UiTheme.TextPrimary);
                }
            }
            else if (lastPositionKey != int.MaxValue)
            {
                lastPositionKey = int.MaxValue;
                lastPositionPeriod = float.NaN;
                // TMP の SetText(string) を使う。書式つきの SetText の後に .text へ代入すると、実機では
                // （エディタと違い）内部の文字列が古いままで「変わっていない」と判断され、代入が捨てられる
                position.SetText(PanelPresenter.ManualTransportHint);
                SetColor(position, UiTheme.TextSecondary);
            }
        }

        private void ApplySession(OperatorPanelState s)
        {
            if (s.ParticipantId != lastParticipant)
            {
                lastParticipant = s.ParticipantId;
                SetText(participant, PanelPresenter.ParticipantLabel(s.ParticipantId));
            }
            if (s.Trials != lastTrials)
            {
                lastTrials = s.Trials;
                SetText(trials, PanelPresenter.TrialsLabel(s.Trials));
            }
            if (!string.Equals(s.LogFile, lastLogFile) || s.LogFailed != lastLogFailed)
            {
                lastLogFile = s.LogFile;
                lastLogFailed = s.LogFailed;
                SetText(since, s.LogFailed ? PanelPresenter.LogStoppedTitle : PanelPresenter.LogStartLabel(s.LogFile));
                SetColor(since, s.LogFailed ? UiTheme.ErrorText : UiTheme.TextTertiary);
                var tone = PanelPresenter.LogTone(s.LogFile, s.LogFailed);
                recChip.Set(PanelPresenter.LogLabel(s.LogFile, s.LogFailed), UiTheme.Keycap, UiTheme.ToneText(tone));
                recChip.SetMark(ShapeKind.Circle, UiTheme.ToneColor(tone));
                SetActive(banner, s.LogFailed);
            }

            int pending = s.HasPendingParticipant ? s.PendingParticipantId : -1;
            if (pending != lastPending)
            {
                lastPending = pending;
                if (pending >= 0) SetText(pendingText, PanelPresenter.PendingLabel(pending));
            }
            SetActive(pendingStrip, pending >= 0 && !s.LogFailed);
        }

        private void ApplyHold(OperatorPanelState s)
        {
            // 左メニュー（Selection に戻る）を優先する。どちらも押し始めは出さない
            if (s.MenuHoldProgress >= PanelPresenter.MenuHoldShowAt)
            {
                int id = s.HasPendingParticipant ? s.PendingParticipantId : -1;
                if (id != lastMenuCaptionId)
                {
                    lastMenuCaptionId = id;
                    menuCaption = PanelPresenter.MenuHoldRelease(id >= 0, id);
                }
                hold.Set(s.MenuHoldProgress, PanelPresenter.MenuHoldTitle, menuCaption);
            }
            else if (s.StopHoldProgress >= PanelPresenter.StopHoldShowAt)
            {
                hold.Set(s.StopHoldProgress, PanelPresenter.StopHoldTitle, PanelPresenter.StopHoldRelease(s.Mode, s.Playback));
            }
            else
            {
                hold.Set(0f, null, null);
            }
        }

        // ---------------------------------------------------------------- アニメーション

        public void Animate(float dt)
        {
            clock += dt;

            if (appear < 1f)
            {
                // 大きさだけを動かす（背景は最初から不透明。薄くするとパススルーが透ける）
                appear = Mathf.Min(1f, appear + dt / AppearSec);
                float e = 1f - (1f - appear) * (1f - appear) * (1f - appear);
                root.localScale = baseScale * Mathf.Lerp(0.97f, 1f, e);
            }

            if (lastCursor >= 0)
            {
                float target = rows[lastCursor].Y;
                if (focusY != target) MoveFocus(Approach(focusY, target, FocusRate, dt, FocusSnap));
            }

            foreach (var r in rows)
            {
                r.Bar?.Animate(dt);
                r.Segmented?.Animate(dt);
                if (r.Value == null) continue;

                // 変えた直後に少し大きくし、続けて変えている間はそのまま、止めて 0.3 秒で戻す
                r.SinceChange += dt;
                float target = r.SinceChange < EmphasisHold ? 1f : 0f;
                if (r.Emphasis != target)
                {
                    r.Emphasis = Approach(r.Emphasis, target, target > r.Emphasis ? 40f : 12f, dt, 0.01f);
                    r.Value.rectTransform.localScale = Vector3.one * (1f + 0.07f * r.Emphasis);
                }
            }

            toast.Animate(dt);
            hold.Animate(dt);
        }

        private void MoveFocus(float y)
        {
            focusY = y;
            focusRt.anchoredPosition = new Vector2(0f, -(y + 3f));
        }

        // ---------------------------------------------------------------- 表示の切り替え

        /// <summary>現れるアニメーションを始める（消すときは即座に消すので、ここでは扱わない）。</summary>
        public void PlayAppear()
        {
            appear = 0f;
            root.localScale = baseScale * 0.97f;
        }

        /// <summary>動きを飛ばして、すぐに最後の見た目にする（プレビュー用）。</summary>
        public void FinishAnimations()
        {
            appear = 1f;
            root.localScale = baseScale;
            for (int i = 0; i < 120; i++) Animate(1f / 30f);
            hold.Finish();
        }

        /// <summary>通知を出す（出ている通知は置き換える。どれを出すかは PanelNotices が決める）。</summary>
        public void ShowToast(string message, StatusTone tone = StatusTone.Neutral, float seconds = 2.2f) =>
            toast.Show(message, tone, seconds);

        public void ShowNotice(in Notice n)
        {
            if (!n.IsNone) toast.Show(n.Message, n.Tone, n.Seconds);
        }

        /// <summary>通知の文の欄（プレビューで、どの通知の文も収まるかを調べる）。</summary>
        public TextMeshProUGUI ToastText => toast.Text;

        /// <summary>いま出ている通知の文（同じ通知が続いたら回数をまとめるため）。出ていなければ null。</summary>
        public string ToastMessage => toast.Visible ? toast.Message : null;

        /// <summary>通知と長押しの帯を消し、値の強調を戻す（隠すときと、出し直すとき）。</summary>
        public void ClearTransient()
        {
            toast.Clear();
            hold.Clear();
            if (rows == null) return;
            foreach (var r in rows)
            {
                r.Emphasis = 0f;
                r.SinceChange = 99f;
                if (r.Value != null) r.Value.rectTransform.localScale = Vector3.one;
            }
        }

        // ---------------------------------------------------------------- 文字列

        /// <summary>-1.00〜+1.00 を 0.01 刻みで、作り置きした文字列から返す（0 には符号を付けない）。</summary>
        private static string Signed(float v)
        {
            if (signedCache == null)
            {
                signedCache = new string[201];
                for (int i = 0; i <= 200; i++)
                    signedCache[i] = ((i - 100) / 100f).ToString("+0.00;-0.00;0.00", Inv);
            }
            if (float.IsNaN(v)) v = 0f;
            int k = Mathf.RoundToInt(Mathf.Clamp(v, -1f, 1f) * 100f) + 100;
            return signedCache[Mathf.Clamp(k, 0, 200)];
        }
    }
}
