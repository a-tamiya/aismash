using UnityEngine;
using PromptFighters.AI;
using PromptFighters.Audio;
using PromptFighters.Battle.Skills;

namespace PromptFighters.Battle
{
    // 音声アイテムのギミックを Fighter に適用する（旧・天使ギミック）。
    public class AngelGimmickApplier : MonoBehaviour
    {
        // ステータス系のバフ・デバフ（速度/ジャンプ/与ダメ/重力/サイズ）は Fighter 側で永続適用
        // （ApplyPermanentX、ラウンドをまたいで保持・後勝ち上書き）。
        // 効果時間系（無敵・反射・カウンター・状態異常・障害物など）の継続時間はこの倍率で一括延長する。
        const float DurationScale = 2f;
        static Sprite _rainBlockSprite;
        static bool _rainBlockTried;
        static Sprite _wallSprite;
        static Sprite _platformSprite;
        // 台画像の不透明上端のピボットからのピクセル（StagePlatformSpawnerと同じ基準）。
        const float PlatformOpaqueTopPixels = 176f;

        // 生成した地形・障害物は時間で消さず、BO3（マッチ）が終わるまで残す。
        // AngelController が試合開始/終了時に ClearObstacles() でまとめて破棄する。
        readonly System.Collections.Generic.List<GameObject> _persistentObstacles =
            new System.Collections.Generic.List<GameObject>();

        void RegisterObstacle(GameObject go)
        {
            if (go != null) _persistentObstacles.Add(go);
        }

