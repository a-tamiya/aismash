using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;
using PromptFighters.Battle;
using PromptFighters.Battle.Skills;

namespace PromptFighters.UI
{
    // マッチ決着（BO3含む）のリザルト画面。
    // AIコメント等は出さず、勝者キャラの立ち絵を画面左にバストアップで大きく表示し、
    // 右に「1P WIN」等を大きく出すシネマティックな演出にする。
    public class BattleResultUI : MonoBehaviour
    {
        // ── Palette (アーケード格ゲー調) ───────────────────────────────
        static readonly Color BgOverlay = new Color(0.01f, 0.01f, 0.02f, 0.96f);
        static readonly Color P1Col     = UITheme.P1Neon;
        static readonly Color P2Col     = UITheme.P2Neon;
        static readonly Color DrawCol   = UITheme.Gold;
        static readonly Color TextWht   = UITheme.Ink;
        static readonly Color TextMuted = new Color(0.55f, 0.50f, 0.34f);

        // ── State ──────────────────────────────────────────────────────
        GameObject       _overlay;
        RectTransform    _portraitFrame;
        Image            _portraitImg;
        Image            _portraitGlow;
        Image            _seam;
        CanvasGroup      _portraitGroup;
        CanvasGroup      _rightGroup;
        RectTransform    _rightBlock;
        Image            _rightBand;
        TextMeshProUGUI  _tagText;
        TextMeshProUGUI  _winText;
        TextMeshProUGUI  _nameText;
        TextMeshProUGUI  _copyText;
        TextMeshProUGUI  _dotsText;
        TextMeshProUGUI  _promptText;

        bool  _visible;
        int   _winnerIndex;
        float _animTimer;
        Color _accent;

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
            _animTimer += Time.unscaledDeltaTime;

            // 立ち絵：左からスライドイン＋フェード
            if (_portraitGroup != null && _portraitFrame != null)
            {
                float k = Mathf.Clamp01(_animTimer / 0.55f);
                float e = 1f - Mathf.Pow(1f - k, 3f); // easeOutCubic
                _portraitGroup.alpha = e;
                _portraitFrame.anchoredPosition = new Vector2(Mathf.Lerp(-70f, 0f, e), 0f);
            }

            // 勝者の色オーラの脈動
            if (_portraitGlow != null)
            {
                float g = 0.22f + 0.14f * Mathf.Sin(_animTimer * 2.2f);
                _portraitGlow.color = new Color(_accent.r, _accent.g, _accent.b, g);
            }

            // 右ブロック：少し遅れてフェード＋WINのパンチ
            if (_rightGroup != null)
            {
                float k = Mathf.Clamp01((_animTimer - 0.25f) / 0.4f);
                _rightGroup.alpha = k;
            }
            if (_winText != null)
            {
                float k = Mathf.Clamp01((_animTimer - 0.25f) / 0.35f);
                float s = Mathf.Lerp(1.35f, 1f, 1f - Mathf.Pow(1f - k, 3f));
                _winText.rectTransform.localScale = new Vector3(s, s, 1f);
            }

            // prompt pulse
            if (_promptText != null)
            {
                float a = 0.55f + 0.45f * Mathf.Sin(_animTimer * 2.8f);
                var c = _promptText.color; c.a = a; _promptText.color = c;
            }

