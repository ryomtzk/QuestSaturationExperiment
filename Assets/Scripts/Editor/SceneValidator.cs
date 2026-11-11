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
    /// 実験シーンの検査。問題があれば一覧を返す（batchmode では終了コード 1）。
    /// batchmode：-executeMethod QuestSaturation.Editor.SceneValidator.RunBatch -quit
    /// </summary>
    public static class SceneValidator
    {
        // シーンに保存されている値。enum の型を変えると同じ数値が別のボタンになるので、数値で確かめる
        public const int ExpectedSelectButton = (int)OVRInput.Button.PrimaryIndexTrigger; // 8192
        public const int ExpectedResetButton = (int)OVRInput.Button.One;                    // 1

        [MenuItem("QuestSaturation/Validate Scene")]
        private static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(SetupScene.ScenePath, OpenSceneMode.Single);
            var errors = Validate(scene);
            EditorUtility.DisplayDialog("QuestSaturation",
                errors.Count == 0 ? "Scene is valid." : "Problems:\n\n" + string.Join("\n", errors), "OK");
        }

        public static void RunBatch()
        {
            try
            {
                var scene = EditorSceneManager.OpenScene(SetupScene.ScenePath, OpenSceneMode.Single);
                var errors = Validate(scene);
                foreach (var e in errors) Debug.LogError("[QSE-TOOL] invalid: " + e);
                Debug.Log("[QSE-TOOL] validate: " + (errors.Count == 0 ? "OK" : errors.Count + " problem(s)"));
                EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
            }
            catch (Exception e)
            {
                Debug.LogError("[QSE-TOOL] validate failed: " + e);
                EditorApplication.Exit(1);
            }
        }

        public static List<string> Validate(Scene scene)
        {
            var errors = new List<string>();

            // Missing Script
            foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                if (missing > 0) errors.Add($"{Path(t)} has {missing} missing script(s)");
            }

            var manager = One<ExperimentManager>(scene, errors);
            var saturation = One<SaturationController>(scene, errors);
            var selector = One<ConditionSelector>(scene, errors);
            One<SettingsStore>(scene, errors);
            One<FreeControlController>(scene, errors);
            One<ControllerShortcuts>(scene, errors);
            One<SessionLogger>(scene, errors);
            One<OperatorPanel>(scene, errors);

            if (manager != null)
            {
                RequireRefs(manager, errors, "passthroughLayer", "saturationController", "selector", "selectionRoot", "mainCamera");
                var so = new SerializedObject(manager);
                Expect(errors, "ExperimentManager.resetButton", so.FindProperty("resetButton").intValue, ExpectedResetButton);
                if (so.FindProperty("resetHoldSec").floatValue < 0.5f) errors.Add("ExperimentManager.resetHoldSec is too short");

                var cam = so.FindProperty("mainCamera").objectReferenceValue as Camera;
                if (cam != null) CheckPostProcessingOff(cam, errors);
            }

            if (saturation != null) RequireRefs(saturation, errors, "passthroughLayer");

            if (selector != null)
            {
                RequireRefs(selector, errors, "rightControllerAnchor", "pointerLine");
                var so = new SerializedObject(selector);
                Expect(errors, "ConditionSelector.selectButton", so.FindProperty("selectButton").intValue, ExpectedSelectButton);

                var opts = selector.Options ?? Array.Empty<SphereOption>();
                if (opts.Length != 3 || opts.Any(o => o == null))
                    errors.Add("ConditionSelector.options must have 3 spheres");
                else
                {
                    var conds = opts.Select(o => o.condition).OrderBy(c => c).ToArray();
                    if (!conds.SequenceEqual(new[] { Condition.A, Condition.B, Condition.C }))
                        errors.Add("spheres must be A, B and C once each (found " + string.Join(",", conds) + ")");
                    foreach (var o in opts)
                    {
                        var col = o.GetComponent<SphereCollider>();
                        if (col == null || !col.enabled) errors.Add(o.name + " needs an enabled SphereCollider");
                        else if (col.isTrigger) errors.Add(o.name + " collider must not be a trigger (the ray ignores triggers)");
                    }
                }

                int needed = SphereLayerBits(selector);
                int mask = so.FindProperty("raycastMask").intValue;
                if ((mask & needed) != needed) errors.Add("ConditionSelector.raycastMask does not include the sphere layers");
            }

            RequireRefs<FreeControlController>(scene, errors, "experiment", "saturationController", "settingsStore");
            RequireRefs<ControllerShortcuts>(scene, errors, "experiment", "freeControl", "panel");
            RequireRefs<SessionLogger>(scene, errors, "experiment", "freeControl", "shortcuts", "settingsStore");
            RequireRefs<OperatorPanel>(scene, errors, "experiment", "freeControl", "sessionLogger", "shortcuts", "head");

            if (!TextMeshProSetup.EssentialsPresent)
                errors.Add("TextMeshPro Essentials are missing (QuestSaturation > Import TextMeshPro Essentials); the operator panel needs them");

            if (!EditorBuildSettings.scenes.Any(s => s.enabled && s.path == SetupScene.ScenePath))
                errors.Add(SetupScene.ScenePath + " is not enabled in Build Settings");

            return errors;
        }

        internal static int SphereLayerBits(ConditionSelector selector)
        {
            int bits = 0;
            foreach (var o in selector.Options ?? Array.Empty<SphereOption>())
                if (o != null) bits |= 1 << o.gameObject.layer;
            return bits;
        }

        // URP のカメラでポストプロセスを有効にすると、パススルーに必要なアルファが失われる
        private static void CheckPostProcessingOff(Camera cam, List<string> errors)
        {
            var data = cam.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "UniversalAdditionalCameraData");
            if (data == null) return;
            var p = new SerializedObject(data).FindProperty("m_RenderPostProcessing");
            if (p != null && p.boolValue) errors.Add("post-processing must be off on " + cam.name + " (it breaks passthrough alpha)");
        }

        private static T One<T>(Scene scene, List<string> errors) where T : Component
        {
            var all = SetupScene.FindAll<T>(scene);
            if (all.Count != 1) errors.Add($"expected exactly one {typeof(T).Name}, found {all.Count}");
            return all.Count == 1 ? all[0] : null;
        }

        private static void RequireRefs<T>(Scene scene, List<string> errors, params string[] fields) where T : Component
        {
            var all = SetupScene.FindAll<T>(scene);
            if (all.Count == 1) RequireRefs(all[0], errors, fields);
        }

        private static void RequireRefs(Object target, List<string> errors, params string[] fields)
        {
            var so = new SerializedObject(target);
            foreach (var f in fields)
            {
                var p = so.FindProperty(f);
                if (p == null) errors.Add($"{target.GetType().Name}.{f} does not exist");
                else if (p.objectReferenceValue == null) errors.Add($"{target.GetType().Name}.{f} is not set");
            }
        }

        private static void Expect(List<string> errors, string what, int actual, int expected)
        {
            if (actual != expected) errors.Add($"{what} is {actual}, expected {expected}");
        }

        private static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
    }
}
