using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PromptFighters.Battle;
using PromptFighters.Battle.Skills;
using PromptFighters.UI;

namespace PromptFighters.GameFlow
{
    // 生成が完全に完了したキャラクターの「完成披露」演出。
    // 待った甲斐を演出で回収する見せ場。バックグラウンド生成のため、
    // プレイヤーが安全な画面（タイトル／キャラ選択）に戻ったタイミングで再生する。
    public partial class PreBattlePanel : MonoBehaviour
    {
        readonly Queue<CharacterData> _revealQueue = new Queue<CharacterData>();
        bool _revealing;
        bool _revealSkip;

        GameObject       _revealPanel;
        CanvasGroup      _revealGroup;
        Image            _revealGlow;
        RectTransform    _revealGlowRt;
        TextMeshProUGUI  _revealKicker;
        RectTransform    _revealKickerRt;
        Image            _revealArt;
        RectTransform    _revealArtRt;
        Image            _revealFlash;
        TextMeshProUGUI  _revealName;
        TextMeshProUGUI  _revealCatch;
        readonly RectTransform[]   _revealSkillRow  = new RectTransform[4];
        readonly TextMeshProUGUI[] _revealSkillText = new TextMeshProUGUI[4];
        TextMeshProUGUI  _revealPrompt;
        CharacterData    _revealData;

        static readonly string[] RevealSlotLabels = { "B", "A", "X", "スマッシュ" };

        public void EnqueueReveal(CharacterData data)
        {
            if (data != null) _revealQueue.Enqueue(data);
        }

        // Update から毎フレーム呼ぶ。演出中／これから始める場合は true を返し、
        // 呼び出し側（Update）はそこで早期 return して他の入力を止める。
        bool UpdateReveal()
        {
            if (_revealing)
            {
                if (WasMenuConfirmPressed() || WasCancelPressed()) _revealSkip = true;
                return true;
            }
            if (_revealQueue.Count == 0) return false;

            // 安全な画面（タイトル／キャラ選択、モーダルなし、試合中でない）でのみ再生する
            var bm = BattleManager.Instance;
            bool inMatch = bm != null && bm.Phase != BattlePhase.Setup;
            bool onSafeScreen =
                ((_panel != null && _panel.activeSelf) || (_titlePanel != null && _titlePanel.activeSelf));
            bool modalOpen =
                (_settingsPanel != null && _settingsPanel.activeSelf) ||
                (_controlsPanel != null && _controlsPanel.activeSelf) ||
                (_deleteConfirmPanel != null && _deleteConfirmPanel.activeSelf) ||
                (_stageSelectPanel != null && _stageSelectPanel.activeSelf) ||
                (_generationSetupPanel != null && _generationSetupPanel.activeSelf) ||
                (_generatingPanel != null && _generatingPanel.activeSelf);
            if (inMatch || _tutorialActive || !onSafeScreen || modalOpen) return false;

            _revealing = true;
            StartCoroutine(RevealRoutine(_revealQueue.Dequeue()));
            return true;
        }

