using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;

namespace QuestSaturation.Editor
{
    /// <summary>
    /// ビルドの直前に git の commit を Resources の一時ファイルへ書き、ビルド後に消す。
    /// SessionLogger が session_start に書き出す。このファイルは .gitignore 済み。
    /// </summary>
    public class BuildInfoWriter : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public const string AssetPath = "Assets/Resources/" + SessionLogger.BuildInfoResource + ".txt";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            // BuildScript から呼んだときは既に書いてある
            if (!File.Exists(AssetPath)) Write();
        }

        public void OnPostprocessBuild(BuildReport report) => Delete();

        public static void Write()
        {
            string commit = Git("rev-parse --short=12 HEAD") ?? "unknown";
            string status = Git("status --porcelain --untracked-files=no");
            if (!string.IsNullOrEmpty(status)) commit += "-dirty";
            string text = "git=" + commit + "\n" +
                          "built_utc=" + DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) + "\n";
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            File.WriteAllText(AssetPath, text);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[QSE-TOOL] build info: " + text.Replace("\n", " "));
        }

        public static void Delete()
        {
            if (File.Exists(AssetPath) || File.Exists(AssetPath + ".meta")) AssetDatabase.DeleteAsset(AssetPath);
        }

        private static string Git(string args)
        {
            foreach (var exe in new[] { "/usr/bin/git", "/opt/homebrew/bin/git", "/usr/local/bin/git", "git" })
            {
                if (exe.StartsWith("/") && !File.Exists(exe)) continue;
                try
                {
                    var psi = new ProcessStartInfo(exe, args)
                    {
                        WorkingDirectory = Directory.GetCurrentDirectory(),
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };
                    using (var p = Process.Start(psi))
                    {
                        string output = p.StandardOutput.ReadToEnd();
                        if (!p.WaitForExit(5000)) return null;
                        return p.ExitCode == 0 ? output.Trim() : null;
                    }
                }
                catch (Exception)
                {
                    // 次の候補を試す
                }
            }
            return null;
        }
    }
}