        // マッチ（BO3）境界で全障害物を破棄。
        public void ClearObstacles()
        {
            for (int i = 0; i < _persistentObstacles.Count; i++)
                if (_persistentObstacles[i] != null) Destroy(_persistentObstacles[i]);
            _persistentObstacles.Clear();
        }

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
                    target1?.ApplyPermanentSpeed(value);
                    target2?.ApplyPermanentSpeed(value);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "speed_down":
                    target1?.ApplyPermanentSpeed(value);
                    target2?.ApplyPermanentSpeed(value);
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "jump_boost":
                    target1?.ApplyPermanentJump(value);
                    target2?.ApplyPermanentJump(value);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "damage_boost":
                    target1?.ApplyPermanentDamage(value);
                    target2?.ApplyPermanentDamage(value);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "transparent":
                case "invincible":
                    target1?.StartTemporaryInvincible(Mathf.Max(duration, 3f) * DurationScale);
                    target2?.StartTemporaryInvincible(Mathf.Max(duration, 3f) * DurationScale);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "chaos":
                    target1?.StartTemporaryChaos(Mathf.Max(duration, 4f) * DurationScale);
                    target2?.StartTemporaryChaos(Mathf.Max(duration, 4f) * DurationScale);
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
                    target1?.ApplyPermanentDamage(Mathf.Clamp(value, 0.1f, 0.99f));
                    target2?.ApplyPermanentDamage(Mathf.Clamp(value, 0.1f, 0.99f));
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "jump_down":
                    target1?.ApplyPermanentJump(Mathf.Clamp(value, 0.1f, 0.99f));
                    target2?.ApplyPermanentJump(Mathf.Clamp(value, 0.1f, 0.99f));
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "gravity_up":
                    target1?.ApplyPermanentGravity(Mathf.Max(value, 1.5f));
                    target2?.ApplyPermanentGravity(Mathf.Max(value, 1.5f));
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "gravity_down":
                    target1?.ApplyPermanentGravity(Mathf.Clamp(value, 0.05f, 0.8f));
                    target2?.ApplyPermanentGravity(Mathf.Clamp(value, 0.05f, 0.8f));
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "size_up":
                    target1?.ApplyPermanentSize(Mathf.Max(value, 1.2f));
                    target2?.ApplyPermanentSize(Mathf.Max(value, 1.2f));
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "size_down":
                    target1?.ApplyPermanentSize(Mathf.Clamp(value, 0.2f, 0.9f));
                    target2?.ApplyPermanentSize(Mathf.Clamp(value, 0.2f, 0.9f));
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "freeze":
                    // 行動不能はしっかり止める（通常技の0.7秒上限を解除し、2.5〜4秒固定する）。
                    float freezeDur = Mathf.Clamp(duration > 0f ? duration : 3f, 2.5f, 4f);
                    target1?.ApplyStatus(StatusType.Stun, freezeDur, freezeDur);
                    target2?.ApplyStatus(StatusType.Stun, freezeDur, freezeDur);
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "burn":
                    target1?.ApplyStatus(StatusType.Burn, Mathf.Max(duration, 4f) * DurationScale);
                    target2?.ApplyStatus(StatusType.Burn, Mathf.Max(duration, 4f) * DurationScale);
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "guard_break":
                    target1?.ApplyStatus(StatusType.GuardBreak, Mathf.Max(duration, 3f) * DurationScale);
                    target2?.ApplyStatus(StatusType.GuardBreak, Mathf.Max(duration, 3f) * DurationScale);
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "obstacle":
                case "obstacle_platform":
                    SpawnPlatform(Mathf.Max(value, 1f), Mathf.Max(duration, 5f) * DurationScale, targetKey, p1, p2);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "obstacle_wall":
                    SpawnWall(Mathf.Max(value, 1f), Mathf.Max(duration, 5f) * DurationScale, targetKey, p1, p2);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "obstacle_bounce":
                    SpawnBouncePad(Mathf.Max(duration, 6f) * DurationScale, targetKey, p1, p2);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "obstacle_rain":
                    SpawnRain(Mathf.Max((int)value, 2), Mathf.Max(duration, 8f) * DurationScale);
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "obstacle_tilt":
                    SpawnTiltedPlatform(Mathf.Max(value, 1f), Mathf.Max(duration, 5f) * DurationScale, targetKey, p1, p2);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "clear_obstacles":
                    // 願いで地形・障害物を全消去する（更地に戻す）。
                    ClearObstacles();
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
                    target1?.ApplyStatus(StatusType.Slow, Mathf.Max(duration, 5f) * DurationScale);
                    target2?.ApplyStatus(StatusType.Slow, Mathf.Max(duration, 5f) * DurationScale);
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "reflect":
                    target1?.StartTemporaryReflect(Mathf.Max(duration, 4f) * DurationScale);
                    target2?.StartTemporaryReflect(Mathf.Max(duration, 4f) * DurationScale);
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
                    target1?.StartCounter(Mathf.Max(duration, 3f) * DurationScale, ctDmg, 8f, new Vector2(1f, 0.3f), 0.4f);
                    target2?.StartCounter(Mathf.Max(duration, 3f) * DurationScale, ctDmg, 8f, new Vector2(1f, 0.3f), 0.4f);
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
                    target1?.StartTemporaryWind(windF, Mathf.Max(duration, 4f) * DurationScale);
                    target2?.StartTemporaryWind(windF, Mathf.Max(duration, 4f) * DurationScale);
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "floor_lava":
                    // 床の溶岩化は「床」の効果なので target に関係なく全員に適用。ダメージは従来の半分。
                    float lavaRatio = Mathf.Clamp(value > 0f ? value : 0.1f, 0.05f, 0.5f) * 0.5f;
                    float lavaDur   = Mathf.Max(duration, 5f) * DurationScale;
                    var fighters = BattleManager.Instance?.Fighters;
                    if (fighters != null && fighters.Count > 0)
                    {
                        for (int i = 0; i < fighters.Count; i++)
                            fighters[i]?.StartTemporaryFloorLava(lavaRatio, lavaDur);
                    }
                    else
                    {
                        p1?.StartTemporaryFloorLava(lavaRatio, lavaDur);
                        p2?.StartTemporaryFloorLava(lavaRatio, lavaDur);
                    }
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "guard_disable":
                    target1?.StartTemporaryGuardDisable(Mathf.Max(duration, 4f) * DurationScale);
                    target2?.StartTemporaryGuardDisable(Mathf.Max(duration, 4f) * DurationScale);
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "skill_seal":
                    int sealSlot = value > 0f ? Mathf.Clamp((int)value - 1, 0, 3) : Random.Range(0, 4);
                    target1?.StartTemporarySkillSeal(sealSlot, Mathf.Max(duration, 5f) * DurationScale);
                    target2?.StartTemporarySkillSeal(sealSlot, Mathf.Max(duration, 5f) * DurationScale);
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "super_knockback":
                    target1?.StartTemporarySuperKnockback(Mathf.Max(duration, 5f) * DurationScale);
                    target2?.StartTemporarySuperKnockback(Mathf.Max(duration, 5f) * DurationScale);
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "obstacle_moving":
                    SpawnMovingPlatform(Mathf.Max(value, 1f), Mathf.Max(duration, 8f) * DurationScale);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "hp_share":
                    if (p1 != null && p2 != null && target1 != null &&
                        p1.State != FighterState.Dead && p2.State != FighterState.Dead)
                    {
                        float shareDur = Mathf.Max(duration, 6f) * DurationScale;
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

        static Sprite RainBlockSprite()
        {
            if (!_rainBlockTried)
            {
                _rainBlockSprite = Resources.Load<Sprite>("Stage/obstacle");
                _rainBlockTried = true;
            }
            return _rainBlockSprite;
        }

        static Sprite WallSprite()
        {
            // null をキャッシュしてしまわないよう、取得できるまで毎回試みる（Resources側で内部キャッシュ）。
            if (_wallSprite == null) _wallSprite = Resources.Load<Sprite>("Stage/wall");
            return _wallSprite;
        }

        static Sprite PlatformSprite()
        {
            if (_platformSprite == null) _platformSprite = Resources.Load<Sprite>("Stage/platform");
            return _platformSprite;
        }

        // 横足場の本体を生成（テクスチャがあれば台画像、無ければ単色バー）。
        // 当たり判定は (w, hCol)。テクスチャは横幅に合わせてアスペクト維持で乗せ、
        // 立てる面（不透明上端）が当たり判定の上面に合うよう配置する。
        GameObject BuildHorizontalPlatform(string name, Vector3 pos, float w, float hCol, Color fallbackCol, bool kinematic)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = kinematic ? RigidbodyType2D.Kinematic : RigidbodyType2D.Static;
            if (kinematic) rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var col = go.AddComponent<BoxCollider2D>();

            // 台と同じワンウェイ仕様（上には乗れる／下からはすり抜ける）。
            col.usedByEffector = true;
            var eff = go.AddComponent<PlatformEffector2D>();
            eff.useOneWay         = true;
            eff.useOneWayGrouping = false;
            eff.surfaceArc        = 170f;
            eff.rotationalOffset  = 0f;

            var tex = PlatformSprite();
            if (tex != null)
            {
                col.size = new Vector2(w, hCol);
                var vis = new GameObject("TexVisual");
                vis.transform.SetParent(go.transform, false);
                float scale = (w * 1.12f) / Mathf.Max(0.01f, tex.bounds.size.x); // アスペクト維持
                vis.transform.localScale = new Vector3(scale, scale, 1f);
                float opaqueTop = PlatformOpaqueTopPixels / tex.pixelsPerUnit * scale;
                vis.transform.localPosition = new Vector3(0f, hCol * 0.5f - opaqueTop, 0f);
                var sr = vis.AddComponent<SpriteRenderer>();
                sr.sprite       = tex;
                sr.color        = Color.white;
                sr.sortingOrder = 6;
            }
            else
            {
                col.size = Vector2.one;
                go.transform.localScale = new Vector3(w, hCol, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
                sr.color  = fallbackCol;
            }
            return go;
        }

        static void ApplyRainBlockVisual(GameObject go)
        {
            var sprite = RainBlockSprite();
            if (sprite == null)
            {
                var fallback = go.AddComponent<SpriteRenderer>();
                fallback.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
                fallback.color = new Color(Random.Range(0.7f, 1f), Random.Range(0.2f, 0.8f), Random.Range(0.1f, 0.5f), 0.9f);
                fallback.sortingOrder = 8;
                return;
            }

            var visual = new GameObject("AngelRainVisual");
            visual.transform.SetParent(go.transform, false);
            float fitX = 1f / Mathf.Max(sprite.bounds.size.x, 0.01f);
            float fitY = 1f / Mathf.Max(sprite.bounds.size.y, 0.01f);
            visual.transform.localScale = new Vector3(fitX, fitY, 1f);

            var sr = visual.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = Color.white;
            sr.sortingOrder = 8;
        }

        void SpawnPlatform(float widthScale, float duration, string posHint, Fighter p1, Fighter p2)
        {
            float w = Mathf.Clamp(widthScale * 1.8f, 1f, 9f);
            float h = 0.4f;
            var go = BuildHorizontalPlatform("AngelPlatform",
                ObstaclePos(posHint, p1, p2, Random.Range(1.5f, 3.5f)),
                w, h, new Color(1f, 0.85f, 0.1f, 0.93f), kinematic: false);
            RegisterObstacle(go);
        }

        void SpawnWall(float heightScale, float duration, string posHint, Fighter p1, Fighter p2)
        {
            var bm = BattleManager.Instance;
            float minX = bm?.StageMinX ?? -5f;
            float maxX = bm?.StageMaxX ??  5f;
            float groundY = bm != null ? bm.StageGroundY : -1.8f;

            // 高さ・厚みをランダムに振って形に変化を出す。
            float h    = Mathf.Clamp(heightScale * Random.Range(1.2f, 2.4f), 1f, 7f);
            float wCol = Random.Range(0.5f, 1.1f);

            // X位置：プレイヤー指定があればその付近、無ければステージ全幅からランダム（中央寄りの偏りを解消）。
            float x;
            if      (posHint == "player1" && p1 != null) x = p1.transform.position.x + Random.Range(-1.2f, 1.2f);
            else if (posHint == "player2" && p2 != null) x = p2.transform.position.x + Random.Range(-1.2f, 1.2f);
            else                                         x = Random.Range(minX + 0.8f, maxX - 0.8f);
            x = Mathf.Clamp(x, minX + 0.6f, maxX - 0.6f);

            // Y位置：基本は接地、たまに浮遊する壁にする。
            float bottomY = groundY;
            if (Random.value < 0.30f) bottomY += Random.Range(0.8f, 2.5f);
            Vector3 pos = new Vector3(x, bottomY + h * 0.5f, 0f);

            var go = new GameObject("AngelWall");
            go.transform.position = pos;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Static;
            var col = go.AddComponent<BoxCollider2D>();

            var sprite = WallSprite();
            if (sprite != null)
            {
                col.size = new Vector2(wCol, h);
                // 見た目はテクスチャの縦横比を保った幅で描く（当たり判定の細さに潰されないように）。
                float aspect = sprite.bounds.size.x / Mathf.Max(0.01f, sprite.bounds.size.y);
                float wVis   = Mathf.Clamp(h * aspect, 1.0f, 2.4f);
                var vis = new GameObject("TexVisual");
                vis.transform.SetParent(go.transform, false);
                vis.transform.localScale = new Vector3(
                    wVis / sprite.bounds.size.x, h / sprite.bounds.size.y, 1f);
                var sr = vis.AddComponent<SpriteRenderer>();
                sr.sprite       = sprite;
                sr.color        = Color.white;
                sr.sortingOrder = 7;
            }
            else
            {
                // フォールバック：従来の青い単色バー
                col.size = Vector2.one;
                go.transform.localScale = new Vector3(wCol, h, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
                sr.color  = new Color(0.4f, 0.65f, 1f, 0.93f);
            }

            // たまに少し傾けて形に変化を出す。
            if (Random.value < 0.25f)
                go.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(-14f, 14f));

            // 壁は回避（空中回避・横回避）中のファイターをすり抜けさせる。
            go.AddComponent<AngelWallPassable>();
            // 攻撃で破壊できる（耐久70）。
            go.AddComponent<DestructibleObstacle>().Init(70f);
            RegisterObstacle(go);
        }

        void SpawnBouncePad(float duration, string posHint, Fighter p1, Fighter p2)
        {
            var go = MakeStaticObstacle("AngelBounce",
                ObstaclePos(posHint, p1, p2, 0.25f),
                new Vector3(2f, 0.3f, 1f),
                new Color(0.15f, 1f, 0.45f, 0.95f));
            go.AddComponent<AngelBouncePad>();
            RegisterObstacle(go);
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
                go.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(-12f, 12f));
                go.transform.localScale = new Vector3(sz, sz, 1f);
                var rb = go.AddComponent<Rigidbody2D>();
                rb.gravityScale = Random.Range(1.8f, 3.5f);
                go.AddComponent<BoxCollider2D>().size = Vector2.one;
                ApplyRainBlockVisual(go);
                RegisterObstacle(go);
            }
        }

