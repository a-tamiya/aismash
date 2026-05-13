using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;
using PromptFighters.Battle;
using PromptFighters.Battle.Skills;

namespace PromptFighters.UI
{
    public class BattleResultUI : MonoBehaviour
    {
        static readonly Color P1Color = new Color(0.4f, 0.75f, 1f);
        static readonly Color P2Color = new Color(1f, 0.55f, 0.35f);

        GameObject _overlay;
        TextMeshProUGUI _winnerText;
        TextMeshProUGUI _p1NameText;
        TextMeshProUGUI _p2NameText;
        TextMeshProUGUI _p1StatsText;
        TextMeshProUGUI _p2StatsText;
        TextMeshProUGUI _commentText;
        TextMeshProUGUI _promptText;
        float _animTimer;
        bool _visible;
        int _winnerIndex;

        void Start()
        {
            BuildOverlay();
            if (BattleManager.Instance != null)
            {
                BattleManager.Instance.OnBattleEnd       += ShowResult;
                BattleManager.Instance.OnReturnedToSetup += HidePanel;
            }
        }

        void BuildOverlay()
        {
            var canvas = GetComponentInParent<Canvas>() ?? FindFirstObjectByType<Canvas>();
            Transform root = canvas != null ? canvas.transform : transform;

            _overlay = Make("ResultOverlay", root);
            Stretch(_overlay.GetComponent<RectTransform>());
            _overlay.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.82f);

