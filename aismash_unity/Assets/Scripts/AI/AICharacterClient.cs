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
        const int MaxGenerateAttempts = 2;

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

            string systemPrompt = BuildSystemPrompt();
            string userPrompt   = BuildUserPrompt(characterName, features);
            string body         = OpenAIRequest.BuildChatBody(Model, systemPrompt, userPrompt, jsonMode: true);
            string lastError = null;

            for (int attempt = 1; attempt <= MaxGenerateAttempts; attempt++)
            {
                using var req = new UnityWebRequest(Endpoint, "POST");
                req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Authorization", "Bearer " + key);
                // 非ストリーミングのため生成中はデータが来ない。低速モデルでは1キャラ2分かかる
                // こともあるので長めに取る（150s×MaxGenerateAttempts回＝最大約5分待つ）。
                req.timeout = 150;

                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    string responseText = req.downloadHandler?.text;
                    lastError = $"{req.error}: {responseText}";
                    Debug.LogWarning($"[AI] 通信エラー({attempt}/{MaxGenerateAttempts}): {lastError}");
                    if (attempt < MaxGenerateAttempts) yield return new WaitForSeconds(1.0f);
                    continue;
                }

                bool parseFailed = false;
                try
                {
                    string content = ParseContent(req.downloadHandler.text);
                    string json    = ExtractJsonBlock(content);
                    var data = SkillJsonParser.ParseOrFallback(json, characterName);
                    if (string.IsNullOrEmpty(data.characterName)) data.characterName = characterName;
                    if (string.IsNullOrEmpty(data.inputFeatures)) data.inputFeatures = features;
                    onSuccess?.Invoke(data);
                    yield break;
                }
                catch (Exception e)
                {
                    lastError = "AI応答の解析に失敗: " + e.Message;
                    Debug.LogWarning($"[AI] 解析エラー({attempt}/{MaxGenerateAttempts}): {e.Message}\nレスポンス: {req.downloadHandler.text}");
                    parseFailed = true;
                }

                if (parseFailed && attempt < MaxGenerateAttempts)
                    yield return new WaitForSeconds(0.5f);
            }

            onError?.Invoke(lastError ?? "AI生成に失敗しました");
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

        // 信頼できる固定指示。ユーザー入力を含まないため、毎回同一でprompt cacheが効く。
        static string BuildUserPrompt(string name, string features) =>
$@"以下の名前と特徴でキャラクターJSONを生成してください。
キャラクター名: {name}
特徴: {features}";

        static string BuildSystemPrompt() =>
$@"2D格闘ゲームのキャラクターJSONを生成してください。JSONのみ出力（説明不要）。
ユーザーメッセージで与えられるキャラクター名と特徴に基づいて生成すること。

━━━━━━━━━━━━━━━━━━━━━━━━━━━
【必須：技配分ルール（テンプレートより優先）】
特徴キーワードから4枠の技構成を決めること。

● 近距離特化（インファイト/格闘/体術/乱闘/接近/肉弾）
  → 近距離技(melee_hitbox/body_hitbox/dash+melee/jump_attack/area_hitbox)を3〜4枠
  → projectileは原則使用禁止。smash_sideのみで使う場合も1枠まで
  → attack_b/cも melee系/area系/jump_attack/buff_self/apply_status から選ぶ

● 遠距離特化（魔法師/弓/銃/砲/射撃/呪文）
  → projectile/trap_hitbox/area_hitboxを2〜3枠
  → 近距離技は1〜2枠でよい

● 設置・制圧型（罠師/トラップ/地雷/設置）
  → trap_hitboxを2〜3枠
  → trap技のrecoveryは0.10〜0.35に抑える（置いてすぐ動ける設計）

● バランス型（上記に当てはまらない）
  → 近距離2〜3枠：遠距離1〜2枠

※ 下記テンプレートのaction typeは「一例」。キャラの特徴に合わせて必ず変更すること。
━━━━━━━━━━━━━━━━━━━━━━━━━━━

