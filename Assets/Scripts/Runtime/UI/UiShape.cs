using UnityEngine;
using UnityEngine.UI;

namespace QuestSaturation.UI
{
    public enum ShapeKind
    {
        Rect,
        RoundedRect, // 角の半径は Radius（単位）
        Circle,      // 矩形に内接する楕円
        Play,        // 右向きの三角形
        Pause,       // 縦棒2本
        Stop,        // 正方形
        ChevronLeft, // 左向きの三角形（値を下げる）
        ChevronRight,
        Menu,        // 横棒3本（左コントローラーのメニューボタン）
    }

    /// <summary>
    /// 図形を三角形で描く Graphic。テクスチャを持たない（uGUI の既定の白テクスチャを使う）ので、
    /// どの図形も同じマテリアルとテクスチャになり、まとめて1回で描ける。
    /// 縁は MSAA（Quest の URP 設定で 4x）で滑らかになる。テクスチャの縁を使わないので、
    /// 遠くから見て縮小されてもちらつかず、パネルの内側のアルファも 1 のまま変わらない。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))] // この uGUI では Graphic を足しても CanvasRenderer が自動では付かない
    public sealed class UiShape : MaskableGraphic
    {
        [SerializeField] private ShapeKind kind;
        [SerializeField] private float radius;

        public ShapeKind Kind
        {
            get => kind;
            set
            {
                if (kind == value) return;
                kind = value;
                SetVerticesDirty();
            }
        }

        /// <summary>角丸の半径（単位）。RoundedRect だけが使う。</summary>
        public float Radius
        {
            get => radius;
            set
            {
                if (Mathf.Approximately(radius, value)) return;
                radius = value;
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            if (!(r.width > 0f) || !(r.height > 0f)) return;
            Color32 c = color;

            switch (kind)
            {
                case ShapeKind.Rect:
                    Quad(vh, r.xMin, r.yMin, r.xMax, r.yMax, c);
                    break;

                case ShapeKind.RoundedRect:
                    RoundedRect(vh, r, radius, c);
                    break;

                case ShapeKind.Circle:
                    Ellipse(vh, r, c);
                    break;

                case ShapeKind.Play:
                    Triangle(vh,
                        P(r, 0.20f, 0.08f), P(r, 0.20f, 0.92f), P(r, 0.92f, 0.5f), c);
                    break;

                case ShapeKind.Pause:
                    Quad(vh, X(r, 0.18f), Y(r, 0.08f), X(r, 0.40f), Y(r, 0.92f), c);
                    Quad(vh, X(r, 0.60f), Y(r, 0.08f), X(r, 0.82f), Y(r, 0.92f), c);
                    break;

                case ShapeKind.Stop:
                    Quad(vh, X(r, 0.14f), Y(r, 0.14f), X(r, 0.86f), Y(r, 0.86f), c);
                    break;

                case ShapeKind.ChevronLeft:
                    Triangle(vh, P(r, 1f, 0f), P(r, 1f, 1f), P(r, 0f, 0.5f), c);
                    break;

                case ShapeKind.ChevronRight:
                    Triangle(vh, P(r, 0f, 1f), P(r, 0f, 0f), P(r, 1f, 0.5f), c);
                    break;

                case ShapeKind.Menu:
                {
                    // 3 本の棒と 2 つの隙間を同じ太さにする（高さ 26 で 5.2 単位。動的解像度の下限でも潰れない）
                    const float bar = 0.2f;
                    for (int i = 0; i < 3; i++)
                    {
                        float top = i * 0.4f;
                        Quad(vh, r.xMin, Y(r, 1f - top - bar), r.xMax, Y(r, 1f - top), c);
                    }
                    break;
                }
            }
        }

        private static float X(Rect r, float u) => r.xMin + r.width * u;
        private static float Y(Rect r, float v) => r.yMin + r.height * v;
        private static Vector2 P(Rect r, float u, float v) => new Vector2(X(r, u), Y(r, v));

        private static void Quad(VertexHelper vh, float x0, float y0, float x1, float y1, Color32 c)
        {
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(x0, y0), c, Vector4.zero);
            vh.AddVert(new Vector3(x0, y1), c, Vector4.zero);
            vh.AddVert(new Vector3(x1, y1), c, Vector4.zero);
            vh.AddVert(new Vector3(x1, y0), c, Vector4.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

        private static void Triangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c2, Color32 c)
        {
            int i = vh.currentVertCount;
            vh.AddVert(a, c, Vector4.zero);
            vh.AddVert(b, c, Vector4.zero);
            vh.AddVert(c2, c, Vector4.zero);
            vh.AddTriangle(i, i + 1, i + 2);
        }

        /// <summary>角丸の矩形。凸なので、中心から扇形に三角形を張る。</summary>
        private static void RoundedRect(VertexHelper vh, Rect r, float radius, Color32 c)
        {
            float rad = Mathf.Min(radius, r.width * 0.5f, r.height * 0.5f);
            if (rad < 0.25f)
            {
                Quad(vh, r.xMin, r.yMin, r.xMax, r.yMax, c);
                return;
            }

            int seg = Mathf.Clamp(Mathf.CeilToInt(rad / 2.5f), 3, 16);
            int center = vh.currentVertCount;
            vh.AddVert(r.center, c, Vector4.zero);

            // 右上 → 左上 → 左下 → 右下 の順に角を回る
            Corner(vh, new Vector2(r.xMax - rad, r.yMax - rad), rad, 0f, seg, c);
            Corner(vh, new Vector2(r.xMin + rad, r.yMax - rad), rad, 90f, seg, c);
            Corner(vh, new Vector2(r.xMin + rad, r.yMin + rad), rad, 180f, seg, c);
            Corner(vh, new Vector2(r.xMax - rad, r.yMin + rad), rad, 270f, seg, c);

            int count = vh.currentVertCount - center - 1;
            for (int k = 0; k < count; k++)
                vh.AddTriangle(center, center + 1 + k, center + 1 + (k + 1) % count);
        }

        private static void Corner(VertexHelper vh, Vector2 o, float rad, float startDeg, int seg, Color32 c)
        {
            for (int k = 0; k <= seg; k++)
            {
                float a = (startDeg + 90f * k / seg) * Mathf.Deg2Rad;
                vh.AddVert(o + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad, c, Vector4.zero);
            }
        }

        private static void Ellipse(VertexHelper vh, Rect r, Color32 c)
        {
            float rx = r.width * 0.5f, ry = r.height * 0.5f;
            int seg = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(rx, ry) * 1.5f), 16, 48);
            int center = vh.currentVertCount;
            vh.AddVert(r.center, c, Vector4.zero);
            for (int k = 0; k < seg; k++)
            {
                float a = 2f * Mathf.PI * k / seg;
                vh.AddVert(r.center + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry), c, Vector4.zero);
            }
            for (int k = 0; k < seg; k++)
                vh.AddTriangle(center, center + 1 + k, center + 1 + (k + 1) % seg);
        }
    }
}
