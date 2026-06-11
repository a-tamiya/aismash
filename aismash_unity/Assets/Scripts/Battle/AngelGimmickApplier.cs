using UnityEngine;
using PromptFighters.AI;
using PromptFighters.Audio;
using PromptFighters.Battle.Skills;

namespace PromptFighters.Battle
{
    // 音声アイテムのギミックを Fighter に適用する（旧・天使ギミック）。
    public class AngelGimmickApplier : MonoBehaviour
    {
        // ステータス系のバフ・デバフ（速度/ジャンプ/与ダメ/重力/サイズ）は試合終了まで永続。
        // 無敵・反射・カウンター・状態異常など「過度／一時的」な効果は各caseの効果時間を維持する。
        const float Permanent = 99999f;

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
                    target1?.StartTemporarySpeedChange(value, Permanent);
                    target2?.StartTemporarySpeedChange(value, Permanent);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "speed_down":
                    target1?.StartTemporarySpeedChange(value, Permanent);
                    target2?.StartTemporarySpeedChange(value, Permanent);
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "jump_boost":
                    target1?.StartTemporaryJumpChange(value, Permanent);
                    target2?.StartTemporaryJumpChange(value, Permanent);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "damage_boost":
                    target1?.StartTemporaryDamageBoost(value, Permanent);
                    target2?.StartTemporaryDamageBoost(value, Permanent);
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
                    target1?.StartTemporaryDamageBoost(Mathf.Clamp(value, 0.1f, 0.99f), Permanent);
                    target2?.StartTemporaryDamageBoost(Mathf.Clamp(value, 0.1f, 0.99f), Permanent);
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "jump_down":
                    target1?.StartTemporaryJumpChange(Mathf.Clamp(value, 0.1f, 0.99f), Permanent);
                    target2?.StartTemporaryJumpChange(Mathf.Clamp(value, 0.1f, 0.99f), Permanent);
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "gravity_up":
                    target1?.StartTemporaryGravityChange(Mathf.Max(value, 1.5f), Permanent);
                    target2?.StartTemporaryGravityChange(Mathf.Max(value, 1.5f), Permanent);
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "gravity_down":
                    target1?.StartTemporaryGravityChange(Mathf.Clamp(value, 0.05f, 0.8f), Permanent);
                    target2?.StartTemporaryGravityChange(Mathf.Clamp(value, 0.05f, 0.8f), Permanent);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "size_up":
                    target1?.StartTemporarySizeChange(Mathf.Max(value, 1.2f), Permanent);
                    target2?.StartTemporarySizeChange(Mathf.Max(value, 1.2f), Permanent);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "size_down":
                    target1?.StartTemporarySizeChange(Mathf.Clamp(value, 0.2f, 0.9f), Permanent);
                    target2?.StartTemporarySizeChange(Mathf.Clamp(value, 0.2f, 0.9f), Permanent);
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

                // ── アクション系ギミック ────────────────────────────────
                case "teleport":
                    TeleportFighter(target1);
                    TeleportFighter(target2);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "position_swap":
                    if (p1 != null && p2 != null)
                    {
                        float tmpX = p1.transform.position.x;
                        p1.transform.position = new Vector3(p2.transform.position.x, p1.transform.position.y, 0f);
                        p2.transform.position = new Vector3(tmpX, p2.transform.position.y, 0f);
                        GameAudioManager.Instance?.PlayGimmickBuff();
                    }
                    break;
                case "launch":
                    float lv = Mathf.Clamp(value, 0.5f, 5f);
                    target1?.ApplyImpulse(new Vector2(Random.Range(-1f,1f) * lv * 4f, lv * 7f), 0.3f);
                    target2?.ApplyImpulse(new Vector2(Random.Range(-1f,1f) * lv * 4f, lv * 7f), 0.3f);
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "slow":
                    target1?.ApplyStatus(StatusType.Slow, Mathf.Max(duration, 5f));
                    target2?.ApplyStatus(StatusType.Slow, Mathf.Max(duration, 5f));
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "reflect":
                    target1?.StartTemporaryReflect(Mathf.Max(duration, 4f));
                    target2?.StartTemporaryReflect(Mathf.Max(duration, 4f));
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "hp_set":
                    if (target1 != null) target1.DebugSetCurrentHP(target1.maxHP * Mathf.Clamp01(value));
                    if (target2 != null) target2.DebugSetCurrentHP(target2.maxHP * Mathf.Clamp01(value));
                    GameAudioManager.Instance?.PlayGimmickHeal();
                    break;
                case "guard_fill":
                    target1?.FillGuard();
                    target2?.FillGuard();
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;

