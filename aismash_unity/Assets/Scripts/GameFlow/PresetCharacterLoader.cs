using System.Collections.Generic;
using System.IO;
using UnityEngine;
using PromptFighters.Battle.Skills;
using PromptFighters.Battle.Skills.Json;

namespace PromptFighters.GameFlow
{
    // StreamingAssets/Presets/ 以下のJSONをロードしてCharacterDataを返す。
    public static class PresetCharacterLoader
    {
        static List<CharacterData> _cache;

        public static List<CharacterData> LoadAll()
        {
            if (_cache != null) return _cache;
            _cache = new List<CharacterData>();

            string dir = Path.Combine(Application.streamingAssetsPath, "Presets");
            if (!Directory.Exists(dir))
            {
                Debug.LogWarning("[PresetLoader] Presetsフォルダが見つかりません: " + dir);
                return _cache;
            }

            foreach (var path in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var data = SkillJsonParser.Parse(json);
                    if (data != null)
                    {
                        _cache.Add(data);
                        Debug.Log($"[PresetLoader] ロード成功: {data.characterName}");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[PresetLoader] 読み込みエラー ({Path.GetFileName(path)}): {e.Message}");
                }
            }

            return _cache;
        }

        public static void ClearCache() => _cache = null;
    }
}