【多様性ルール（最重要・テンプレ固定化の防止）】
テンプレートのactionは「穴埋め例」に過ぎない。特徴が違えば技構成も大きく変えること。
- 4枠が「melee/melee/trap/dash+melee」のような無難な定番配置に毎回ならないようにする。特徴に少しでも合えば、summon/counter/reflector/beam/teleport/area_hitbox(cone/ring)/boomerang/homing/落下弾/多段持続 などの個性的な機構を最低1つは積極的に採用する。
- smash_sideも毎回「dash+melee_hitbox」にしない。遠距離キャラはcharge付きprojectile/beam、大型キャラは単発高威力のbody_hitbox/area_hitbox(cone)など、キャラに合った決め技にする。
- 下記のアーキタイプ例を参考に、特徴から最も近いものを選び、その骨格で組む（複数混合も可）。例に無い独自構成も歓迎:
  ・ラッシュ/連撃: 近接中心＋follow_up_actionsを1〜2枠
  ・カウンター/受け: counter or reflectorを1枠＋確定反撃用の近接技
  ・召喚/使い魔: summonを1〜2枠（player_controlled/homingで個性付け）＋自衛の近接1枠
  ・トリックスター/瞬間移動: teleport＋area_hitbox(ring)＋設置や攪乱
  ・砲撃/狙撃: charge付きprojectile/beam＋落下弾(spawn_y高め)＋牽制trap
  ・重量/パワー: 単発高威力body_hitbox/area_hitbox(cone)＋knockback大＋startup大
  ・空中/機動: jump_attack＋空中移動性能高め＋diagonal_up/upノックバック
- element も physical に偏らせない。特徴に色（炎・氷・雷・闇・風）があれば対応する属性と状態異常・機構を必ず結びつける。
- 技名・キャッチコピー・description はテンプレ語（「○○斬り」「○○弾」）の使い回しを避け、そのキャラ固有の語彙で命名する。

