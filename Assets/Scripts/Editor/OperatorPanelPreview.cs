using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using QuestSaturation.Core;
using QuestSaturation.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using MenuItem = QuestSaturation.Core.MenuItem;
using Object = UnityEngine.Object;

namespace QuestSaturation.Editor
{
    /// <summary>
    /// オペレーターパネルを、見本の状態で PNG に書き出し、あわせて次のことを調べる（README の画像と、見た目の確認用）。
    /// - パネルの内側のアルファが 1 のままか（パススルーはアイバッファのアルファで合成されるので、
    ///   1 より小さいところからはパススルーが透けて見える）。描画の経路がアルファを捨てていないかも、
    ///   パネルの外（アルファ 0 のはず）と、外に置いた半透明の見本（アルファ 0.5 のはず）で確かめる。
    /// - 文字が欄からはみ出していないか、ほかの部品と重なっていないか、固定アトラスにない字を使っていないか。
    /// - UI/Default と TextMeshPro/Mobile/Distance Field 以外のシェーダーを使っていないか。
    /// 検査はすべての見本で行う。既定では README で使う見本だけを、資料用（1 単位 = 1 ピクセル）の画像として docs/images に書く。
    /// -previewOut を付けると、そのフォルダーにすべての見本を書き、Quest 3 のアイバッファ相当の密度の画像も加える
    /// （1 単位 ≈ 0.49 ピクセル = 動的解像度 1.0 → _device、≈ 0.34 ピクセル = 下限 0.7 → _floor。見やすいよう 2 倍に拡大して保存する）。
    /// batchmode（描画するので -nographics は付けない）：
    ///   -executeMethod QuestSaturation.Editor.OperatorPanelPreview.RunBatch [-previewOut &lt;dir&gt;]
    /// 終了コード：0 = 問題なし、1 = 検査で問題あり、2 = GPU が無い（-nographics で起動した）。
    /// </summary>
    public static class OperatorPanelPreview
    {
        public const string DefaultOutDir = "docs/images";
        private const int PreviewLayer = 31;
        private const float MetersPerUnit = 0.0005f;
        private const float MarginUnits = 24f;
        private const float SwatchUnits = 14f;

        private static readonly (string suffix, float pxPerUnit, int upscale)[] Densities =
        {
            ("", 1f, 1),
            ("_device", 0.49f, 2),
            ("_floor", 0.34f, 2),
        };

        private static readonly string[] AllowedShaders = { "UI/Default", "TextMeshPro/Mobile/Distance Field" };

        /// <summary>資料用の画像を重ねる背景（パススルーの代わりの中間の灰色）。</summary>
        private static readonly Color32 DocBackground = new Color32(0x6E, 0x6E, 0x6E, 0xFF);

