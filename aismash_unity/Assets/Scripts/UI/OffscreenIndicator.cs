using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PromptFighters.Battle;

namespace PromptFighters.UI
{
    // 画面外に飛ばされたファイターを、画面端の矢印＋キャラアイコン（虫眼鏡表示）で示す。
    // 上への大きな吹っ飛びやKO演出でキャラが見切れても位置が分かるようにする保険。
    public class OffscreenIndicator : MonoBehaviour
    {
        const float EdgeMargin = 64f;   // 画面端からの内側マージン（キャンバス基準単位）
        const float MarkerSize = 84f;   // マーカー直径
        const float IconSize   = 62f;   // キャラアイコンの表示枠

        class Marker
        {
            public GameObject go;
            public RectTransform rt;
            public Image ring;
            public Image icon;
            public RectTransform arrow;
            public Image arrowImg;
            public TextMeshProUGUI tag;
        }

        RectTransform _canvasRect;
        Camera        _cam;
        Marker[]      _markers;
        static Sprite _triangle;

        void Start()
        {
            // 専用 Canvas を最前面に生成（sortOrder=999 で他の全 UI より上）
            var go = new GameObject("OffscreenCanvas");
            DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;
            go.AddComponent<UnityEngine.UI.CanvasScaler>();
            go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            _canvasRect = go.transform as RectTransform;

            _cam = Camera.main;

            _markers = new Marker[3];
            for (int i = 0; i < 3; i++) _markers[i] = BuildMarker();
        }

        Marker BuildMarker()
        {
            var m = new Marker();
            m.go = new GameObject("OffscreenMarker");
            m.go.layer = gameObject.layer;
            m.rt = m.go.AddComponent<RectTransform>();
            m.rt.SetParent(_canvasRect != null ? _canvasRect : transform, false);
            m.rt.anchorMin = m.rt.anchorMax = new Vector2(0.5f, 0.5f);
            m.rt.pivot = new Vector2(0.5f, 0.5f);
            m.rt.sizeDelta = new Vector2(MarkerSize, MarkerSize);

            // 矢印（画面外方向を指す）。マーカーの外側に置く。
            var arrowGo = new GameObject("Arrow");
            arrowGo.layer = gameObject.layer;
            m.arrow = arrowGo.AddComponent<RectTransform>();
            m.arrow.SetParent(m.rt, false);
            m.arrow.sizeDelta = new Vector2(34f, 34f);
            m.arrowImg = arrowGo.AddComponent<Image>();
            m.arrowImg.sprite = TriangleSprite();
            m.arrowImg.raycastTarget = false;

            // 円形の縁（プレイヤー色）。
            var ringGo = new GameObject("Ring");
            ringGo.layer = gameObject.layer;
            var ringRt = ringGo.AddComponent<RectTransform>();
            ringRt.SetParent(m.rt, false);
            ringRt.anchorMin = Vector2.zero; ringRt.anchorMax = Vector2.one;
            ringRt.offsetMin = ringRt.offsetMax = Vector2.zero;
            m.ring = ringGo.AddComponent<Image>();
            m.ring.sprite = PromptFighters.Battle.Skills.RuntimeSprite.Circle();
            m.ring.type = Image.Type.Simple;
            m.ring.raycastTarget = false;

            // 内側の暗い下地（キャラアイコンを見やすく）。
            var bgGo = new GameObject("Bg");
            bgGo.layer = gameObject.layer;
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.SetParent(m.rt, false);
            bgRt.anchorMin = bgRt.anchorMax = new Vector2(0.5f, 0.5f);
            bgRt.pivot = new Vector2(0.5f, 0.5f);
            bgRt.sizeDelta = new Vector2(MarkerSize - 10f, MarkerSize - 10f);
            var bg = bgGo.AddComponent<Image>();
            bg.sprite = PromptFighters.Battle.Skills.RuntimeSprite.Circle();
            bg.color = new Color(0.03f, 0.04f, 0.06f, 0.95f);
            bg.raycastTarget = false;
            // 円形マスクでキャラをはみ出させない。
            var mask = bgGo.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            // キャラアイコン（現在のスプライト。頭が上に来るよう上寄せ表示）。
            var iconGo = new GameObject("Icon");
            iconGo.layer = gameObject.layer;
            var iconRt = iconGo.AddComponent<RectTransform>();
            iconRt.SetParent(bgRt, false);
            iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.5f, 0.5f);
            iconRt.pivot = new Vector2(0.5f, 0.5f);
            iconRt.sizeDelta = new Vector2(IconSize, IconSize);
            m.icon = iconGo.AddComponent<Image>();
            m.icon.preserveAspect = true;
            m.icon.raycastTarget = false;