【パラメーター設計ガイド】
- 素早い・俊敏 → startup小(0.02-0.08)・recovery小・knockback小・移動性能高め
- 重い・大型・遅い → startup大(0.16-0.32)・recovery大・damage大・knockback大・移動低め
- 連続攻撃 → hit_count多(3-4)・damage/hit小。全段ヒット合計は最大6程度
- 一撃必殺 → hit_count=1・damage大・startup大・recovery長
- 格闘型・素手 → body_hitboxを使う。hide_effect=true・follow_owner=true
- 補助・攪乱タイプ → buff_selfやteleportを使ってよい（status: speed/jump/transparent/damage）
- 技4枠は同じ構造にしない。最低2枠は action構成・range・startup/recovery の傾向を変える
- 属性と状態異常: fire→burn、ice→slow、lightning→stun、dark→pull_enemy/guard_break、wind→push_enemy/dash/jump_attack
- キャラの個性に応じて積極的に新機能を使うこと: ブーメラン使い→boomerang:true、追尾魔法→homing:true、散弾銃→projectile_count:3、ビーム→beam、スパイクコンボ→knockback_direction:""spike""、ジャグル→knockback_direction:""up""
- stats範囲: groundMoveSpeed 2.5〜9.5、airMoveSpeed 2.0〜8.5、jumpForce 7〜19、airJumpHeightMultiplier 0.3〜0.6、walkSpeedRatio 0.2〜0.5、guardDurability 40〜90、lightness 0.45〜2.0、weight 0.45〜2.0、groundDodgeDistance 1.2〜3.8、airDodgeDistance 0.8〜3.2
- damage範囲: attack_a/bは4〜12、attack_cは3〜10、smash_sideは14〜26。多段技は1hit1〜2程度で全段最大6
- smash_side（横スマッシュ）は必ず攻撃技（melee_hitbox/body_hitbox/projectile/beam/jump_attack）にすること。counter/reflector/buff_self/summonをsmash_sideに入れない
- startup範囲: attack_a 0.02〜0.12、attack_b 0.03〜0.18、attack_c 0.04〜0.22、smash_side 0.08〜0.32
- recovery範囲: attack_a 0.10〜0.45、attack_b 0.12〜0.65、attack_c 0.12〜0.55、smash_side 0.18〜0.62。操作感が重すぎない範囲にする
- trap_hitbox設置技のrecoveryは特に0.10〜0.35とし、極端な後隙を避ける
- range: 近距離hitbox 0.7〜3.6、遠距離弾 5〜22
- knockback: attack_a/b 2〜10、attack_c 3〜12、smash_side 7〜16
- guard_damage: attack_a 0.5〜2.0、attack_b 0.8〜2.6、attack_c 0.8〜2.8、smash_side 2.0〜5.0
- action type: melee_hitbox/body_hitbox/projectile/area_hitbox/trap_hitbox/beam/dash/jump_attack/push_enemy/pull_enemy/apply_status/buff_self/teleport/delay/summon/counter/reflector/command_throw/shockwave/gravity_well/lifesteal/heal_self/barrier
- 各技のactionsは空にしない。attack_a/attack_b/attack_c/smash_sideは必ず1つ以上、実際に攻撃・接触・召喚・防御反応などゲーム内効果が起きるactionを入れる
- delay単体、dash単体、teleport単体、apply_status単体、push_enemy単体、pull_enemy単体だけの技は禁止。使う場合はmelee_hitbox/body_hitbox/projectile/area_hitbox/trap_hitbox/beam/jump_attack/summon/counter/reflectorのいずれかと組み合わせる
- body_hitbox: キャラ自身に判定。follow_owner/hide_effect自動。spawn_x=0でキャラ中心(スピン・全身)、spawn_x>0で前方張り出し。size_y 1.4〜2.2で全身カバー
- projectile: spawn_y 0.5〜6.0（通常は頭〜胸の高さ0.75〜1.35。落下弾・隕石は3〜6で上空から発生）
- 落下弾の例: spawn_x:2〜4（前方斜め）、spawn_y:3〜6（上空）、projectile_angle:-80〜-90（真下〜斜め下）、gravity_scale:1.5〜2.5、projectile_speed:4〜8
- beam: 0.3秒ほど溜めてから出る貫通ビーム。beam actionのtimeは0.3以上。size_xで長さ(2〜12)・size_yで太さ・spawn_yで高さ指定。長いビームほどUnity側で縦の厚みも少し増やす。最大5体貫通。発射前はUnity側で色の溜め表示を出す
- 【近接技 判定位置】パンチ/頭突き: spawn_y 0.9〜1.3。ボディ/正拳: spawn_y 0.5〜0.8。キック/下段: spawn_y 0.05〜0.35。薙ぎ払い: size_y 1.5〜2.2（縦広）。全身スピン/体当たり: body_hitboxでspawn_x=0、size_y 1.6〜2.0
- 【持続近接判定】active_time 0.15〜0.4=持続あり(スピン・回転)。0.3〜0.6=長持続(竜巻・ドリル)。持続技はhit_count 2〜5で多段。follow_owner=trueで移動中も持続
- 移動しながら当てる技: dash→melee_hitbox。追従: follow_owner=true
- trap_hitbox: duration 2.0〜5.0、size_x/y・spawn_x/y必須
- 多段: hit_count 2〜6、1hitあたり低ダメ、全段最大6
- 【飛び道具拡張】
  projectile_angle: 発射角度(度数)。0=水平(default)、45=斜め上、-45=斜め下、90=真上(間欠泉)、-90=真下(落石・隕石)
  homing: true=追尾弾。homing_strength 0〜1（追尾の曲がりやすさ、default 0.5）
  boomerang: true=ブーメラン（中間点で折り返す）
  projectile_count: 1〜5発（散弾・扇形）。spread_angle: 発射間の角度(5〜60、default 15)
  gravity_scale: 0=無重力(default)、1〜2=重力あり（山なり弾・落下弾）
- 【ノックバック方向】knockback_direction:
  ""away""=通常(default)、""up""=真上(ジャグル)、""spike""=真下(スパイク)、""toward""=引き寄せ(コンボ)、""diagonal_up""=斜め上(遠くへ)、""ground_bounce""=下方向に叩きつけ地面バウンド（コンボ延長）
- 【新アクション types】
  status:""stun"" を使う場合、duration または status_duration は必ず0.4〜0.7秒にする
  counter: duration=カウンター受付秒(1.0〜1.5)、damage_override=カウンター反撃ダメージ。発動中に攻撃を受けると自動で反撃。黄色く光る。
    ★ counter技のactionsにはcounterアクション1つだけを入れること。melee_hitboxやprojectileと混在させない（counterが反撃を自動処理するため不要）
  reflector: duration=反射受付秒(1.0〜3.0)。発動中は相手の飛び道具を速度・威力1.2倍で逆方向に反射する。攻撃判定は一切なし・反射のみ。ピンク色に光る。
    ★ reflector技のactionsにはreflectorアクション1つだけを入れること。melee_hitboxやprojectileと混在させない
  summon: duration=召喚体の寿命(1〜6)、power=移動速度(0.5〜5)、spawn_x/y=出現位置、damage_override=接触ダメージ。召喚するものの見た目はその技スロットのeffect画像として生成される前提で、skill_name/descriptionに召喚物の名前や形状を明確に含める。directionで移動方向を指定できる（forward/backward/left/right/toward_enemy/away_enemy/stationary）。player_controlled:trueならプレイヤーの左右入力で操縦。homing:trueなら敵を追う。knockback_directionとstatus/status_duration/chanceも接触時に有効。recovery 0.10〜0.35（召喚直後すぐ動ける）
