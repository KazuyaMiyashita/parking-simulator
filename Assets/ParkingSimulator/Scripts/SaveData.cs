using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ParkingSim
{
    /// <summary>記録と設定。persistentDataPath の save.json に保存する</summary>
    [System.Serializable]
    public class SaveData
    {
        // 記録
        public int parkings;          // 駐車回数(枠に収まってエンジンを切った回数)
        public float distance;        // 総走行距離(m)
        public List<string> visited = new List<string>(); // 一度でも停めた場所(Place.id)
        public bool cleared;

        // 設定
        public bool creep;
        public bool sensorSound = true;
        public bool clearance3D;      // 他の車などとの距離を 3D で表示する
        public float volume = 0.8f;
        public int carIndex;

        [System.NonSerialized] public bool readOnly; // 動作確認(autoshot)中は保存しない

        static string FilePath => Path.Combine(Application.persistentDataPath, "save.json");

        public static SaveData Load()
        {
            try
            {
                if (File.Exists(FilePath)) return JsonUtility.FromJson<SaveData>(File.ReadAllText(FilePath)) ?? new SaveData();
            }
            catch (System.Exception e) { Debug.LogWarning($"記録を読み込めませんでした: {e.Message}"); }
            return new SaveData();
        }

        public void Save()
        {
            if (readOnly) return;
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(this, true)); }
            catch (System.Exception e) { Debug.LogWarning($"記録を保存できませんでした: {e.Message}"); }
        }

        public void ResetRecords()
        {
            parkings = 0;
            distance = 0f;
            visited.Clear();
            cleared = false;
        }

        public bool Visited(Place p) => p != null && visited.Contains(p.id);
    }
}