            // プレイヤータグ（1P/2P/BOSS）。
            var tagGo = new GameObject("Tag");
            tagGo.layer = gameObject.layer;
            var tagRt = tagGo.AddComponent<RectTransform>();
            tagRt.SetParent(m.rt, false);
            tagRt.anchorMin = new Vector2(0.5f, 0f); tagRt.anchorMax = new Vector2(0.5f, 0f);
            tagRt.pivot = new Vector2(0.5f, 1f);
            tagRt.anchoredPosition = new Vector2(0f, -2f);
            tagRt.sizeDelta = new Vector2(90f, 22f);
            m.tag = tagGo.AddComponent<TextMeshProUGUI>();
            m.tag.fontSize = 18f;
            m.tag.fontStyle = FontStyles.Bold | FontStyles.Italic;
            m.tag.alignment = TextAlignmentOptions.Center;
            UITheme.Apply(m.tag);

            m.go.SetActive(false);
            return m;
        }

        void LateUpdate()
        {
            if (_markers == null) return;
            if (_cam == null) _cam = Camera.main;

            var bm = BattleManager.Instance;
            bool active = bm != null && _cam != null && _canvasRect != null &&
                          (bm.Phase == BattlePhase.Fighting ||
                           bm.Phase == BattlePhase.Countdown ||
                           bm.Phase == BattlePhase.Training ||
                           bm.IsKoSlowActive);
            if (!active)
            {
                for (int i = 0; i < _markers.Length; i++) _markers[i].go.SetActive(false);
                return;
            }

            bool coop = bm.Mode == BattleMode.CoopVsBoss;
            UpdateFor(0, bm.fighter1, UITheme.P1Neon, "1P");
            UpdateFor(1, bm.fighter2, UITheme.P2Neon, "2P");
            UpdateFor(2, coop ? bm.boss : null, new Color(0.15f, 0.15f, 0.18f), "BOSS");
        }

        void UpdateFor(int idx, Fighter f, Color col, string tag)
        {
            var m = _markers[idx];
            if (f == null || !f.gameObject.activeInHierarchy || f.State == FighterState.Dead)
            {
                if (m.go.activeSelf) m.go.SetActive(false);
                return;
            }

            Vector3 world = f.transform.position + Vector3.up * 1.2f;
            Vector3 sp = _cam.WorldToScreenPoint(world);
            // カメラ後方（通常起きないが保険）は反転して端に寄せる。
            if (sp.z < 0f) { sp.x = Screen.width - sp.x; sp.y = Screen.height - sp.y; }

            bool onScreen = sp.z > 0f &&
                            sp.x >= EdgeMargin && sp.x <= Screen.width  - EdgeMargin &&
                            sp.y >= EdgeMargin && sp.y <= Screen.height - EdgeMargin;
            if (onScreen)
            {
                if (m.go.activeSelf) m.go.SetActive(false);
                return;
            }

            // 画面中心から対象方向へ、内側マージンの矩形境界までクランプ。
            Vector2 c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 dir = new Vector2(sp.x, sp.y) - c;
            if (dir.sqrMagnitude < 0.001f) dir = Vector2.up;
            float halfW = Screen.width  * 0.5f - EdgeMargin;
            float halfH = Screen.height * 0.5f - EdgeMargin;
            float tx = Mathf.Abs(dir.x) > 0.0001f ? halfW / Mathf.Abs(dir.x) : float.MaxValue;
            float ty = Mathf.Abs(dir.y) > 0.0001f ? halfH / Mathf.Abs(dir.y) : float.MaxValue;
            float t = Mathf.Min(tx, ty);
            Vector2 edge = c + dir * t;

            if (!m.go.activeSelf) m.go.SetActive(true);

            // 画面座標→キャンバスローカル座標（Overlay Canvas なのでカメラは null）。
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, edge, null, out local);
            m.rt.anchoredPosition = local;

            // 色・タグ・アイコン。
            m.ring.color = col;
            m.arrowImg.color = col;
            m.tag.text = tag;
            m.tag.color = col;
            var live = f.VisualRenderer != null ? f.VisualRenderer.sprite : null;
            m.icon.enabled = live != null;
            if (live != null) m.icon.sprite = live;

            // 矢印は対象方向を向け、マーカー外周に配置。
            float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            m.arrow.localRotation = Quaternion.Euler(0, 0, ang);
            Vector2 outDir = dir.normalized;
            m.arrow.anchoredPosition = outDir * (MarkerSize * 0.5f + 12f);
        }

        // 右向きの三角形スプライト（矢印用）。
        static Sprite TriangleSprite()
        {
            if (_triangle != null) return _triangle;
            const int N = 32;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                // 右向き三角形：x が大きいほど許容する縦幅が狭くなる。
                float fx = x / (float)(N - 1);              // 0..1
                float dyAllow = (1f - fx) * 0.5f;           // 左端0.5→右端0
                float dy = Mathf.Abs(y / (float)(N - 1) - 0.5f);
                bool inside = dy <= dyAllow;
                tex.SetPixel(x, y, inside ? Color.white : new Color(1, 1, 1, 0));
            }
            tex.Apply();
            _triangle = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
            return _triangle;
        }
    }
}
