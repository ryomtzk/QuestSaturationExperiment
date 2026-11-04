using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace QuestSaturation.Editor
{
    /// <summary>
    /// Quest 用 APK をビルドする。インストールはしない（BuildOptions.None。adb も使わない）。
    /// 出力先：~/Downloads/QuestBuilds/QuestSaturationExperiment_operator.apk。
    /// 同名のファイルがあるときは上書きせず、時刻を付けた名前にする。
    /// batchmode：-executeMethod QuestSaturation.Editor.BuildScript.BuildQuestApk -quit
    /// </summary>
    public static class BuildScript
    {
        public const string BaseName = "QuestSaturationExperiment_operator";

        public static string OutputDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "QuestBuilds");

        [MenuItem("QuestSaturation/Build Quest APK")]
        private static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            bool ok = Build(out string message);
            EditorUtility.DisplayDialog("QuestSaturation", message, "OK");
            if (ok) EditorUtility.RevealInFinder(LastOutputPath);
        }

        public static void BuildQuestApk()
        {
            bool ok;
            string message;
            try
            {
                ok = Build(out message);
            }
            catch (Exception e)
            {
                ok = false;
                message = e.ToString();
            }
            if (ok) Debug.Log("[QSE-TOOL] build: " + message);
            else Debug.LogError("[QSE-TOOL] build failed: " + message);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        public static string LastOutputPath { get; private set; }

        private static bool Build(out string message)
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                message = "Active build target must be Android (now " + EditorUserBuildSettings.activeBuildTarget + ")";
                return false;
            }

            var scene = EditorSceneManager.OpenScene(SetupScene.ScenePath, OpenSceneMode.Single);
            var problems = SceneValidator.Validate(scene);
            if (problems.Count > 0)
            {
                message = "Scene is not valid. Run QuestSaturation/Setup Scene first.\n" + string.Join("\n", problems);
                return false;
            }

            string path = UniquePath();
            Directory.CreateDirectory(OutputDir);

            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = path,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            };

            bool wasAab = EditorUserBuildSettings.buildAppBundle;
            EditorUserBuildSettings.buildAppBundle = false; // APK を出す
            BuildReport report;
            try
            {
                BuildInfoWriter.Write();
                report = BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                BuildInfoWriter.Delete();
                EditorUserBuildSettings.buildAppBundle = wasAab;
            }

            var summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                message = $"{summary.result}: {summary.totalErrors} error(s)";
                return false;
            }

            LastOutputPath = path;
            message = string.Format(CultureInfo.InvariantCulture, "{0} ({1:0.0} MB, {2:0}s)",
                path, summary.totalSize / (1024.0 * 1024.0), summary.totalTime.TotalSeconds);
            return true;
        }

        private static string UniquePath()
        {
            string path = Path.Combine(OutputDir, BaseName + ".apk");
            if (!File.Exists(path)) return path;
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            return Path.Combine(OutputDir, BaseName + "_" + stamp + ".apk");
        }
    }
}
