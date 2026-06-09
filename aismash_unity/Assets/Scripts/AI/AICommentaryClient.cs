using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PromptFighters.AI
{
    public struct CommentaryBattleState
    {
        public string player1Name;
        public float  player1HpRatio;   // 0〜1
        public string player2Name;
        public float  player2HpRatio;
        public float  timeRemaining;
        public string mostUsedSkillP1;
        public string mostUsedSkillP2;
        public float  totalDamageP1;
        public float  totalDamageP2;
        public string recentEvents;
        // 強化センシング
        public string lastSkillP1;
        public string lastSkillP2;
        public int    hitStreakP1;
        public int    hitStreakP2;
        public int    recentHitsP1;     // 直近5秒のヒット数
        public int    recentHitsP2;
        public int    guardBreaksP1;    // 累計ガード破壊数
        public int    guardBreaksP2;
    }

    public static class AICommentaryClient
    {
        public static Coroutine Generate(MonoBehaviour runner,
            CommentaryBattleState state,
            Action<string> onSuccess,
            Action<string> onError = null)
        {
            return runner.StartCoroutine(GenerateCoroutine(state, onSuccess, onError));
        }

        static IEnumerator GenerateCoroutine(CommentaryBattleState state,
            Action<string> onSuccess, Action<string> onError)
        {
            string key = AIImageClient.ApiKey;
            if (!AIImageClient.IsConfiguredApiKey(key))
            {
                onError?.Invoke("APIキー未設定");
                yield break;
            }

            string body = OpenAIRequest.BuildChatBody(
                AICharacterClient.LightModel, BuildSystemPrompt(), BuildUserPrompt(state));
            using var req = new UnityWebRequest(AICharacterClient.Endpoint, "POST");
            req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + key);
            req.timeout = 15;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Commentary] {req.error}\n{req.downloadHandler?.text}");
                onError?.Invoke(req.error);
                yield break;
            }

            try
            {
                onSuccess?.Invoke(AICharacterClient.ParseContent(req.downloadHandler.text).Trim());
            }
            catch (Exception e)
            {
                onError?.Invoke(e.Message);
            }
        }

        static string BuildSystemPrompt() =>
            "2D格闘ゲームの試合実況を1〜2文で熱く行ってください（日本語・スポーツ実況風）。" +
            "直近の出来事があれば必ず拾い、同じ言い回しを避けてください。" +
            "出力は実況1〜2文のみ・前置き不要。";

        static string BuildUserPrompt(CommentaryBattleState s)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"{s.player1Name} HP:{s.player1HpRatio*100f:0}% 与ダメ:{s.totalDamageP1:0}");
            if (s.hitStreakP1 >= 3) sb.Append($" 連続{s.hitStreakP1}hit中!");
            if (s.guardBreaksP1 > 0) sb.Append($" ガード破壊{s.guardBreaksP1}回");
            if (!string.IsNullOrEmpty(s.lastSkillP1) && s.lastSkillP1 != "---") sb.Append($" 直前技:{s.lastSkillP1}");
            sb.Append($"\n{s.player2Name} HP:{s.player2HpRatio*100f:0}% 与ダメ:{s.totalDamageP2:0}");
            if (s.hitStreakP2 >= 3) sb.Append($" 連続{s.hitStreakP2}hit中!");
            if (s.guardBreaksP2 > 0) sb.Append($" ガード破壊{s.guardBreaksP2}回");
            if (!string.IsNullOrEmpty(s.lastSkillP2) && s.lastSkillP2 != "---") sb.Append($" 直前技:{s.lastSkillP2}");
            if (!string.IsNullOrEmpty(s.mostUsedSkillP1) && s.mostUsedSkillP1 != "---")
                sb.Append($"\n{s.player1Name}の多用技:{s.mostUsedSkillP1}");
            if (!string.IsNullOrEmpty(s.mostUsedSkillP2) && s.mostUsedSkillP2 != "---")
                sb.Append($"\n{s.player2Name}の多用技:{s.mostUsedSkillP2}");
            if (!string.IsNullOrEmpty(s.recentEvents))
                sb.Append($"\n直近:{s.recentEvents}");

            return $"状況:\n{sb}\n残り{s.timeRemaining:0}秒";
        }
    }
}
