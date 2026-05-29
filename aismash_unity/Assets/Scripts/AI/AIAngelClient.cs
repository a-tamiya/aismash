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
        public static Coroutine DecideGimmick(MonoBehaviour runner,
            string voiceText,
            CommentaryBattleState battleState,
            Action<GimmickData> onSuccess,
            Action<string> onError = null)
        {
            return runner.StartCoroutine(
                DecideCoroutine(voiceText, battleState, onSuccess, onError));
        }

        static IEnumerator DecideCoroutine(string voiceText,
            CommentaryBattleState state,
            Action<GimmickData> onSuccess,
            Action<string> onError)
        {
            string key = AIImageClient.ApiKey;
            if (!AIImageClient.IsConfiguredApiKey(key))
            {
                onSuccess?.Invoke(FallbackGimmick());
                yield break;
            }

            string body = OpenAIRequest.BuildChatBody(
                AICharacterClient.Model, BuildSystemPrompt(), BuildUserPrompt(voiceText, state),
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
            "あなたは2D格闘ゲームを見守る「気まぐれ天使」AIです。気まぐれにギミックを発動して試合をかき乱します。\n\n【天使の性格・ルール】\n- 観客の声がある場合はそれに応答してください\n- ただし強すぎる願い（即死・永続無敵など）ほど言うことを聞かない。機嫌を損ねて願った側にデバフをかけてもよい\n- 観客の声がない場合は試合を面白くする介入をする（負けている側を有利にするなど）\n- gimmick2/gimmick3を積極的に使い複合効果を出すこと\n- value/durationは観客の要求内容に応じて自由に決めてよい\n\n【ギミック一覧】\nバフ: hp_recover（value=回復割合）, hp_full（全回復）, speed_boost（value=速度倍率）, jump_boost（value=ジャンプ倍率）, damage_boost（value=ダメージ倍率）, invincible（無敵）, gravity_down（浮遊 value=重力倍率0.1〜0.8）, guard_fill（ガード全回復）, reflect（ダメージ反射 duration=秒）\nデバフ: hp_drain（value=削減割合）, hp_set（HPを指定割合にする value=割合）, speed_down, jump_down, damage_down, chaos（操作反転）, freeze（行動不能 duration=秒）, burn, guard_break, gravity_up（重力増 value=1.5〜5）, slow（スロー）, launch（吹き飛ばし value=強さ1〜5）\n特殊: hp_swap（HP入れ替え）, size_up, size_down, teleport（ランダム瞬間移動）, position_swap（P1とP2の位置入れ替え）\n地形: obstacle_platform（横足場 value=幅）, obstacle_wall（縦壁 value=高さ）, obstacle_bounce（バウンスパッド）, obstacle_rain（落下物 value=個数）, obstacle_tilt（斜め足場）\n※地形targetでplayer1=P1付近/player2=P2付近/その他=中央。gimmick2/3で複数同時可\n\ntarget: player1 / player2 / both / weaker / stronger / random\n\nmessageはプレイヤーへの人格攻撃なしで天使のセリフ（日本語・気まぐれな口調・30字以内）\n\nJSONのみ出力（不要フィールドは空文字/0でOK）：\n{\"gimmick\":\"...\",\"target\":\"...\",\"value\":0.0,\"duration\":0.0,\"gimmick2\":\"\",\"target2\":\"\",\"value2\":0.0,\"duration2\":0.0,\"gimmick3\":\"\",\"target3\":\"\",\"value3\":0.0,\"duration3\":0.0,\"message\":\"...\"}";

        static string BuildUserPrompt(string voiceText, CommentaryBattleState s)
        {
            string p1streak = s.hitStreakP1 >= 3 ? $" 連続{s.hitStreakP1}hit中" : "";
            string p2streak = s.hitStreakP2 >= 3 ? $" 連続{s.hitStreakP2}hit中" : "";
            string p1skill  = s.lastSkillP1 != "---" ? $" 直前技:{s.lastSkillP1}" : "";
            string p2skill  = s.lastSkillP2 != "---" ? $" 直前技:{s.lastSkillP2}" : "";
            string events   = !string.IsNullOrEmpty(s.recentEvents) ? $"\n直近の出来事: {s.recentEvents}" : "";
            return $"観客の声：「{voiceText}」\n\n試合状況：\n- {s.player1Name} HP:{s.player1HpRatio*100f:0}% 与ダメ:{s.totalDamageP1:0}{p1streak}{p1skill}\n- {s.player2Name} HP:{s.player2HpRatio*100f:0}% 与ダメ:{s.totalDamageP2:0}{p2streak}{p2skill}\n- 残り時間: {s.timeRemaining:0}秒{events}";
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
