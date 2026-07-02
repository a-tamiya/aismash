using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PromptFighters.AI
{
    [Serializable]
    public class GimmickData
    {
        public string gimmick;
        public string target;
        public float  value;
        public float  duration;
        public string message;

        public string gimmick2;
        public string target2;
        public float  value2;
        public float  duration2;

        public string gimmick3;
        public string target3;
        public float  value3;
        public float  duration3;
    }

    public static class AIAngelClient
    {
        // acquirerSlot: アイテム取得者（"player1"/"player2"）。指定すると取得者の願いを優先し、
        // 「自分/相手」を取得者基準で解決させる。nullも可（旧仕様互換）。
        public static Coroutine DecideGimmick(MonoBehaviour runner,
            string voiceText,
            CommentaryBattleState battleState,
            Action<GimmickData> onSuccess,
            Action<string> onError = null,
            string acquirerSlot = null)
        {
            return runner.StartCoroutine(
                DecideCoroutine(voiceText, battleState, onSuccess, onError, acquirerSlot));
        }

        static IEnumerator DecideCoroutine(string voiceText,
            CommentaryBattleState state,
            Action<GimmickData> onSuccess,
            Action<string> onError,
            string acquirerSlot = null)
        {
            string key = AIImageClient.ApiKey;
            if (!AIImageClient.IsConfiguredApiKey(key))
            {
                onSuccess?.Invoke(FallbackGimmick());
                yield break;
            }

            string body = OpenAIRequest.BuildChatBody(
                AICharacterClient.LightModel, BuildSystemPrompt(), BuildUserPrompt(voiceText, state, acquirerSlot),
                jsonMode: true);
            using var req = new UnityWebRequest(AICharacterClient.Endpoint, "POST");
            req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + key);
            req.timeout = 20;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Angel] API失敗、フォールバック使用: {req.error}\n{req.downloadHandler?.text}");
                onSuccess?.Invoke(FallbackGimmick());
                yield break;
            }

            try
            {
                string content = AICharacterClient.ParseContent(req.downloadHandler.text);
                string json    = ExtractJson(content);
                var data = JsonUtility.FromJson<GimmickData>(json);
                if (data == null || string.IsNullOrEmpty(data.gimmick))
                    throw new Exception("ギミックデータが空");
                onSuccess?.Invoke(data);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Angel] 解析失敗、フォールバック使用: {e.Message}");
                onSuccess?.Invoke(FallbackGimmick());
            }
        }

        static GimmickData FallbackGimmick() => new GimmickData
        {
            gimmick  = "hp_recover",
            target   = "weaker",
            value    = 0.15f,
            duration = 0f,
            message  = "ちょっと退屈だから…HP少しあげる♪"
        };

        static string BuildSystemPrompt() =>
            "あなたは2D格闘ゲームの「願いを叶える精霊」AIです。アイテム取得者の願いを聞き、その言葉に最も近いギミックを発動します。\n\n【最重要・絶対ルール】\n- 取得者の願いは必ず忠実に叶えること。言われた内容と無関係な効果を出してはいけない。\n- 願いの言葉から「効果の種類」を読み取り、下記一覧から最も意味の近いギミックを必ず選ぶ。\n- 効果の向き（誰に効くか）も願い通りにする。「相手を◯◯」ならtarget=相手、「自分を◯◯」ならtarget=取得者。\n- 強すぎる願い（即死・永続無敵など）は『効果の種類は守ったまま』value/durationを控えめにする。種類そのものを変えてはいけない（例：無敵の願い→無敵のまま短時間にする。回復に化けさせない）。\n- 願いが曖昧でも、最も近い1つを必ず選ぶ。「何もしない」は禁止。\n\n【願いの言葉→ギミック 解釈例】\n- 「風／風を吹かせて／突風／吹き荒れて」→ wind（継続的な横風）、「一発で吹き飛ばして／ぶっ飛ばして」→ launch\n- 「燃やして／火／炎／焼いて」→ burn、「床を溶岩に／地面が熱い」→ floor_lava\n- 「凍らせて／氷／止めて／動けなく」→ freeze\n- 「ガードできなく／防御封じ」→ guard_disable、「技を封じて／技を使えなく」→ skill_seal、「めっちゃ吹っ飛ぶ／ふっとびやすく」→ super_knockback\n- 「HP同じに／HP平等に」→ hp_equal、「HP共有」→ hp_share、「カウンター／反撃」→ counter_gimmick、「地面で跳ねる／トランポリン床」→ ground_bounce、「動く足場」→ obstacle_moving\n- 「回復／治して／HP回復」→ hp_recover（全部なら hp_full）\n- 「速く／スピードアップ」→ speed_boost、「遅く／鈍く」→ speed_down / slow\n- 「大きく／巨大化」→ size_up、「小さく」→ size_down\n- 「ジャンプ／跳ねたい」→ jump_boost\n- 「強く／パワーアップ／火力」→ damage_boost、「弱く」→ damage_down\n- 「無敵／ダメージ受けない」→ invincible\n- 「ふわふわ／浮かせて／重力下げ」→ gravity_down、「重く／重力上げ」→ gravity_up\n- 「雨／降らせて／たくさん落として」→ obstacle_rain、「壁」→ obstacle_wall、「足場／台」→ obstacle_platform、「跳ねる床」→ obstacle_bounce\n- 「障害物消して／足場消して／片付けて／更地に／きれいにして／全部消して」→ clear_obstacles\n- 「ワープ／瞬間移動」→ teleport、「入れ替え（位置）」→ position_swap、「HP入れ替え」→ hp_swap\n- 「混乱／操作反転」→ chaos、「ガード回復」→ guard_fill、「ガード壊して」→ guard_break、「反射」→ reflect\n\n【ギミック一覧】\nバフ: hp_recover（value=回復割合）, hp_full（全回復）, speed_boost（value=速度倍率）, jump_boost（value=ジャンプ倍率）, damage_boost（value=ダメージ倍率）, invincible（無敵）, gravity_down（浮遊 value=重力倍率0.1〜0.8）, guard_fill（ガード全回復）, reflect（ダメージ反射 duration=秒）\nデバフ: hp_drain（value=削減割合）, hp_set（HPを指定割合にする value=割合）, speed_down, jump_down, damage_down, chaos（操作反転）, freeze（行動不能 duration=秒）, burn, guard_break, gravity_up（重力増 value=1.5〜5）, slow（スロー）, launch（吹き飛ばし value=強さ1〜5）\n追加デバフ: wind（横風 value=強さ0.2〜3 duration=秒）, floor_lava（床ダメージ value=割合 duration=秒）, guard_disable（ガード不可 duration=秒）, skill_seal（技封印 value=封印スロット1〜4 duration=秒）, super_knockback（被ノックバック増 duration=秒）\n特殊: hp_swap（HP入れ替え）, hp_equal（HP平均化）, hp_share（HP共有 duration=秒）, counter_gimmick（カウンター付与 duration=秒）, ground_bounce（着地で跳ねる value=強さ）, size_up, size_down, teleport（ランダム瞬間移動）, position_swap（P1とP2の位置入れ替え）\n地形: obstacle_platform（横足場 value=幅）, obstacle_wall（縦壁 value=高さ）, obstacle_bounce（バウンスパッド）, obstacle_rain（落下物 value=個数）, obstacle_tilt（斜め足場）, obstacle_moving（動く足場 value=幅 duration=秒）, clear_obstacles（地形・障害物を全消去）\n※地形targetでplayer1=P1付近/player2=P2付近/その他=中央。\n- 願いが複数の効果を含む場合のみ gimmick2/gimmick3 を使う。単一の願いなら無理に複数効果にしない（願いを薄めないため）。\n\ntarget: player1 / player2 / both / weaker / stronger / random\n\nmessageは願いを叶える精霊のセリフ（日本語・30字以内・人格攻撃なし）\n\nJSONのみ出力（不要フィールドは空文字/0でOK）：\n{\"gimmick\":\"...\",\"target\":\"...\",\"value\":0.0,\"duration\":0.0,\"gimmick2\":\"\",\"target2\":\"\",\"value2\":0.0,\"duration2\":0.0,\"gimmick3\":\"\",\"target3\":\"\",\"value3\":0.0,\"duration3\":0.0,\"message\":\"...\"}";

        static string BuildUserPrompt(string voiceText, CommentaryBattleState s, string acquirerSlot)
        {
            string p1streak = s.hitStreakP1 >= 3 ? $" 連続{s.hitStreakP1}hit中" : "";
            string p2streak = s.hitStreakP2 >= 3 ? $" 連続{s.hitStreakP2}hit中" : "";
            string p1skill  = s.lastSkillP1 != "---" ? $" 直前技:{s.lastSkillP1}" : "";
            string p2skill  = s.lastSkillP2 != "---" ? $" 直前技:{s.lastSkillP2}" : "";
            string events   = !string.IsNullOrEmpty(s.recentEvents) ? $"\n直近の出来事: {s.recentEvents}" : "";

            string acquirerLine = "";
            string voiceLine = $"観客の声：「{voiceText}」";
            if (acquirerSlot == "player1" || acquirerSlot == "player2")
            {
                string oppSlot = acquirerSlot == "player1" ? "player2" : "player1";
                string who     = acquirerSlot == "player1" ? s.player1Name : s.player2Name;
                string oppWho  = acquirerSlot == "player1" ? s.player2Name : s.player1Name;
                string selfNum = acquirerSlot == "player1" ? "1P" : "2P";
                string oppNum  = acquirerSlot == "player1" ? "2P" : "1P";
                acquirerLine =
                    $"【アイテム取得者】{acquirerSlot}（{who}）。願いを言ったのはこのプレイヤー。\n" +
                    $"・「自分/わたし/オレ/ぼく/こっち/{selfNum}」と言ったら target={acquirerSlot}。\n" +
                    $"・「相手/敵/あいつ/向こう/{oppNum}/ボス」と言ったら target={oppSlot}（{oppWho}）。\n" +
                    $"・主語が省略された願いは、有利効果なら取得者({acquirerSlot})、不利効果なら相手({oppSlot})に向ける。\n" +
                    $"取得者の願いは必ず叶えること（gimmick も target も願い通りにする）。極端な即死・永続無敵だけ控えめに。\n\n";
                voiceLine = $"取得者の願い：「{voiceText}」";
            }
            return $"{acquirerLine}{voiceLine}\n\n試合状況：\n- {s.player1Name} HP:{s.player1HpRatio*100f:0}% 与ダメ:{s.totalDamageP1:0}{p1streak}{p1skill}\n- {s.player2Name} HP:{s.player2HpRatio*100f:0}% 与ダメ:{s.totalDamageP2:0}{p2streak}{p2skill}\n- 残り時間: {s.timeRemaining:0}秒{events}";
        }

        static string ExtractJson(string text)
        {
            int start = text.IndexOf('{');
            int end   = text.LastIndexOf('}');
            if (start < 0 || end < 0 || end <= start)
                throw new Exception("JSONが見つかりません");
            return text.Substring(start, end - start + 1);
        }
    }
}