        void BuildRevealPanel()
        {
            _revealPanel = CreateUIObject("RevealOverlay", transform);
            StretchFull(_revealPanel.GetComponent<RectTransform>());
            _revealGroup = _revealPanel.AddComponent<CanvasGroup>();
            _revealGroup.interactable = true;
            _revealGroup.blocksRaycasts = true;

            var backdrop = _revealPanel.AddComponent<Image>();
            // Linearカラー空間ではα<1で裏の明るいUIが透けるため完全不透明にする
            backdrop.color = new Color(0.01f, 0.012f, 0.03f, 1f);

            var t = _revealPanel.transform;

            // 背後の放射グロー（脈動・回転）
            var glowGo = CreateUIObject("RevealGlow", t);
            _revealGlowRt = glowGo.GetComponent<RectTransform>();
            _revealGlowRt.anchoredPosition = new Vector2(0f, 20f);
            _revealGlowRt.sizeDelta = new Vector2(1100f, 1100f);
            _revealGlow = glowGo.AddComponent<Image>();
            _revealGlow.sprite = CreateGradientSprite(
                new Color(PromptFighters.UI.UITheme.Gold.r, PromptFighters.UI.UITheme.Gold.g, PromptFighters.UI.UITheme.Gold.b, 0.0f),
                new Color(PromptFighters.UI.UITheme.Gold.r, PromptFighters.UI.UITheme.Gold.g, PromptFighters.UI.UITheme.Gold.b, 0.22f),
                new Color(PromptFighters.UI.UITheme.Gold.r, PromptFighters.UI.UITheme.Gold.g, PromptFighters.UI.UITheme.Gold.b, 0.22f),
                new Color(PromptFighters.UI.UITheme.Gold.r, PromptFighters.UI.UITheme.Gold.g, PromptFighters.UI.UITheme.Gold.b, 0.0f));
            _revealGlow.raycastTarget = false;

            // 斜めネオンストライプ（1P青/2P赤で挟む・お祝いムード）
            MakeSlantBar(t, "RevealStripeL", new Vector2(-720f, 0f), new Vector2(120f, 1300f),
                new Color(PromptFighters.UI.UITheme.P1Neon.r, PromptFighters.UI.UITheme.P1Neon.g, PromptFighters.UI.UITheme.P1Neon.b, 0.10f), 90f);
            MakeSlantBar(t, "RevealStripeR", new Vector2(720f, 0f), new Vector2(120f, 1300f),
                new Color(PromptFighters.UI.UITheme.P2Neon.r, PromptFighters.UI.UITheme.P2Neon.g, PromptFighters.UI.UITheme.P2Neon.b, 0.10f), 90f);

            // 上部キッカー「NEW FIGHTER!」
            MakeSlantBar(t, "RevealKickerPlate", new Vector2(0f, 402f), new Vector2(560f, 66f),
                new Color(PromptFighters.UI.UITheme.GoldDim.r, PromptFighters.UI.UITheme.GoldDim.g, PromptFighters.UI.UITheme.GoldDim.b, 0.35f), 26f);
            _revealKicker = MakeLabel(t, "RevealKicker", "NEW FIGHTER!",
                new Vector2(0f, 402f), new Vector2(760f, 70f), 44f, PromptFighters.UI.UITheme.Gold);
            _revealKicker.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _revealKickerRt = _revealKicker.rectTransform;

            // キャラ立ち絵
            var artGo = CreateUIObject("RevealArt", t);
            _revealArtRt = artGo.GetComponent<RectTransform>();
            _revealArtRt.anchoredPosition = new Vector2(0f, 10f);
            _revealArtRt.sizeDelta = new Vector2(430f, 540f);
            _revealArt = artGo.AddComponent<Image>();
            _revealArt.preserveAspect = true;
            _revealArt.raycastTarget = false;

            // 登場フラッシュ（白）
            var flashGo = CreateUIObject("RevealFlash", t);
            StretchFull(flashGo.GetComponent<RectTransform>());
            _revealFlash = flashGo.AddComponent<Image>();
            _revealFlash.color = new Color(1f, 1f, 1f, 0f);
            _revealFlash.raycastTarget = false;

            // 名前・キャッチコピー
            _revealName = MakeLabel(t, "RevealName", "",
                new Vector2(0f, -300f), new Vector2(1200f, 70f), 46f, Color.white);
            _revealName.fontStyle = FontStyles.Bold | FontStyles.Italic;
            _revealName.textWrappingMode = TextWrappingModes.NoWrap;
            _revealName.overflowMode = TextOverflowModes.Ellipsis;

            _revealCatch = MakeLabel(t, "RevealCatch", "",
                new Vector2(0f, -352f), new Vector2(1100f, 40f), 24f, PromptFighters.UI.UITheme.Gold);
            _revealCatch.fontStyle = FontStyles.Bold | FontStyles.Italic;

            // 技名4行（右側にスライドイン）
            for (int i = 0; i < 4; i++)
            {
                float y = 190f - i * 88f;
                var rowGo = CreateUIObject($"RevealSkillRow{i}", t);
                var rt = rowGo.GetComponent<RectTransform>();
                rt.anchoredPosition = new Vector2(430f, y);
                rt.sizeDelta = new Vector2(430f, 74f);
                _revealSkillRow[i] = rt;

                var plate = rowGo.AddComponent<Image>();
                plate.sprite = PromptFighters.UI.UITheme.VGradient;
                plate.type = Image.Type.Simple;
                plate.color = new Color(0.06f, 0.07f, 0.11f, 0.9f);
                plate.raycastTarget = false;

                MakeSlantBar(rowGo.transform, "SlotPlate", new Vector2(-168f, 0f), new Vector2(74f, 66f),
                    PromptFighters.UI.UITheme.Gold, 12f);
                var slot = MakeLabel(rowGo.transform, "Slot", RevealSlotLabels[i],
                    new Vector2(-168f, 0f), new Vector2(74f, 66f), i == 3 ? 15f : 26f, new Color(0.12f, 0.08f, 0f));
                slot.fontStyle = FontStyles.Bold | FontStyles.Italic;

                _revealSkillText[i] = MakeLabel(rowGo.transform, "Name", "",
                    new Vector2(34f, 0f), new Vector2(340f, 66f), 22f, Color.white);
                _revealSkillText[i].alignment = TextAlignmentOptions.Left;
                _revealSkillText[i].fontStyle = FontStyles.Bold;
                _revealSkillText[i].textWrappingMode = TextWrappingModes.NoWrap;
                _revealSkillText[i].overflowMode = TextOverflowModes.Ellipsis;
            }

            _revealPrompt = MakeLabel(t, "RevealPrompt", "▶ スペース / A で決定",
                new Vector2(0f, -430f), new Vector2(700f, 40f), 20f, PromptFighters.UI.UITheme.InkDim);
            _revealPrompt.fontStyle = FontStyles.Bold | FontStyles.Italic;

            _revealPanel.SetActive(false);
        }

