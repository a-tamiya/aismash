using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using PromptFighters.Battle.Skills;
using PromptFighters.Battle.Skills.Json;

namespace PromptFighters.AI
{
    // OpenAI Chat Completions API でキャラクターデータ・技JSONを生成する。
    public static class AICharacterClient
    {
        public static readonly string Endpoint = "https://api.openai.com/v1/chat/completions";
        public static readonly string Model    = "gpt-5.4-nano";

        public static string ApiKey => AIImageClient.ApiKey;

        public static Coroutine Generate(MonoBehaviour runner,
            string characterName, string features,
            Action<CharacterData> onSuccess, Action<string> onError)
        {
            return runner.StartCoroutine(GenerateCoroutine(characterName, features, onSuccess, onError));
        }

        static IEnumerator GenerateCoroutine(
            string characterName, string features,
            Action<CharacterData> onSuccess, Action<string> onError)
        {
            string key = ApiKey;
            if (string.IsNullOrEmpty(key))
            {
                onError?.Invoke("OpenAI APIキーが未設定です");
                yield break;
            }

            string prompt = BuildPrompt(characterName, features);
            string body   = BuildBody(prompt);

            using var req = new UnityWebRequest(Endpoint, "POST");
            req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + key);
            req.timeout = 120;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[AI] 通信エラー: {req.error}");
                onError?.Invoke(req.error);
                yield break;
            }

            try
            {
                string content = ParseContent(req.downloadHandler.text);
                string json    = ExtractJsonBlock(content);
                var data = SkillJsonParser.ParseOrFallback(json, characterName);
                if (string.IsNullOrEmpty(data.characterName)) data.characterName = characterName;
                if (string.IsNullOrEmpty(data.inputFeatures)) data.inputFeatures = features;
                onSuccess?.Invoke(data);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AI] 解析エラー: {e.Message}\nレスポンス: {req.downloadHandler.text}");
                onError?.Invoke("AI応答の解析に失敗: " + e.Message);
            }
        }

        // OpenAI Chat Completions レスポンスから content を取り出す
        [Serializable] class OAIResp   { public OAIChoice[] choices; }
        [Serializable] class OAIChoice { public OAIMsg message; }
        [Serializable] class OAIMsg    { public string content; }

        public static string ParseContent(string responseText)
        {
            var resp = JsonUtility.FromJson<OAIResp>(responseText);
            if (resp?.choices != null && resp.choices.Length > 0 && resp.choices[0].message?.content != null)
                return resp.choices[0].message.content;
            throw new Exception("choices[0].message.contentが見つかりません");
        }

        // LLM出力中の最初の { ... } ブロックを抽出する
        static string ExtractJsonBlock(string text)
        {
            int depth = 0;
            int start = -1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '{')
                {
                    if (depth == 0) start = i;
                    depth++;
                }
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                        return text.Substring(start, i - start + 1);
                }
            }
            throw new Exception("JSONブロックが見つかりません");
        }

        static string BuildBody(string prompt)
        {
            string esc = prompt
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "")
                .Replace("\t", " ");
            return $"{{\"model\":\"{Model}\",\"messages\":[{{\"role\":\"user\",\"content\":\"{esc}\"}}]}}";
        }

        static string BuildPrompt(string name, string features) =>
$@"2D格闘ゲームのキャラクターJSONを生成してください。JSONのみ出力（説明不要）。

キャラクター名: {name}
特徴: {features}

【パラメーター設計ガイド】キャラの特徴からパラメーターを推論してください:
- 素早い・俊敏 → startup小(0.08-0.12)・recovery小・cooldown短・knockback小
- 重い・大型・遅い → startup大(0.3-0.5)・recovery大・damage大・knockback大
- 連続攻撃 → hit_count多(3-4)・damage/hit小
- 一撃必殺 → hit_count=1・damage大・startup大・cooldown長
- 遠距離タイプ → ranged cooldown短・close cooldown長
- 近距離タイプ → close cooldown短・ranged cooldown長
- damage範囲: 近距離8〜14、遠距離6〜12、特殊4〜10、必殺18〜30
- range意味: 近距離/特殊/必殺はヒットボックスサイズ(1.0〜2.5)、遠距離は弾の射程(8〜16)
- knockback範囲: 近距離1〜8、遠距離1〜6、特殊2〜10、必殺6〜14

