using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PromptFighters.AI;
using PromptFighters.Audio;
using PromptFighters.Battle.Skills;

namespace PromptFighters.Battle
{
    // ボイスボールのギミックを Fighter に適用する。
    public class AngelGimmickApplier : MonoBehaviour
    {
        // ステータス系のバフ・デバフ（速度/ジャンプ/与ダメ/重力/サイズ）は Fighter 側で永続適用
        // （ApplyPermanentX、ラウンドをまたいで保持・後勝ち上書き）。
        // 効果時間系（無敵・反射・カウンター・状態異常・障害物など）の継続時間はこの倍率で一括延長する。
        const float DurationScale = 2f;
        const int MaxSpawnedGimmickObjects = 24;
        const float SpatialWarningSeconds = 0.85f;

        // 発動者（音声アイテム取得者）。hp_set など「発動者自身に跳ね返る」ギミックで使う。
        // AngelController が Apply 直前にセットする。
        public Fighter Acquirer;

        static Sprite _rainBlockSprite;
        static bool _rainBlockTried;
        static Sprite _wallSprite;
        static Sprite _platformSprite;
        static Sprite _bouncePadSprite;
        static bool _bouncePadTried;
        // 台画像の不透明上端のピボットからのピクセル（StagePlatformSpawnerと同じ基準）。
        const float PlatformOpaqueTopPixels = 176f;

        // 生成した地形・障害物は時間で消さず、BO3（マッチ）が終わるまで残す。
        // AngelController が試合開始/終了時に ClearObstacles() でまとめて破棄する。
        readonly System.Collections.Generic.List<GameObject> _persistentObstacles =
            new System.Collections.Generic.List<GameObject>();

