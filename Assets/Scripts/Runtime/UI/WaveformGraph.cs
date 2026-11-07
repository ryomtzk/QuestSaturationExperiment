using QuestSaturation.Core;
using UnityEngine;
using UnityEngine.UI;

namespace QuestSaturation.UI
{
    /// <summary>
    /// 波形の折れ線と、0 の線までの塗りを描く Graphic。メッシュを作り直すのは曲線か大きさが変わったときだけ
    /// （再生位置の線と点は、別の Canvas の部品として動かす）。
    /// - テクスチャは使わない（ほかの図形と同じ白テクスチャ）。線の縁は、外側へ向けて透明になる細い帯を
    ///   頂点で足して滑らかにする（不透明なカードの上なので、パネルのアルファは 1 のまま）。
    /// - 区間ごとに四角形を描き、折れ目には円を置いてつなぐ。尖った角が伸びすぎることも、
    ///   長さ 0 の区間で計算が壊れることもない。
    /// - 塗りは区間が 0 をまたぐところで分けて、ねじれた四角形にならないようにする。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))] // この uGUI では Graphic を足しても CanvasRenderer が自動では付かない
    public sealed class WaveformGraph : MaskableGraphic
    {
        private const float Fringe = 1.5f;     // 縁の透明になる帯の幅（単位）
        private const float MinSpacing = 0.5f; // これより近い頂点はまとめる
        private const int JointSegments = 10;

        private readonly float[] xs = new float[PanelPresenter.MaxSamples];
        private readonly float[] ys = new float[PanelPresenter.MaxSamples];
        private readonly Vector2[] pts = new Vector2[PanelPresenter.MaxSamples];
        private int count;
        private float yMin = -1f;
        private float yMax = 1f;
        private float thickness = 5f;
        private Color lineColor = Color.white;
        private Color areaColor = Color.gray;

        public float YMin => yMin;
        public float YMax => yMax;

        /// <summary>曲線を差し替える。前と同じなら何もしない。</summary>
        public void SetCurve(float[] sx, float[] sy, int n, float min, float max)
        {
            n = Mathf.Clamp(n, 0, xs.Length);
            if (n == count && min == yMin && max == yMax && Same(sx, xs, n) && Same(sy, ys, n)) return;
            System.Array.Copy(sx, xs, n);
            System.Array.Copy(sy, ys, n);
            count = n;
            yMin = min;
            yMax = max;
            SetVerticesDirty();
        }

        public void SetStyle(Color line, Color area, float lineThickness)
        {
            if (line == lineColor && area == areaColor && Mathf.Approximately(lineThickness, thickness)) return;
            lineColor = line;
            areaColor = area;
            thickness = lineThickness;
            SetVerticesDirty();
        }

        /// <summary>グラフの座標 (x ∈ [0,1], y ∈ [yMin, yMax]) を、この RectTransform のローカル座標にする。</summary>
        public Vector2 ToLocal(float x, float y)
        {
            var r = rectTransform.rect;
            float ty = yMax > yMin ? (y - yMin) / (yMax - yMin) : 0.5f;
            return new Vector2(r.xMin + Mathf.Clamp01(x) * r.width, r.yMin + Mathf.Clamp01(ty) * r.height);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (count < 2) return;

            // 近すぎる頂点をまとめる（Wave from と Wave to が同じときの Step など）
            int m = 0;
            for (int i = 0; i < count; i++)
            {
                Vector2 p = ToLocal(xs[i], ys[i]);
                if (m > 0 && (p - pts[m - 1]).sqrMagnitude < MinSpacing * MinSpacing)
                {
                    if (i == count - 1) pts[m - 1] = p; // 最後の点は端に合わせる
                    continue;
                }
                pts[m++] = p;
            }
            if (m < 2) return;

            Color32 area = areaColor * color;
            Color32 line = lineColor * color;
            Color32 clear = new Color32(line.r, line.g, line.b, 0);
            float baseY = ToLocal(0f, Mathf.Clamp(0f, yMin, yMax)).y;

            // 0 の線までの塗り
            for (int i = 0; i < m - 1; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                if (b.x - a.x < 1e-3f) continue; // 縦の区間は面積が無い
                float da = a.y - baseY, db = b.y - baseY;
                if (da * db < 0f)
                {
                    float t = da / (da - db);
                    var c = new Vector2(Mathf.Lerp(a.x, b.x, t), baseY);
                    Tri(vh, a, c, new Vector2(a.x, baseY), area);
                    Tri(vh, c, b, new Vector2(b.x, baseY), area);
                }
                else
                {
                    Quad(vh, new Vector2(a.x, baseY), a, b, new Vector2(b.x, baseY), area, area, area, area);
                }
            }

            // 線：区間ごとの芯と、両側の縁
            float half = thickness * 0.5f;
            for (int i = 0; i < m - 1; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                Vector2 d = b - a;
                float len = d.magnitude;
                if (len < 1e-4f) continue;
                Vector2 n = new Vector2(-d.y, d.x) / len;
                Vector2 inner = n * half, outer = n * (half + Fringe);
                Quad(vh, a - inner, a + inner, b + inner, b - inner, line, line, line, line);
                Quad(vh, a + inner, a + outer, b + outer, b + inner, line, clear, clear, line);
                Quad(vh, a - outer, a - inner, b - inner, b - outer, clear, line, line, clear);
            }

            // 折れ目をつなぐ円（ほぼまっすぐなところは要らない）
            for (int i = 1; i < m - 1; i++)
            {
                Vector2 dIn = (pts[i] - pts[i - 1]).normalized;
                Vector2 dOut = (pts[i + 1] - pts[i]).normalized;
                if (Vector2.Dot(dIn, dOut) > 0.999f) continue;
                Disc(vh, pts[i], half, line);
            }
        }

        private static void Disc(VertexHelper vh, Vector2 c, float r, Color32 col)
        {
            int center = vh.currentVertCount;
            vh.AddVert(c, col, Vector4.zero);
            for (int k = 0; k < JointSegments; k++)
            {
                float a = 2f * Mathf.PI * k / JointSegments;
                vh.AddVert(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, col, Vector4.zero);
            }
            for (int k = 0; k < JointSegments; k++)
                vh.AddTriangle(center, center + 1 + k, center + 1 + (k + 1) % JointSegments);
        }

        private static void Tri(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color32 col)
        {
            int i = vh.currentVertCount;
            vh.AddVert(a, col, Vector4.zero);
            vh.AddVert(b, col, Vector4.zero);
            vh.AddVert(c, col, Vector4.zero);
            vh.AddTriangle(i, i + 1, i + 2);
        }

        private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d,
            Color32 ca, Color32 cb, Color32 cc, Color32 cd)
        {
            int i = vh.currentVertCount;
            vh.AddVert(a, ca, Vector4.zero);
            vh.AddVert(b, cb, Vector4.zero);
            vh.AddVert(c, cc, Vector4.zero);
            vh.AddVert(d, cd, Vector4.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i, i + 2, i + 3);
        }

        private static bool Same(float[] a, float[] b, int n)
        {
            if (a == null || b == null || a.Length < n || b.Length < n) return false;
            for (int i = 0; i < n; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