                // ── 拡張ギミック ────────────────────────────────────────
                case "hp_equal":
                    if (p1 != null && p2 != null &&
                        p1.State != FighterState.Dead && p2.State != FighterState.Dead)
                    {
                        float avg = (p1.CurrentHP + p2.CurrentHP) * 0.5f;
                        p1.DebugSetCurrentHP(Mathf.Min(avg, p1.maxHP));
                        p2.DebugSetCurrentHP(Mathf.Min(avg, p2.maxHP));
                        GameAudioManager.Instance?.PlayGimmickBuff();
                    }
                    break;
                case "counter_gimmick":
                    float ctDmg = Mathf.Max(value * 30f, 30f);
                    target1?.StartCounter(Mathf.Max(duration, 3f), ctDmg, 8f, new Vector2(1f, 0.3f), 0.4f);
                    target2?.StartCounter(Mathf.Max(duration, 3f), ctDmg, 8f, new Vector2(1f, 0.3f), 0.4f);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "ground_bounce":
                    float bounceF = Mathf.Clamp(value > 0f ? value * 10f : 12f, 5f, 20f);
                    target1?.StartGroundBounce(bounceF);
                    target2?.StartGroundBounce(bounceF);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "wind":
                    float windF = value != 0f ? Mathf.Clamp(value * 5f, 1f, 15f) : 4f;
                    target1?.StartTemporaryWind(windF, Mathf.Max(duration, 4f));
                    target2?.StartTemporaryWind(windF, Mathf.Max(duration, 4f));
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "floor_lava":
                    target1?.StartTemporaryFloorLava(Mathf.Clamp(value > 0f ? value : 0.1f, 0.05f, 0.5f), Mathf.Max(duration, 5f));
                    target2?.StartTemporaryFloorLava(Mathf.Clamp(value > 0f ? value : 0.1f, 0.05f, 0.5f), Mathf.Max(duration, 5f));
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "guard_disable":
                    target1?.StartTemporaryGuardDisable(Mathf.Max(duration, 4f));
                    target2?.StartTemporaryGuardDisable(Mathf.Max(duration, 4f));
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "skill_seal":
                    int sealSlot = value > 0f ? Mathf.Clamp((int)value - 1, 0, 3) : Random.Range(0, 4);
                    target1?.StartTemporarySkillSeal(sealSlot, Mathf.Max(duration, 5f));
                    target2?.StartTemporarySkillSeal(sealSlot, Mathf.Max(duration, 5f));
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "super_knockback":
                    target1?.StartTemporarySuperKnockback(Mathf.Max(duration, 5f));
                    target2?.StartTemporarySuperKnockback(Mathf.Max(duration, 5f));
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "obstacle_moving":
                    SpawnMovingPlatform(Mathf.Max(value, 1f), Mathf.Max(duration, 8f));
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "hp_share":
                    if (p1 != null && p2 != null && target1 != null &&
                        p1.State != FighterState.Dead && p2.State != FighterState.Dead)
                    {
                        float shareDur = Mathf.Max(duration, 6f);
                        Fighter shareOther = (target1 == p1) ? p2 : p1;
                        target1.StartHPShare(shareOther, shareDur);
                        shareOther.StartHPShare(target1, shareDur);
                        GameAudioManager.Instance?.PlayGimmickBuff();
                    }
                    break;
            }
        }

        void TeleportFighter(Fighter f)
        {
            if (f == null) return;
            var bm = BattleManager.Instance;
            float minX = bm?.StageMinX ?? -5f;
            float maxX = bm?.StageMaxX ??  5f;
            float x = Random.Range(minX + 1f, maxX - 1f);
            f.transform.position = new Vector3(x, Mathf.Max(f.transform.position.y, 0.5f), 0f);
            PromptFighters.UI.DamagePopup.SpawnText(
                f.transform.position + Vector3.up, "WARP!", new Color(0.8f, 0.3f, 1f), 1.5f);
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

        void SpawnMovingPlatform(float widthScale, float duration)
        {
            var bm   = BattleManager.Instance;
            float minX = bm?.StageMinX ?? -5f;
            float maxX = bm?.StageMaxX ??  5f;
            float w  = Mathf.Clamp(widthScale * 1.8f, 1f, 7f);
            var go   = new GameObject("AngelMovingPlatform");
            go.transform.position   = new Vector3(0f, Random.Range(1.5f, 3.5f), 0f);
            go.transform.localScale = new Vector3(w, 0.35f, 1f);
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            go.AddComponent<BoxCollider2D>().size = Vector2.one;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = Sprite.Create(Texture2D.whiteTexture, new Rect(0,0,1,1), new Vector2(0.5f,0.5f), 1f);
            sr.color        = new Color(0.3f, 1f, 0.8f, 0.93f);
            sr.sortingOrder = 6;
            go.AddComponent<AngelMovingPlatform>().Init(minX + 1f, maxX - 1f, duration);
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

    // 左右に往復する動くプラットフォーム
    public class AngelMovingPlatform : MonoBehaviour
    {
        float _minX, _maxX, _speed;
        int   _dir = 1;
        Rigidbody2D _rb;

        public void Init(float minX, float maxX, float duration)
        {
            _minX  = minX;
            _maxX  = maxX;
            _speed = (_maxX - _minX) / Mathf.Max(duration * 0.4f, 1f);
            _rb    = GetComponent<Rigidbody2D>();
        }

        void FixedUpdate()
        {
            if (_rb == null) return;
            float nx = _rb.position.x + _dir * _speed * Time.fixedDeltaTime;
            if (nx >= _maxX) { nx = _maxX; _dir = -1; }
            if (nx <= _minX) { nx = _minX; _dir =  1; }
            _rb.MovePosition(new Vector2(nx, _rb.position.y));
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