        [UnityEditor.MenuItem("QuestSaturation/Capture Operator Panel Previews")]
        private static void Menu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("QuestSaturation", "Exit Play mode first, then run this again.", "OK");
                return;
            }
            // 見本は一時的なシーンを足して描く。保存していない無題のシーンが開いていると、シーンを足せない
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (!string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path)) continue;
                EditorUtility.DisplayDialog("QuestSaturation", "Save or close the untitled scene first, then run this again.", "OK");
                return;
            }
            var lines = new List<string>();
            int code;
            try
            {
                code = Capture(DefaultOutDir, additive: true, allDensities: false, lines);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("QuestSaturation", "The preview failed:\n\n" + e.Message, "OK");
                return;
            }
            EditorUtility.DisplayDialog("QuestSaturation", (code == 0 ? "OK\n\n" : "Problems found\n\n") + string.Join("\n", lines), "OK");
        }

        public static void RunBatch()
        {
            int code;
            var lines = new List<string>();
            try
            {
                string outDir = ArgValue("-previewOut");
                code = Capture(outDir ?? DefaultOutDir, additive: false, allDensities: outDir != null, lines);
            }
            catch (Exception e)
            {
                lines.Add("preview failed: " + e);
                code = 1;
            }
            foreach (var l in lines)
            {
                if (code == 0) Debug.Log("[QSE-TOOL] preview: " + l);
                else Debug.LogWarning("[QSE-TOOL] preview: " + l);
            }
            Debug.Log("[QSE-TOOL] preview finished with exit code " + code);
            EditorApplication.Exit(code);
        }

        /// <summary>見本の状態を順に描いて保存し、検査する。戻り値は終了コード。lines に結果を足す。</summary>
        public static int Capture(string outDir, bool additive, bool allDensities, List<string> lines)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                lines.Add("the preview needs a GPU (remove -nographics)");
                return 2;
            }

            Directory.CreateDirectory(outDir);
            var problems = new List<string>();

            bool asyncBefore = ShaderUtil.allowAsyncCompilation;
            int qualityBefore = QualitySettings.GetQualityLevel();
            UniversalRenderPipelineAsset urp = null;
            float scaleBefore = 1f;
            int mobile = -1, aaBefore = 0;

            Scene scene = default;
            Scene activeBefore = SceneManager.GetActiveScene();
            GameObject host = null, cameraGo = null, swatchGo = null;
            var targets = new List<Object>();
            try
            {
                scene = additive
                    ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive)
                    : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                // 作る GameObject が、開いている利用者のシーンに一瞬でも入らないようにする
                if (additive) SceneManager.SetActiveScene(scene);

                // 端末と同じ品質設定（Android の既定は Mobile）で描く。拡大率は 1 にする（保存はしない）
                ShaderUtil.allowAsyncCompilation = false;
                mobile = Array.IndexOf(QualitySettings.names, "Mobile");
                if (mobile >= 0)
                {
                    QualitySettings.SetQualityLevel(mobile, true);
                    // URP は描くときに QualitySettings.antiAliasing を自分の MSAA に書き換え、エディタはそれを保存してしまう。
                    // 終わったら元に戻して、ProjectSettings に差分を残さない
                    aaBefore = QualitySettings.antiAliasing;
                }
                urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (urp != null)
                {
                    scaleBefore = urp.renderScale;
                    lines.Add(string.Format("pipeline {0} (render scale {1:0.##} -> 1, HDR {2})", urp.name, scaleBefore, urp.supportsHDR));
                    urp.renderScale = 1f;
                }

                host = new GameObject("[QSE] Panel Preview") { layer = PreviewLayer, hideFlags = HideFlags.DontSave };
                SceneManager.MoveGameObjectToScene(host, scene);
                cameraGo = new GameObject("[QSE] Preview Camera") { layer = PreviewLayer, hideFlags = HideFlags.DontSave };
                SceneManager.MoveGameObjectToScene(cameraGo, scene);

                var view = OperatorPanelView.Create(host.transform, MetersPerUnit);
                view.gameObject.SetActive(true);
                view.SetVersion("v" + Application.version);
                view.transform.position = Vector3.zero;

                swatchGo = CreateSwatch(scene);
                var cam = CreateCamera(cameraGo);
                CheckOverlapProbe(view, problems);
                CheckNoticeFit(view, problems, lines);

                foreach (var (name, docs, settled, setup) in Cases)
                {
                    var state = Baseline();
                    view.ClearTransient();
                    setup(state, view);
                    CheckContent(view, name, problems);
                    if (settled) CheckOverlaps(view, name, problems);

                    foreach (var (suffix, density, upscale) in Densities)
                    {
                        bool save = allDensities || (docs && density == 1f);
                        if (density != 1f && !save) continue;
                        var pixels = Render(cam, view, density, targets, out int w, out int h);
                        if (density == 1f) CheckAlpha(pixels, w, h, name, problems, lines);
                        if (!save) continue;
                        ClearMargin(pixels, w, h, density);
                        var flat = Flatten(pixels, DocBackground);
                        string path = Path.Combine(outDir, "operator_panel_" + name + suffix + ".png");
                        File.WriteAllBytes(path, EncodePng(flat, w, h, upscale));
                        lines.Add(path);
                    }
                }
            }
            finally
            {
                foreach (var t in targets)
                {
                    if (t is RenderTexture rt) rt.Release();
                    Object.DestroyImmediate(t);
                }
                if (host != null) Object.DestroyImmediate(host);
                if (cameraGo != null) Object.DestroyImmediate(cameraGo);
                if (swatchGo != null) Object.DestroyImmediate(swatchGo);
                if (urp != null) urp.renderScale = scaleBefore;
                if (mobile >= 0)
                {
                    QualitySettings.SetQualityLevel(mobile, false);
                    if (QualitySettings.antiAliasing != aaBefore) QualitySettings.antiAliasing = aaBefore;
                }
                QualitySettings.SetQualityLevel(qualityBefore, true);
                ShaderUtil.allowAsyncCompilation = asyncBefore;
                if (additive && scene.IsValid())
                {
                    if (activeBefore.IsValid() && activeBefore.isLoaded) SceneManager.SetActiveScene(activeBefore);
                    EditorSceneManager.CloseScene(scene, true);
                }
            }

            lines.AddRange(problems);
            return problems.Count == 0 ? 0 : 1;
        }

        // ---------------------------------------------------------------- 描画

        private static Camera CreateCamera(GameObject go)
        {
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.transform.SetPositionAndRotation(new Vector3(0f, 0f, -1f), Quaternion.identity);
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 10f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.cullingMask = 1 << PreviewLayer;
            cam.allowHDR = false;
            cam.allowMSAA = true;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.enabled = false;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            return cam;
        }

        /// <summary>パネルの外（左下の余白）に置く半透明の見本。描画の経路がアルファを残しているかを確かめる。</summary>
        private static GameObject CreateSwatch(Scene scene)
        {
            var go = new GameObject("[QSE] Alpha Swatch", typeof(RectTransform)) { layer = PreviewLayer, hideFlags = HideFlags.DontSave };
            SceneManager.MoveGameObjectToScene(go, scene);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            UiFactory.ConfigureCanvas(c);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(SwatchUnits, SwatchUnits);
            rt.localScale = Vector3.one * MetersPerUnit;
            float cx = -(OperatorPanelView.Width * 0.5f + MarginUnits * 0.5f);
            float cy = -(OperatorPanelView.Height * 0.5f + MarginUnits * 0.5f);
            rt.position = new Vector3(cx * MetersPerUnit, cy * MetersPerUnit, 0f);
            var shape = UiFactory.CreateSolid("Swatch", rt, new Color(1f, 1f, 1f, 0.5f));
            UiFactory.Stretch(shape.rectTransform);
            return go;
        }

        private static Color32[] Render(Camera cam, OperatorPanelView view, float density, List<Object> targets, out int w, out int h)
        {
            float totalW = OperatorPanelView.Width + MarginUnits * 2f;
            float totalH = OperatorPanelView.Height + MarginUnits * 2f;
            w = Mathf.RoundToInt(totalW * density);
            h = Mathf.RoundToInt(totalH * density);

            var rt = new RenderTexture(w, h, GraphicsFormat.R8G8B8A8_SRGB, GraphicsFormat.D24_UNorm_S8_UInt)
            {
                antiAliasing = 4, // 端末と同じ MSAA
                name = "QSE Panel Preview",
            };
            rt.Create();
            targets.Add(rt);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            targets.Add(tex);

            cam.orthographicSize = totalH * 0.5f * MetersPerUnit;
            cam.aspect = (float)w / h;
            cam.targetTexture = rt;

            Canvas.ForceUpdateCanvases();
            foreach (var t in view.Texts)
                if (t.isActiveAndEnabled) t.ForceMeshUpdate();
            Canvas.ForceUpdateCanvases();

            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
            else cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            return tex.GetPixels32();
        }

        // ---------------------------------------------------------------- 検査

        /// <summary>
        /// パネルの内側（角の丸みと 3 ピクセルを除く）のアルファがすべて 255 か。
        /// あわせて、パネルの外の四隅が 0、半透明の見本が約 128 であること（描画の経路がアルファを捨てていないこと）。
        /// </summary>
        private static void CheckAlpha(Color32[] px, int w, int h, string name, List<string> problems, List<string> lines)
        {
            int inset = Mathf.CeilToInt(MarginUnits + OperatorPanelView.CornerRadius + 3f);
            byte min = 255;
            int below = 0;
            for (int y = inset; y < h - inset; y++)
            for (int x = inset; x < w - inset; x++)
            {
                byte a = px[y * w + x].a;
                if (a < 255) below++;
                if (a < min) min = a;
            }
            lines.Add(string.Format("{0}: interior min alpha {1}, pixels below 255: {2}", name, min, below));
            if (below > 0) problems.Add(string.Format("ALPHA BELOW 1 in '{0}' ({1} pixels, min {2})", name, below, min));

            int[,] corners = { { 2, 2 }, { w - 3, 2 }, { 2, h - 3 }, { w - 3, h - 3 } };
            for (int i = 0; i < 4; i++)
            {
                byte a = px[corners[i, 1] * w + corners[i, 0]].a;
                if (a != 0) problems.Add(string.Format("PIPELINE ALPHA: '{0}' corner {1} reads alpha {2} (expected 0); the alpha check is not trustworthy", name, i, a));
            }

            // 見本は左下の余白の中央（読み出した画像は下から上の順）
            int sx = Mathf.RoundToInt(MarginUnits * 0.5f), sy = Mathf.RoundToInt(MarginUnits * 0.5f);
            byte sa = px[sy * w + sx].a;
            if (sa < 110 || sa > 146)
                problems.Add(string.Format("PIPELINE ALPHA: '{0}' 50% swatch reads alpha {1} (expected about 128); the alpha check is not trustworthy", name, sa));
        }

        /// <summary>文字のはみ出し、固定アトラスにない字、決めたもの以外のシェーダー。</summary>
        private static void CheckContent(OperatorPanelView view, string name, List<string> problems)
        {
            Canvas.ForceUpdateCanvases();
            foreach (var t in view.Texts)
            {
                if (!t.isActiveAndEnabled || string.IsNullOrEmpty(t.text)) continue;
                t.ForceMeshUpdate();
                float pref = t.GetPreferredValues(t.text).x;
                float width = t.rectTransform.rect.width;
                if (t.isTextTruncated || pref > width + 1f)
                    problems.Add(string.Format("TEXT OVERFLOW in '{0}': {1} \"{2}\" needs {3:0} units, has {4:0}", name, PathOf(t.transform), t.text, pref, width));
                if (!PanelPresenter.UsesOnlyStaticGlyphs(t.text))
                    problems.Add(string.Format("GLYPH outside the static atlas in '{0}': {1} \"{2}\"", name, PathOf(t.transform), t.text));
                if (t.font != null && !t.font.HasCharacters(t.text, out List<char> missing))
                    problems.Add(string.Format("GLYPH missing from {0} in '{1}': {2} ({3})", t.font.name, name, PathOf(t.transform), new string(missing.ToArray())));
            }

            foreach (var g in view.GetComponentsInChildren<Graphic>(true))
            {
                var m = g.materialForRendering;
                string shader = m != null && m.shader != null ? m.shader.name : "(none)";
                if (Array.IndexOf(AllowedShaders, shader) < 0)
                    problems.Add(string.Format("SHADER {0} on {1} (only {2})", shader, PathOf(g.transform), string.Join(", ", AllowedShaders)));
            }
        }

        /// <summary>
        /// 文字が、ほかの部品と重なっていないか（チップが名前を隠す、状態の点が版の文字に乗る、通知が凡例にかかる、など）。
        /// 入れ子の Canvas もまとめて、パネル全体を階層の順に比べる（入れ子の Canvas は並べ替えを使っていないので、
        /// 階層の順がそのまま描く順になる）。
        /// - 文字どうしは、重なれば問題。
        /// - 文字より後に描く部品は、重なれば文字を隠すので問題。
        /// - 文字より先に描く部品は、文字の高さをすべて覆う帯（背景、進み具合の帯、選択の帯）なら下地とみなし、
        ///   それ以外は問題にする。
        /// - 上に重ねて出す層（Overlay：長押しの帯と通知）が文字をすっかり覆うときは、わざと隠しているので数えない
        ///   （少しだけかかるのは問題）。
        /// - 2つの間に描く不透明な図形が、重なったところを覆っていれば、下のほうは見えないので数えない
        ///   （長押しの帯の文字と、帯の下に隠れたヘッダーの点、など）。
        /// 動いている途中の見本では調べない。
        /// </summary>
        private static void CheckOverlaps(OperatorPanelView view, string name, List<string> problems)
        {
            Canvas.ForceUpdateCanvases();
            var root = view.Root;
            var overlay = root.Find("Overlay");
            var all = view.GetComponentsInChildren<Graphic>(false); // 階層の順 = 描く順

            var rects = new Rect[all.Length];
            var drawn = new bool[all.Length];
            for (int i = 0; i < all.Length; i++)
            {
                var g = all[i];
                if (!IsDrawn(g)) continue;
                if (g is TextMeshProUGUI tm)
                {
                    if (string.IsNullOrEmpty(tm.text)) continue;
                    rects[i] = TextRect(tm, root);
                    if (rects[i].width <= 0f || rects[i].height <= 0f) continue;
                }
                else
                {
                    rects[i] = RectIn(g.rectTransform, root);
                }
                drawn[i] = true;
            }

            // 上に重ねた層にすっかり覆われた文字は見えないので、ほかと比べない
            var hidden = new bool[all.Length];
            if (overlay != null)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    if (!drawn[i] || !(all[i] is TextMeshProUGUI) || all[i].transform.IsChildOf(overlay)) continue;
                    for (int o = i + 1; o < all.Length; o++)
                    {
                        if (!drawn[o] || all[o] is TextMeshProUGUI || !all[o].transform.IsChildOf(overlay) || !IsOpaque(all[o])) continue;
                        if (!Contains(rects[o], rects[i])) continue;
                        hidden[i] = true;
                        break;
                    }
                }
            }

            var reported = new HashSet<(int, int)>();
            for (int ti = 0; ti < all.Length; ti++)
            {
                if (!drawn[ti] || hidden[ti] || !(all[ti] is TextMeshProUGUI t)) continue;
                Rect tr = rects[ti];
                for (int gi = 0; gi < all.Length; gi++)
                {
                    if (gi == ti || !drawn[gi] || hidden[gi]) continue;
                    var g = all[gi];
                    Rect gr = rects[gi];
                    if (!(g is TextMeshProUGUI))
                    {
                        bool underlay = gi < ti && gr.yMin <= tr.yMin && gr.yMax >= tr.yMax;
                        if (underlay) continue;
                    }
                    if (!gr.Overlaps(tr)) continue;
                    if (Occluded(all, rects, drawn, Mathf.Min(gi, ti), Mathf.Max(gi, ti), Intersect(gr, tr))) continue;
                    if (!reported.Add(gi < ti ? (gi, ti) : (ti, gi))) continue;
                    problems.Add(string.Format("OVERLAP in '{0}': {1} \"{2}\" and {3}", name, PathOf(t.transform), t.text, PathOf(g.transform)));
                }
            }
        }

        /// <summary>lower と upper の間に描く不透明な図形が、area をすっかり覆うか（そのとき lower はそこでは見えない）。</summary>
        private static bool Occluded(Graphic[] all, Rect[] rects, bool[] drawn, int lower, int upper, Rect area)
        {
            for (int h = lower + 1; h < upper; h++)
                if (drawn[h] && !(all[h] is TextMeshProUGUI) && IsOpaque(all[h]) && Contains(rects[h], area)) return true;
            return false;
        }

        private static Rect Intersect(Rect a, Rect b) =>
            Rect.MinMaxRect(Mathf.Max(a.xMin, b.xMin), Mathf.Max(a.yMin, b.yMin), Mathf.Min(a.xMax, b.xMax), Mathf.Min(a.yMax, b.yMax));

        /// <summary>
        /// 重なりの検査そのものが働いているかの確認（アルファの検査の、外の四隅と半透明の見本と同じ考え）。
        /// 題の文字にわざと部品を重ね、見つけるべきもの（文字、上や下に描いた図形、上に描いて高さすべてやすっかり覆う図形、
        /// 入れ子の Canvas の図形、一部だけかかる上の層）を見つけ、見逃すべきもの（下地の帯、間の帯に隠れた図形、
        /// すっかり覆う上の層）を見逃すかを確かめる。
        /// </summary>
        private static void CheckOverlapProbe(OperatorPanelView view, List<string> problems)
        {
            var header = view.Root.Find("Header");
            var titleT = header != null ? header.Find("Title") : null;
            var title = titleT != null ? titleT.GetComponent<TextMeshProUGUI>() : null;
            var overlay = view.Root.Find("Overlay");
            if (title == null || overlay == null)
            {
                problems.Add("OVERLAP CHECK: header title or overlay not found; the overlap check is not trustworthy");
                return;
            }
            view.ClearTransient();
            view.Apply(Baseline());
            view.FinishAnimations();
            title.ForceMeshUpdate();
            Rect ink = InkBounds(title);
            if (ink.width <= 0f)
            {
                problems.Add("OVERLAP CHECK: header title has no text; the overlap check is not trustworthy");
                return;
            }

            // 題の文字の範囲に対する位置：左右は 1/4〜1/2、上下は「上半分だけ」か「高さすべて」
            Rect upper = Rect.MinMaxRect(ink.xMin + ink.width * 0.25f, ink.center.y, ink.xMin + ink.width * 0.5f, ink.yMax + 4f);
            Rect tall = Rect.MinMaxRect(upper.xMin, ink.yMin - 4f, upper.xMax, ink.yMax + 4f);
            Rect cover = Rect.MinMaxRect(ink.xMin - 4f, ink.yMin - 4f, ink.xMax + 4f, ink.yMax + 4f);
            int before = titleT.GetSiblingIndex();

            RunProbe(view, problems, "Text", true, () =>
            {
                var probe = UiFactory.CreateText("[QSE] Probe Text", header, "Probe", UiTheme.SizeTitle, UiTheme.TextPrimary);
                var rt = probe.rectTransform;
                var tr = (RectTransform)titleT;
                rt.anchorMin = tr.anchorMin;
                rt.anchorMax = tr.anchorMax;
                rt.pivot = tr.pivot;
                rt.anchoredPosition = tr.anchoredPosition + new Vector2(20f, 0f);
                rt.sizeDelta = tr.sizeDelta;
                probe.ForceMeshUpdate();
                return probe.gameObject;
            });
            RunProbe(view, problems, "Later", true, () => ProbeShape("[QSE] Probe Later", title, header, -1, upper));
            // 後に描く図形は、高さすべてを覆っても（下地の扱いは先に描くものだけ）、すっかり覆っても（隠すのは上の層だけ）問題
            RunProbe(view, problems, "Later Tall", true, () => ProbeShape("[QSE] Probe Later Tall", title, header, -1, tall));
            RunProbe(view, problems, "Later Cover", true, () => ProbeShape("[QSE] Probe Later Cover", title, header, -1, cover));
            RunProbe(view, problems, "Under", true, () => ProbeShape("[QSE] Probe Under", title, header, before, upper));
            RunProbe(view, problems, "Underlay", false, () => ProbeShape("[QSE] Probe Underlay", title, header, before, tall));
            RunProbe(view, problems, "Occluded", false, () =>
            {
                // 文字の下の図形が、その上の不透明な帯（文字の下地）に隠れている
                var group = UiFactory.CreateRect("[QSE] Probe Occluded", header);
                group.SetSiblingIndex(before);
                ProbeShape("[QSE] Probe Occluded Lower", title, group, -1, upper);
                ProbeShape("[QSE] Probe Occluded Band", title, group, -1, tall);
                return group.gameObject;
            });
            RunProbe(view, problems, "Nested", true, () =>
            {
                var nested = UiFactory.CreateRect("[QSE] Probe Nested", header);
                UiFactory.AddSubCanvas(nested.gameObject);
                ProbeShape("[QSE] Probe Nested Shape", title, nested, -1, upper);
                return nested.gameObject;
            });
            RunProbe(view, problems, "Overlay Edge", true, () => ProbeShape("[QSE] Probe Overlay Edge", title, overlay, -1, upper));
            RunProbe(view, problems, "Overlay Cover", false, () => ProbeShape("[QSE] Probe Overlay Cover", title, overlay, -1, cover));
        }

        /// <summary>見本の部品を足して重なりを調べ、その部品が見つかったかを期待と比べる。終わったら部品を消す。</summary>
        private static void RunProbe(OperatorPanelView view, List<string> problems, string label, bool expectFound, Func<GameObject> make)
        {
            GameObject probe = null;
            try
            {
                probe = make();
                var found = new List<string>();
                CheckOverlaps(view, "probe", found);
                // 題と見本の組で見つかったときだけ数える（ほかの重なりで、たまたま通らないように）
                bool hit = found.Exists(l => l.Contains("Header/Title") && l.Contains("[QSE] Probe"));
                if (hit != expectFound)
                    problems.Add(string.Format("OVERLAP CHECK: probe '{0}' was {1}; the overlap check is not trustworthy",
                        label, expectFound ? "not detected" : "reported although it should be ignored"));
            }
            finally
            {
                if (probe != null) Object.DestroyImmediate(probe);
            }
        }

        /// <summary>題の文字の座標（local）で矩形を作り、同じ位置のまま parent の子にする（index が 0 以上なら、その順番に入れる）。</summary>
        private static GameObject ProbeShape(string name, TextMeshProUGUI title, Transform parent, int index, Rect local)
        {
            var s = UiFactory.CreateSolid(name, title.transform, UiTheme.TextPrimary);
            var rt = s.rectTransform;
            rt.anchorMin = rt.anchorMax = title.rectTransform.pivot; // 親の原点（pivot）を基準にする
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = local.center;
            rt.sizeDelta = local.size;
            rt.SetParent(parent, true);
            if (index >= 0) rt.SetSiblingIndex(index);
            return s.gameObject;
        }

        /// <summary>
        /// 通知の文は見本の状態では一部しか出ないので、出しうる文（PanelNotices.Samples、いちばん長い組み合わせを含む）を
        /// すべて通知の欄で測る。あわせて字も調べる。
        /// </summary>
        private static void CheckNoticeFit(OperatorPanelView view, List<string> problems, List<string> lines)
        {
            var t = view.ToastText;
            float width = t.rectTransform.rect.width;
            int count = 0;
            float widest = 0f;
            string widestText = "";
            foreach (var s in PanelNotices.Samples())
            {
                float pref = t.GetPreferredValues(s).x;
                count++;
                if (pref > widest)
                {
                    widest = pref;
                    widestText = s;
                }
                if (pref > width + 1f)
                    problems.Add(string.Format("TEXT OVERFLOW in notices: \"{0}\" needs {1:0} units, has {2:0}", s, pref, width));
                if (!PanelPresenter.UsesOnlyStaticGlyphs(s))
                    problems.Add(string.Format("GLYPH outside the static atlas in notices: \"{0}\"", s));
                if (t.font != null && !t.font.HasCharacters(s, out List<char> missing))
                    problems.Add(string.Format("GLYPH missing from {0} in notices: \"{1}\" ({2})", t.font.name, s, new string(missing.ToArray())));
            }
            lines.Add(string.Format("notices: {0} texts measured, widest {1:0} of {2:0} units (\"{3}\")", count, widest, width, widestText));
        }

        private static bool IsDrawn(Graphic g) =>
            g.isActiveAndEnabled && g.color.a > 0f && g.canvasRenderer.GetInheritedAlpha() > 0.001f;

        private static bool IsOpaque(Graphic g) => g.color.a >= 0.999f && g.canvasRenderer.GetInheritedAlpha() >= 0.999f;

        private static bool Contains(Rect outer, Rect inner) =>
            outer.xMin <= inner.xMin && outer.xMax >= inner.xMax && outer.yMin <= inner.yMin && outer.yMax >= inner.yMax;

        /// <summary>
        /// 文字のインクの範囲（文字の座標）。TMP の textBounds は行の高さ（ascender〜descender）なので、上端を
        /// 大文字の高さの近く（0.17 em 下）まで下げ、左右も 1 単位詰めて、実際のインクに近づける。
        /// </summary>
        private static Rect InkBounds(TextMeshProUGUI t)
        {
            var b = t.textBounds;
            if (b.size.x <= 0f) return default;
            return Rect.MinMaxRect(b.min.x + 1f, b.min.y, b.max.x - 1f, b.max.y - 0.17f * t.fontSize);
        }

        /// <summary>文字のインクの範囲（root の座標）。</summary>
        private static Rect TextRect(TextMeshProUGUI t, RectTransform root)
        {
            Rect r = InkBounds(t);
            if (r.width <= 0f) return default;
            return Span(root, t.transform.TransformPoint(r.min), t.transform.TransformPoint(r.max));
        }

        private static Rect RectIn(RectTransform rt, RectTransform root)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            return Span(root, corners[0], corners[2]);
        }

        private static Rect Span(RectTransform root, Vector3 worldA, Vector3 worldB)
        {
            Vector3 a = root.InverseTransformPoint(worldA), b = root.InverseTransformPoint(worldB);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private static string PathOf(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null && p.GetComponent<OperatorPanelView>() == null; p = p.parent)
                sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        // ---------------------------------------------------------------- 画像

        /// <summary>パネルの外の余白を透明にする（検査に使った左下のアルファの見本を、保存する画像に残さない）。</summary>
        private static void ClearMargin(Color32[] px, int w, int h, float density)
        {
            int m = Mathf.FloorToInt(MarginUnits * density);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (x < m || x >= w - m || y < m || y >= h - m) px[y * w + x] = default;
        }

        /// <summary>アルファ乗算済みの色を、背景の上に重ねた不透明な色にする（PNG ビューアーで縁が黒くならないように）。</summary>
        private static Color32[] Flatten(Color32[] px, Color32 bg)
        {
            var o = new Color32[px.Length];
            for (int i = 0; i < px.Length; i++)
            {
                var p = px[i];
                int k = 255 - p.a;
                o[i] = new Color32(
                    (byte)Mathf.Min(255, p.r + bg.r * k / 255),
                    (byte)Mathf.Min(255, p.g + bg.g * k / 255),
                    (byte)Mathf.Min(255, p.b + bg.b * k / 255),
                    255);
            }
            return o;
        }

        private static byte[] EncodePng(Color32[] px, int w, int h, int upscale)
        {
            int ow = w * upscale, oh = h * upscale;
            var o = px;
            if (upscale > 1)
            {
                o = new Color32[ow * oh];
                for (int y = 0; y < oh; y++)
                for (int x = 0; x < ow; x++)
                    o[y * ow + x] = px[(y / upscale) * w + x / upscale];
            }
            var tex = new Texture2D(ow, oh, TextureFormat.RGBA32, false, false);
            try
            {
                tex.SetPixels32(o);
                tex.Apply();
                return tex.EncodeToPNG();
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }

        // ---------------------------------------------------------------- 見本の状態

        private static OperatorPanelState Baseline()
        {
            var s = new OperatorPanelState();
            var settings = FreeControlSettings.CreateDefault();
            settings.Saturation = -0.35f;
            settings.Brightness = 0.05f;
            settings.Contrast = 0.1f;
            settings.ParticipantId = 7;
            foreach (var def in ParameterDefs.All)
                s.Set(def.Item, def.Item == MenuItem.Mode ? 0f : settings.Get(def.Item));
            s.Mode = FreeMode.Manual;
            s.Cursor = (int)MenuItem.Saturation;
            s.Output = settings.ManualColor;
            s.Passthrough = PassthroughInit.Ready;
            s.HasLayer = true;
            s.LayerEnabled = true;
            s.PassthroughVisible = true;
            s.ParticipantId = 7;
            s.Trials = 3;
            s.LogFile = "session_20261107T123045Z_P007.csv";
            s.BatteryLevel = 0.82f;
            s.Clock = "21:34";
            return s;
        }

        private static void Auto(OperatorPanelState s, Waveform w, float from, float to, float period, int steps, double phase, PlaybackStatus playback)
        {
            s.Mode = FreeMode.Auto;
            s.Set(MenuItem.Mode, 1f);
            s.Set(MenuItem.Waveform, (int)w);
            s.Set(MenuItem.From, from);
            s.Set(MenuItem.To, to);
            s.Set(MenuItem.Period, period);
            s.Set(MenuItem.Steps, steps);
            s.Playback = playback;
            s.Phase = phase;
            s.WaveValue = WaveformMath.Evaluate(w, phase, steps, from, to);
            s.Output = s.Output.WithSaturation(s.WaveValue);
        }

        /// <summary>見本：名前、README の画像にするか、動きが止まった状態か（重なりを調べる）、作り方。</summary>
        private static readonly (string, bool, bool, Action<OperatorPanelState, OperatorPanelView>)[] Cases =
        {
            ("manual", false, true, (s, v) =>
            {
                v.Apply(s);
                v.FinishAnimations();
            }),
            ("auto", true, true, (s, v) =>
            {
                Auto(s, Waveform.Step, 0.4f, -0.8f, 12f, 5, 0.37, PlaybackStatus.Playing);
                s.Cursor = (int)MenuItem.Period;
                v.Apply(s);
                v.FinishAnimations();
                v.ShowToast(PanelPresenter.MarkerToast(1, 1234.5), StatusTone.Good, 2f);
                v.Animate(0.4f);
            }),
            ("attention", true, true, (s, v) =>
            {
                // 警告がいくつも重なったとき（ヘッダーの状態の列がいちばん長くなる）。長押しの帯で隠さない
                Auto(s, Waveform.Ramp, 0f, -1f, 30f, 5, 0.62, PlaybackStatus.Paused);
                s.Cursor = (int)MenuItem.ParticipantId;
                s.Set(MenuItem.ParticipantId, 8f);
                s.PendingParticipantId = 8;
                s.Passthrough = PassthroughInit.Starting;
                s.PassthroughVisible = false;
                s.LogFailed = true;
                s.LogFile = "";
                s.BatteryLevel = 0.12f;
                s.Transitioning = true;
                v.Apply(s);
                v.FinishAnimations();
            }),
            ("failed", false, true, (s, v) =>
            {
                // ログが止まった直後（ヘッダーとセッションカードの表示、赤い通知）
                s.LogFailed = true;
                s.LogFile = "";
                v.Apply(s);
                v.FinishAnimations();
                v.ShowToast(PanelPresenter.LogFailedToast, StatusTone.Error, PanelNotices.LogFailedSec);
                v.Animate(0.4f);
            }),
            ("hold", false, true, (s, v) =>
            {
                s.Cursor = (int)MenuItem.Brightness;
                s.Set(MenuItem.Brightness, 0.5f);
                s.Output = new ColorAdjustment(0.5f, s.Output.Contrast, s.Output.Saturation);
                s.Set(MenuItem.ParticipantId, 8f);
                s.PendingParticipantId = 8;
                s.MenuHoldProgress = 0.55f;
                v.Apply(s);
                v.FinishAnimations();
                var reset = ParameterDefs.Get(MenuItem.Period);
                v.ShowToast(PanelPresenter.ResetToast(reset, 37.5f, reset.Default), StatusTone.Neutral, 2.5f);
                v.Animate(0.4f);
            }),
            ("stophold", false, true, (s, v) =>
            {
                // Auto で止まっている Linear（再生状態のチップがいちばん長い）と、右B の長押し。Mode の行は Auto の説明になる
                Auto(s, Waveform.LinearPingPong, 0f, -1f, 10f, 5, 0.0, PlaybackStatus.Stopped);
                s.Cursor = (int)MenuItem.Mode;
                s.StopHoldProgress = 0.65f;
                v.Apply(s);
                v.FinishAnimations();
            }),
            ("motion", false, false, (s, v) =>
            {
                // 動いている途中（選択の帯が1行ぶん滑っている、波形の選択が動いている、通知が現れかけ）でもアルファが 1 のままか。
                // 2 行以上の移動は折り返しとみなして飛ぶので、1 行だけ動かす
                Auto(s, Waveform.LinearPingPong, 0f, -1f, 10f, 5, 0.5, PlaybackStatus.Playing);
                s.Cursor = (int)MenuItem.Mode;
                v.Apply(s);
                v.FinishAnimations();
                Auto(s, Waveform.Ramp, 0f, -1f, 10f, 5, 1.0, PlaybackStatus.Finished);
                s.Cursor = (int)MenuItem.Saturation;
                v.Apply(s);
                v.ShowToast(PanelPresenter.RampFinishedToast(-1f), StatusTone.Neutral, 2f);
                v.Animate(0.05f);
            }),
        };

        private static string ArgValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
