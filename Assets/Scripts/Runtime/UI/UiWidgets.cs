using QuestSaturation.Core;
using TMPro;
using UnityEngine;
using static QuestSaturation.UI.UiFactory;

namespace QuestSaturation.UI
{
    /// <summary>
    /// 値のバー。トラック、起点から値までの塗り、既定値の目盛り、つまみ。
    /// 位置はアンカーで動かすので、値が変わってもレイアウト計算はしない。動きは目標に着いたら止まる。
    /// </summary>
    public sealed class ValueBar
    {
        private const float Rate = 24f;
        private const float Snap = 0.002f; // バーの幅に対する割合（150 単位で 0.3 単位）
        private const float TrackHeight = 10f;
        private const float KnobSize = 24f;

        private readonly UiShape fill;
        private readonly UiShape knob;
        private readonly RectTransform fillRt;
        private readonly RectTransform knobRt;
        private readonly RectTransform tickRt;

        private float shownMin, shownMax, shownValue;
        private float targetMin, targetMax, targetValue;
        private bool hasValue;

        public ValueBar(Transform parent, float x, float y, float w, float h)
        {
            var root = Place(CreateRect("Bar", parent), x, y, w, h);
            // つまみがはみ出さないよう、トラックはつまみの半径ぶん内側に置く
            var area = Stretch(CreateRect("Area", root), KnobSize * 0.5f, 0f, KnobSize * 0.5f, 0f);

            var track = CreatePanel("Track", area, UiTheme.Track, TrackHeight * 0.5f);
            var trackRt = track.rectTransform;
            trackRt.anchorMin = new Vector2(0f, 0.5f);
            trackRt.anchorMax = new Vector2(1f, 0.5f);
            trackRt.sizeDelta = new Vector2(KnobSize, TrackHeight); // 両端をつまみの半径ぶん伸ばす

            var tick = CreateSolid("Default", area, UiTheme.Tick);
            tickRt = tick.rectTransform;
            tickRt.sizeDelta = new Vector2(4f, 26f);

            fill = CreatePanel("Fill", area, UiTheme.Fill, TrackHeight * 0.5f);
            fillRt = fill.rectTransform;

            knob = CreateShape("Knob", area, ShapeKind.Circle, UiTheme.Knob);
            knobRt = knob.rectTransform;
            knobRt.sizeDelta = new Vector2(KnobSize, KnobSize);
        }

        public void Set(BarGeometry g, bool snap)
        {
            targetMin = g.FillMin;
            targetMax = g.FillMax;
            targetValue = g.Value;
            SetAnchorX(tickRt, g.Default);
            if (snap || !hasValue)
            {
                shownMin = targetMin;
                shownMax = targetMax;
                shownValue = targetValue;
                hasValue = true;
                Layout();
            }
        }

        public void SetStyle(bool focused, bool dim)
        {
            SetColor(fill, dim ? UiTheme.FillDim : focused ? UiTheme.Fill : UiTheme.FillIdle);
            SetColor(knob, dim ? UiTheme.FillDim : focused ? UiTheme.Knob : UiTheme.FillIdle);
            var scale = Vector3.one * (focused ? 1f : 0.8f);
            if (knobRt.localScale != scale) knobRt.localScale = scale;
        }

        public bool Animating => shownMin != targetMin || shownMax != targetMax || shownValue != targetValue;

        public void Animate(float dt)
        {
            if (!Animating) return;
            shownMin = Approach(shownMin, targetMin, Rate, dt, Snap);
            shownMax = Approach(shownMax, targetMax, Rate, dt, Snap);
            shownValue = Approach(shownValue, targetValue, Rate, dt, Snap);
            Layout();
        }

        private void Layout()
        {
            fillRt.anchorMin = new Vector2(shownMin, 0.5f);
            fillRt.anchorMax = new Vector2(shownMax, 0.5f);
            // 0 幅でも丸い点として見えるよう、トラックの高さぶん幅を足す
            fillRt.sizeDelta = new Vector2(TrackHeight, TrackHeight);
            fillRt.anchoredPosition = Vector2.zero;
            SetAnchorX(knobRt, shownValue);
        }

