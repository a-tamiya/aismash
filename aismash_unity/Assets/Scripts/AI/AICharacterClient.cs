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
            if (!AIImageClient.IsConfiguredApiKey(key))
            {
                onError?.Invoke("OpenAI APIキーが未設定です。環境変数 OPENAI_API_KEY を確認してください。");
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
                string responseText = req.downloadHandler?.text;
                Debug.LogWarning($"[AI] 通信エラー: {req.error}\n{responseText}");
                onError?.Invoke($"{req.error}: {responseText}");
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
- 素早い・俊敏 → startup小(0.05-0.10)・recovery小・knockback小
- 重い・大型・遅い → startup大(0.20-0.35)・recovery大・damage大・knockback大
- 連続攻撃 → hit_count多(3-4)・damage/hit小
- 一撃必殺 → hit_count=1・damage大・startup大・recovery長
- 遠距離タイプ → attack_a/b/cにprojectileを多めに使う
- 近距離タイプ → attack_a/b/cにmelee_hitboxやdashを多めに使う
- キャラクター性能も特徴から推論する。素早い/軽い → 地上・空中移動速度とジャンプ力高め、ガード耐久低め、lightness高め、weight低め
- 重い/大型/鎧/頑丈 → 移動とジャンプ低め、ガード耐久高め、lightness低め、weight高め
- 飛行/風/鳥/浮遊 → 空中移動速度とジャンプ力高め
- stats範囲: groundMoveSpeed 3.2〜7.5、airMoveSpeed 2.5〜6.5、jumpForce 8〜16、guardDurability 40〜90、lightness 0.6〜1.6、weight 0.6〜1.6
- damage範囲: attack_a/bは6〜14、attack_cは4〜12、smash_sideは18〜30
- startup範囲: attack_a 0.02〜0.06、attack_b 0.03〜0.10、attack_c 0.03〜0.10、smash_side 0.08〜0.18
- recovery範囲: attack_a 0.10〜0.30、attack_b 0.18〜0.50、attack_c 0.30〜0.80、smash_side 0.45〜1.20
- range意味: 近距離/特殊/スマッシュはヒットボックスサイズ(1.0〜2.5)、遠距離は弾の射程(8〜16)
- knockback範囲: attack_a/b 1〜8、attack_c 2〜10、smash_side 6〜14
- melee_hitbox/projectile actionには必要に応じて spawn_x / spawn_y / size_y を指定する。projectileは頭〜胸の高さ(spawn_y 0.8〜1.2)から出す。低い足元固定は禁止。

{{
  ""character_name"": ""{name}"",
  ""input_features"": ""{features}"",
  ""base_visual_prompt"": ""2D anime standing character, full body, [英語で外見のみ説明。実在のゲーム・アニメ・版権キャラクター名は絶対に含めず、髪色・服装・体格・特徴的なアイテムのみ記述する]"",
  ""visual_description"": ""[日本語で外見説明]"",
  ""stats"": {{
    ""groundMoveSpeed"": [推論],
    ""airMoveSpeed"": [推論],
    ""jumpForce"": [推論],
    ""guardDurability"": [推論],
    ""lightness"": [推論],
    ""weight"": [推論]
  }},
  ""skills"": [
    {{
      ""slot"": ""attack_a"",
      ""skill_name"": ""[基本技A名（日本語）]"",
      ""description"": ""[技説明30字以内]"",
      ""element"": ""[特徴に合う属性]"",
      ""risk_level"": ""medium"",
      ""parameters"": {{""damage"":[推論],""hit_count"":[推論],""range"":[1.0〜2.5],""startup"":[推論],""active_time"":[推論],""recovery"":[推論],""knockback"":[推論],""stun_time"":[推論],""guard_damage"":[推論],""move_force"":[推論]}},
      ""actions"": [
        {{""type"":""dash"",""time"":0.0,""power"":[推論],""direction"":""forward""}},
        {{""type"":""melee_hitbox"",""time"":[推論],""range"":[1.0〜2.5],""spawn_x"":1.0,""spawn_y"":0.35,""size_y"":1.2,""hit_count"":1}}
      ]
    }},
    {{
      ""slot"": ""attack_b"",
      ""skill_name"": ""[基本技B名（日本語）]"",
      ""description"": ""[技説明30字以内]"",
      ""element"": ""[特徴に合う属性]"",
      ""risk_level"": ""medium"",
      ""parameters"": {{""damage"":[推論],""range"":12.0,""startup"":[推論],""active_time"":0.1,""recovery"":[推論],""knockback"":[推論],""guard_damage"":[推論]}},
      ""actions"": [
        {{""type"":""projectile"",""time"":[推論],""spawn_x"":0.8,""spawn_y"":1.05,""projectile_speed"":10.0,""projectile_lifetime"":1.5}}
      ]
    }},
    {{
      ""slot"": ""attack_c"",
      ""skill_name"": ""[基本技C名（日本語）]"",
      ""description"": ""[技説明30字以内]"",
      ""element"": ""[特徴に合う属性]"",
      ""risk_level"": ""medium"",
      ""parameters"": {{""damage"":[推論],""range"":[推論],""startup"":[推論],""active_time"":[推論],""recovery"":[推論],""knockback"":[推論],""stun_time"":[推論]}},
      ""actions"": [
        {{""type"":""dash"",""time"":0.0,""power"":[推論],""direction"":""forward""}},
        {{""type"":""melee_hitbox"",""time"":[推論],""range"":[推論],""spawn_x"":1.1,""spawn_y"":0.15,""size_y"":1.45,""hit_count"":1}},
        {{""type"":""apply_status"",""time"":[推論],""status"":""stun"",""duration"":[推論],""chance"":[推論]}}
      ]
    }},
    {{
      ""slot"": ""smash_side"",
      ""skill_name"": ""[横スマッシュ名（日本語）]"",
      ""description"": ""[技説明30字以内]"",
      ""element"": ""[特徴に合う属性]"",
      ""risk_level"": ""high"",
      ""parameters"": {{""damage"":[推論],""range"":[推論],""startup"":[推論],""active_time"":[推論],""recovery"":[推論],""knockback"":[推論],""stun_time"":[推論],""guard_damage"":[推論]}},
      ""actions"": [
        {{""type"":""dash"",""time"":0.1,""power"":[推論],""direction"":""forward""}},
        {{""type"":""melee_hitbox"",""time"":[推論],""range"":[推論],""spawn_x"":1.25,""spawn_y"":0.55,""size_y"":1.7,""hit_count"":1}}
      ]
    }}
  ],
  ""grab_parameters"": {{
    ""range"": 1.5,
    ""startup"": 0.12,
    ""recovery"": 1.0
  }},
  ""throw_parameters"": {{
    ""front_damage"": 10,
    ""front_knockback"": 8,
    ""back_damage"": 10,
    ""back_knockback"": 10
  }}
}}

注意: [推論]をすべて数値に置き換えること。statsは必ず数値で出力し、lightnessは大きいほど軽く、weightは大きいほど重くノックバックを受けにくい。elementはphysical/fire/ice/lightning/dark/windから選択。
base_visual_prompt は外見の説明のみを英語で記述し、実在のゲーム・アニメ・映画・漫画の版権キャラクター名・作品名・固有名詞を絶対に含めないこと。髪色・服装・体格・武器・色合いなど視覚的特徴のみで説明すること。";
    }
}