        void SpawnTiltedPlatform(float widthScale, float duration, string posHint, Fighter p1, Fighter p2)
        {
            float w = Mathf.Clamp(widthScale * 1.8f, 1f, 7f);
            float angle = Random.Range(15f, 40f) * (Random.value > 0.5f ? 1f : -1f);
            var go = BuildHorizontalPlatform("AngelTilt",
                ObstaclePos(posHint, p1, p2, Random.Range(1.5f, 3f)),
                w, 0.4f, new Color(1f, 0.5f, 0.15f, 0.93f), kinematic: false);
            go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            RegisterObstacle(go);
        }

        void SpawnMovingPlatform(float widthScale, float duration)
        {
            var bm   = BattleManager.Instance;
            float minX = bm?.StageMinX ?? -5f;
            float maxX = bm?.StageMaxX ??  5f;
            float w  = Mathf.Clamp(widthScale * 1.8f, 1f, 7f);
            var go = BuildHorizontalPlatform("AngelMovingPlatform",
                new Vector3(0f, Random.Range(1.5f, 3.5f), 0f),
                w, 0.35f, new Color(0.3f, 1f, 0.8f, 0.93f), kinematic: true);
            // 移動ペースは固定の基準秒で決める（BO3終了まで残り続けても一定速度で往復）。
            go.AddComponent<AngelMovingPlatform>().Init(minX + 1f, maxX - 1f, 8f);
            RegisterObstacle(go);
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

    // 壁の障害物：回避（空中回避・横回避）中のファイターとの当たり判定を無効化してすり抜けさせる。
    public class AngelWallPassable : MonoBehaviour
    {
        Collider2D _wallCol;

        void Awake() { _wallCol = GetComponent<Collider2D>(); }

        void FixedUpdate()
        {
            if (_wallCol == null) return;
            var fighters = BattleManager.Instance?.Fighters;
            if (fighters == null) return;
            for (int i = 0; i < fighters.Count; i++)
            {
                var f = fighters[i];
                if (f == null) continue;
                var fc = f.GetComponent<Collider2D>();
                if (fc == null) continue;
                // 回避中はすり抜け（衝突無視）、通常時は衝突を戻す。
                Physics2D.IgnoreCollision(_wallCol, fc, f.IsDodging);
            }
        }
    }

    // 攻撃で破壊できる障害物（壁など）。Hitbox/Projectile から TakeHit(dmg, attacker) が呼ばれる中立物。
    public class DestructibleObstacle : MonoBehaviour
    {
        float _hp = 70f;
        float _maxHp = 70f;
        SpriteRenderer _sr;
        Color _baseColor = Color.white;

        public void Init(float hp)
        {
            _hp = _maxHp = Mathf.Max(1f, hp);
            _sr = GetComponentInChildren<SpriteRenderer>();
            if (_sr != null) _baseColor = _sr.color;
        }

        public void TakeHit(float dmg, Fighter attacker)
        {
            if (dmg <= 0f) return;
            _hp -= dmg;

            PromptFighters.UI.DamagePopup.SpawnText(transform.position + Vector3.up * 0.5f,
                Mathf.RoundToInt(dmg).ToString(), new Color(0.7f, 0.85f, 1f), 0.9f);
            CameraShake.Shake(0.05f, 0.08f);

            // 残り耐久に応じて暗く＆赤みを増し、壊れそうな見た目に。
            if (_sr != null)
            {
                float r = Mathf.Clamp01(_hp / _maxHp);
                _sr.color = new Color(_baseColor.r, _baseColor.g * (0.4f + 0.6f * r),
                    _baseColor.b * (0.4f + 0.6f * r), _baseColor.a);
            }

            if (_hp <= 0f)
            {
                PromptFighters.UI.DamagePopup.SpawnText(transform.position + Vector3.up * 0.7f,
                    "BREAK!", new Color(1f, 0.7f, 0.3f), 1.4f);
                CameraShake.Shake(0.15f, 0.15f);
                GameAudioManager.Instance?.PlayGimmickDebuff();
                Destroy(gameObject);
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
