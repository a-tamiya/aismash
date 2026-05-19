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
- 連続攻撃 → hit_count多(3-4)・damage/hit小。全段ヒット時の合計ダメージは最大6程度に抑える
- 一撃必殺 → hit_count=1・damage大・startup大・recovery長
- 遠距離タイプ → attack_a/b/cにprojectile、trap_hitbox、area_hitboxを多めに使い、必ず近距離技を混ぜる必要はない
- 近距離タイプ → attack_a/b/cにmelee_hitbox、dash+melee_hitbox、jump_attack、area_hitboxを多めに使い、必ず遠距離技を混ぜる必要はない
- 格闘型・素手・武器や魔法を使わないタイプ → body_hitboxを使う。技エフェクト画像に頼らず、キャラの拳・蹴り・体当たり部分に攻撃判定を置く
- 設置・制圧タイプ → trap_hitboxやarea_hitboxを使い、少し前方・足元・空中などに残る攻撃を作る
- 補助・攪乱タイプ → buff_selfやteleportを使ってよい。buff_selfのstatusは speed / jump / transparent / damage から選ぶ。transparentは無敵ではなく半透明化のみ
- 技4枠は同じ構造にしない。最低2枠は action構成、range、spawn_y、size_x/size_y、hit_count、startup/recovery の傾向を明確に変える
- 属性と状態異常をキャラ性に合わせる。fireはburn、iceはslow、lightningはstun、darkはpull_enemy/guard_break、windはpush_enemy/dash/jump_attackと相性が良い
- キャラクター性能も特徴から推論する。素早い/軽い → 地上・空中移動速度とジャンプ力高め、ガード耐久低め、lightness高め、weight低め
- 重い/大型/鎧/頑丈 → 移動とジャンプ低め、ガード耐久高め、lightness低め、weight高め
- 飛行/風/鳥/浮遊 → 空中移動速度とジャンプ力高め
- 回避距離も特徴から推論する。素早い/忍者/軽量 → groundDodgeDistanceとairDodgeDistance高め、重い/大型/鈍重 → 低め、飛行/風/浮遊 → airDodgeDistance高め
- 2段ジャンプ高さと歩き速度も特徴から推論する。軽快/身軽/空中戦 → airJumpHeightMultiplier高め、重い/鈍重 → 低め。慎重/武人/重装 → walkSpeedRatio低め、素早い/軽快 → 高め
- stats範囲: groundMoveSpeed 2.5〜9.5、airMoveSpeed 2.0〜8.5、jumpForce 7〜19、airJumpHeightMultiplier 0.3〜0.6、walkSpeedRatio 0.2〜0.5、guardDurability 40〜90（Unity側で実耐久値は2倍にする）、lightness 0.45〜2.0、weight 0.45〜2.0、groundDodgeDistance 1.2〜3.8、airDodgeDistance 0.8〜3.2
- damage範囲: attack_a/bは4〜12、attack_cは3〜10、smash_sideは14〜26。projectileなど遠距離攻撃は単発でも4〜6程度を基本にする。多段技は1ヒット1〜2程度にし、全段ヒットで最大6程度にする
- startup範囲: attack_a 0.02〜0.12、attack_b 0.03〜0.18、attack_c 0.04〜0.22、smash_side 0.08〜0.32
- recovery範囲: attack_a 0.10〜0.50、attack_b 0.16〜0.78、attack_c 0.24〜1.05、smash_side 0.18〜0.62。技連打は避けるが、操作感が重すぎない範囲にする
- range意味: 近距離/特殊/スマッシュはヒットボックスサイズ(0.7〜3.6)、遠距離は弾の射程(5〜22)。小技は短く、槍・鞭・魔法刃などは長くしてよい
- knockback範囲: attack_a/b 2〜10、attack_c 3〜12、smash_side 7〜16。技ハメ防止のため、命中時に相手が少し離れる程度のノックバックを必ず持たせる
- guard_damage範囲: attack_a 0.5〜2.0、attack_b 0.8〜2.6、attack_c 0.8〜2.8、smash_side 2.0〜5.0。ガード耐久値は減りすぎないよう控えめにする
- action typeは melee_hitbox / body_hitbox / projectile / area_hitbox / trap_hitbox / dash / jump_attack / push_enemy / pull_enemy / apply_status / buff_self / teleport / delay を使用可。
- melee_hitbox/projectile/area_hitbox/trap_hitbox actionには必要に応じて spawn_x / spawn_y / size_x / size_y を指定する。Unity側でエフェクト表示サイズと当たり判定サイズは一致するため、見た目どおりの大きさを数値化する。
- body_hitboxは格闘・体術用。hide_effect=true、follow_owner=trueを付け、拳・脚・体の位置に判定を置く。武器や魔法を使わないキャラではbody_hitboxを優先する。
- projectileは頭〜胸の高さ(spawn_y 0.75〜1.35)から出す。近接は下段0.05〜0.35、胴体0.35〜0.8、上段0.8〜1.3を技に応じて使い分け、低い足元固定は禁止。
- 移動しながら当てる攻撃は dash → melee_hitbox にし、追従させたい melee_hitbox / area_hitbox には ""follow_owner"": true を付ける。
- 置き技は trap_hitbox を使い、duration 2.0〜5.0、size_x/size_y、spawn_x/spawn_y を必ず指定する。本人のrecoveryはtrapのdurationより短くしてよい。
- 多段攻撃はhit_countを2〜6にし、damageは1ヒットあたり低くする。全段ヒット時の合計火力は最大6程度にする。