            // 勝者帯
            var banner = Make("Banner", _overlay.transform);
            var brt = banner.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0f, 0.72f);
            brt.anchorMax = new Vector2(1f, 1f);
            brt.offsetMin = brt.offsetMax = Vector2.zero;
            banner.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.4f);

            _winnerText = MakeLabel(_overlay.transform, "WinnerText",
                "", new Vector2(0, 180), new Vector2(900, 100), 72f, Color.white);
            _winnerText.fontStyle = FontStyles.Bold;

            // 中央区切り線
            MakeRect(_overlay.transform, "Divider",
                new Vector2(0, 40), new Vector2(3, 520), new Color(1f, 1f, 1f, 0.12f));

            // 1P情報（左）
            _p1NameText = MakeLabel(_overlay.transform, "P1Name",
                "", new Vector2(-380, 80), new Vector2(500, 44), 26f, P1Color);
            _p1NameText.fontStyle = FontStyles.Bold;
            _p1StatsText = MakeLabel(_overlay.transform, "P1Stats",
                "", new Vector2(-380, -20), new Vector2(500, 180), 16f, new Color(0.88f, 0.93f, 1f));
            _p1StatsText.alignment = TextAlignmentOptions.TopLeft;

            // 2P情報（右）
            _p2NameText = MakeLabel(_overlay.transform, "P2Name",
                "", new Vector2(380, 80), new Vector2(500, 44), 26f, P2Color);
            _p2NameText.fontStyle = FontStyles.Bold;
            _p2StatsText = MakeLabel(_overlay.transform, "P2Stats",
                "", new Vector2(380, -20), new Vector2(500, 180), 16f, new Color(1f, 0.92f, 0.88f));
            _p2StatsText.alignment = TextAlignmentOptions.TopRight;

            // AIコメント欄
            MakeRect(_overlay.transform, "CommentBg",
                new Vector2(0, -230), new Vector2(860, 120), new Color(0.05f, 0.08f, 0.12f, 0.85f));
            _commentText = MakeLabel(_overlay.transform, "Comment",
                "AIが試合を分析中...", new Vector2(0, -230), new Vector2(830, 110), 16f,
                new Color(0.9f, 0.95f, 1f));
            _commentText.textWrappingMode = TMPro.TextWrappingModes.Normal;

            // フッター
            _promptText = MakeLabel(_overlay.transform, "PromptText",
                "スペースキーでリスタート", new Vector2(0, -340), new Vector2(700, 32), 18f,
                new Color(0.7f, 0.7f, 0.7f));

            _overlay.SetActive(false);
        }

        void ShowResult(int winnerIndex)
        {
            if (_overlay == null) return;
            _winnerIndex = winnerIndex;
            _overlay.SetActive(true);
            _visible  = true;
            _animTimer = 0f;

            Color accent = winnerIndex == 0 ? P1Color : winnerIndex == 1 ? P2Color : new Color(0.9f, 0.85f, 0.3f);

            if (_winnerText != null)
            {
                _winnerText.text  = winnerIndex == 0 ? "1P 勝利！" : winnerIndex == 1 ? "2P 勝利！" : "引き分け";
                _winnerText.color = accent;
            }

            RefreshCharInfo();
            RefreshStats();
            StartCoroutine(GenerateComment());
        }

        void RefreshCharInfo()
        {
            var bm = BattleManager.Instance;
            if (bm == null) return;

            string p1Name = bm.Character1?.characterName ?? "1P";
            string p2Name = bm.Character2?.characterName ?? "2P";
            if (_p1NameText != null) _p1NameText.text = p1Name;
            if (_p2NameText != null) _p2NameText.text = p2Name;
        }

        void RefreshStats()
        {
            if (BattleLogger.Instance == null) return;
            var p1 = BattleLogger.Instance.P1;
            var p2 = BattleLogger.Instance.P2;

            if (_p1StatsText != null)
                _p1StatsText.text = BuildStatsText(p1);
            if (_p2StatsText != null)
                _p2StatsText.text = BuildStatsText(p2);
        }

        static string BuildStatsText(PlayerLog log)
        {
            if (log == null) return "---";
            var sb = new StringBuilder();
            sb.AppendLine($"与ダメージ: {log.totalDamageDealt:F0}");
            sb.Append($"最多使用技: {log.MostUsedSkillName()}");
            return sb.ToString();
        }

        IEnumerator GenerateComment()
        {
            if (_commentText != null) _commentText.text = "AIが試合を分析中...";

            var bm = BattleManager.Instance;
            if (bm == null) yield break;

            string log = BuildMatchLog(bm, _winnerIndex);
            string prompt = BuildCommentPrompt(log);
            string body   = BuildBody(prompt);

            using var req = new UnityWebRequest(PromptFighters.AI.AICharacterClient.Endpoint, "POST");
            req.uploadHandler   = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + PromptFighters.AI.AICharacterClient.ApiKey);
            req.timeout = 30;

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    string content = ParseContent(req.downloadHandler.text);
                    if (_commentText != null) _commentText.text = content.Trim();
                    yield break;
                }
                catch { }
            }

            // フォールバックコメント
            if (_commentText != null)
                _commentText.text = FallbackComment(_winnerIndex, bm);
        }

        static string BuildMatchLog(BattleManager bm, int winnerIdx)
        {
            var sb = new StringBuilder();
            sb.Append($"勝者:{(winnerIdx == 0 ? "1P" : winnerIdx == 1 ? "2P" : "引き分け")}");
            if (bm.Character1 != null)
            {
                sb.Append($" 1P({bm.Character1.characterName})");
                if (BattleLogger.Instance != null)
                    sb.Append($"残HP:{bm.fighter1?.CurrentHP:F0} 与ダメ:{BattleLogger.Instance.P1.totalDamageDealt:F0} 最多技:{BattleLogger.Instance.P1.MostUsedSkillName()}");
            }
            if (bm.Character2 != null)
            {
                sb.Append($" 2P({bm.Character2.characterName})");
                if (BattleLogger.Instance != null)
                    sb.Append($"残HP:{bm.fighter2?.CurrentHP:F0} 与ダメ:{BattleLogger.Instance.P2.totalDamageDealt:F0} 最多技:{BattleLogger.Instance.P2.MostUsedSkillName()}");
            }
            return sb.ToString();
        }

        static string BuildCommentPrompt(string matchLog) =>
            $"2D格闘ゲーム「プロンプトファイターズ」の試合結果を2〜3文で実況風にコメントしてください。日本語で、短く、テンポよく。\n試合データ: {matchLog}";

        static string BuildBody(string prompt)
        {
            string esc = prompt
                .Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "");
            return $"{{\"model\":\"{PromptFighters.AI.AICharacterClient.Model}\",\"messages\":[{{\"role\":\"user\",\"content\":\"{esc}\"}}]}}";
        }

        [System.Serializable] class _Resp   { public _Choice[] choices; }
        [System.Serializable] class _Choice { public _Msg message; }
        [System.Serializable] class _Msg    { public string content; }
        static string ParseContent(string raw)
        {
            var r = JsonUtility.FromJson<_Resp>(raw);
            if (r?.choices != null && r.choices.Length > 0 && r.choices[0].message?.content != null)
                return r.choices[0].message.content;
            throw new System.Exception("no content");
        }

        static string FallbackComment(int winner, BattleManager bm)
        {
            if (winner < 0) return "接戦の末、引き分けとなりました。両者互角の戦いでした。";
            string wName = winner == 0 ? bm.Character1?.characterName ?? "1P" : bm.Character2?.characterName ?? "2P";
            return $"{wName}の勝利！自分の技の特徴をうまく活かして戦いを制しました。";
        }

        void Update()
        {
            if (!_visible) return;
            _animTimer += Time.deltaTime;
            if (_winnerText != null)
            {
                float pulse = 1f + 0.03f * Mathf.Sin(_animTimer * 3f);
                _winnerText.transform.localScale = Vector3.one * pulse;
            }

            var kb = Keyboard.current;
            if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
            {
                HidePanel();
                BattleManager.Instance?.ReturnToSetup();
            }
        }

        void HidePanel()
        {
            _visible = false;
            if (_overlay != null) _overlay.SetActive(false);
        }

        // UI ヘルパー
        static GameObject Make(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.AddComponent<RectTransform>().SetParent(parent, false);
            return go;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static TextMeshProUGUI MakeLabel(Transform parent, string name, string text,
            Vector2 pos, Vector2 size, float fontSize, Color color)
        {
            var go = Make(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.fontSize  = fontSize;
            tmp.color     = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TMPro.TextWrappingModes.Normal;
            UITheme.Apply(tmp);
            return tmp;
        }

        static void MakeRect(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var go = Make(name, parent);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            go.AddComponent<Image>().color = color;
        }
    }
}