        private static void SetAnchorX(RectTransform rt, float x)
        {
            var a = new Vector2(x, 0.5f);
            if (rt.anchorMin == a && rt.anchorMax == a && rt.anchoredPosition == Vector2.zero) return;
            rt.anchorMin = a;
            rt.anchorMax = a;
            rt.anchoredPosition = Vector2.zero;
        }
    }

    /// <summary>
    /// 選択肢をすべて並べ、選んでいるものを反転（明るい下地に暗い文字）して示す。下地は選択肢の間を滑って動く。
    /// 行を選んでいるときは下地をさらに明るくする（行の選択と選択肢の選択を、別の見た目で示す）。
    /// </summary>
    public sealed class SegmentedControl
    {
        private const float Rate = 22f;
        private const float Snap = 0.01f;
        private const float Pad = 4f;

        private readonly RectTransform highlightRt;
        private readonly UiShape highlight;
        private readonly TextMeshProUGUI[] labels;
        private readonly float segment;
        private float shown = -1f;
        private int target;
        private bool focused;
        private bool dim;
        private bool styled;

        public SegmentedControl(Transform parent, float x, float y, float w, float h, string[] options)
        {
            var root = Place(CreateRect("Segmented", parent), x, y, w, h);
            Stretch(CreatePanel("Track", root, UiTheme.SegmentTrack, UiTheme.CornerChip + Pad).rectTransform);

            segment = (w - Pad * 2f) / options.Length;
            highlight = CreatePanel("Highlight", root, UiTheme.SelectionIdle, UiTheme.CornerChip);
            highlightRt = Place(highlight.rectTransform, Pad, Pad, segment, h - Pad * 2f);

            labels = new TextMeshProUGUI[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                var t = CreateText("Option " + options[i], root, options[i], UiTheme.SizeSecondary, UiTheme.TextSecondary, TextAlignmentOptions.Center);
                Place(t.rectTransform, Pad + segment * i, 0f, segment, h);
                labels[i] = t;
            }
        }

        public void Set(int index, bool snap)
        {
            target = Mathf.Clamp(index, 0, labels.Length - 1);
            if (snap || shown < 0f)
            {
                shown = target;
                Layout();
            }
        }

        public void SetStyle(bool isFocused, bool isDim)
        {
            if (styled && isFocused == focused && isDim == dim) return;
            styled = true;
            focused = isFocused;
            dim = isDim;
            SetColor(highlight, focused ? UiTheme.Selection : UiTheme.SelectionIdle);
            Layout();
        }

        public bool Animating => shown != target;

        public void Animate(float dt)
        {
            if (!Animating) return;
            shown = Approach(shown, target, Rate, dt, Snap);
            Layout();
        }

        private void Layout()
        {
            highlightRt.anchoredPosition = new Vector2(Pad + segment * shown, -Pad);
            Color on = UiTheme.TextOnLight;
            Color off = dim ? UiTheme.TextDim : UiTheme.TextSecondary;
            for (int i = 0; i < labels.Length; i++)
            {
                // 下地が重なっている割合で文字色を混ぜる（滑っている間も読める色にする）
                float k = Mathf.Clamp01(1f - Mathf.Abs(shown - i));
                SetColor(labels[i], Color.Lerp(off, on, k));
                var style = k > 0.5f ? FontStyles.Bold : FontStyles.Normal;
                if (labels[i].fontStyle != style) labels[i].fontStyle = style;
            }
        }
    }

    /// <summary>
    /// 角丸の小さな札（AUTO、PENDING、REC、再生状態など）。左に点か図形を付けられる。
    /// 幅は文字に合わせる。alignRight なら右端を、そうでなければ左端を固定する。
    /// </summary>
    public sealed class Chip
    {
        private const float PadL = 12f, PadR = 14f, MarkGap = 8f;

