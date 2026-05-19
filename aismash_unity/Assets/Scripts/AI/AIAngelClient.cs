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
        // hp_recover / speed_boost / speed_down / jump_boost / damage_boost / transparent / invincible / chaos
        public string gimmick;
        // player1 / player2 / both / weaker / stronger
        public string target;
        public float  value;
        public float  duration;
        public string message;

        // P1/P2 に別々のギミックを適用する場合の 2 つ目（省略可）
        public string gimmick2;
        public string target2;
        public float  value2;
        public float  duration2;
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

            string body = BuildBody(BuildPrompt(voiceText, state));
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

        static string BuildPrompt(string voiceText, CommentaryBattleState s)
        {
            return $"あなたは2D格闘ゲームを見守る「気まぐれ天使」AIです。観客の声と試合状況から、面白いギミックを決めてください。\n\n観客の声：「{voiceText}」\n\n試合状況：\n- {s.player1Name} HP: {s.player1HpRatio * 100f:0}%\n- {s.player2Name} HP: {s.player2HpRatio * 100f:0}%\n- 残り時間: {s.timeRemaining:0}秒\n\n【ルール】\n- 特定の指示がない場合は、負けている（HP少ない）プレイヤーが有利になるようにしてください。\n- P1とP2に別々のギミックを指示された場合は gimmick2/target2/value2/duration2 も使ってください。\n\nギミック種類：\n- hp_recover: HP回復（value=回復割合0.05〜0.25）\n- speed_boost: 移動速度上昇（value=倍率1.3〜2.0、duration=秒5〜10）\n- speed_down: 移動速度低下（value=倍率0.3〜0.7、duration=秒5〜10）\n- jump_boost: ジャンプ力上昇（value=倍率1.3〜2.0、duration=秒5〜10）\n- damage_boost: ダメージ上昇（value=倍率1.2〜1.6、duration=秒5〜10）\n- transparent: 無敵化（duration=秒3〜6）\n- chaos: 移動キー反転（duration=秒4〜8）\n\ntarget: player1 / player2 / both / weaker（HP少ない方）/ stronger（HP多い方）\n\nmessageは天使のセリフ（日本語・気まぐれで可愛い口調・30字以内）\n\nJSONのみ出力（gimmick2は不要なら省略可）：\n{{\"gimmick\":\"...\",\"target\":\"...\",\"value\":0.0,\"duration\":0.0,\"gimmick2\":\"\",\"target2\":\"\",\"value2\":0.0,\"duration2\":0.0,\"message\":\"...\"}}";
        }

        static string BuildBody(string prompt)
        {
            string esc = prompt
                .Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "").Replace("\t", " ");
            return $"{{\"model\":\"{AICharacterClient.Model}\",\"messages\":[{{\"role\":\"user\",\"content\":\"{esc}\"}}]}}";
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