        IEnumerator RevealRoutine(CharacterData data)
        {
            _revealData = data;
            _revealSkip = false;

            // 内容セット
            _revealName.text  = data.characterName;
            _revealCatch.text = string.IsNullOrEmpty(data.catchCopy) ? "" : $"「{data.catchCopy}」";
            _revealName.color = Color.white;
            EnsurePreviewSprite(data);
            Sprite art = data.characterSprite;
            _revealArt.sprite  = art;
            _revealArt.enabled = art != null;
            for (int i = 0; i < 4; i++)
            {
                var s = (data.skills != null && i < data.skills.Length) ? data.skills[i] : null;
                _revealSkillText[i].text = s != null ? s.skill_name : "---";
                _revealSkillRow[i].gameObject.SetActive(false);
            }

            _revealPanel.transform.SetAsLastSibling();
            _revealPanel.SetActive(true);
            _revealGroup.alpha = 0f;
            _revealFlash.color = new Color(1f, 1f, 1f, 0f);
            _revealKicker.rectTransform.localScale = Vector3.one;

            PromptFighters.Audio.GameAudioManager.Instance?.PlayGimmickBuff();

            // フェードイン＋立ち絵ポップ（実時間）
            float t0 = 0f;
            const float appear = 0.42f;
            _revealArtRt.localScale = Vector3.one * 0.3f;
            while (t0 < appear && !_revealSkip)
            {
                t0 += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t0 / appear);
                float ease = 1f - (1f - k) * (1f - k);
                _revealGroup.alpha = k;
                // わずかにオーバーシュートして着地
                float pop = 1f + 0.12f * Mathf.Sin(ease * Mathf.PI);
                _revealArtRt.localScale = Vector3.one * Mathf.Lerp(0.3f, pop, ease);
                _revealFlash.color = new Color(1f, 1f, 1f, 0.5f * (1f - k));
                yield return null;
            }
            _revealGroup.alpha = 1f;
            _revealArtRt.localScale = Vector3.one;
            _revealFlash.color = new Color(1f, 1f, 1f, 0f);

            // 技名を1行ずつスライドイン
            for (int i = 0; i < 4 && !_revealSkip; i++)
            {
                _revealSkillRow[i].gameObject.SetActive(true);
                PromptFighters.Audio.GameAudioManager.Instance?.PlayGimmickHeal();
                float ts = 0f;
                const float slideDur = 0.18f;
                while (ts < slideDur && !_revealSkip)
                {
                    ts += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(ts / slideDur);
                    float ease = 1f - (1f - k) * (1f - k);
                    _revealSkillRow[i].anchoredPosition = new Vector2(
                        Mathf.Lerp(700f, 430f, ease), _revealSkillRow[i].anchoredPosition.y);
                    yield return null;
                }
                _revealSkillRow[i].anchoredPosition = new Vector2(430f, _revealSkillRow[i].anchoredPosition.y);
            }
            // スキップされた場合は全部即表示
            for (int i = 0; i < 4; i++)
            {
                _revealSkillRow[i].gameObject.SetActive(true);
                _revealSkillRow[i].anchoredPosition = new Vector2(430f, _revealSkillRow[i].anchoredPosition.y);
            }

            // 待機（決定 or 自動）＋アイドルアニメ・グロー回転・プロンプト点滅
            float hold = 0f;
            const float autoDismiss = 6.5f;
            int idleFrame = 0;
            float idleTimer = 0f;
            _revealSkip = false; // ここからの入力で閉じる
            while (hold < autoDismiss && !_revealSkip)
            {
                hold += Time.unscaledDeltaTime;
                idleTimer += Time.unscaledDeltaTime;
                if (idleTimer >= 0.3f)
                {
                    idleTimer = 0f;
                    idleFrame = (idleFrame + 1) % 3;
                    ApplyRevealIdleFrame(data, idleFrame);
                }
                if (_revealGlowRt != null)
                {
                    _revealGlowRt.localRotation = Quaternion.Euler(0f, 0f, hold * 18f);
                    float pulse = 0.5f + 0.5f * Mathf.Sin(hold * 3f);
                    _revealGlowRt.localScale = Vector3.one * Mathf.Lerp(0.94f, 1.04f, pulse);
                }
                if (_revealPrompt != null)
                {
                    float b = 0.5f + 0.5f * Mathf.Sin(hold * 5f);
                    var c = PromptFighters.UI.UITheme.Ink;
                    _revealPrompt.color = new Color(c.r, c.g, c.b, Mathf.Lerp(0.35f, 1f, b));
                }
                yield return null;
            }

            // フェードアウト
            float tf = 0f;
            const float fade = 0.3f;
            while (tf < fade)
            {
                tf += Time.unscaledDeltaTime;
                _revealGroup.alpha = 1f - tf / fade;
                yield return null;
            }
            _revealPanel.SetActive(false);
            _revealGroup.alpha = 1f;
            _revealing = false;
        }

        void ApplyRevealIdleFrame(CharacterData data, int frame)
        {
            if (_revealArt == null || data?.spriteSet == null) return;
            var id = (CharacterSpriteId)((int)CharacterSpriteId.Idle1 + frame);
            var s = data.spriteSet.Get(id, data.characterSprite);
            if (s != null) { _revealArt.sprite = s; _revealArt.enabled = true; }
        }
    }
}
