using UnityEngine;
using PromptFighters.AI;
using PromptFighters.Audio;
using PromptFighters.Battle.Skills;

namespace PromptFighters.Battle
{
    // 天使ギミックを Fighter に適用する
    public class AngelGimmickApplier : MonoBehaviour
    {
        public void Apply(GimmickData data, Fighter p1, Fighter p2)
        {
            if (data == null) return;

            // hp_swap は両者同時処理が必要なので特別扱い
            if (data.gimmick == "hp_swap") { SwapHP(p1, p2); }
            else ApplySingle(data.gimmick, data.target, data.value, data.duration, p1, p2);

            if (!string.IsNullOrEmpty(data.gimmick2))
            {
                if (data.gimmick2 == "hp_swap") SwapHP(p1, p2);
                else ApplySingle(data.gimmick2, data.target2, data.value2, data.duration2, p1, p2);
            }
            if (!string.IsNullOrEmpty(data.gimmick3))
            {
                if (data.gimmick3 == "hp_swap") SwapHP(p1, p2);
                else ApplySingle(data.gimmick3, data.target3, data.value3, data.duration3, p1, p2);
            }
        }

        static void SwapHP(Fighter p1, Fighter p2)
        {
            if (p1 == null || p2 == null) return;
            float hp1 = p1.CurrentHP;
            float hp2 = p2.CurrentHP;
            p1.DebugSetCurrentHP(Mathf.Min(hp2, p1.maxHP));
            p2.DebugSetCurrentHP(Mathf.Min(hp1, p2.maxHP));
            GameAudioManager.Instance?.PlayGimmickBuff();
        }

        void ApplySingle(string gimmick, string targetKey, float value, float duration, Fighter p1, Fighter p2)
        {
            Fighter target1 = ResolveTarget(targetKey, p1, p2, primary: true);
            Fighter target2 = targetKey == "both"
                ? ResolveTarget(targetKey, p1, p2, primary: false)
                : null;

            switch (gimmick)
            {
                case "hp_recover":
                    HealIfAlive(target1, value);
                    HealIfAlive(target2, value);
                    GameAudioManager.Instance?.PlayGimmickHeal();
                    break;
                case "speed_boost":
                    target1?.StartTemporarySpeedChange(value, duration);
                    target2?.StartTemporarySpeedChange(value, duration);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "speed_down":
                    target1?.StartTemporarySpeedChange(value, duration);
                    target2?.StartTemporarySpeedChange(value, duration);
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "jump_boost":
                    target1?.StartTemporaryJumpChange(value, duration);
                    target2?.StartTemporaryJumpChange(value, duration);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "damage_boost":
                    target1?.StartTemporaryDamageBoost(value, duration);
                    target2?.StartTemporaryDamageBoost(value, duration);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "transparent":
                case "invincible":
                    target1?.StartTemporaryInvincible(Mathf.Max(duration, 3f));
                    target2?.StartTemporaryInvincible(Mathf.Max(duration, 3f));
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "chaos":
                    target1?.StartTemporaryChaos(Mathf.Max(duration, 4f));
                    target2?.StartTemporaryChaos(Mathf.Max(duration, 4f));
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;

                // ── 新ギミック ──────────────────────────────────────────
                case "hp_drain":
                    target1?.DrainHP(Mathf.Max(value, 0.05f));
                    target2?.DrainHP(Mathf.Max(value, 0.05f));
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "hp_full":
                    HealIfAlive(target1, 1f);
                    HealIfAlive(target2, 1f);
                    GameAudioManager.Instance?.PlayGimmickHeal();
                    break;
                case "damage_down":
                    target1?.StartTemporaryDamageBoost(Mathf.Clamp(value, 0.1f, 0.99f), Mathf.Max(duration, 5f));
                    target2?.StartTemporaryDamageBoost(Mathf.Clamp(value, 0.1f, 0.99f), Mathf.Max(duration, 5f));
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "jump_down":
                    target1?.StartTemporaryJumpChange(Mathf.Clamp(value, 0.1f, 0.99f), Mathf.Max(duration, 5f));
                    target2?.StartTemporaryJumpChange(Mathf.Clamp(value, 0.1f, 0.99f), Mathf.Max(duration, 5f));
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "gravity_up":
                    target1?.StartTemporaryGravityChange(Mathf.Max(value, 1.5f), Mathf.Max(duration, 5f));
                    target2?.StartTemporaryGravityChange(Mathf.Max(value, 1.5f), Mathf.Max(duration, 5f));
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "gravity_down":
                    target1?.StartTemporaryGravityChange(Mathf.Clamp(value, 0.05f, 0.8f), Mathf.Max(duration, 5f));
                    target2?.StartTemporaryGravityChange(Mathf.Clamp(value, 0.05f, 0.8f), Mathf.Max(duration, 5f));
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "size_up":
                    target1?.StartTemporarySizeChange(Mathf.Max(value, 1.2f), Mathf.Max(duration, 5f));
                    target2?.StartTemporarySizeChange(Mathf.Max(value, 1.2f), Mathf.Max(duration, 5f));
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "size_down":
                    target1?.StartTemporarySizeChange(Mathf.Clamp(value, 0.2f, 0.9f), Mathf.Max(duration, 5f));
                    target2?.StartTemporarySizeChange(Mathf.Clamp(value, 0.2f, 0.9f), Mathf.Max(duration, 5f));
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "freeze":
                    target1?.ApplyStatus(StatusType.Stun, Mathf.Clamp(duration, 1f, 5f));
                    target2?.ApplyStatus(StatusType.Stun, Mathf.Clamp(duration, 1f, 5f));
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "burn":
                    target1?.ApplyStatus(StatusType.Burn, Mathf.Max(duration, 4f));
                    target2?.ApplyStatus(StatusType.Burn, Mathf.Max(duration, 4f));
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "guard_break":
                    target1?.ApplyStatus(StatusType.GuardBreak, Mathf.Max(duration, 3f));
                    target2?.ApplyStatus(StatusType.GuardBreak, Mathf.Max(duration, 3f));
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "obstacle":
                case "obstacle_platform":
                    SpawnPlatform(Mathf.Max(value, 1f), Mathf.Max(duration, 5f), targetKey, p1, p2);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "obstacle_wall":
                    SpawnWall(Mathf.Max(value, 1f), Mathf.Max(duration, 5f), targetKey, p1, p2);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "obstacle_bounce":
                    SpawnBouncePad(Mathf.Max(duration, 6f), targetKey, p1, p2);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "obstacle_rain":
                    SpawnRain(Mathf.Max((int)value, 2), Mathf.Max(duration, 8f));
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "obstacle_tilt":
                    SpawnTiltedPlatform(Mathf.Max(value, 1f), Mathf.Max(duration, 5f), targetKey, p1, p2);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
            }
        }