- 【area_hitbox の形状】area_hitboxにshapeフィールドを追加可能:
  shape: ""box""(default/四角)、""cone""(前方扇形・幅広)、""ring""(自分の周囲円形)
- 【派生技 follow_up_actions】必要な技だけスキルJSON最上位に追加:
  follow_up_actions: [...actionリスト...]  ← 同じボタンの追加入力で発動する派生アクション
  follow_up_window: 0.3〜1.0  ← 受付時間(秒)。迷う場合は0.6
  入力特徴に「派生」「コンボ」「連撃」「連続攻撃」「追撃」「ラッシュ」「combo」「follow-up」などが含まれる場合は、原則としてfollow_up_actionsを必ず使う
  派生・コンボ系キャラは近接技を優先して1〜2スロットにfollow_up_actionsを付ける。超コンボキャラ・連撃特化キャラならattack_a/b/cの3スロットすべてに付けてもよい
  派生・コンボ系の特徴がない通常キャラでは、キャラクター性に合う場合だけfollow_up_actionsを使う。不要なら省略する
  派生各段は必ず単発技にする。follow_up_actions内の各actionはhit_count:1に固定し、多数の判定や長い多段持続にはしない
  派生各段はかなり低威力にする。damage_overrideを使う場合は元技の25〜40%程度、未指定の場合もUnity側で派生ダメージを大きく抑える
  派生はヒット/空振りに関係なく、受付時間内に同じボタンを押すと最大3回まで出る。派生中はUnity側で最終段以外を弱く引き寄せる
- 【チャージ技】主に飛び道具・ビーム技向けにスキルJSON最上位へ追加:
  chargeable: true  ← bool値で出力する。文字列 ""true"" にはしない。ボタン長押しで威力上昇(1.0〜1.8倍)
  max_charge_time: 1.0〜3.0  ← フルチャージ時間(秒)。迷う場合は1.5〜2.0
  ★ chargeable:trueはprojectile/beam系のattack_bまたはattack_cを優先すること。attack_aは横スマッシュ入力と競合しやすいため原則不可
- buff_selfのstatus: ""reflect""もリフレクター効果（reflectorアクションと同じ）
- 【追加アクション types（グラップラー/制圧/回復系）】
  command_throw: ガード不能の投げ。range/size_y内の相手に確定ダメージ＋吹き飛ばし。damage=投げダメージ、knockback=吹き飛ばし、knockback_directionで方向。投げキャラ・組み技キャラ向け。
    ★ command_throwのactionsにはcommand_throw1つだけを入れ、melee_hitbox等と混在させない（範囲判定で自動的に掴む）。rangeは1.2〜2.2の近距離に。
  shockwave: 地面叩きつけで自分の左右両方に同時発生する衝撃波。range=中心から左右への距離(1.5〜3.5)、size_x=波の幅、size_y=高さ(0.6〜1.2)、spawn_y=地面からの高さ(0.2〜0.5)。範囲制圧・起き攻め向け。knockback_direction:""up""で打ち上げると映える。
  gravity_well: 一定時間、指定地点へ相手を継続的に引き寄せる重力場（攻撃判定なし）。spawn_x/y=中心位置、range=引き寄せ半径(2.5〜5)、power=引き寄せ力(8〜30)、duration=持続(0.8〜2.0)。単体では削れないので、projectile/area_hitboxやcommand_throwと別スロットで組み合わせてコンボの起点にする。
  lifesteal: 与えたダメージの一部を自分のHPに回復する近接技。melee_hitboxと同じパラメータ＋lifesteal_ratio(0.2〜0.6、与ダメージの回復割合)。吸血鬼・闇属性・持久キャラ向け。dark属性と相性良い。
  heal_self: 自分のHPを回復する。power=回復HP量(未指定なら最大HPの8%)。攻撃判定なし。持久・回復役キャラ向け。startup/recoveryは長めにして隙を作る。
  barrier: 一定量のダメージを吸収するシールドを張る。power=吸収量(10〜30)、duration=持続秒(2〜5)。攻撃判定なし。防御・タンク型キャラ向け。
  ※ command_throw/heal_self/barrier/gravity_well は攻撃判定が無い/特殊なので、smash_sideには入れない（smash_sideは必ず直接攻撃技）。lifesteal/shockwaveはsmash_sideに使ってよい。