        private readonly RectTransform root;
        private readonly UiShape bg;
        private readonly UiShape mark;
        private readonly TextMeshProUGUI text;
        private readonly float anchorX;
        private readonly bool alignRight;
        private readonly float markSize;
        private string lastText;

        public Chip(Transform parent, float x, float y, float h, bool alignRight, float textSize, float markSize = 0f,
            FontStyles style = FontStyles.Bold)
        {
            anchorX = x;
            this.alignRight = alignRight;
            this.markSize = markSize;
            root = Place(CreateRect("Chip", parent), x, y, 10f, h);
            bg = CreatePanel("Bg", root, UiTheme.Keycap, UiTheme.CornerChip);
            Stretch(bg.rectTransform);
            if (markSize > 0f)
            {
                mark = CreateShape("Mark", root, ShapeKind.Circle, UiTheme.TextSecondary);
                Place(mark.rectTransform, PadL, (h - markSize) * 0.5f, markSize, markSize);
            }
            text = CreateText("Text", root, "", textSize, UiTheme.TextSecondary, TextAlignmentOptions.MidlineLeft, style);
            Place(text.rectTransform, TextX, 0f, 10f, h);
        }

        private float TextX => markSize > 0f ? PadL + markSize + MarkGap : PadL;

        public GameObject GameObject => root.gameObject;

        public float Width => root.sizeDelta.x;

        public void Set(string label, Color background, Color foreground)
        {
            if (!string.Equals(label, lastText))
            {
                lastText = label;
                text.text = label;
                float tw = Measure(text, label) + 2f;
                float w = TextX + tw + PadR;
                text.rectTransform.sizeDelta = new Vector2(tw, root.sizeDelta.y);
                root.sizeDelta = new Vector2(w, root.sizeDelta.y);
                root.anchoredPosition = new Vector2(alignRight ? anchorX - w : anchorX, root.anchoredPosition.y);
            }
            SetColor(bg, background);
            SetColor(text, foreground);
        }

        /// <summary>左の印（点、再生の三角など）。</summary>
        public void SetMark(ShapeKind kind, Color color)
        {
            if (mark == null) return;
            mark.Kind = kind;
            SetColor(mark, color);
        }

        public void SetVisible(bool visible) => SetActive(root.gameObject, visible);
    }

    /// <summary>
    /// ヘッダー右の状態表示（パススルー、ログ、電池、時計）。右から順に詰めて並べ、文字が変わったときだけ並べ直す。
    /// </summary>
    public sealed class StatusStrip
    {
        private const float Gap = 30f;
        private const float DotSize = 14f;
        private const float DotGap = 10f;

        private sealed class Item
        {
            public UiShape Dot;
            public TextMeshProUGUI Text;
            public float Width;
        }

        private readonly Item[] items;
        private readonly float right;
        private readonly float height;
        private bool dirty = true;

        /// <summary>いちばん左に出ている項目の左端（親の左端から）。右から詰めて並べるので、項目が長いと左へ伸びる。</summary>
        public float Left { get; private set; }

        public StatusStrip(Transform parent, float right, float y, float h, float textSize, params bool[] withDot)
        {
            this.right = right;
            height = h;
            var root = Place(CreateRect("Status", parent), 0f, y, right, h);
            items = new Item[withDot.Length];
            for (int i = 0; i < withDot.Length; i++)
            {
                var item = new Item
                {
                    Text = CreateText("Item " + i, root, "", textSize, UiTheme.TextSecondary, TextAlignmentOptions.MidlineLeft),
                };
                Place(item.Text.rectTransform, 0f, 0f, 10f, h);
                if (withDot[i])
                {
                    item.Dot = CreateShape("Dot " + i, root, ShapeKind.Circle, UiTheme.TextTertiary);
                    Place(item.Dot.rectTransform, 0f, (h - DotSize) * 0.5f, DotSize, DotSize);
                }
                items[i] = item;
            }
        }