        // ── 足場・地形生成 ───────────────────────────────────────────

        Vector3 ObstaclePos(string posHint, Fighter p1, Fighter p2, float y)
        {
            var bm = BattleManager.Instance;
            float minX = bm?.StageMinX ?? -5f;
            float maxX = bm?.StageMaxX ??  5f;
            float x;
            if      (posHint == "player1" && p1 != null) x = p1.transform.position.x + Random.Range(-0.5f, 0.5f);
            else if (posHint == "player2" && p2 != null) x = p2.transform.position.x + Random.Range(-0.5f, 0.5f);
            else x = (minX + maxX) * 0.5f + Random.Range(-2.5f, 2.5f);
            return new Vector3(Mathf.Clamp(x, minX + 1.2f, maxX - 1.2f), y, 0f);
        }

        static GameObject MakeStaticObstacle(string name, Vector3 pos, Vector3 scale, Color col)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            go.transform.localScale = scale;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Static;
            go.AddComponent<BoxCollider2D>().size = Vector2.one;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0,0,1,1), new Vector2(0.5f,0.5f), 1f);
            sr.color = col;
            return go;
        }

        void SpawnPlatform(float widthScale, float duration, string posHint, Fighter p1, Fighter p2)
        {
            float w = Mathf.Clamp(widthScale * 1.8f, 1f, 9f);
            float h = 0.4f;
            var go = MakeStaticObstacle("AngelPlatform",
                ObstaclePos(posHint, p1, p2, Random.Range(1.5f, 3.5f)),
                new Vector3(w, h, 1f),
                new Color(1f, 0.85f, 0.1f, 0.93f));
            StartCoroutine(DestroyAfter(go, duration));
        }

        void SpawnWall(float heightScale, float duration, string posHint, Fighter p1, Fighter p2)
        {
            float h = Mathf.Clamp(heightScale * 1.6f, 1f, 7f);
            float w = 0.45f;
            var go = MakeStaticObstacle("AngelWall",
                ObstaclePos(posHint, p1, p2, h * 0.5f),
                new Vector3(w, h, 1f),
                new Color(0.4f, 0.65f, 1f, 0.93f));
            StartCoroutine(DestroyAfter(go, duration));
        }

        void SpawnBouncePad(float duration, string posHint, Fighter p1, Fighter p2)
        {
            var go = MakeStaticObstacle("AngelBounce",
                ObstaclePos(posHint, p1, p2, 0.25f),
                new Vector3(2f, 0.3f, 1f),
                new Color(0.15f, 1f, 0.45f, 0.95f));
            go.AddComponent<AngelBouncePad>();
            StartCoroutine(DestroyAfter(go, duration));
        }

        void SpawnRain(int count, float duration)
        {
            var bm = BattleManager.Instance;
            float minX = bm?.StageMinX ?? -5f;
            float maxX = bm?.StageMaxX ??  5f;
            count = Mathf.Clamp(count, 2, 10);
            for (int i = 0; i < count; i++)
            {
                float x = Mathf.Lerp(minX + 0.5f, maxX - 0.5f,
                    (float)i / Mathf.Max(count - 1, 1)) + Random.Range(-0.6f, 0.6f);
                float sz = Random.Range(0.5f, 1.3f);
                var go = new GameObject("AngelRain");
                go.transform.position = new Vector3(x, Random.Range(7f, 11f), 0f);
                go.transform.localScale = new Vector3(sz, sz, 1f);
                var rb = go.AddComponent<Rigidbody2D>();
                rb.gravityScale = Random.Range(1.8f, 3.5f);
                go.AddComponent<BoxCollider2D>().size = Vector2.one;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0,0,1,1), new Vector2(0.5f,0.5f), 1f);
                sr.color = new Color(Random.Range(0.7f,1f), Random.Range(0.2f,0.8f), Random.Range(0.1f,0.5f), 0.9f);
                StartCoroutine(DestroyAfter(go, duration));
            }
        }

        void SpawnTiltedPlatform(float widthScale, float duration, string posHint, Fighter p1, Fighter p2)
        {
            float w = Mathf.Clamp(widthScale * 1.8f, 1f, 7f);
            float angle = Random.Range(15f, 40f) * (Random.value > 0.5f ? 1f : -1f);
            var go = MakeStaticObstacle("AngelTilt",
                ObstaclePos(posHint, p1, p2, Random.Range(1.5f, 3f)),
                new Vector3(w, 0.4f, 1f),
                new Color(1f, 0.5f, 0.15f, 0.93f));
            go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            StartCoroutine(DestroyAfter(go, duration));
        }

        System.Collections.IEnumerator DestroyAfter(GameObject go, float duration)
        {
            yield return new WaitForSeconds(duration);
            if (go != null) Destroy(go);
        }

        static void HealIfAlive(Fighter f, float ratio)
        {
            if (f == null || f.State == FighterState.Dead) return;
            f.HealHP(f.maxHP * Mathf.Clamp(ratio, 0f, 1f));
        }

        static Fighter ResolveTarget(string target, Fighter p1, Fighter p2, bool primary)
        {
            switch (target)
            {
                case "player1":  return primary ? p1 : null;
                case "player2":  return primary ? p2 : null;
                case "both":     return primary ? p1 : p2;
                case "weaker":
                    if (p1 == null) return primary ? p2 : null;
                    if (p2 == null) return primary ? p1 : null;
                    return primary ? (p1.CurrentHP <= p2.CurrentHP ? p1 : p2) : null;
                case "stronger":
                    if (p1 == null) return primary ? p2 : null;
                    if (p2 == null) return primary ? p1 : null;
                    return primary ? (p1.CurrentHP >= p2.CurrentHP ? p1 : p2) : null;
                case "random":
                    return primary ? (Random.value > 0.5f ? p1 : p2) : null;
                default:
                    return primary ? p1 : p2;
            }
        }
    }

    // 踏んだファイターを上方向に弾くバウンスパッド
    public class AngelBouncePad : MonoBehaviour
    {
        void OnCollisionEnter2D(Collision2D col)
        {
            var f = col.gameObject.GetComponent<Fighter>();
            if (f == null) return;
            f.ApplyImpulse(new Vector2(0f, 22f), 0.12f);
            PromptFighters.UI.DamagePopup.SpawnText(
                transform.position + Vector3.up * 0.5f,
                "BOUNCE!", new Color(0.15f, 1f, 0.45f), 1.5f);
        }
    }
}
