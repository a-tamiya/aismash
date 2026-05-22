using UnityEngine;
using PromptFighters.UI;

namespace PromptFighters.Battle
{
    // 連続ヒット数を追跡し "N HIT!!" を画面に表示する。BattleManager が自動追加する。
    public class ComboCounter : MonoBehaviour
    {
        const float ComboResetTime  = 1.8f;
        const int   MinDisplayCombo = 2;

        int   _p1Combo, _p2Combo;
        float _p1Timer, _p2Timer;
        Vector3 _p1LastPos, _p2LastPos;

        void Start()
        {
            var bm = BattleManager.Instance;
            if (bm == null) return;

            // fighter1 が受けた → P2 のコンボカウント
            if (bm.fighter1 != null)
                bm.fighter1.OnDamageReceived += (dmg, blocked) =>
                {
                    if (!blocked && dmg > 0f && bm.IsFighting && !bm.IsTraining)
                    {
                        _p2Combo++;
                        _p2Timer = ComboResetTime;
                        _p2LastPos = bm.fighter1 != null ? bm.fighter1.transform.position : Vector3.zero;
                        ShowCombo(_p2Combo, _p2LastPos);
                    }
                };

            // fighter2 が受けた → P1 のコンボカウント
            if (bm.fighter2 != null)
                bm.fighter2.OnDamageReceived += (dmg, blocked) =>
                {
                    if (!blocked && dmg > 0f && bm.IsFighting && !bm.IsTraining)
                    {
                        _p1Combo++;
                        _p1Timer = ComboResetTime;
                        _p1LastPos = bm.fighter2 != null ? bm.fighter2.transform.position : Vector3.zero;
                        ShowCombo(_p1Combo, _p1LastPos);
                    }
                };
        }

        void Update()
        {
            if (_p1Timer > 0f) { _p1Timer -= Time.deltaTime; if (_p1Timer <= 0f) _p1Combo = 0; }
            if (_p2Timer > 0f) { _p2Timer -= Time.deltaTime; if (_p2Timer <= 0f) _p2Combo = 0; }
        }

        static void ShowCombo(int count, Vector3 victimPos)
        {
            if (count < MinDisplayCombo) return;
            string label = $"{count} HIT" + (count >= 5 ? "!!" : "!");
            float size   = count >= 10 ? 2.5f : count >= 5 ? 2.0f : 1.6f;
            var col = count >= 10 ? new Color(1f, 0.25f, 0.05f)
                    : count >= 5  ? new Color(1f, 0.72f, 0.08f)
                    :               new Color(1f, 1f, 0.2f);
            DamagePopup.SpawnText(victimPos + Vector3.up * 2.0f, label, col, size);
        }
    }
}
