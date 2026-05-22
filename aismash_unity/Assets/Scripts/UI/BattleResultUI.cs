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
        // ── Palette ────────────────────────────────────────────────────
        static readonly Color BgOverlay  = new Color(0.01f, 0.02f, 0.05f, 0.90f);
        static readonly Color BgPanel    = new Color(0.02f, 0.05f, 0.10f, 0.97f);
        static readonly Color BgCard     = new Color(0.03f, 0.07f, 0.14f, 1.00f);
        static readonly Color BgComment  = new Color(0.02f, 0.06f, 0.12f, 1.00f);
        static readonly Color P1Col      = new Color(0.12f, 0.62f, 1.00f);
        static readonly Color P2Col      = new Color(1.00f, 0.20f, 0.20f);
        static readonly Color DrawCol    = new Color(1.00f, 0.82f, 0.10f);
        static readonly Color TextWht    = Color.white;
        static readonly Color TextDim    = new Color(0.55f, 0.68f, 0.82f);
        static readonly Color TextMuted  = new Color(0.38f, 0.50f, 0.65f);
        static readonly Color EdgeLine   = new Color(0.22f, 0.50f, 1.00f, 0.40f);

        // ── State ──────────────────────────────────────────────────────
        GameObject      _overlay;
        Image           _winnerBand;
        TextMeshProUGUI _winnerText;
        TextMeshProUGUI _p1NameText, _p2NameText;
        TextMeshProUGUI _p1StatsText, _p2StatsText;
        TextMeshProUGUI _commentText;
        TextMeshProUGUI _promptText;

        bool  _visible;
        int   _winnerIndex;
        float _animTimer;

        // ── Lifecycle ──────────────────────────────────────────────────
        void Start()
        {
            BuildUI();
            if (BattleManager.Instance != null)
            {
                BattleManager.Instance.OnBattleEnd       += ShowResult;
                BattleManager.Instance.OnReturnedToSetup += HidePanel;
            }
        }

        void Update()
        {
            if (!_visible) return;
            _animTimer += Time.deltaTime;

            // prompt pulse
            if (_promptText != null)
            {
                float a = 0.55f + 0.45f * Mathf.Sin(_animTimer * 2.8f);
                var c = _promptText.color; c.a = a; _promptText.color = c;
            }

            // restart input
            var kb = Keyboard.current;
            if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
            {
                HidePanel();
                BattleManager.Instance?.ReturnToSetup();
            }
        }

        // ── UI Construction ────────────────────────────────────────────
        void BuildUI()
        {
            var canvas = GetComponentInParent<Canvas>() ?? FindFirstObjectByType<Canvas>();
            Transform root = canvas != null ? canvas.transform : transform;

            // ─ Overlay ──────────────────────────────────────────────
            _overlay = Make("ResultOverlay", root);
            StretchFill(_overlay.GetComponent<RectTransform>());
            _overlay.AddComponent<Image>().color = BgOverlay;

            // ─ Main panel (centered card) ──────────────────────────
            var panel = Make("ResultPanel", _overlay.transform);
            var pRt = panel.GetComponent<RectTransform>();
            pRt.anchorMin = pRt.anchorMax = new Vector2(0.5f, 0.5f);
            pRt.sizeDelta = new Vector2(860f, 540f);
            pRt.anchoredPosition = Vector2.zero;
            panel.AddComponent<Image>().color = BgPanel;

            // panel border lines
            AddLine(panel.transform, "BdT", 0,1,1,1, 0,-1f,0,0,    EdgeLine);
            AddLine(panel.transform, "BdB", 0,0,1,0, 0, 0f,0,1f,   EdgeLine);
            AddLine(panel.transform, "BdL", 0,0,0,1, 0, 0f,1f,0,   EdgeLine);
            AddLine(panel.transform, "BdR", 1,0,1,1,-1f,0f,0,0,    EdgeLine);

            // ─ Winner band (top strip) ────────────────────────────
            var band = Make("WinnerBand", panel.transform);
            Anch(band, 0,1,1,1, 0,-100f,0,0);
            _winnerBand = band.AddComponent<Image>();
            _winnerBand.color = new Color(0.10f, 0.62f, 1.00f, 0.18f);

            // corner accent lines on band
            AddLine(panel.transform, "BandLine", 0,1,1,1, 0,-100f,0,-98f, new Color(1,1,1,0.08f));

            // winner label ("1P WIN" / "2P WIN" / "DRAW")
            var wGo = Make("WinnerLabel", panel.transform);
            Anch(wGo, 0,1,1,1, 0,-100f,0,-6f);
            _winnerText = wGo.AddComponent<TextMeshProUGUI>();
            _winnerText.text      = "1P WIN";
            _winnerText.fontSize  = 68f;
            _winnerText.fontStyle = FontStyles.Bold;
            _winnerText.alignment = TextAlignmentOptions.Center;
            _winnerText.color     = P1Col;
            UITheme.Apply(_winnerText);

            // ─ Vertical divider ──────────────────────────────────
            AddLine(panel.transform, "Divider", 0.5f,0,0.5f,1, 0,24f,1f,-108f, new Color(1,1,1,0.08f));

            // ─ P1 stats card (left) ───────────────────────────────
            var c1 = BuildStatCard(panel.transform, true);

            // ─ P2 stats card (right) ─────────────────────────────
            var c2 = BuildStatCard(panel.transform, false);

            // ─ AI comment box ─────────────────────────────────────
            var cmtBg = Make("CommentBg", panel.transform);
            Anch(cmtBg, 0,0,1,0, 20f,58f,-20f,148f);
            cmtBg.AddComponent<Image>().color = BgComment;
            AddLine(cmtBg.transform, "CmtT", 0,1,1,1, 0,-1f,0,0, new Color(0.22f,0.50f,1f,0.35f));

            // "AI ANALYSIS" label
            var cmtHeader = Make("CmtHeader", cmtBg.transform);
            Anch(cmtHeader, 0,1,1,1, 10f,-20f,-10f,-2f);
            var cmtH = cmtHeader.AddComponent<TextMeshProUGUI>();
            cmtH.text = "■  AI ANALYSIS"; cmtH.fontSize = 9f;
            cmtH.alignment = TextAlignmentOptions.Left; cmtH.color = new Color(0.28f,0.62f,1f,0.8f);
            UITheme.Apply(cmtH);

            var cmtGo = Make("CommentText", cmtBg.transform);
            Anch(cmtGo, 0,0,1,1, 10f,6f,-10f,-22f);
            _commentText = cmtGo.AddComponent<TextMeshProUGUI>();
            _commentText.text = "AI が試合を分析中…";
            _commentText.fontSize = 14f;
            _commentText.alignment = TextAlignmentOptions.TopLeft;
            _commentText.color = new Color(0.82f, 0.90f, 1.00f);
            _commentText.textWrappingMode = TextWrappingModes.Normal;
            UITheme.Apply(_commentText);

            // ─ Restart prompt ─────────────────────────────────────
            var prmtGo = Make("Prompt", panel.transform);
            Anch(prmtGo, 0,0,1,0, 0,14f,0,38f);
            _promptText = prmtGo.AddComponent<TextMeshProUGUI>();
            _promptText.text = "[ SPACE / ENTER ]  REMATCH";
            _promptText.fontSize = 13f;
            _promptText.alignment = TextAlignmentOptions.Center;
            _promptText.color = TextMuted;
            UITheme.Apply(_promptText);

            _overlay.SetActive(false);
        }

        GameObject BuildStatCard(Transform panel, bool isP1)
        {
            Color pCol = isP1 ? P1Col : P2Col;

            var card = Make(isP1 ? "Card1P" : "Card2P", panel);
            if (isP1) Anch(card, 0,0,0.5f,1, 16f,152f,-8f,-108f);
            else      Anch(card, 0.5f,0,1f,1, 8f,152f,-16f,-108f);
            card.AddComponent<Image>().color = BgCard;

            // top accent line in player color
            var cardTop = Make("CardTop", card.transform);
            Anch(cardTop, 0,1,1,1, 0,-2f,0,0);
            cardTop.AddComponent<Image>().color = new Color(pCol.r, pCol.g, pCol.b, 0.7f);

            // player tag pill
            var tagGo = Make("Tag", card.transform);
            Anch(tagGo, 0,1,0,1, 8f,-30f,36f,-8f);
            tagGo.AddComponent<Image>().color = new Color(pCol.r, pCol.g, pCol.b, 0.20f);
            var tagTxt = Make("TagTxt", tagGo.transform);
            FillRect(tagTxt.GetComponent<RectTransform>());
            var tt = tagTxt.AddComponent<TextMeshProUGUI>();
            tt.text = isP1 ? "1P" : "2P"; tt.fontSize = 11f; tt.fontStyle = FontStyles.Bold;
            tt.alignment = TextAlignmentOptions.Center; tt.color = pCol;
            UITheme.Apply(tt);

            // character name
            var nameGo = Make("CharName", card.transform);
            Anch(nameGo, 0,1,1,1, 44f,-32f,-8f,-8f);
            var nm = nameGo.AddComponent<TextMeshProUGUI>();
            nm.text = isP1 ? "1P" : "2P"; nm.fontSize = 18f; nm.fontStyle = FontStyles.Bold;
            nm.alignment = TextAlignmentOptions.Left; nm.color = TextWht;
            nm.textWrappingMode = TextWrappingModes.NoWrap;
            nm.overflowMode = TextOverflowModes.Ellipsis;
            UITheme.Apply(nm);
            if (isP1) _p1NameText = nm; else _p2NameText = nm;

            // stats text
            var statsGo = Make("Stats", card.transform);
            Anch(statsGo, 0,0,1,1, 10f,8f,-10f,-38f);
            var st = statsGo.AddComponent<TextMeshProUGUI>();
            st.text = "---"; st.fontSize = 14f;
            st.alignment = TextAlignmentOptions.TopLeft;
            st.color = new Color(0.78f, 0.88f, 1.00f);
            st.textWrappingMode = TextWrappingModes.Normal;
            UITheme.Apply(st);
            if (isP1) _p1StatsText = st; else _p2StatsText = st;

            return card;
        }

        // ── Show / Hide ────────────────────────────────────────────────
        void ShowResult(int winnerIndex)
        {
            if (_overlay == null) return;
            _winnerIndex = winnerIndex;
            _overlay.SetActive(true);
            _visible   = true;
            _animTimer = 0f;

            Color accent = winnerIndex == 0 ? P1Col : winnerIndex == 1 ? P2Col : DrawCol;

            if (_winnerBand != null)
                _winnerBand.color = new Color(accent.r, accent.g, accent.b, 0.15f);

            if (_winnerText != null)
            {
                _winnerText.text  = winnerIndex == 0 ? "1P  WIN" : winnerIndex == 1 ? "2P  WIN" : "DRAW";
                _winnerText.color = accent;
            }

            // top accent line matches winner
            RefreshCharInfo();
            RefreshStats();
            StartCoroutine(GenerateComment());
        }

        void HidePanel()
        {
            _visible = false;
            if (_overlay != null) _overlay.SetActive(false);
        }

        void RefreshCharInfo()
        {
            var bm = BattleManager.Instance;
            if (bm == null) return;
            if (_p1NameText)
            {
                string cc1 = bm.Character1?.catchCopy;
                _p1NameText.text = !string.IsNullOrEmpty(cc1)
                    ? $"{bm.Character1.characterName}\n<size=11><color=#aaccff>「{cc1}」</color></size>"
                    : bm.Character1?.characterName ?? "1P";
            }
            if (_p2NameText)
            {
                string cc2 = bm.Character2?.catchCopy;
                _p2NameText.text = !string.IsNullOrEmpty(cc2)
                    ? $"{bm.Character2.characterName}\n<size=11><color=#ffaaaa>「{cc2}」</color></size>"
                    : bm.Character2?.characterName ?? "2P";
            }
        }

        void RefreshStats()
        {
            if (BattleLogger.Instance == null) return;
            if (_p1StatsText) _p1StatsText.text = BuildStats(BattleLogger.Instance.P1);
            if (_p2StatsText) _p2StatsText.text = BuildStats(BattleLogger.Instance.P2);
        }

        static string BuildStats(PlayerLog log)
        {
            if (log == null) return "---";
            var sb = new StringBuilder();
            sb.AppendLine($"与ダメージ   {log.totalDamageDealt:F0}");
            sb.Append($"最多使用技   {log.MostUsedSkillName()}");
            return sb.ToString();
        }

        // ── AI comment (same HTTP logic as before) ─────────────────────
        IEnumerator GenerateComment()
        {
            if (_commentText != null) _commentText.text = "AI が試合を分析中…";

            var bm = BattleManager.Instance;
            if (bm == null) yield break;

            string matchLog = BuildMatchLog(bm, _winnerIndex);
            string prompt   = $"2D格闘ゲーム「プロンプトファイターズ」の試合結果を2〜3文で実況風にコメントしてください。日本語で、短く、テンポよく。\n試合データ: {matchLog}";
            string body     = BuildBody(prompt);

            using var req = new UnityWebRequest(PromptFighters.AI.AICharacterClient.Endpoint, "POST");
            req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
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

        static string BuildBody(string prompt)
        {
            string esc = prompt.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\n","\\n").Replace("\r","");
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
            if (winner < 0) return "互角の戦いの末、引き分けとなりました。";
            string wName = winner == 0 ? bm.Character1?.characterName ?? "1P" : bm.Character2?.characterName ?? "2P";
            return $"{wName} の勝利！自らの技の特徴を最大限に活かし、完璧な戦いを見せた。";
        }

        // ── UI Helpers ─────────────────────────────────────────────────
        static GameObject Make(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.layer = parent.gameObject.layer;
            go.AddComponent<RectTransform>().SetParent(parent, false);
            return go;
        }

        static void StretchFill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static void FillRect(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static void Anch(GameObject go,
            float axMin, float ayMin, float axMax, float ayMax,
            float oxMin, float oyMin, float oxMax, float oyMax)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(axMin, ayMin); rt.anchorMax = new Vector2(axMax, ayMax);
            rt.offsetMin = new Vector2(oxMin, oyMin); rt.offsetMax = new Vector2(oxMax, oyMax);
        }

        static void AddLine(Transform parent, string name,
            float axMin, float ayMin, float axMax, float ayMax,
            float oxMin, float oyMin, float oxMax, float oyMax, Color col)
        {
            var go = Make(name, parent);
            Anch(go, axMin,ayMin, axMax,ayMax, oxMin,oyMin, oxMax,oyMax);
            go.AddComponent<Image>().color = col;
        }
    }
}