{{
  ""character_name"": ""{name}"",
  ""input_features"": ""{features}"",
  ""visual_prompt"": ""2D anime standing character, full body, [英語で外見説明]"",
  ""visual_description"": ""[日本語で外見説明]"",
  ""skills"": [
    {{
      ""slot"": ""close"",
      ""skill_name"": ""[近距離技名（日本語）]"",
      ""description"": ""[技説明30字以内]"",
      ""element"": ""[特徴に合う属性]"",
      ""risk_level"": ""medium"",
      ""parameters"": {{""damage"":[推論],""hit_count"":[推論],""range"":[1.0〜2.5],""startup"":[推論],""active_time"":[推論],""recovery"":[推論],""cooldown"":[推論],""knockback"":[推論],""stun_time"":[推論],""guard_damage"":[推論],""move_force"":[推論]}},
      ""actions"": [
        {{""type"":""dash"",""time"":0.0,""power"":[推論],""direction"":""forward""}},
        {{""type"":""melee_hitbox"",""time"":[推論],""range"":[1.0〜2.5],""hit_count"":1}}
      ]
    }},
    {{
      ""slot"": ""ranged"",
      ""skill_name"": ""[遠距離技名（日本語）]"",
      ""description"": ""[技説明30字以内]"",
      ""element"": ""[特徴に合う属性]"",
      ""risk_level"": ""medium"",
      ""parameters"": {{""damage"":[推論],""range"":12.0,""startup"":[推論],""active_time"":0.1,""recovery"":[推論],""cooldown"":[推論],""knockback"":[推論],""guard_damage"":[推論]}},
      ""actions"": [
        {{""type"":""projectile"",""time"":[推論],""projectile_speed"":10.0,""projectile_lifetime"":1.5}}
      ]
    }},
    {{
      ""slot"": ""special"",
      ""skill_name"": ""[特殊技名（日本語）]"",
      ""description"": ""[技説明30字以内]"",
      ""element"": ""[特徴に合う属性]"",
      ""risk_level"": ""medium"",
      ""parameters"": {{""damage"":[推論],""range"":[推論],""startup"":[推論],""active_time"":[推論],""recovery"":[推論],""cooldown"":[推論],""knockback"":[推論],""stun_time"":[推論]}},
      ""actions"": [
        {{""type"":""dash"",""time"":0.0,""power"":[推論],""direction"":""forward""}},
        {{""type"":""melee_hitbox"",""time"":[推論],""range"":[推論],""hit_count"":1}},
        {{""type"":""apply_status"",""time"":[推論],""status"":""stun"",""duration"":[推論],""chance"":[推論]}}
      ]
    }},
    {{
      ""slot"": ""ultimate"",
      ""skill_name"": ""[必殺技名（日本語）]"",
      ""description"": ""[技説明30字以内]"",
      ""element"": ""[特徴に合う属性]"",
      ""risk_level"": ""high"",
      ""parameters"": {{""damage"":[推論],""range"":[推論],""startup"":[推論],""active_time"":[推論],""recovery"":[推論],""cooldown"":[推論],""knockback"":[推論],""stun_time"":[推論],""guard_damage"":[推論]}},
      ""actions"": [
        {{""type"":""dash"",""time"":0.1,""power"":[推論],""direction"":""forward""}},
        {{""type"":""melee_hitbox"",""time"":[推論],""range"":[推論],""hit_count"":1}}
      ]
    }}
  ]
}}

注意: [推論]をすべて数値に置き換えること。elementはphysical/fire/ice/lightning/dark/windから選択。";
    }
}
