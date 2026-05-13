using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using PromptFighters.Battle.Skills;
using PromptFighters.Battle.Skills.Json;

namespace PromptFighters.AI
{
    // Ollama（開発中）またはOpenAI（展示時）にリクエストしてCharacterDataを生成する。
    public static class AICharacterClient
    {
        public static string OllamaEndpoint = "http://localhost:11434/api/chat";
        public static string OllamaModel    = "qwen3-vl:8b";

        // runner MonoBehaviourのStartCoroutineを使って非同期生成を実行する。
        // 成功時はonSuccess(CharacterData)、失敗時はonError(エラー文字列)を呼ぶ。
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
            string prompt = BuildPrompt(characterName, features);
            string body   = BuildBody(prompt);

            using var req = new UnityWebRequest(OllamaEndpoint, "POST");
            req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
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
                string content = ParseOllamaContent(req.downloadHandler.text);
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

        // Ollama /api/chat レスポンスからcontent文字列を取り出す
        [Serializable] class OllamaResp { public OllamaMsg message; }
        [Serializable] class OllamaMsg  { public string content; }

        static string ParseOllamaContent(string responseText)
        {
            var resp = JsonUtility.FromJson<OllamaResp>(responseText);
            if (resp?.message?.content != null) return resp.message.content;
            throw new Exception("message.contentフィールドが見つかりません");
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
            // qwen3系はthinkingモードを無効化して高速化 (/no_think)
            string actualPrompt = OllamaModel.StartsWith("qwen3")
                ? prompt + "\n/no_think"
                : prompt;

            string esc = actualPrompt
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "")
                .Replace("\t", " ");
            return $"{{\"model\":\"{OllamaModel}\",\"messages\":[{{\"role\":\"user\",\"content\":\"{esc}\"}}],\"stream\":false}}";
        }

        static string BuildPrompt(string name, string features) =>
$@"あなたは2D格闘ゲーム「プロンプトファイターズ」のキャラクター生成AIです。
以下のキャラクター情報からゲーム用JSONデータを生成してください。JSONのみを出力してください（前後に説明文を入れないでください）。

キャラクター名: {name}
特徴: {features}

以下のJSON形式で出力:
{{
  ""character_name"": ""{name}"",
  ""input_features"": ""{features}"",
  ""visual_prompt"": ""2D anime standing character, full body, [英語で外見説明]"",
  ""visual_description"": ""[日本語で外見説明]"",
  ""skills"": [
    {{
      ""slot"": ""close"",
      ""skill_name"": ""[近距離技名（日本語）]"",
      ""description"": ""[技説明（日本語、30字以内）]"",
      ""element"": ""physical"",
      ""risk_level"": ""medium"",
      ""parameters"": {{""damage"":12,""hit_count"":2,""range"":1.4,""startup"":0.15,""active_time"":0.2,""recovery"":0.4,""cooldown"":1.5,""knockback"":4.0,""stun_time"":0.2,""guard_damage"":2.0,""move_force"":0.3}},
      ""actions"": [
        {{""type"":""dash"",""time"":0.0,""power"":3.0,""direction"":""forward""}},
        {{""type"":""melee_hitbox"",""time"":0.15,""range"":1.4,""hit_count"":1}},
        {{""type"":""melee_hitbox"",""time"":0.28,""range"":1.4,""hit_count"":1}}
      ]
    }},
    {{
      ""slot"": ""ranged"",
      ""skill_name"": ""[遠距離技名（日本語）]"",
      ""description"": ""[技説明（日本語、30字以内）]"",
      ""element"": ""fire"",
      ""risk_level"": ""medium"",
      ""parameters"": {{""damage"":10,""range"":12.0,""startup"":0.2,""active_time"":0.1,""recovery"":0.4,""cooldown"":2.5,""knockback"":4.0,""guard_damage"":3.0}},
      ""actions"": [
        {{""type"":""projectile"",""time"":0.2,""projectile_speed"":10.0,""projectile_lifetime"":1.5}}
      ]
    }},
    {{
      ""slot"": ""special"",
      ""skill_name"": ""[特殊技名（日本語）]"",
      ""description"": ""[技説明（日本語、30字以内）]"",
      ""element"": ""lightning"",
      ""risk_level"": ""medium"",
      ""parameters"": {{""damage"":7,""range"":1.5,""startup"":0.15,""active_time"":0.15,""recovery"":0.5,""cooldown"":5.0,""knockback"":3.0,""stun_time"":0.5}},
      ""actions"": [
        {{""type"":""dash"",""time"":0.0,""power"":7.0,""direction"":""forward""}},
        {{""type"":""melee_hitbox"",""time"":0.15,""range"":1.5,""hit_count"":1}},
        {{""type"":""apply_status"",""time"":0.15,""status"":""stun"",""duration"":0.5,""chance"":0.8}}
      ]
    }},
    {{
      ""slot"": ""ultimate"",
      ""skill_name"": ""[必殺技名（日本語）]"",
      ""description"": ""[技説明（日本語、30字以内）]"",
      ""element"": ""fire"",
      ""risk_level"": ""high"",
      ""parameters"": {{""damage"":24,""range"":2.0,""startup"":0.45,""active_time"":0.2,""recovery"":0.8,""cooldown"":10.0,""knockback"":9.0,""stun_time"":0.3,""guard_damage"":8.0}},
      ""actions"": [
        {{""type"":""dash"",""time"":0.1,""power"":10.0,""direction"":""forward""}},
        {{""type"":""melee_hitbox"",""time"":0.45,""range"":2.0,""hit_count"":1}}
      ]
    }}
  ]
}}

ルール:
- elementはキャラの特徴から選択: none / physical / fire / ice / lightning / dark / wind
- 近距離技のdamageは8〜14、遠距離技は6〜12、特殊技は4〜10、必殺技は18〜30
- 技名と説明はキャラクターの特徴を反映した日本語にすること
- actionsのtypeはmelee_hitbox/projectile/dash/apply_statusのみ使用可能";
    }
}
