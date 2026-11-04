using QuestSaturation.Core;
using UnityEngine;

namespace QuestSaturation
{
    /// <summary>
    /// FreeControl のパラメータと被験者 ID を PlayerPrefs のキー1つに JSON で保存する。
    /// 変更が止まって 1 秒後（変更が続いても最長 5 秒後）にまとめて保存し、
    /// 一時停止・フォーカス喪失・終了のときにも保存する（Android では OnApplicationQuit が呼ばれないことがあるため）。
    /// </summary>
    [DefaultExecutionOrder(-40)] // 他のコンポーネントの Start より先に読み込む
    public class SettingsStore : MonoBehaviour
    {
        public const string PrefsKey = "QuestSaturation.Settings";

        [SerializeField] private float saveDelaySec = 1f;
        [SerializeField] private float maxSaveDelaySec = 5f;

        private bool dirty;
        private float firstDirtyTime;
        private float lastDirtyTime;

        public FreeControlSettings Settings { get; private set; }

        /// <summary>読み込み時に補正や既定値への置き換えがあったときの理由。問題がなければ null。</summary>
        public string LoadWarning { get; private set; }

        void Awake() => EnsureLoaded();

        /// <summary>まだ読んでいなければ読み込む。Awake の順序に頼らずに使えるようにしている。</summary>
        public FreeControlSettings EnsureLoaded()
        {
            if (Settings != null) return Settings;

            string json = PlayerPrefs.GetString(PrefsKey, "");
            if (string.IsNullOrEmpty(json))
            {
                Settings = FreeControlSettings.CreateDefault();
                LoadWarning = null;
            }
            else
            {
                Settings = SettingsCodec.Decode(json, out var warning);
                LoadWarning = warning;
                if (warning != null) Debug.LogWarning("[QSE] settings: " + warning + " (using defaults where needed)");
            }
            return Settings;
        }

        public void MarkDirty()
        {
            float now = Time.unscaledTime;
            if (!dirty) firstDirtyTime = now;
            lastDirtyTime = now;
            dirty = true;
        }

        void Update()
        {
            if (!dirty) return;
            float now = Time.unscaledTime;
            if (now - lastDirtyTime >= saveDelaySec || now - firstDirtyTime >= maxSaveDelaySec) Save();
        }

        public void Save()
        {
            if (!dirty || Settings == null) return;
            PlayerPrefs.SetString(PrefsKey, SettingsCodec.Encode(Settings));
            PlayerPrefs.Save();
            dirty = false;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) Save();
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused) Save();
        }

        void OnApplicationQuit() => Save();

        void OnDisable() => Save();
    }
}