        public void Set(int index, string label, Color textColor, Color dotColor)
        {
            var item = items[index];
            if (!string.Equals(item.Text.text, label))
            {
                item.Text.text = label;
                item.Width = Measure(item.Text, label) + 2f;
                dirty = true;
            }
            SetColor(item.Text, textColor);
            if (item.Dot != null) SetColor(item.Dot, dotColor);
            if (dirty) Layout();
        }

        private void Layout()
        {
            dirty = false;
            float x = right;
            for (int i = items.Length - 1; i >= 0; i--)
            {
                var item = items[i];
                bool show = item.Width > 2f;
                SetActive(item.Text, show);
                if (item.Dot != null) SetActive(item.Dot, show);
                if (!show) continue;
                x -= item.Width;
                Place(item.Text.rectTransform, x, 0f, item.Width, height);
                if (item.Dot != null)
                {
                    x -= DotGap + DotSize;
                    Place(item.Dot.rectTransform, x, (height - DotSize) * 0.5f, DotSize, DotSize);
                }
                x -= Gap;
            }
            Left = x + Gap;
        }
    }

    /// <summary>
    /// 帯を重ねるときの出し方。先に帯（不透明な背景）を出して下の文字を隠し、それから帯の中の文字を出す
    /// （消すときは逆）。途中のコマで、下の文字と帯の文字が二重に見えることがない。
    /// </summary>
    public static class UiFade
    {
        public static CanvasGroup AddGroup(GameObject go)
        {
            var g = go.AddComponent<CanvasGroup>();
            g.interactable = false;
            g.blocksRaycasts = false;
            return g;
        }

        /// <summary>t（0〜1）の前半で帯、後半で中身を出す。</summary>
        public static void Staged(CanvasGroup band, CanvasGroup content, float t)
        {
            float b = Mathf.Clamp01(2f * t);
            float c = Mathf.Clamp01(2f * t - 1f);
            if (band.alpha != b) band.alpha = b;
            if (content.alpha != c) content.alpha = c;
        }
    }

    /// <summary>
    /// 長押しの帯。ヘッダーの上に不透明な帯を重ね、左から進み具合を塗る。
    /// 現れるとき 80 ms、離したとき 120 ms で切り替える（不透明なヘッダーの上なので、途中でも透けない）。
    /// </summary>
    public sealed class HoldBar
    {
        private const float FadeIn = 0.08f;
        private const float FadeOut = 0.12f;

        private readonly RectTransform root;
        private readonly CanvasGroup group;
        private readonly CanvasGroup content;
        private readonly RectTransform fillRt;
        private readonly TextMeshProUGUI title;
        private readonly TextMeshProUGUI caption;
        private readonly float width;
        private float alpha;
        private bool active;

        public HoldBar(Transform parent, float x, float y, float w, float h)
        {
            width = w;
            root = Place(CreateRect("Hold", parent), x, y, w, h);
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            Stretch(CreatePanel("Bg", root, UiTheme.SurfaceRaised, 16f).rectTransform);
            var c = Stretch(CreateRect("Content", root));
            content = UiFade.AddGroup(c.gameObject);
            var fill = CreatePanel("Progress", c, UiTheme.HoldFill, 16f);
            fillRt = Place(fill.rectTransform, 0f, 0f, 0f, h);
            title = CreateText("Title", c, "", UiTheme.SizeBody, UiTheme.TextPrimary, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            Place(title.rectTransform, 28f, 0f, w * 0.5f - 28f, h);
            caption = CreateText("Caption", c, "", UiTheme.SizeSecondary, UiTheme.TextPrimary, TextAlignmentOptions.MidlineRight);
            Place(caption.rectTransform, w * 0.5f, 0f, w * 0.5f - 28f, h);
            SetActive(root.gameObject, false);
        }

        public bool Visible => root.gameObject.activeSelf;

        /// <summary>進み具合（0〜1）。0 で隠し始める（最後の進み具合のまま薄れる）。</summary>
        public void Set(float progress, string titleText, string captionText)
        {
            active = progress > 0f;
            if (!active) return;
            SetText(title, titleText);
            SetText(caption, captionText);
            float w = Mathf.Round(width * Mathf.Clamp01(progress));
            if (!Mathf.Approximately(fillRt.sizeDelta.x, w)) fillRt.sizeDelta = new Vector2(w, fillRt.sizeDelta.y);
            if (!Visible)
            {
                alpha = 0f;
                UiFade.Staged(group, content, 0f);
                SetActive(root.gameObject, true);
            }
        }

        public void Animate(float dt)
        {
            if (!Visible) return;
            float target = active ? 1f : 0f;
            if (alpha != target)
            {
                alpha = Mathf.MoveTowards(alpha, target, dt / (active ? FadeIn : FadeOut));
                UiFade.Staged(group, content, alpha);
            }
            if (!active && alpha <= 0f) SetActive(root.gameObject, false);
        }

        public void Clear()
        {
            active = false;
            alpha = 0f;
            UiFade.Staged(group, content, 0f);
            SetActive(root.gameObject, false);
        }

        /// <summary>動きを飛ばして、いまの目標の見た目にする（プレビュー用）。</summary>
        public void Finish()
        {
            alpha = active ? 1f : 0f;
            UiFade.Staged(group, content, alpha);
            SetActive(root.gameObject, active);
        }
    }