            // restart input
            var kb = Keyboard.current;
            bool restart = kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame);
            var gp = Gamepad.current;
            if (gp != null && (gp.startButton.wasPressedThisFrame || gp.buttonSouth.wasPressedThisFrame)) restart = true;
            if (restart)
            {
                HidePanel();
                BattleManager.Instance?.ReturnToSetup();
            }
        }

        // ── UI Construction ────────────────────────────────────────────
        void BuildUI()
        {
            var canvas = GetComponentInParent<Canvas>() ?? FindAnyObjectByType<Canvas>();
            Transform root = canvas != null ? canvas.transform : transform;

            // ─ Overlay ──────────────────────────────────────────────
            _overlay = Make("ResultOverlay", root);
            StretchFill(_overlay.GetComponent<RectTransform>());
            _overlay.AddComponent<Image>().color = BgOverlay;

            // ─ 左：立ち絵フレーム（バストアップ・下半身は枠外へクリップ） ─
            var frame = Make("PortraitFrame", _overlay.transform);
            _portraitFrame = frame.GetComponent<RectTransform>();
            Anch(frame, 0f,0f, 0.58f,1f, 0,0,0,0);
            frame.AddComponent<Image>().color = UITheme.SteelDark;         // 地
            frame.AddComponent<RectMask2D>();                              // はみ出しをクリップ
            _portraitGroup = frame.AddComponent<CanvasGroup>();
            _portraitGroup.alpha = 0f;

            // 勝者カラーの背景オーラ
            var glow = Make("PortraitGlow", frame.transform);
            Anch(glow, 0f,0f, 1f,1f, 0,0,0,0);
            _portraitGlow = glow.AddComponent<Image>();
            _portraitGlow.color = new Color(P1Col.r, P1Col.g, P1Col.b, 0.22f);
            _portraitGlow.sprite = RadialSprite();
            _portraitGlow.type = Image.Type.Simple;

            // 立ち絵本体（サイズはShowResultで確定）
            var pImg = Make("Portrait", frame.transform);
            _portraitImg = pImg.AddComponent<Image>();
            _portraitImg.preserveAspect = true;
            _portraitImg.raycastTarget  = false;
            _portraitImg.rectTransform.anchorMin = _portraitImg.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            _portraitImg.rectTransform.pivot     = new Vector2(0.5f, 1f);
            _portraitImg.enabled = false;

            // ─ 斜めの継ぎ目ライン（ゴールド） ────────────────────────
            var seam = Make("Seam", _overlay.transform);
            _seam = seam.AddComponent<Image>();
            _seam.color = new Color(UITheme.Gold.r, UITheme.Gold.g, UITheme.Gold.b, 0.85f);
            var seRt = _seam.rectTransform;
            seRt.anchorMin = seRt.anchorMax = new Vector2(0.58f, 0.5f);
            seRt.pivot = new Vector2(0.5f, 0.5f);
            seRt.sizeDelta = new Vector2(7f, 2400f);
            seRt.localRotation = Quaternion.Euler(0, 0, 7f);

            // ─ 右：勝敗テキストブロック ──────────────────────────────
            var right = Make("RightBlock", _overlay.transform);
            _rightBlock = right.GetComponent<RectTransform>();
            Anch(right, 0.58f,0f, 1f,1f, 0,0,0,0);
            _rightGroup = right.AddComponent<CanvasGroup>();
            _rightGroup.alpha = 0f;

            // 右背景に薄い勝者カラーの縦バンド
            var band = Make("RightBand", right.transform);
            Anch(band, 0f,0f, 1f,1f, 0,0,0,0);
            _rightBand = band.AddComponent<Image>();
            _rightBand.color = new Color(P1Col.r, P1Col.g, P1Col.b, 0.06f);
            _rightBand.raycastTarget = false;

            // 中央寄せの内容コンテナ
            var block = Make("WinBlock", right.transform);
            Anch(block, 0.08f,0.30f, 0.98f,0.74f, 0,0,0,0);

            // プレイヤータグ "1P"
            var tag = Make("Tag", block.transform);
            Anch(tag, 0f,0.82f, 1f,1f, 0,0,0,0);
            _tagText = tag.AddComponent<TextMeshProUGUI>();
            _tagText.text = "1P"; _tagText.fontSize = 44f;
            _tagText.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _tagText.alignment = TextAlignmentOptions.Left;
            _tagText.color = P1Col;
            UITheme.Apply(_tagText);

            // 巨大 "WIN"
            var win = Make("WinText", block.transform);
            Anch(win, 0f,0.30f, 1f,0.86f, 0,0,0,0);
            _winText = win.AddComponent<TextMeshProUGUI>();
            _winText.text = "WIN"; _winText.fontSize = 150f;
            _winText.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _winText.alignment = TextAlignmentOptions.Left;
            _winText.color = TextWht;
            _winText.textWrappingMode = TextWrappingModes.NoWrap;
            UITheme.Apply(_winText);

            // キャラ名
            var nm = Make("Name", block.transform);
            Anch(nm, 0f,0.14f, 1f,0.32f, 0,0,0,0);
            _nameText = nm.AddComponent<TextMeshProUGUI>();
            _nameText.text = ""; _nameText.fontSize = 30f;
            _nameText.fontStyle = FontStyles.Bold;
            _nameText.alignment = TextAlignmentOptions.Left;
            _nameText.color = TextWht;
            _nameText.textWrappingMode = TextWrappingModes.NoWrap;
            _nameText.overflowMode = TextOverflowModes.Ellipsis;
            UITheme.Apply(_nameText);

            // キャッチコピー
            var cc = Make("Copy", block.transform);
            Anch(cc, 0f,0f, 1f,0.16f, 0,0,0,0);
            _copyText = cc.AddComponent<TextMeshProUGUI>();
            _copyText.text = ""; _copyText.fontSize = 17f;
            _copyText.fontStyle = FontStyles.Italic;
            _copyText.alignment = TextAlignmentOptions.Left;
            _copyText.color = UITheme.InkDim;
            _copyText.textWrappingMode = TextWrappingModes.NoWrap;
            _copyText.overflowMode = TextOverflowModes.Ellipsis;
            UITheme.Apply(_copyText);

            // ラウンドドット（BO3）
            var dots = Make("Dots", right.transform);
            Anch(dots, 0.08f,0.22f, 0.98f,0.28f, 0,0,0,0);
            _dotsText = dots.AddComponent<TextMeshProUGUI>();
            _dotsText.text = ""; _dotsText.fontSize = 30f;
            _dotsText.alignment = TextAlignmentOptions.Left;
            UITheme.Apply(_dotsText);

            // ─ Restart prompt ─────────────────────────────────────
            var prmt = Make("Prompt", _overlay.transform);
            Anch(prmt, 0f,0f, 1f,0f, 0,26f,0,60f);
            _promptText = prmt.AddComponent<TextMeshProUGUI>();
            _promptText.text = "[ SPACE / ENTER / START ]  REMATCH";
            _promptText.fontSize = 18f;
            _promptText.alignment = TextAlignmentOptions.Center;
            _promptText.color = TextMuted;
            UITheme.Apply(_promptText);

            _overlay.SetActive(false);
        }

        // ── Show / Hide ────────────────────────────────────────────────
        void ShowResult(int winnerIndex)
        {
            StopAllCoroutines();
            StartCoroutine(ShowResultDelayed(winnerIndex));
        }

        IEnumerator ShowResultDelayed(int winnerIndex)
        {
            // KOスロー（2.5s）＋KOバナーが終わるまで待ってから表示
            yield return new WaitForSecondsRealtime(2.9f);

            if (_overlay == null) yield break;
            _winnerIndex = winnerIndex;
            _overlay.SetActive(true);
            _visible   = true;
            _animTimer = 0f;

            var bm = BattleManager.Instance;
            bool coop = bm != null && bm.Mode == BattleMode.CoopVsBoss;

            _accent = coop
                ? (winnerIndex == 0 ? P1Col : DrawCol)
                : (winnerIndex == 0 ? P1Col : winnerIndex == 1 ? P2Col : DrawCol);

            // 勝者キャラと表示文言
            CharacterData winner = null;
            string tag = "";
            string winWord = "WIN";
            if (coop)
            {
                winner  = winnerIndex == 0 ? bm.Character1 : bm.Character2;
                tag     = winnerIndex == 0 ? "PLAYERS" : "BOSS";
                winWord = winnerIndex == 0 ? "WIN" : "LOSE";
            }
            else if (winnerIndex == 0) { winner = bm?.Character1; tag = "1P"; }
            else if (winnerIndex == 1) { winner = bm?.Character2; tag = "2P"; }
            else { winner = null; tag = ""; winWord = "DRAW"; }

            // アクセント色を各所へ
            _tagText.color   = _accent;
            _winText.color   = winWord == "LOSE" ? new Color(0.75f,0.78f,0.82f) : TextWht;
            _tagText.text    = tag;
            _winText.text    = winWord;
            _rightBand.color = new Color(_accent.r, _accent.g, _accent.b, 0.06f);
            _seam.color      = new Color(_accent.r, _accent.g, _accent.b, 0.85f);
            _portraitGlow.color = new Color(_accent.r, _accent.g, _accent.b, 0.22f);

            // キャラ名・コピー
            _nameText.text = winner?.characterName ?? "";
            string cc = winner?.catchCopy;
            _copyText.text = string.IsNullOrEmpty(cc) ? "" : $"「{cc}」";

            // ラウンドドット
            if (!coop && bm != null && bm.bestOf3)
                _dotsText.text = RoundDotsText(winnerIndex, bm);
            else
                _dotsText.text = "";

            // 立ち絵をセット
            SetupPortrait(winner);
        }

        void SetupPortrait(CharacterData winner)
        {
            Sprite s = winner?.spriteSet?.Get(CharacterSpriteId.Idle1) ?? winner?.characterSprite;
            if (s == null)
            {
                _portraitImg.enabled = false;
                if (_portraitGlow != null) _portraitGlow.enabled = false;
                return;
            }
            if (_portraitGlow != null) _portraitGlow.enabled = true;
            _portraitImg.enabled = true;
            _portraitImg.sprite  = s;

            // フレーム高さに対してバストアップになるよう大きめに配置し、下半身は枠外へ。
            Canvas.ForceUpdateCanvases();
            float fh = _portraitFrame.rect.height;
            if (fh <= 1f) fh = 1080f;
            float targetH = fh * 1.42f;
            float aspect  = s.rect.height > 0f ? s.rect.width / s.rect.height : 0.6f;
            var rt = _portraitImg.rectTransform;
            rt.sizeDelta        = new Vector2(targetH * aspect, targetH);
            rt.anchoredPosition = new Vector2(0f, fh * 0.06f); // 頭上の余白ぶん少し上へ
        }

        static string RoundDotsText(int winner, BattleManager bm)
        {
            string p1 = DotsFor(bm.P1RoundWins, winner == 0 ? P1Col : new Color(0.4f, 0.55f, 0.75f));
            string p2 = DotsFor(bm.P2RoundWins, winner == 1 ? P2Col : new Color(0.75f, 0.4f, 0.45f));
            return $"1P {p1}    2P {p2}";
        }

        static string DotsFor(int wins, Color col)
        {
            string hex = ColorUtility.ToHtmlStringRGB(col);
            string filled = $"<color=#{hex}>●</color>";
            string empty  = "<color=#445566>○</color>";
            string result = "";
            for (int i = 0; i < 2; i++)
                result += i < wins ? filled : empty;
            return result;
        }

        void HidePanel()
        {
            _visible = false;
            if (_overlay != null) _overlay.SetActive(false);
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

        static void Anch(GameObject go,
            float axMin, float ayMin, float axMax, float ayMax,
            float oxMin, float oyMin, float oxMax, float oyMax)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(axMin, ayMin); rt.anchorMax = new Vector2(axMax, ayMax);
            rt.offsetMin = new Vector2(oxMin, oyMin); rt.offsetMax = new Vector2(oxMax, oyMax);
        }

        // 中央が明るい放射状スプライト（オーラ用）。無ければ単色。
        static Sprite _radial;
        static Sprite RadialSprite()
        {
            if (_radial != null) return _radial;
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            Vector2 c = new Vector2((N - 1) * 0.5f, (N - 1) * 0.5f);
            float maxD = c.x;
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c) / maxD;
                float a = Mathf.Clamp01(1f - d);
                a = a * a;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            _radial = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
            return _radial;
        }
    }
}
