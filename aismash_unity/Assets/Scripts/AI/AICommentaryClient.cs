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
        // イベント駆動実況用
        public string focusEvent;       // 今まさに起きた注目イベント（最優先で実況させる）
        public string avoidLines;       // 直前の実況文（言い回しの繰り返し防止）
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
            "あなたはテレビの格闘中継の熱血実況アナウンサー。今この瞬間の試合を日本語で1〜2文・60文字以内で実況する。\n" +
            "・「決まったァ！」「なんという猛攻だ！」のような勢いのある口語で、絶叫と短い分析を織り交ぜる\n" +
            "・キャラクター名・技名を積極的に呼ぶ（名前は与えられた表記のまま正確に）\n" +
            "・「実況の焦点」が指定されていたら、必ずその瞬間を最優先で実況する\n" +
            "・数値の読み上げ（HP45%等）はせず「残りわずか」「圧倒的リード」など状況の言葉に置き換える\n" +
            "・直前の実況と同じ言い回し・同じ書き出しを避け、毎回変化を付ける\n" +
            "・出力は実況本文のみ。前置き・カギ括弧・記号装飾は不要";

        static string BuildUserPrompt(CommentaryBattleState s)
        {
            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(s.focusEvent))
                sb.Append($"実況の焦点（今まさに起きた。最優先で拾う）: {s.focusEvent}\n");
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
            if (!string.IsNullOrEmpty(s.avoidLines))
                sb.Append($"\n直前の実況（同じ言い回し禁止）: {s.avoidLines}");

            return $"状況:\n{sb}\n残り{s.timeRemaining:0}秒";
        }
    }
}