    /// <summary>
    /// 短い通知。フッターの説明の行に、不透明な帯として重ねる（ほかの内容は隠さない）。
    /// 新しい通知は前のものを置き換える。
    /// </summary>
    public sealed class ToastView
    {
        private const float FadeIn = 0.15f;
        private const float FadeOut = 0.25f;

        private readonly RectTransform root;
        private readonly CanvasGroup group;
        private readonly CanvasGroup content;
        private readonly UiShape dot;
        private readonly TextMeshProUGUI text;
        private float age;
        private float duration;

        public ToastView(Transform parent, float x, float y, float w, float h)
        {
            root = Place(CreateRect("Toast", parent), x, y, w, h);
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            Stretch(CreatePanel("Bg", root, UiTheme.SurfaceRaised, 12f).rectTransform);
            var c = Stretch(CreateRect("Content", root));
            content = UiFade.AddGroup(c.gameObject);
            dot = CreateShape("Dot", c, ShapeKind.Circle, UiTheme.TextSecondary);
            Place(dot.rectTransform, 20f, (h - 16f) * 0.5f, 16f, 16f);
            text = CreateText("Text", c, "", UiTheme.SizeSecondary, UiTheme.TextPrimary);
            Place(text.rectTransform, 50f, 0f, w - 70f, h);
            SetActive(root.gameObject, false);
        }

        public bool Visible => root.gameObject.activeSelf;

        public string Message => text.text;

        public TextMeshProUGUI Text => text;

        /// <summary>
        /// 通知を出す。出ている通知は置き換える（どれを出すかは呼ぶ側が決める）。ログの停止などの警告は、
        /// 通知が消えてもヘッダーとセッションカードに出たままなので、ここで残す必要はない。
        /// </summary>
        public void Show(string message, StatusTone tone, float seconds)
        {
            SetText(text, message);
            SetColor(dot, tone == StatusTone.Neutral ? UiTheme.TextSecondary : UiTheme.ToneColor(tone));
            // 表示中に次が来たら、出し直さずに中身だけ替えて時間を延ばす
            age = Visible ? Mathf.Min(age, FadeIn) : 0f;
            duration = Mathf.Max(0.5f, seconds);
            SetActive(root.gameObject, true);
            Apply();
        }

        public void Clear()
        {
            age = 0f;
            duration = 0f;
            SetActive(root.gameObject, false);
        }

        public void Animate(float dt)
        {
            if (!Visible) return;
            age += dt;
            if (age >= duration + FadeOut)
            {
                Clear();
                return;
            }
            Apply();
        }

        private void Apply()
        {
            float a = age < FadeIn ? age / FadeIn : age > duration ? 1f - (age - duration) / FadeOut : 1f;
            UiFade.Staged(group, content, Mathf.Clamp01(a));
        }
    }
}
