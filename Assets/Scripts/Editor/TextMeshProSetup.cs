using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace QuestSaturation.Editor
{
    /// <summary>
    /// オペレーターパネルは TextMeshPro で描く。TMP の Essential Resources（フォント、シェーダー、TMP Settings）は
    /// パッケージに同梱された unitypackage をプロジェクトに取り込んで使うので、無ければ取り込む。
    /// batchmode：-executeMethod QuestSaturation.Editor.TextMeshProSetup.RunBatch
    /// （-quit は付けない。取り込みは次のエディタの更新で進むので、-quit だと取り込む前に終了してしまう。
    /// RunBatch は取り込みが終わってから自分で終了する）
    /// </summary>
    public static class TextMeshProSetup
    {
        public const string SettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        /// <summary>TMP Settings が読めて、既定のフォントが入っている（パネルの文字が描ける）。</summary>
        public static bool EssentialsPresent =>
            File.Exists(SettingsPath) &&
            AssetDatabase.LoadAssetAtPath<TMPro.TMP_Settings>(SettingsPath) != null &&
            TMPro.TMP_Settings.defaultFontAsset != null;

        [MenuItem("QuestSaturation/Import TextMeshPro Essentials")]
        private static void Menu()
        {
            var log = new List<string>();
            Ensure(log);
            EditorUtility.DisplayDialog("QuestSaturation", log.Count == 0 ? "TextMeshPro Essentials are already in the project." : string.Join("\n", log), "OK");
        }

        public static void RunBatch()
        {
            try
            {
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-quit") >= 0 && !EssentialsPresent)
                {
                    Debug.LogError("[QSE-TOOL] tmp: remove -quit; the import runs on a later editor update and -quit exits before it");
                    EditorApplication.Exit(1);
                    return;
                }
                if (EssentialsPresent)
                {
                    Debug.Log("[QSE-TOOL] tmp: already present");
                    EditorApplication.Exit(0);
                    return;
                }

                // ImportPackage は非同期なので、終わってから終了する
                AssetDatabase.importPackageCompleted += _ => FinishBatch("imported");
                AssetDatabase.importPackageFailed += (_, error) => FinishBatch("import failed: " + error);
                AssetDatabase.importPackageCancelled += _ => FinishBatch("import cancelled");
                double deadline = EditorApplication.timeSinceStartup + 300.0;
                EditorApplication.update += () =>
                {
                    if (EditorApplication.timeSinceStartup > deadline) FinishBatch("import timed out");
                };
                StartImport();
            }
            catch (Exception e)
            {
                Debug.LogError("[QSE-TOOL] tmp import failed: " + e);
                EditorApplication.Exit(1);
            }
        }

        private static void FinishBatch(string what)
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            bool ok = EssentialsPresent;
            Debug.Log("[QSE-TOOL] tmp: " + what + (ok ? " (ok)" : " (TMP Settings still missing)"));
            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>
        /// Essentials が無ければ取り込みを始める（確認ダイアログは出さない）。取り込みは非同期で、
        /// エディタが次に更新されたときに終わる。始めたら log に1行足す。
        /// </summary>
        public static void Ensure(List<string> log)
        {
            if (EssentialsPresent) return;
            StartImport();
            log.Add("importing TextMeshPro Essential Resources (finishes in a moment)");
        }

        private static void StartImport() =>
            TMPro.TMP_PackageResourceImporter.ImportResources(importEssentials: true, importExamples: false, interactive: false);
    }
}
