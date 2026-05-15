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
- 素早い・俊敏 → startup小(0.02-0.08)・recovery小・knockback小・移動性能を大きく高める
- 重い・大型・遅い → startup大(0.16-0.32)・recovery大・damage大・knockback大・移動性能を大きく低める
- 連続攻撃 → hit_count多(3-4)・damage/hit小
- 一撃必殺 → hit_count=1・damage大・startup大・recovery長
- 遠距離タイプ → attack_a/b/cにprojectileを多めに使い、射程・弾速・発射高さを技ごとに変える
- 近距離タイプ → attack_a/b/cにmelee_hitboxやdashを多めに使い、短い小技・長い突き・上段攻撃・低姿勢攻撃を混ぜる
- 技4枠は同じ構造にしない。最低2枠は action構成、range、spawn_y、hit_count、startup/recovery の傾向を明確に変える
- キャラクター性能も特徴から推論する。素早い/軽い → 地上・空中移動速度とジャンプ力高め、ガード耐久低め、lightness高め、weight低め
- 重い/大型/鎧/頑丈 → 移動とジャンプ低め、ガード耐久高め、lightness低め、weight高め
- 飛行/風/鳥/浮遊 → 空中移動速度とジャンプ力高め
- 回避距離も特徴から推論する。素早い/忍者/軽量 → groundDodgeDistanceとairDodgeDistance高め、重い/大型/鈍重 → 低め、飛行/風/浮遊 → airDodgeDistance高め
- stats範囲: groundMoveSpeed 2.5〜9.5、airMoveSpeed 2.0〜8.5、jumpForce 7〜19、guardDurability 40〜90、lightness 0.45〜2.0、weight 0.45〜2.0、groundDodgeDistance 1.2〜3.8、airDodgeDistance 0.8〜3.2
- damage範囲: attack_a/bは6〜14、attack_cは4〜12、smash_sideは18〜30
- startup範囲: attack_a 0.02〜0.12、attack_b 0.03〜0.18、attack_c 0.04〜0.22、smash_side 0.08〜0.32
- recovery範囲: attack_a 0.08〜0.45、attack_b 0.14〜0.75、attack_c 0.22〜1.05、smash_side 0.38〜1.45
- range意味: 近距離/特殊/スマッシュはヒットボックスサイズ(0.7〜3.6)、遠距離は弾の射程(5〜22)。小技は短く、槍・鞭・魔法刃などは長くしてよい
- knockback範囲: attack_a/b 1〜8、attack_c 2〜10、smash_side 6〜14
- melee_hitbox/projectile actionには必要に応じて spawn_x / spawn_y / size_y を指定する。projectileは頭〜胸の高さ(spawn_y 0.75〜1.35)から出す。近接は下段0.05〜0.35、胴体0.35〜0.8、上段0.8〜1.3を技に応じて使い分け、低い足元固定は禁止。

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
    ""weight"": [推論],
    ""groundDodgeDistance"": [推論],
    ""airDodgeDistance"": [推論]
  }},
  ""skills"": [
    {{
      ""slot"": ""attack_a"",
      ""skill_name"": ""[基本技A名（日本語）]"",
      ""description"": ""[技説明30字以内]"",
      ""element"": ""[特徴に合う属性]"",
      ""risk_level"": ""medium"",
      ""parameters"": {{""damage"":[推論],""hit_count"":[推論],""range"":[0.7〜3.4],""startup"":[推論],""active_time"":[推論],""recovery"":[推論],""knockback"":[推論],""stun_time"":[推論],""guard_damage"":[推論],""move_force"":[推論]}},
      ""actions"": [
        {{""type"":""dash"",""time"":0.0,""power"":[推論],""direction"":""forward""}},
        {{""type"":""melee_hitbox"",""time"":[推論],""range"":[0.7〜3.4],""spawn_x"":[0.6〜1.6],""spawn_y"":[0.1〜1.1],""size_y"":[0.7〜1.8],""hit_count"":1}}
      ]
    }},
    {{
      ""slot"": ""attack_b"",
      ""skill_name"": ""[基本技B名（日本語）]"",
      ""description"": ""[技説明30字以内]"",
      ""element"": ""[特徴に合う属性]"",
      ""risk_level"": ""medium"",
      ""parameters"": {{""damage"":[推論],""range"":[5〜22],""startup"":[推論],""active_time"":0.1,""recovery"":[推論],""knockback"":[推論],""guard_damage"":[推論]}},
      ""actions"": [
        {{""type"":""projectile"",""time"":[推論],""spawn_x"":[0.6〜1.2],""spawn_y"":[0.75〜1.35],""projectile_speed"":[6〜16],""projectile_lifetime"":[0.6〜2.4]}}
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
        {{""type"":""melee_hitbox"",""time"":[推論],""range"":[0.8〜3.6],""spawn_x"":[0.7〜1.8],""spawn_y"":[0.05〜1.25],""size_y"":[0.8〜2.0],""hit_count"":1}},
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
        {{""type"":""melee_hitbox"",""time"":[推論],""range"":[1.4〜4.0],""spawn_x"":[1.0〜2.2],""spawn_y"":[0.25〜1.25],""size_y"":[1.2〜2.3],""hit_count"":1}}
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

注意: [推論]をすべて数値に置き換えること。statsは必ず数値で出力し、lightnessは大きいほど軽く、weightは大きいほど重くノックバックを受けにくい。groundDodgeDistanceは地上横回避の移動距離、airDodgeDistanceは空中横回避の移動距離。elementはphysical/fire/ice/lightning/dark/windから選択。
base_visual_prompt は外見の説明のみを英語で記述し、実在のゲーム・アニメ・映画・漫画の版権キャラクター名・作品名・固有名詞を絶対に含めないこと。髪色・服装・体格・武器・色合いなど視覚的特徴のみで説明すること。";
    }
}