{{
  ""character_name"": ""[ユーザー指定のキャラクター名をそのまま]"",
  ""input_features"": ""[ユーザー指定の特徴をそのまま]"",
  ""catch_copy"": ""[このキャラクターを一言で表すキャッチコピー（日本語・15文字以内・感嘆符推奨）。例: 最速の電撃剣士！、炎をまとう不死鳥！]"",
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
        {{""type"":""[特徴に合わせてmelee_hitbox/body_hitbox/projectile/area_hitbox等から選択]"",""time"":[推論],""range"":[推論],""spawn_x"":[推論],""spawn_y"":[推論],""size_x"":[推論],""size_y"":[推論],""hit_count"":[推論]}}
      ]
    }},
    {{
      ""slot"": ""attack_b"",
      ""skill_name"": ""[基本技B名（日本語）]"",
      ""description"": ""[技説明30字以内]"",
      ""element"": ""[特徴に合う属性]"",
      ""risk_level"": ""medium"",
      ""parameters"": {{""damage"":[推論],""range"":[推論],""startup"":[推論],""active_time"":[推論],""recovery"":[推論],""knockback"":[推論],""guard_damage"":[推論]}},
      ""actions"": [
        {{""type"":""[近距離特化ならmelee_hitbox/body_hitbox/area_hitbox/jump_attack、遠距離ならprojectile]"",""time"":[推論],""spawn_x"":[推論],""spawn_y"":[推論],""size_x"":[推論],""size_y"":[推論]}}
      ]
    }},
    {{
      ""slot"": ""attack_c"",
      ""skill_name"": ""[基本技C名（日本語）]"",
      ""description"": ""[技説明30字以内]"",
      ""element"": ""[特徴に合う属性]"",
      ""risk_level"": ""medium"",
      ""parameters"": {{""damage"":[推論],""range"":[推論],""startup"":[推論],""active_time"":[推論],""recovery"":[0.12〜0.55、設置技は0.10〜0.35],""knockback"":[推論],""stun_time"":[推論]}},
      ""actions"": [
        {{""type"":""[近距離特化ならmelee/area/jump_attack/buff_self/apply_status、設置型ならtrap_hitbox]"",""time"":[推論],""duration"":[trap時2.0〜5.0],""spawn_x"":[推論],""spawn_y"":[推論],""size_x"":[推論],""size_y"":[推論],""hit_count"":[推論],""status"":""[属性に合う状態異常]"",""chance"":[推論]}}
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
    ""up_knockback"": 9,
    ""down_damage"": 8,
    ""down_knockback"": 7
  }}
}}

注意: [推論]をすべて数値に置き換えること。statsは必ず数値で出力し、lightnessは大きいほど軽く、weightは大きいほど重くノックバックを受けにくい。airJumpHeightMultiplierは2段ジャンプ高さの地上ジャンプ比率、walkSpeedRatioは歩き速度のダッシュ速度比率、groundDodgeDistanceは地上横回避の移動距離、airDodgeDistanceは空中横回避の移動距離。elementはphysical/fire/ice/lightning/dark/windから選択。
throw_parameters: front=前投げ(横へ飛ばす)、back=後ろ投げ(反対方向へ)、up=上投げ(真上へ)、down=下投げ(斜め上前方向へ飛ばしコンボにつながりやすい)。ダメージ8-12、ノックバック5-14の範囲で設定。
base_visual_prompt は外見の説明のみを英語で記述し、実在のゲーム・アニメ・映画・漫画の版権キャラクター名・作品名・固有名詞を絶対に含めないこと。髪色・服装・体格・武器・色合いなど視覚的特徴のみで説明すること。";
    }
}
