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

            string body = BuildBody(BuildPrompt(state));
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

        static string BuildPrompt(CommentaryBattleState s)
        {
            string skills = "";
            if (!string.IsNullOrEmpty(s.mostUsedSkillP1) && s.mostUsedSkillP1 != "---")
                skills += $"\n{s.player1Name}の多用技: {s.mostUsedSkillP1}";
            if (!string.IsNullOrEmpty(s.mostUsedSkillP2) && s.mostUsedSkillP2 != "---")
                skills += $"\n{s.player2Name}の多用技: {s.mostUsedSkillP2}";

            return $"2D格闘ゲームの試合実況を1〜2文で熱く行ってください（日本語・スポーツ実況風）。\n状況:\n{s.player1Name} HP:{s.player1HpRatio * 100f:0}% vs {s.player2Name} HP:{s.player2HpRatio * 100f:0}%\n残り{s.timeRemaining:0}秒{skills}\n\n実況（1〜2文のみ、前置き不要）:";
        }

        static string BuildBody(string prompt)
        {
            string esc = prompt
                .Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "").Replace("\t", " ");
            return $"{{\"model\":\"{AICharacterClient.Model}\",\"messages\":[{{\"role\":\"user\",\"content\":\"{esc}\"}}]}}";
        }
    }
}
