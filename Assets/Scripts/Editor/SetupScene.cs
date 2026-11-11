using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace QuestSaturation.Editor
{
    /// <summary>
    /// PassthroughScene に足りないオブジェクトを作り、参照をつないで保存する。
    /// 何度実行しても同じ結果になる（既にあるものは使い回し、値が同じなら書き換えない）。
    /// batchmode：-executeMethod QuestSaturation.Editor.SetupScene.RunBatch -quit
    /// </summary>
    public static class SetupScene
    {
        public const string ScenePath = "Assets/Scenes/PassthroughScene.unity";
        public const string OperatorObjectName = "[QSE] Operator";

        [MenuItem("QuestSaturation/Setup Scene")]
        private static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var log = Run();
            EditorUtility.DisplayDialog("QuestSaturation", "Setup finished.\n\n" + string.Join("\n", log), "OK");
        }

        public static void RunBatch()
        {
            try
            {
                foreach (var line in Run()) Debug.Log("[QSE-TOOL] setup: " + line);
                Debug.Log("[QSE-TOOL] setup: done");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[QSE-TOOL] setup failed: " + e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>シーンを開いて整え、保存する。行った変更を返す。</summary>
        public static List<string> Run()
        {
            var log = new List<string>();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var manager = FindSingle<ExperimentManager>(scene) ?? throw new InvalidOperationException("ExperimentManager not found in " + ScenePath);
            var managerSo = new SerializedObject(manager);
            var saturation = managerSo.FindProperty("saturationController").objectReferenceValue as SaturationController
                             ?? throw new InvalidOperationException("ExperimentManager.saturationController is not set");
            var camera = managerSo.FindProperty("mainCamera").objectReferenceValue as Camera
                         ?? throw new InvalidOperationException("ExperimentManager.mainCamera is not set");
            var selector = managerSo.FindProperty("selector").objectReferenceValue as ConditionSelector
                           ?? throw new InvalidOperationException("ExperimentManager.selector is not set");

            // 新しいコンポーネントは1つの GameObject にまとめる。既にシーンのどこかにあればそれを使う
            var op = scene.GetRootGameObjects().FirstOrDefault(g => g.name == OperatorObjectName);
            if (op == null)
            {
                op = new GameObject(OperatorObjectName);
                SceneManager.MoveGameObjectToScene(op, scene);
                log.Add("created " + OperatorObjectName);
            }

            var store = GetOrAdd<SettingsStore>(scene, op, log);
            var free = GetOrAdd<FreeControlController>(scene, op, log);
            var shortcuts = GetOrAdd<ControllerShortcuts>(scene, op, log);
            var logger = GetOrAdd<SessionLogger>(scene, op, log);
            var panel = GetOrAdd<OperatorPanel>(scene, op, log);

            Wire(free, log, ("experiment", manager), ("saturationController", saturation), ("settingsStore", store));
            Wire(shortcuts, log, ("experiment", manager), ("freeControl", free), ("panel", panel));
            Wire(logger, log, ("experiment", manager), ("freeControl", free), ("shortcuts", shortcuts), ("settingsStore", store));
            Wire(panel, log, ("experiment", manager), ("freeControl", free), ("sessionLogger", logger), ("shortcuts", shortcuts), ("head", camera.transform));

            // パネルの文字は TextMeshPro で描く。取り込みは非同期なので、ここでは始めずに知らせるだけにする
            // （シーンの保存と同じ batch の中で取り込むと、終わる前にエディタが終了する）
            if (!TextMeshProSetup.EssentialsPresent)
                log.Add("WARNING: TextMeshPro Essentials are missing. Run QuestSaturation > Import TextMeshPro Essentials " +
                        "(batch: -executeMethod QuestSaturation.Editor.TextMeshProSetup.RunBatch) before building.");

            // 球の当たり判定に使うレイヤーが、選択レイのマスクに入っているようにする
            var selSo = new SerializedObject(selector);
            var mask = selSo.FindProperty("raycastMask");
            int needed = SceneValidator.SphereLayerBits(selector);
            if ((mask.intValue & needed) != needed)
            {
                mask.intValue = Physics.DefaultRaycastLayers | needed;
                selSo.ApplyModifiedPropertiesWithoutUndo();
                log.Add("ConditionSelector.raycastMask reset to default layers");
            }

            // 保存し直すことで、移動前のアセンブリ名（m_EditorClassIdentifier）や消したフィールドも整理される
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("failed to save " + ScenePath);
            if (log.Count == 0) log.Add("no changes needed");
            return log;
        }

        internal static T FindSingle<T>(Scene scene) where T : Component
        {
            var all = FindAll<T>(scene);
            if (all.Count > 1) throw new InvalidOperationException("more than one " + typeof(T).Name + " in the scene");
            return all.FirstOrDefault();
        }

        internal static List<T> FindAll<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToList();

        private static T GetOrAdd<T>(Scene scene, GameObject host, List<string> log) where T : Component
        {
            var existing = FindSingle<T>(scene);
            if (existing != null) return existing;
            log.Add("added " + typeof(T).Name);
            return host.AddComponent<T>();
        }

        private static void Wire(Object target, List<string> log, params (string field, Object value)[] refs)
        {
            var so = new SerializedObject(target);
            foreach (var (field, value) in refs)
            {
                var p = so.FindProperty(field) ?? throw new InvalidOperationException(target.GetType().Name + "." + field + " not found");
                if (p.objectReferenceValue == value) continue;
                p.objectReferenceValue = value;
                log.Add(target.GetType().Name + "." + field + " -> " + (value != null ? value.name : "null"));
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