        void RegisterObstacle(GameObject go)
        {
            if (go == null) return;
            _persistentObstacles.RemoveAll(item => item == null);
            while (_persistentObstacles.Count >= MaxSpawnedGimmickObjects)
            {
                var oldest = _persistentObstacles[0];
                _persistentObstacles.RemoveAt(0);
                if (oldest != null) Destroy(oldest);
            }
            _persistentObstacles.Add(go);
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
            else ApplySingle(data.gimmick, data.target, data.value, data.duration,
                data.has_origin, data.origin, data.direction, data.shape, data.radius, data.count, p1, p2);

            if (!string.IsNullOrEmpty(data.gimmick2))
            {
                if (data.gimmick2 == "hp_swap") SwapHP(p1, p2);
                else ApplySingle(data.gimmick2, data.target2, data.value2, data.duration2,
                    data.has_origin2, data.origin2, data.direction2, data.shape2, data.radius2, data.count2, p1, p2);
            }
            if (!string.IsNullOrEmpty(data.gimmick3))
            {
                if (data.gimmick3 == "hp_swap") SwapHP(p1, p2);
                else ApplySingle(data.gimmick3, data.target3, data.value3, data.duration3,
                    data.has_origin3, data.origin3, data.direction3, data.shape3, data.radius3, data.count3, p1, p2);
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

        // 旧形式を残し、既存呼び出しは空間指定なしとして扱う。
        void ApplySingle(string gimmick, string targetKey, float value, float duration, Fighter p1, Fighter p2)
            => ApplySingle(gimmick, targetKey, value, duration,
                false, Vector2.zero, Vector2.zero, null, 0f, 0, p1, p2);

        void ApplySingle(string gimmick, string targetKey, float value, float duration,
            bool hasOrigin, Vector2 origin, Vector2 direction, string shape, float radius, int count,
            Fighter p1, Fighter p2)
        {
            // 非ゼロoriginを出した旧/不完全JSONはboolがなくても尊重する。has_originは
            // 値がゼロの「舞台中央」を省略と区別するためにだけ必須。
            hasOrigin = hasOrigin || origin.sqrMagnitude > 0.0001f;
            Fighter target1 = ResolveTarget(targetKey, p1, p2, primary: true);
            Fighter target2 = targetKey == "both"
                ? ResolveTarget(targetKey, p1, p2, primary: false)
                : null;
            // random はこの効果の開始時に一度だけ抽選し、効果対象・省略origin・地形位置で
            // 同じプレイヤーを使う。各ヘルパーで再抽選すると、対象と表示位置が食い違う。
            if (targetKey == "random")
                targetKey = target1 == p2 ? "player2" : "player1";

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
                    SpawnDirectionalRain(count > 0 ? count : Mathf.Max((int)value, 2), duration,
                        hasOrigin, origin, direction.sqrMagnitude > 0.01f ? direction : Vector2.down,
                        radius, targetKey, p1, p2);
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "obstacle_rain_directional":
                case "directional_obstacle_rain":
                    SpawnDirectionalRain(count > 0 ? count : Mathf.Max((int)value, 2), duration,
                        hasOrigin, origin, direction, radius, targetKey, p1, p2);
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "obstacle_tilt":
                    SpawnTiltedPlatform(Mathf.Max(value, 1f), Mathf.Max(duration, 5f) * DurationScale, targetKey, p1, p2);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "platform_line":
                case "obstacle_platform_line":
                    SpawnPlatformSequence(value, hasOrigin, origin, direction, count, false,
                        targetKey, p1, p2);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "platform_stairs":
                case "obstacle_platform_stairs":
                    SpawnPlatformSequence(value, hasOrigin, origin, direction, count, true,
                        targetKey, p1, p2);
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
                    Vector2 legacyLaunch = direction.sqrMagnitude > 0.01f
                        ? SafeDirection(direction, Vector2.up) * lv * 8f
                        : new Vector2(Random.Range(-1f, 1f) * lv * 4f, lv * 7f);
                    target1?.ApplyImpulse(legacyLaunch, 0.3f);
                    target2?.ApplyImpulse(legacyLaunch, 0.3f);
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "launch_vector":
                    float vectorPower = Mathf.Clamp(Mathf.Abs(value) > 0.01f ? Mathf.Abs(value) : 2.5f, 0.5f, 5f);
                    Vector2 launchVector = SafeDirection(direction, Vector2.up) * vectorPower * 8f;
                    target1?.ApplyImpulse(launchVector, 0.3f);
                    target2?.ApplyImpulse(launchVector, 0.3f);
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
                    // 一発逆転ギミック：成功率10%でのみ発動し、対象HPを大きく削る。
                    // 失敗時(90%)は発動者(取得者)自身にランダムなデバフが跳ね返る。
                    if (Random.value < 0.10f)
                    {
                        float setRatio = Mathf.Clamp01(value > 0f ? value : 0.15f);
                        if (target1 != null) target1.DebugSetCurrentHP(target1.maxHP * setRatio);
                        if (target2 != null) target2.DebugSetCurrentHP(target2.maxHP * setRatio);
                        GameAudioManager.Instance?.PlayGimmickHeal();
                        var hit = target1 ?? target2;
                        if (hit != null)
                            PromptFighters.UI.DamagePopup.SpawnText(
                                hit.transform.position + Vector3.up * 1.2f, "大成功！",
                                new Color(1f, 0.85f, 0.15f), 1.8f);
                    }
                    else
                    {
                        Fighter caster = Acquirer ?? target1;
                        string debuffName = ApplyRandomSelfDebuff(caster);
                        GameAudioManager.Instance?.PlayGimmickDebuff();
                        if (caster != null)
                            PromptFighters.UI.DamagePopup.SpawnText(
                                caster.transform.position + Vector3.up * 1.2f,
                                $"失敗…自分に {debuffName}",
                                new Color(1f, 0.4f, 0.3f), 1.6f);
                    }
                    break;
                case "defense_up":
                    // 防御力ギミックは固定倍率（0.7=被ダメ0.7倍）。願いの value は使わない。
                    target1?.ApplyPermanentDefense(0.7f);
                    target2?.ApplyPermanentDefense(0.7f);
                    GameAudioManager.Instance?.PlayGimmickBuff();
                    break;
                case "defense_down":
                    // 防御力ギミックは固定倍率（1.3=被ダメ1.3倍）。願いの value は使わない。
                    target1?.ApplyPermanentDefense(1.3f);
                    target2?.ApplyPermanentDefense(1.3f);
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "ally_revive":
                    // ボス戦（協力モード）専用：ダウン中の味方を復活させる。
                    ReviveDownedAlly();
                    GameAudioManager.Instance?.PlayGimmickHeal();
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
                    if (HasSpatialRequest(hasOrigin, origin, direction, shape, radius, count))
                    {
                        SpawnSpatialZone(AngelSpatialEffect.DirectionalWind, targetKey, value, duration,
                            hasOrigin, origin, direction, shape, radius, p1, p2);
                    }
                    else
                    {
                        float windF = value != 0f ? Mathf.Clamp(value * 5f, -15f, 15f) : 4f;
                        target1?.StartTemporaryWind(windF, Mathf.Max(duration, 4f) * DurationScale);
                        target2?.StartTemporaryWind(windF, Mathf.Max(duration, 4f) * DurationScale);
                    }
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "wind_directional":
                case "directional_wind":
                    SpawnSpatialZone(AngelSpatialEffect.DirectionalWind, targetKey, value, duration,
                        hasOrigin, origin, direction, shape, radius, p1, p2);
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "wind_radial":
                case "radial_wind":
                    SpawnSpatialZone(AngelSpatialEffect.RadialWind, targetKey, value, duration,
                        hasOrigin, origin, direction, "circle", radius, p1, p2);
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "gravity_zone":
                case "local_gravity":
                    SpawnSpatialZone(AngelSpatialEffect.Gravity, targetKey, value, duration,
                        hasOrigin, origin, direction, shape, radius, p1, p2);
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "floor_lava":
                    // 全床を覆う旧効果も、範囲を可視化して発動前に必ず予告する。
                    SpawnFullFloorLava(value, duration, p1, p2);
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "lava_strip":
                    SpawnSpatialZone(AngelSpatialEffect.Lava, targetKey, value, duration,
                        hasOrigin, origin, direction, "line", radius, p1, p2);
                    GameAudioManager.Instance?.PlayGimmickDebuff();
                    break;
                case "heal_zone":
                    SpawnSpatialZone(AngelSpatialEffect.Heal, targetKey, value, duration,
                        hasOrigin, origin, direction, shape, radius, p1, p2);
                    GameAudioManager.Instance?.PlayGimmickHeal();
                    break;
                case "damage_zone":
                    SpawnSpatialZone(AngelSpatialEffect.Damage, targetKey, value, duration,
                        hasOrigin, origin, direction, shape, radius, p1, p2);
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

        // hp_set 失敗時のペナルティ：発動者にランダムなデバフを与え、デバフ名を返す。
        string ApplyRandomSelfDebuff(Fighter f)
        {
            if (f == null || f.State == FighterState.Dead) return "デバフ";
            switch (Random.Range(0, 7))
            {
                case 0: f.ApplyPermanentSpeed(0.65f);                       return "スピード DOWN ↓";
                case 1: f.ApplyPermanentJump(0.70f);                        return "ジャンプ DOWN ↓";
                case 2: f.ApplyPermanentDamage(0.65f);                      return "パワー DOWN ↓";
                case 3: f.ApplyPermanentGravity(1.8f);                      return "重力増加 ↓↓";
                case 4: f.ApplyPermanentSize(0.70f);                        return "縮小化";
                case 5: f.ApplyStatus(StatusType.Slow, 6f * DurationScale); return "スロー状態";
                case 6: f.ApplyStatus(StatusType.Burn, 5f * DurationScale); return "バーン状態";
                default:                                                     return "デバフ";
            }
        }

        // ボス戦（協力モード）でダウン中の味方プレイヤーを1体復活させる。
        // 通常対戦モードやダウン中の味方がいない場合は何もしない。
        void ReviveDownedAlly()
        {
            var bm = BattleManager.Instance;
            if (bm == null || bm.Mode != BattleMode.CoopVsBoss) return;
            var fighters = bm.Fighters;
            if (fighters == null) return;
            for (int i = 0; i < fighters.Count; i++)
            {
                var f = fighters[i];
                if (f != null && f.Team == FighterTeam.Players && f.IsDowned)
                {
                    f.Revive(0.5f);
                    PromptFighters.UI.DamagePopup.SpawnText(
                        f.transform.position + Vector3.up, "復活!", new Color(0.4f, 1f, 0.6f), 1.8f);
                    return;
                }
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

        static bool HasSpatialRequest(bool hasOrigin, Vector2 origin, Vector2 direction, string shape, float radius, int count)
            => hasOrigin || origin.sqrMagnitude > 0.0001f || direction.sqrMagnitude > 0.0001f ||
               !string.IsNullOrEmpty(shape) || radius > 0f || count > 0;

        static Vector2 SafeDirection(Vector2 direction, Vector2 fallback)
        {
            if (direction.sqrMagnitude < 0.0001f) direction = fallback;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector2.right;
            return direction.normalized;
        }

        Vector2 ResolveSpatialOrigin(Vector2 normalized, bool hasOrigin, string targetKey,
            Fighter p1, Fighter p2, float defaultY)
        {
            var bm = BattleManager.Instance;
            float minX = bm?.StageMinX ?? -5f;
            float maxX = bm?.StageMaxX ??  5f;
            float groundY = bm?.StageGroundY ?? -1.8f;

            // JSONでoriginが省略された場合はVector2.zero。個別targetならその現在位置、
            // both等なら舞台中央を既定位置にする。
            if (!hasOrigin)
            {
                Fighter target = ResolveTarget(targetKey, p1, p2, primary: true);
                if ((targetKey == "player1" || targetKey == "player2" ||
                     targetKey == "weaker" || targetKey == "stronger") && target != null)
                {
                    return new Vector2(
                        Mathf.Clamp(target.transform.position.x, minX + 0.5f, maxX - 0.5f),
                        Mathf.Clamp(target.transform.position.y + 0.6f, groundY + 0.3f, groundY + 6f));
                }
                return new Vector2((minX + maxX) * 0.5f, defaultY);
            }

            float nx = Mathf.Clamp(normalized.x, -1f, 1f);
            float ny = Mathf.Clamp(normalized.y, -1f, 1f);
            return new Vector2(
                Mathf.Lerp(minX + 0.5f, maxX - 0.5f, (nx + 1f) * 0.5f),
                Mathf.Lerp(groundY + 0.3f, groundY + 6f, (ny + 1f) * 0.5f));
        }

        static Vector2 ClampSpatialCenter(Vector2 center, Vector2 size, float rotationDegrees = 0f)
        {
            var bm = BattleManager.Instance;
            float minX = bm?.StageMinX ?? -5f;
            float maxX = bm?.StageMaxX ??  5f;
            float groundY = bm?.StageGroundY ?? -1.8f;
            float radians = rotationDegrees * Mathf.Deg2Rad;
            float c = Mathf.Abs(Mathf.Cos(radians));
            float s = Mathf.Abs(Mathf.Sin(radians));
            float halfX = Mathf.Max(0.01f, size.x) * 0.5f;
            float halfY = Mathf.Max(0.01f, size.y) * 0.5f;
            float extentX = c * halfX + s * halfY;
            float extentY = s * halfX + c * halfY;
            float lowX = minX + extentX;
            float highX = maxX - extentX;
            float lowY = groundY + extentY;
            float highY = groundY + 8f - extentY;
            center.x = lowX <= highX ? Mathf.Clamp(center.x, lowX, highX) : (minX + maxX) * 0.5f;
            center.y = lowY <= highY ? Mathf.Clamp(center.y, lowY, highY) : groundY + 4f;
            return center;
        }

        Fighter[] ResolveZoneTargets(string targetKey, Fighter p1, Fighter p2, bool allFighters = false)
        {
            var result = new List<Fighter>();
            if (allFighters)
            {
                var fighters = BattleManager.Instance?.Fighters;
                if (fighters != null)
                {
                    for (int i = 0; i < fighters.Count; i++)
                        if (fighters[i] != null && !result.Contains(fighters[i])) result.Add(fighters[i]);
                }
            }
            else
            {
                Fighter first = ResolveTarget(targetKey, p1, p2, primary: true);
                if (first != null) result.Add(first);
                if (targetKey == "both")
                {
                    Fighter second = ResolveTarget(targetKey, p1, p2, primary: false);
                    if (second != null && !result.Contains(second)) result.Add(second);
                }
            }

            if (result.Count == 0)
            {
                if (p1 != null) result.Add(p1);
                if (p2 != null && p2 != p1) result.Add(p2);
            }
            return result.ToArray();
        }

        void SpawnSpatialZone(AngelSpatialEffect effect, string targetKey, float value, float duration,
            bool hasOrigin, Vector2 origin, Vector2 direction, string shape, float radius, Fighter p1, Fighter p2)
        {
            var bm = BattleManager.Instance;
            float groundY = bm?.StageGroundY ?? -1.8f;
            float r = Mathf.Clamp(radius > 0f ? radius : 2.2f, 0.75f, 4f);
            float zoneDuration = Mathf.Clamp(duration > 0f ? duration : 6f, 2f, 12f);
            Vector2 dir = SafeDirection(direction,
                effect == AngelSpatialEffect.Gravity ? Vector2.down : Vector2.right);
            bool circle = shape == "circle" || effect == AngelSpatialEffect.RadialWind ||
                          (string.IsNullOrEmpty(shape) && effect != AngelSpatialEffect.Lava);
            Vector2 size;
            if (effect == AngelSpatialEffect.Lava)
                size = new Vector2(r * 2f, 0.65f);
            else if (shape == "line")
                size = new Vector2(r * 2f, Mathf.Max(0.65f, r * 0.45f));
            else if (shape == "box")
                size = new Vector2(r * 2f, Mathf.Max(1f, r * 1.35f));
            else
                size = Vector2.one * (r * 2f);

            Vector2 center = ResolveSpatialOrigin(origin, hasOrigin, targetKey, p1, p2,
                effect == AngelSpatialEffect.Lava ? groundY + 0.3f : groundY + 1.8f);
            if (effect == AngelSpatialEffect.Lava) center.y = groundY + size.y * 0.5f;
            bool rotatesWithDirection = !circle && effect != AngelSpatialEffect.Lava && size.x > size.y;
            float zoneAngle = rotatesWithDirection
                ? Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg
                : 0f;
            center = ClampSpatialCenter(center, size, zoneAngle);

            float strength;
            switch (effect)
            {
                case AngelSpatialEffect.DirectionalWind:
                    if (value < 0f) dir = -dir;
                    strength = Mathf.Clamp(Mathf.Abs(value) > 0.01f ? Mathf.Abs(value) * 5f : 5f, 1f, 15f);
                    break;
                case AngelSpatialEffect.RadialWind:
                    float radialSign = value < 0f ? -1f : 1f;
                    strength = radialSign * Mathf.Clamp(Mathf.Abs(value) > 0.01f ? Mathf.Abs(value) * 5f : 5f, 1f, 15f);
                    break;
                case AngelSpatialEffect.Gravity:
                    if (value < 0f) dir = -dir;
                    strength = Mathf.Clamp(Mathf.Abs(value) > 0.01f ? Mathf.Abs(value) : 7f, 2f, 18f);
                    break;
                case AngelSpatialEffect.Heal:
                    strength = Mathf.Clamp(Mathf.Abs(value) > 0.001f ? Mathf.Abs(value) : 0.04f, 0.01f, 0.10f);
                    break;
                case AngelSpatialEffect.Damage:
                    strength = Mathf.Clamp(Mathf.Abs(value) > 0.001f ? Mathf.Abs(value) : 0.035f, 0.01f, 0.08f);
                    break;
                default: // Lava
                    strength = Mathf.Clamp(Mathf.Abs(value) > 0.001f ? Mathf.Abs(value) * 0.5f : 0.04f, 0.015f, 0.10f);
                    break;
            }

            CreateSpatialZone(effect, center, size, circle, dir, strength, zoneDuration,
                ResolveZoneTargets(targetKey, p1, p2));
        }

        void SpawnFullFloorLava(float value, float duration, Fighter p1, Fighter p2)
        {
            var bm = BattleManager.Instance;
            float minX = bm?.StageMinX ?? -5f;
            float maxX = bm?.StageMaxX ??  5f;
            float groundY = bm?.StageGroundY ?? -1.8f;
            Vector2 size = new Vector2(Mathf.Max(1f, maxX - minX - 0.2f), 0.65f);
            Vector2 center = new Vector2((minX + maxX) * 0.5f, groundY + size.y * 0.5f);
            float strength = Mathf.Clamp(value > 0f ? value * 0.5f : 0.04f, 0.015f, 0.10f);
            float zoneDuration = Mathf.Clamp(Mathf.Max(duration, 5f) * DurationScale, 5f, 15f);
            CreateSpatialZone(AngelSpatialEffect.Lava, center, size, false, Vector2.right,
                strength, zoneDuration, ResolveZoneTargets("both", p1, p2, allFighters: true));
        }

        void CreateSpatialZone(AngelSpatialEffect effect, Vector2 center, Vector2 size, bool circle,
            Vector2 direction, float strength, float duration, Fighter[] targets)
        {
            var go = new GameObject("AngelSpatial_" + effect);
            go.transform.position = center;
            var zone = go.AddComponent<AngelSpatialZone>();
            zone.Init(effect, size, circle, direction, strength, duration, SpatialWarningSeconds, targets);
            RegisterObstacle(go);
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

        static Sprite BouncePadSprite()
        {
            if (!_bouncePadTried) { _bouncePadSprite = Resources.Load<Sprite>("Stage/bounce_pad"); _bouncePadTried = true; }
            return _bouncePadSprite;
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

        // 触れると上に跳ねる設置物。物理的な壁ではないため、当たり判定はトリガー専用にする
        // （プレイヤーはすり抜けて重なることができ、重なった瞬間だけ上方向へ弾かれる）。
        // 出現位置は対象キャラの足場（今いる地面/台の上面）に合わせる。時間経過による自動消滅はしない
        // （他の設置物と同様、_persistentObstaclesに登録しBO3終了までClearObstacles()で残す）。
        void SpawnBouncePad(float duration, string posHint, Fighter p1, Fighter p2)
        {
            var bm = BattleManager.Instance;
            float minX = bm?.StageMinX ?? -5f;
            float maxX = bm?.StageMaxX ??  5f;

            float x;
            Vector2 rayOrigin;
            if (posHint == "player1" && p1 != null)
            {
                x = Mathf.Clamp(p1.transform.position.x + Random.Range(-0.5f, 0.5f), minX + 1.2f, maxX - 1.2f);
                rayOrigin = new Vector2(x, p1.transform.position.y + 0.1f);
            }
            else if (posHint == "player2" && p2 != null)
            {
                x = Mathf.Clamp(p2.transform.position.x + Random.Range(-0.5f, 0.5f), minX + 1.2f, maxX - 1.2f);
                rayOrigin = new Vector2(x, p2.transform.position.y + 0.1f);
            }
            else
            {
                x = Mathf.Clamp((minX + maxX) * 0.5f + Random.Range(-2.5f, 2.5f), minX + 1.2f, maxX - 1.2f);
                rayOrigin = new Vector2(x, 20f);
            }
            float groundY = ResolveGroundYBelow(rayOrigin, p1, p2);

            var go = new GameObject("AngelBounce");

            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Static;
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;

            var sprite = BouncePadSprite();
            float hVis;
            if (sprite != null)
            {
                float aspect = sprite.bounds.size.x / Mathf.Max(0.01f, sprite.bounds.size.y);
                float wVis   = 2f;
                hVis         = wVis / aspect;
                col.size = new Vector2(wVis, hVis);
                go.transform.localScale = new Vector3(wVis / sprite.bounds.size.x, hVis / sprite.bounds.size.y, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite       = sprite;
                sr.color        = Color.white;
                sr.sortingOrder = 7;
            }
            else
            {
                // フォールバック：従来の緑単色バー
                hVis = 0.3f;
                col.size = Vector2.one;
                go.transform.localScale = new Vector3(2f, hVis, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
                sr.color  = new Color(0.15f, 1f, 0.45f, 0.95f);
            }

            // スプライトの中心ピボット基準なので、足場の上面 + 半分の高さをセンターY位置にする。
            go.transform.position = new Vector3(x, groundY + hVis * 0.5f, 0f);

            go.AddComponent<AngelBouncePad>();
            RegisterObstacle(go);
        }

        // 指定位置から真下にある地面/台の上面Yを検出する（BlobShadowと同じ手法）。
        // 対象キャラ自身の当たり判定は地面として扱わないよう除外する。見つからなければステージ基本地面高さにフォールバック。
        static float ResolveGroundYBelow(Vector2 origin, Fighter excludeA, Fighter excludeB)
        {
            var filter = new ContactFilter2D();
            filter.useTriggers  = false;
            filter.useLayerMask = false;
            var hits = new RaycastHit2D[8];
            int count = Physics2D.Raycast(origin, Vector2.down, filter, hits, 40f);

            var colA = excludeA != null ? excludeA.GetComponent<Collider2D>() : null;
            var colB = excludeB != null ? excludeB.GetComponent<Collider2D>() : null;
            float best = float.NegativeInfinity;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider == colA || hits[i].collider == colB) continue;
                if (hits[i].point.y > best) { best = hits[i].point.y; found = true; }
            }

            var bm = BattleManager.Instance;
            return found ? best : (bm != null ? bm.StageGroundY : -1.8f);
        }

        void SpawnDirectionalRain(int requestedCount, float duration, bool hasOrigin, Vector2 origin,
            Vector2 direction, float radius, string targetKey, Fighter p1, Fighter p2)
        {
            StartCoroutine(DirectionalRainRoutine(requestedCount, duration, hasOrigin, origin,
                direction, radius, targetKey, p1, p2));
        }

        IEnumerator DirectionalRainRoutine(int requestedCount, float duration, bool hasOrigin, Vector2 origin,
            Vector2 direction, float radius, string targetKey, Fighter p1, Fighter p2)
        {
            var bm = BattleManager.Instance;
            float minX = bm?.StageMinX ?? -5f;
            float maxX = bm?.StageMaxX ??  5f;
            float groundY = bm?.StageGroundY ?? -1.8f;
            int spawnCount = Mathf.Clamp(requestedCount > 0 ? requestedCount : 5, 2, 8);
            float lifetime = Mathf.Clamp(duration > 0f ? duration : 8f, 3f, 12f);
            Vector2 dir = SafeDirection(direction, Vector2.down);
            Vector2 perpendicular = new Vector2(-dir.y, dir.x);
            bool mostlyVertical = Mathf.Abs(dir.y) >= Mathf.Abs(dir.x);
            float spread = Mathf.Clamp(radius > 0f ? radius : (mostlyVertical ? (maxX - minX) * 0.4f : 2.6f),
                0.8f, mostlyVertical ? (maxX - minX) * 0.45f : 3.5f);
            Vector2 center = ResolveSpatialOrigin(origin, hasOrigin, targetKey, p1, p2, groundY + 3f);
            var starts = new List<Vector2>(spawnCount);
            var ends = new List<Vector2>(spawnCount);
            var sizes = new List<float>(spawnCount);

            for (int i = 0; i < spawnCount; i++)
            {
                float laneT = spawnCount <= 1 ? 0f : i / (float)(spawnCount - 1) - 0.5f;
                Vector2 lane = center + perpendicular * (laneT * spread * 2f);
                float blockSize = Random.Range(0.55f, 1.05f);
                // 回転した正方形のAABB半径ぶん内側を通し、移動中もCollider全体を舞台内に保つ。
                float inset = blockSize * 0.5f * (Mathf.Abs(dir.x) + Mathf.Abs(dir.y)) + 0.02f;
                float laneMinX = minX + inset;
                float laneMaxX = maxX - inset;
                float laneMinY = groundY + inset;
                float laneMaxY = groundY + 6.3f - inset;
                if (!TryClipLineToRect(lane, dir, laneMinX, laneMaxX, laneMinY, laneMaxY,
                        out Vector2 start, out Vector2 end))
                {
                    // 端の平行レーンが矩形を外れた場合だけ最近傍へ寄せ、指定方向は変えない。
                    lane = new Vector2(Mathf.Clamp(lane.x, laneMinX, laneMaxX),
                        Mathf.Clamp(lane.y, laneMinY, laneMaxY));
                    TryClipLineToRect(lane, dir, laneMinX, laneMaxX, laneMinY, laneMaxY,
                        out start, out end);
                }
                starts.Add(start);
                ends.Add(end);
                sizes.Add(blockSize);
                SpawnWarningLane(start, end, Mathf.Max(0.5f, blockSize), SpatialWarningSeconds);
            }

            yield return new WaitForSeconds(SpatialWarningSeconds);
            if (bm != null && !bm.IsFighting) yield break;

            for (int i = 0; i < starts.Count; i++)
            {
                Vector2 travelDir = SafeDirection(ends[i] - starts[i], dir);
                var go = new GameObject("AngelDirectionalRain");
                go.transform.position = starts[i];
                // 正方形の一辺を進路に揃え、予告レーン幅と実Colliderの占有幅を一致させる。
                go.transform.rotation = Quaternion.Euler(0f, 0f,
                    Mathf.Atan2(travelDir.y, travelDir.x) * Mathf.Rad2Deg);
                go.transform.localScale = Vector3.one * sizes[i];
                var rb = go.AddComponent<Rigidbody2D>();
                // 予告した直線から物理衝突で逸れないよう、進路を外力に左右されないKinematicにする。
                // DynamicなFighterとは接触するため、障害物としての押し返しは維持される。
                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.gravityScale = 0f;
                rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                rb.linearVelocity = travelDir * Random.Range(5.5f, 7.5f);
                go.AddComponent<BoxCollider2D>().size = Vector2.one;
                ApplyRainBlockVisual(go);
                go.AddComponent<AngelTimedDestroy>().Init(lifetime);
                RegisterObstacle(go);
            }
        }

        // 無限直線 point+t*direction を矩形で切り取り、directionへ進む入口→出口を返す。
        // 水平/垂直の特殊分岐を持たないため、斜め指定でも角度が変化しない。
        static bool TryClipLineToRect(Vector2 point, Vector2 direction,
            float minX, float maxX, float minY, float maxY, out Vector2 start, out Vector2 end)
        {
            start = point;
            end = point;
            float tMin = float.NegativeInfinity;
            float tMax = float.PositiveInfinity;

            bool ClipAxis(float p, float d, float low, float high)
            {
                if (Mathf.Abs(d) < 0.0001f) return p >= low && p <= high;
                float t1 = (low - p) / d;
                float t2 = (high - p) / d;
                if (t1 > t2) (t1, t2) = (t2, t1);
                tMin = Mathf.Max(tMin, t1);
                tMax = Mathf.Min(tMax, t2);
                return tMin <= tMax;
            }

            if (!ClipAxis(point.x, direction.x, minX, maxX) ||
                !ClipAxis(point.y, direction.y, minY, maxY)) return false;
            start = point + direction * tMin;
            end = point + direction * tMax;
            return true;
        }

        void SpawnWarningLane(Vector2 start, Vector2 end, float width, float duration)
        {
            Vector2 delta = end - start;
            var go = new GameObject("AngelHazardWarning");
            go.transform.position = (start + end) * 0.5f;
            go.transform.rotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            go.AddComponent<AngelTelegraph>().Init(
                new Vector2(Mathf.Max(delta.magnitude, 0.5f), width),
                new Color(1f, 0.22f, 0.08f, 0.42f), duration);
            // 予告は自己消滅する一時表示。永続障害物の24件枠へ入れると、8本の予告だけで
            // 既存足場をFIFO削除してしまうため登録しない。
        }

        void SpawnPlatformSequence(float widthScale, bool hasOrigin, Vector2 origin, Vector2 direction,
            int requestedCount, bool stairs, string targetKey, Fighter p1, Fighter p2)
        {
            var bm = BattleManager.Instance;
            float minX = bm?.StageMinX ?? -5f;
            float maxX = bm?.StageMaxX ??  5f;
            float groundY = bm?.StageGroundY ?? -1.8f;
            int platformCount = Mathf.Clamp(requestedCount > 0 ? requestedCount : 4, 2, 6);
            float width = Mathf.Clamp((widthScale > 0f ? widthScale : 1f) * 1.35f, 1f, 2.6f);
            Vector2 dir = SafeDirection(direction, Vector2.right);
            if (stairs)
            {
                float xSign = Mathf.Abs(dir.x) > 0.1f ? Mathf.Sign(dir.x) : 1f;
                float ySign = direction.y < -0.1f ? -1f : 1f;
                dir = new Vector2(xSign, 0.55f * ySign).normalized;
            }
            Vector2 center = ResolveSpatialOrigin(origin, hasOrigin, targetKey, p1, p2, groundY + 1.5f);
            float stepDistance = width + 0.4f;
            float minCenterX = minX + width * 0.5f;
            float maxCenterX = maxX - width * 0.5f;
            float minCenterY = groundY + 0.65f;
            float maxCenterY = groundY + 5.5f;

            // 端で各要素を個別clampすると複数足場が同じ座標へ潰れる。列全体が収まる個数へ
            // 先に減らし、次に中心だけを平行移動して間隔を維持する。
            if (Mathf.Abs(dir.x) > 0.001f)
            {
                int fitX = Mathf.FloorToInt((maxCenterX - minCenterX) /
                    (Mathf.Abs(dir.x) * stepDistance)) + 1;
                platformCount = Mathf.Min(platformCount, Mathf.Max(1, fitX));
            }
            if (Mathf.Abs(dir.y) > 0.001f)
            {
                int fitY = Mathf.FloorToInt((maxCenterY - minCenterY) /
                    (Mathf.Abs(dir.y) * stepDistance)) + 1;
                platformCount = Mathf.Min(platformCount, Mathf.Max(1, fitY));
            }

            float halfSpan = (platformCount - 1) * stepDistance * 0.5f;
            float lowX = center.x - Mathf.Abs(dir.x) * halfSpan;
            float highX = center.x + Mathf.Abs(dir.x) * halfSpan;
            if (lowX < minCenterX) center.x += minCenterX - lowX;
            else if (highX > maxCenterX) center.x -= highX - maxCenterX;
            float lowY = center.y - Mathf.Abs(dir.y) * halfSpan;
            float highY = center.y + Mathf.Abs(dir.y) * halfSpan;
            if (lowY < minCenterY) center.y += minCenterY - lowY;
            else if (highY > maxCenterY) center.y -= highY - maxCenterY;

            for (int i = 0; i < platformCount; i++)
            {
                float offset = (i - (platformCount - 1) * 0.5f) * stepDistance;
                Vector2 pos = center + dir * offset;
                pos.x = Mathf.Clamp(pos.x, minCenterX, maxCenterX);
                pos.y = Mathf.Clamp(pos.y, minCenterY, maxCenterY);
                var go = BuildHorizontalPlatform(stairs ? "AngelPlatformStair" : "AngelPlatformLine",
                    new Vector3(pos.x, pos.y, 0f), width, 0.35f,
                    stairs ? new Color(0.35f, 0.9f, 1f, 0.93f) : new Color(1f, 0.85f, 0.1f, 0.93f),
                    kinematic: false);
                RegisterObstacle(go);
            }
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
                    if (!primary) return null;
                    if (p1 == null) return p2;
                    if (p2 == null) return p1;
                    return Random.value > 0.5f ? p1 : p2;
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
        // ファイターごとの直近の無視状態。値が変化したときだけ Physics2D.IgnoreCollision を呼ぶ。
        readonly Dictionary<Fighter, bool> _ignoredState = new Dictionary<Fighter, bool>();

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
                // 毎フレーム同じ値でPhysics2D.IgnoreCollisionを呼び続けると、壁の上に乗っている
                // ファイターの接地コンタクトが物理的に不安定になり、壁の上でジャンプできない・
                // 壁際の挙動がおかしくなる不具合の原因になるため、状態が変化したときだけ呼ぶ。
                bool shouldIgnore = f.IsDodging;
                if (_ignoredState.TryGetValue(f, out bool prev) && prev == shouldIgnore) continue;
                _ignoredState[f] = shouldIgnore;
                Physics2D.IgnoreCollision(_wallCol, fc, shouldIgnore);
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
        void OnTriggerEnter2D(Collider2D other)
        {
            var f = other.GetComponent<Fighter>();
            if (f == null) return;
            f.ApplyImpulse(new Vector2(0f, 22f * 0.7f), 0.12f);
            PromptFighters.UI.DamagePopup.SpawnText(
                transform.position + Vector3.up * 0.5f,
                "BOUNCE!", new Color(0.15f, 1f, 0.45f), 1.5f);
        }
    }
}
