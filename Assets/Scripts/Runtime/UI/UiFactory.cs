using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace QuestSaturation.UI
{
    /// <summary>
    /// uGUI の部品をコードで作る小さな道具。配置は LayoutGroup を使わず座標で決める
    /// （毎フレームのレイアウト計算をしないため。パネルの大きさは固定）。
    /// 座標は親の左上を原点に、右と下を正にとる。どの部品もレイの対象にしない。
    /// 使うシェーダーは UI/Default（図形）と TextMeshPro/Mobile/Distance Field（文字）の2つだけ。
    /// どちらも色をアルファ乗算済みで合成するので、不透明な背景の上ではアルファが 1 のまま保たれる。
    /// </summary>
    public static class UiFactory
    {
        /// <summary>
        /// 文字の太さの底上げ（TMP の _FaceDilate）。Quest 3 のアイバッファでは 1 単位が 1 ピクセルに満たないので、
        /// 標準の太さでは縦線が 1 ピクセルほどになり、頭の動きでちらつく。
        /// </summary>
        public const float TextDilate = 0.12f;

        private static TMP_FontAsset defaultFont;
        private static Material textMaterial;

        /// <summary>TMP Settings の既定フォント（LiberationSans SDF）。Essentials が無ければ null。</summary>
        public static TMP_FontAsset DefaultFont
        {
            get
            {
                if (defaultFont == null) defaultFont = TMP_Settings.defaultFontAsset;
                return defaultFont;
            }
        }

        /// <summary>
        /// パネルの文字すべてで共有するマテリアル（フォントのマテリアルを複製して太さだけ変えたもの）。
        /// 全員が同じものを使うので、まとめて描ける。fontMaterial（個別の複製）は使わない。
        /// </summary>
        public static Material TextMaterial
        {
            get
            {
                if (textMaterial != null) return textMaterial;
                var font = DefaultFont;
                if (font == null || font.material == null) return null;
                var m = new Material(font.material)
                {
                    name = font.material.name + " (QSE Panel)",
                    hideFlags = HideFlags.DontSave,
                };
                if (m.HasProperty(ShaderUtilities.ID_FaceDilate)) m.SetFloat(ShaderUtilities.ID_FaceDilate, TextDilate);
                ShaderUtilities.UpdateShaderRatios(m);
                return textMaterial = m;
            }
        }

        /// <summary>
        /// Canvas の共通設定。TMP に要る頂点の情報を足し、頂点色をガンマ空間のまま渡す
        /// （リニア空間のプロジェクトで、暗い灰色の段階が 8 ビットの量子化でつぶれないように）。
        /// </summary>
        public static void ConfigureCanvas(Canvas c)
        {
            c.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 |
                                          AdditionalCanvasShaderChannels.Normal |
                                          AdditionalCanvasShaderChannels.Tangent;
            c.vertexColorAlwaysGammaSpace = true;
        }

        /// <summary>入れ子の Canvas を足す。中の部品が変わっても、外側の Canvas を組み直さずに済む。</summary>
        public static Canvas AddSubCanvas(GameObject go)
        {
            var c = go.AddComponent<Canvas>();
            ConfigureCanvas(c);
            return c;
        }

        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent != null ? parent.gameObject.layer : 0;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>親の左上から (x, y) の位置に w × h で置く。</summary>
        public static RectTransform Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        /// <summary>親いっぱいに広げ、四辺を内側へ寄せる。</summary>
        public static RectTransform Stretch(RectTransform rt, float left = 0f, float top = 0f, float right = 0f, float bottom = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static UiShape CreateShape(string name, Transform parent, ShapeKind kind, Color color, float radius = 0f)
        {
            var rt = CreateRect(name, parent);
            var s = rt.gameObject.AddComponent<UiShape>();
            s.Kind = kind;
            s.Radius = radius;
            s.color = color;
            s.raycastTarget = false;
            s.maskable = false;
            return s;
        }

        /// <summary>角丸の矩形。radius は単位で指定する。</summary>
        public static UiShape CreatePanel(string name, Transform parent, Color color, float radius) =>
            CreateShape(name, parent, ShapeKind.RoundedRect, color, radius);

        /// <summary>角の無い矩形（線や目盛り）。</summary>
        public static UiShape CreateSolid(string name, Transform parent, Color color) =>
            CreateShape(name, parent, ShapeKind.Rect, color);

        public static TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, FontStyles style = FontStyles.Normal)
        {
            var rt = CreateRect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (DefaultFont != null) t.font = DefaultFont;
            if (TextMaterial != null) t.fontSharedMaterial = TextMaterial;
            t.text = text;
            t.fontSize = size;
            t.enableAutoSizing = false;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            t.maskable = false;
            t.richText = false; // タグも絵文字も解釈しない（絵文字のシェーダーはパネルのアルファを下げる）
            t.extraPadding = false;
            t.isTextObjectScaleStatic = true; // パネルの拡大縮小で SDF を作り直さない
            return t;
        }

        /// <summary>
        /// 文字を変える。同じなら何もしない（メッシュを作り直さない）。
        /// TMP の書式つき SetText（"{0:1}" など）を使う部品には使わないこと。実機ではその後の .text への代入が
        /// 「変わっていない」と判断されて捨てられることがある（エディタでは起きない）。
        /// </summary>
        public static void SetText(TextMeshProUGUI t, string s)
        {
            if (t != null && !string.Equals(t.text, s)) t.text = s;
        }

        public static void SetColor(Graphic g, Color c)
        {
            if (g != null && g.color != c) g.color = c;
        }

        public static void SetActive(Component c, bool active)
        {
            if (c != null && c.gameObject.activeSelf != active) c.gameObject.SetActive(active);
        }

        public static void SetActive(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active) go.SetActive(active);
        }

        /// <summary>
        /// 指数的に目標へ近づける（フレームレートに依存しない）。snap より近づいたら目標に合わせて止める
        /// （いつまでも少しずつ動いて、毎フレーム Canvas を組み直すことにならないように）。
        /// </summary>
        public static float Approach(float current, float target, float rate, float dt, float snap)
        {
            if (dt <= 0f) return current;
            float k = 1f - Mathf.Exp(-rate * dt);
            float next = current + (target - current) * k;
            return Mathf.Abs(target - next) < snap ? target : next;
        }

        /// <summary>文字の幅（単位）。配置の計算用。</summary>
        public static float Measure(TextMeshProUGUI t, string s) => s.Length == 0 ? 0f : Mathf.Ceil(t.GetPreferredValues(s).x);
    }
}