{{
  ""character_name"": ""{name}"",
  ""input_features"": ""{features}"",
  ""base_visual_prompt"": ""2D anime standing character, full body, [英語で外見のみ説明。実在のゲーム・アニメ・版権キャラクター名は絶対に含めず、髪色・服装・体格・特徴的なアイテムのみ記述する]"",
  ""visual_description"": ""[日本語で外見説明]"",
  ""stats"": {{
    ""groundMoveSpeed"": [推論],
    ""airMoveSpeed"": [推論],
    ""jumpForce"": [推論],
    ""airJumpHeightMultiplier"": [推論],
    ""walkSpeedRatio"": [推論],
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
        {{""type"":""body_hitbox"",""time"":[推論],""range"":[0.7〜1.4],""spawn_x"":[0.3〜0.9],""spawn_y"":[0.2〜1.1],""size_y"":[0.6〜1.5],""hit_count"":[1〜4],""follow_owner"":true,""hide_effect"":true}}
      ]
    }},
    {{
      ""slot"": ""attack_b"",
      ""skill_name"": ""[基本技B名（日本語）]"",
      ""description"": ""[技説明30字以内]"",
      ""element"": ""[特徴に合う属性]"",
      ""risk_level"": ""medium"",
      ""parameters"": {{""damage"":[4〜6],""range"":[5〜22],""startup"":[推論],""active_time"":0.1,""recovery"":[推論],""knockback"":[推論],""guard_damage"":[推論]}},
      ""actions"": [
        {{""type"":""projectile"",""time"":[推論],""spawn_x"":[0.6〜1.2],""spawn_y"":[0.75〜1.35],""size_x"":[0.7〜2.0],""size_y"":[0.4〜1.4],""projectile_speed"":[6〜16],""projectile_lifetime"":[0.6〜2.4],""status"":""[必要なら状態異常]"",""duration"":[推論],""chance"":[推論]}}
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
        {{""type"":""trap_hitbox"",""time"":[推論],""duration"":[2.0〜5.0],""spawn_x"":[0.8〜2.6],""spawn_y"":[0.05〜1.2],""size_x"":[0.8〜3.2],""size_y"":[0.5〜2.0],""hit_count"":[1〜3],""status"":""[属性に合う状態異常]"",""chance"":[推論]}}
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
    ""startup"": 0.08,
    ""recovery"": 0.14
  }},
  ""throw_parameters"": {{
    ""front_damage"": 10,
    ""front_knockback"": 8,
    ""back_damage"": 10,
    ""back_knockback"": 10,
    ""up_damage"": 10,
    ""up_knockback"": 9
  }}
}}

注意: [推論]をすべて数値に置き換えること。statsは必ず数値で出力し、lightnessは大きいほど軽く、weightは大きいほど重くノックバックを受けにくい。airJumpHeightMultiplierは2段ジャンプ高さの地上ジャンプ比率、walkSpeedRatioは歩き速度のダッシュ速度比率、groundDodgeDistanceは地上横回避の移動距離、airDodgeDistanceは空中横回避の移動距離。elementはphysical/fire/ice/lightning/dark/windから選択。
base_visual_prompt は外見の説明のみを英語で記述し、実在のゲーム・アニメ・映画・漫画の版権キャラクター名・作品名・固有名詞を絶対に含めないこと。髪色・服装・体格・武器・色合いなど視覚的特徴のみで説明すること。";
    }
}
