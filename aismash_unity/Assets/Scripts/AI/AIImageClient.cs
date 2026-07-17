using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using PromptFighters.Battle.Skills;
using PromptFighters.Utils;

namespace PromptFighters.AI
{
    // OpenAI Images API でキャラクタースプライトセットを生成する。
    // ベース画像(Idle1)を /v1/images/generations で生成後、残りのポーズ/エフェクトを
    // /v1/images/edits で並列生成する。
    // 複数キャラを同時生成しても全リクエストは共通レートリミッターを通り、瞬間的なバーストを防ぐ。
    public static class AIImageClient
    {
        const string GenerationsEndpoint = "https://api.openai.com/v1/images/generations";
        const string EditsEndpoint = "https://api.openai.com/v1/images/edits";
        const string Model = "gpt-image-2";
        // 主モデルが全滅した場合のフォールバック（キャラ生成を失敗で終わらせないための保険）
        const string FallbackModel = "gpt-image-1.5";
        const string CharacterSize = "1024x1536";
        const string EffectSize = "1536x1024";
        const string Quality = "low";
        // 正常応答後の解析・画像変換など、レート制限以外の処理失敗に対する再生成上限。
        const int    MaxImageAttempts = 3;
        // 429・5xx・タイムアウトなど一時障害は通常の再生成回数に含めず、待機して再送する。
        // 無限再送や料金暴走は避けるため、1画像・1モデルごとに上限を設ける。
        const int    MaxTransientRetries = 8;
        // gpt-image-2 Tier 5 の250 IPMに対して約20%の余裕を持たせ、最大約200 IPMで開始する。
        // 生成完了数ではなくAPIリクエストの開始時刻を全キャラ共通で制御する。
        const double ImageRequestIntervalSeconds = 0.30;

        static string _cachedApiKey;
        static double _nextImageRequestTime;

        static IEnumerator WaitForImageRequestSlot()
        {
            while (true)
            {
                double now = Time.realtimeSinceStartupAsDouble;
                if (now >= _nextImageRequestTime)
                {
                    _nextImageRequestTime = now + ImageRequestIntervalSeconds;
                    yield break;
                }

                yield return null;
            }
        }

        static bool IsTransientRequestFailure(UnityWebRequest req)
        {
            if (req == null) return true;

            long code = req.responseCode;
            if (code == 408 || code == 409 || code == 425 || code == 429 || code >= 500)
                return true;

            // responseCode=0 のタイムアウト・DNS・接続切断なども再送対象。
            return code == 0 &&
                   (req.result == UnityWebRequest.Result.ConnectionError ||
                    req.result == UnityWebRequest.Result.DataProcessingError);
        }

        static void ApplyTransientCooldown(UnityWebRequest req, int retryCount)
        {
            float delaySeconds = 0f;
            string retryAfter = req?.GetResponseHeader("Retry-After");
            bool hasRetryAfter = req?.responseCode == 429 &&
                float.TryParse(retryAfter, NumberStyles.Float, CultureInfo.InvariantCulture, out delaySeconds) &&
                delaySeconds > 0f;
            if (!hasRetryAfter)
            {
                // 指示が無い場合は指数バックオフ＋jitter。同時に失敗した要求の再衝突を避ける。
                delaySeconds = Mathf.Min(Mathf.Pow(2f, retryCount), 30f) +
                               UnityEngine.Random.Range(0.25f, 1.0f);
            }

            if (delaySeconds <= 0f)
                delaySeconds = 1f;

            double resumeAt = Time.realtimeSinceStartupAsDouble + delaySeconds;
            if (resumeAt > _nextImageRequestTime)
                _nextImageRequestTime = resumeAt;

            string reason = req?.responseCode == 429 ? "レート制限" : "一時的な通信/API障害";
            Debug.LogWarning($"[AIImage] {reason}を検出。画像リクエスト全体を{delaySeconds:F1}秒待機して再送します " +
                             $"({retryCount}/{MaxTransientRetries})");
        }

        public static string ApiKey
        {
            get
            {
                if (IsConfiguredApiKey(_cachedApiKey)) return _cachedApiKey;
                _cachedApiKey = LoadApiKey();
                return _cachedApiKey;
            }
            set => _cachedApiKey = value;
        }

        static string LoadApiKey()
        {
            string fromProcess = System.Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Trim();
            if (!string.IsNullOrEmpty(fromProcess)) return fromProcess;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            string userValue = System.Environment.GetEnvironmentVariable(
                "OPENAI_API_KEY", System.EnvironmentVariableTarget.User);
            if (!string.IsNullOrWhiteSpace(userValue)) return userValue.Trim();
#endif
            return "";
        }

        public static bool HasConfiguredApiKey(out string error)
        {
            error = null;
            if (IsConfiguredApiKey(ApiKey)) return true;
            error = "OpenAI APIキーが未設定です。環境変数 OPENAI_API_KEY に実キーを設定してください。";
            return false;
        }

        public static bool IsConfiguredApiKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            string trimmed = key.Trim();
            if (trimmed == "YOUR_API_KEY_HERE") return false;
            if (trimmed.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        // 全スプライト共通の制約サフィックス
        const string CharSuffix   = "facing right, single character only, one character, complete full body from head to toe not cropped, flat chroma key green background (#00FF00), no text, no watermark, no shadow, no duplicate. Anime-style character with sharp, bold lines. Highly saturated and energetic color palette.";
        const string EffectSuffix = "2D game visual effect only, no character figure, no text, flat chroma key green background (#00FF00), bright energetic colors, centered in frame";

        // 追加ポーズはIdle1を直接参照するeditとして生成する。外見説明を再解釈して別人化しないよう、
        // 同一性・全身・基準位置・一時的な技エフェクトを描かないことを強く固定する。
        const string IdentityLockedPoseSuffix =
            "STRICT REFERENCE-IMAGE EDIT. Preserve the exact same character identity from the supplied image: " +
            "identical face, eyes, skin tone, hairstyle silhouette and colors, body proportions, outfit layers, " +
            "patterns and colors, footwear, accessories, weapon design, and weapon hand. Change the pose only. " +
            "Do not redesign, add, remove, or replace any costume piece, accessory, limb, or weapon. " +
            "Keep the pelvis/root at the same horizontal canvas position and keep exactly the same apparent body scale. " +
            "For a grounded pose, keep the lowest planted foot on the same bottom baseline as the reference. " +
            "Show one complete full body from head to toe with every limb and the entire weapon visible, generous margins, " +
            "never cropped, never duplicated, facing right. Character pose only: no projectile, beam, summoned creature, " +
            "detached visual effect, aura, impact burst, motion trail, text, watermark, or shadow. " +
            "Flat chroma key green background (#00FF00). Match the reference line art, shading, rendering style, and palette exactly.";

        // (id, filename, editPrompt) — ベース画像を参照して生成するバリエーション
        static readonly (CharacterSpriteId id, string filename, string prompt, string size)[] BaseEditEntries =
        {
            (CharacterSpriteId.Idle2,      "idle2",       $"subtle second idle animation keyframe, a small readable weight shift only, {IdentityLockedPoseSuffix}", CharacterSize),
            (CharacterSpriteId.Idle3,      "idle3",       $"subtle third idle animation keyframe that loops naturally back to the reference stance, {IdentityLockedPoseSuffix}", CharacterSize),
            (CharacterSpriteId.Jump,       "jump",        $"dynamic ascending jump pose, both feet clearly off the ground and the complete figure safely inside frame, {IdentityLockedPoseSuffix}", CharacterSize),
            (CharacterSpriteId.Damage,     "damage",      $"taking a hit and recoiling backward with a clear hurt silhouette, {IdentityLockedPoseSuffix}", CharacterSize),
            (CharacterSpriteId.Grab,       "grab",        $"reaching forward with both arms or the free hand to grab an opponent, existing weapon retained and fully visible, {IdentityLockedPoseSuffix}", CharacterSize),
            (CharacterSpriteId.Dash,       "dash",        $"fast running or dashing to the right with a readable forward lean, {IdentityLockedPoseSuffix}", CharacterSize),
            (CharacterSpriteId.AttackA,    "attack_a",    $"active impact keyframe for attack A toward the right, {IdentityLockedPoseSuffix}", CharacterSize),
            (CharacterSpriteId.AttackB,    "attack_b",    $"active impact keyframe for attack B toward the right, {IdentityLockedPoseSuffix}", CharacterSize),
            (CharacterSpriteId.AttackC,    "attack_c",    $"active impact keyframe for special attack C toward the right, {IdentityLockedPoseSuffix}", CharacterSize),
            (CharacterSpriteId.SmashSide,  "smash_side",  $"active impact keyframe for a powerful heavy side smash toward the right, {IdentityLockedPoseSuffix}", CharacterSize),
            (CharacterSpriteId.EffectA,    "effect_a",    $"attack A visual effect, {EffectSuffix}", EffectSize),
            (CharacterSpriteId.EffectB,    "effect_b",    $"projectile visual effect, {EffectSuffix}", EffectSize),
            (CharacterSpriteId.EffectC,    "effect_c",    $"special attack visual effect, {EffectSuffix}", EffectSize),
            (CharacterSpriteId.EffectSmash,"effect_smash",$"large powerful smash effect, {EffectSuffix}", EffectSize),
            (CharacterSpriteId.Guard,      "guard",       $"grounded defensive guard pose, knees bent and weight braced, forearms or the existing weapon protecting the torso, both feet firmly planted on the reference baseline, do not draw a shield or barrier, {IdentityLockedPoseSuffix}", CharacterSize),
            (CharacterSpriteId.Fall,       "fall",        $"descending airborne pose just after the jump apex, gravity pulling the body downward, legs below the torso and arms or clothing trailing slightly upward, both feet visibly off the ground, keep the complete figure safely inside the canvas, {IdentityLockedPoseSuffix}", CharacterSize),
            (CharacterSpriteId.AttackA_Windup, "attack_a_windup", $"clear anticipation pose immediately before attack A, weight loaded away from the attack direction, no attack released yet, {IdentityLockedPoseSuffix}", CharacterSize),
            (CharacterSpriteId.AttackB_Windup, "attack_b_windup", $"clear anticipation pose immediately before attack B, weapon or hands drawn back to prepare the technique, no attack released yet, {IdentityLockedPoseSuffix}", CharacterSize),
            (CharacterSpriteId.AttackC_Windup, "attack_c_windup", $"clear anticipation pose immediately before attack C, visibly preparing the special technique, no attack released yet, {IdentityLockedPoseSuffix}", CharacterSize),
            (CharacterSpriteId.Smash_Windup,   "smash_windup",    $"strong exaggerated anticipation pose immediately before a heavy side smash to the right, hips and shoulders wound back, weight planted, no attack released yet, {IdentityLockedPoseSuffix}", CharacterSize),
        };

        public static Coroutine GenerateSpriteSet(MonoBehaviour runner,
            CharacterData data,
            Action<string> onProgress,
            Action<CharacterSpriteSet> onSuccess,
            Action<string> onError,
            string saveDir = null)
        {
            return runner.StartCoroutine(
                GenerateSpriteSetCoroutine(runner, data?.visualPrompt ?? "", data, onProgress, onSuccess, onError, saveDir));
        }

        // saveDir: PNG保存先ディレクトリ（null なら保存しない）
        public static Coroutine GenerateSpriteSet(MonoBehaviour runner,
            string baseVisualPrompt,
            Action<string> onProgress,
            Action<CharacterSpriteSet> onSuccess,
            Action<string> onError,
            string saveDir = null)
        {
            return runner.StartCoroutine(
                GenerateSpriteSetCoroutine(runner, baseVisualPrompt, null, onProgress, onSuccess, onError, saveDir));
        }

        static IEnumerator GenerateSpriteSetCoroutine(
            MonoBehaviour runner,
            string baseVisualPrompt,
            CharacterData data,
            Action<string> onProgress,
            Action<CharacterSpriteSet> onSuccess,
            Action<string> onError,
            string saveDir)
        {
            if (!HasConfiguredApiKey(out string keyError))
            {
                onError?.Invoke(keyError);
                yield break;
            }
            string key = ApiKey;

            // Step 1: ベース画像 (Idle1) を生成
            onProgress?.Invoke("ベース画像を生成中...");
            Sprite baseSprite = null;
            byte[] baseRawBytes = null; // 編集リファレンス用オリジナルバイト列
            string baseError = null;
            string usedModel = Model;   // ベースが成功したモデル（editsも同じモデルを使う）

            yield return GenerateBaseCoroutine(baseVisualPrompt, key,
                message => onProgress?.Invoke(message),
                (sprite, rawBytes, model) => { baseSprite = sprite; baseRawBytes = rawBytes; usedModel = model; },
                err => baseError = err);

            if (baseSprite == null)
            {
                onError?.Invoke(baseError ?? "ベース画像(Idle1)の生成に失敗しました");
                yield break;
            }

            var set = new CharacterSpriteSet();
            set.Set(CharacterSpriteId.Idle1, baseSprite);

            if (saveDir != null)
            {
                TrySavePng(saveDir, "idle1", baseSprite);
            }

            // Step 2: 残りの画像を並列生成 (images/edits)。各Coroutineは共通レートリミッターで開始をずらす。
            var editEntries = BuildEditEntries(data);
            onProgress?.Invoke($"バリエーション画像を並列生成中... ({editEntries.Count}枚)");
            int pending = editEntries.Count;
            int failedCount = 0;
            var failedIds = new List<CharacterSpriteId>();

            foreach (var entry in editEntries)
            {
                var (id, filename, editPrompt, size) = entry;
                if (string.IsNullOrEmpty(editPrompt))
                {
                    set.Set(id, null);
                    pending--;
                    continue;
                }
                // baseVisualPrompt（外見説明）+ 同一性固定済みのポーズ指示
                string fullPrompt = baseVisualPrompt + ", " + editPrompt;
                runner.StartCoroutine(GenerateEditCoroutine(
                    id, filename, fullPrompt, size, baseRawBytes, key, saveDir, usedModel,
                    IsEffectSprite(id),
                    message => onProgress?.Invoke(message),
                    (spriteId, fname, sprite) =>
                    {
                        set.Set(spriteId, sprite);
                        pending--;
                        onProgress?.Invoke($"生成完了: {fname} (残り {pending} 枚)");
                    },
                    (spriteId, err) =>
                    {
                        // 追加ポーズは全並列ジョブ完了後、対応する意味的に近いポーズへ代替する。
                        // 先にIdle1を入れると、後からJump/Attackが成功しても適切な代替へ更新できない。
                        if (IsAdditionalPose(spriteId) || IsEffectSprite(spriteId))
                            set.Set(spriteId, null);
                        else
                            set.Set(spriteId, baseSprite);
                        Debug.LogWarning($"[AIImage] {spriteId} 生成失敗（フォールバック予定）: {err}");
                        failedCount++;
                        failedIds.Add(spriteId);
                        pending--;
                    }));
            }

            // 各edit個別タイムアウト(180s)を超えても全コールバックが返らない異常時の保険。
            // これがないと pending が 0 にならず永久にハングする。
            // 429時の共有クールダウン中も待てるよう、通常時より長い安全タイムアウトを取る。
            const float overallTimeout = 600f;
            float elapsed = 0f;
            while (pending > 0 && elapsed < overallTimeout)
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
            if (pending > 0)
            {
                Debug.LogWarning($"[AIImage] 画像生成が{overallTimeout:F0}秒以内に完了しませんでした（残り{pending}枚を代替）");
                onProgress?.Invoke($"⚠ 画像生成タイムアウト（残り{pending}枚を代替）");
                failedCount += pending;
                pending = 0;
            }

            EnsureAdditionalPoseFallbacks(set, baseSprite);

            if (failedCount > 0)
            {
                string failedList = string.Join(", ", failedIds);
                Debug.LogWarning($"[AIImage] {failedCount}枚の画像生成に失敗し代替ポーズを使用します: {failedList}");
                onProgress?.Invoke($"⚠ {failedCount}枚の画像生成に失敗（代替ポーズを使用）: {failedList}");
            }

            onSuccess?.Invoke(set);
        }

        // ボス専用: 通常スプライト一式に加えて、追加技プール(data.extraSkills)の各技に専用のポーズ・エフェクト画像を生成する。
        // 既存のGenerateSpriteSet/BuildEditEntries/呼び出し元(PreBattlePanel)には一切触れない、完全に独立した経路。
        public static Coroutine GenerateBossSpriteSet(MonoBehaviour runner,
            CharacterData data,
            Action<string> onProgress,
            Action<CharacterSpriteSet, List<Sprite>, List<Sprite>> onSuccess,
            Action<string> onError,
            string saveDir = null)
        {
            return runner.StartCoroutine(
                GenerateBossSpriteSetCoroutine(runner, data, onProgress, onSuccess, onError, saveDir));
        }

        static IEnumerator GenerateBossSpriteSetCoroutine(
            MonoBehaviour runner,
            CharacterData data,
            Action<string> onProgress,
            Action<CharacterSpriteSet, List<Sprite>, List<Sprite>> onSuccess,
            Action<string> onError,
            string saveDir)
        {
            if (!HasConfiguredApiKey(out string keyError))
            {
                onError?.Invoke(keyError);
                yield break;
            }
            string key = ApiKey;
            string baseVisualPrompt = data?.visualPrompt ?? "";

            onProgress?.Invoke("ベース画像を生成中...");
            Sprite baseSprite = null;
            byte[] baseRawBytes = null;
            string baseError = null;
            string usedModel = Model;

            yield return GenerateBaseCoroutine(baseVisualPrompt, key,
                message => onProgress?.Invoke(message),
                (sprite, rawBytes, model) => { baseSprite = sprite; baseRawBytes = rawBytes; usedModel = model; },
                err => baseError = err);

            if (baseSprite == null)
            {
                onError?.Invoke(baseError ?? "ベース画像(Idle1)の生成に失敗しました");
                yield break;
            }

            var set = new CharacterSpriteSet();
            set.Set(CharacterSpriteId.Idle1, baseSprite);
            if (saveDir != null) TrySavePng(saveDir, "idle1", baseSprite);

            // 通常バリエーション（既存ロジックを完全再利用）
            var editEntries = BuildEditEntries(data);

            // 追加技プール分のポーズ・エフェクトエントリを組み立てる
            var extraPoseEntries = new List<(int index, string filename, string prompt, string size)>();
            var extraEffectEntries = new List<(int index, string filename, string prompt, string size)>();
            int extraCount = data?.extraSkills?.Count ?? 0;

            if (data?.extraSkills != null)
            {
                for (int i = 0; i < data.extraSkills.Count; i++)
                {
                    var skill = data.extraSkills[i];
                    if (skill == null) continue;

                    string posePrompt =
                        $"active animation keyframe performing '{skill.skill_name}' ({skill.description}), " +
                        IdentityLockedPoseSuffix;
                    extraPoseEntries.Add((i, $"extra_pose_{i}", posePrompt, CharacterSize));

                    if (NeedsSeparateEffect(skill))
                    {
                        var (effectPrompt, effectSize) = BuildEffectPrompt(skill);
                        extraEffectEntries.Add((i, $"extra_effect_{i}", effectPrompt, effectSize));
                    }
                }
            }

            int totalCount = editEntries.Count + extraPoseEntries.Count + extraEffectEntries.Count;
            onProgress?.Invoke($"バリエーション画像を並列生成中... ({totalCount}枚)");

            int pending = totalCount;
            int failedCount = 0;
            var failedNames = new List<string>();
            var extraPoseSprites = new List<Sprite>(new Sprite[extraCount]);
            var extraEffectSprites = new List<Sprite>(new Sprite[extraCount]);

            foreach (var entry in editEntries)
            {
                var (id, filename, editPrompt, size) = entry;
                if (string.IsNullOrEmpty(editPrompt))
                {
                    set.Set(id, null);
                    pending--;
                    continue;
                }
                string fullPrompt = baseVisualPrompt + ", " + editPrompt;
                runner.StartCoroutine(GenerateEditCoroutine(
                    id, filename, fullPrompt, size, baseRawBytes, key, saveDir, usedModel,
                    IsEffectSprite(id),
                    message => onProgress?.Invoke(message),
                    (spriteId, fname, sprite) =>
                    {
                        set.Set(spriteId, sprite);
                        pending--;
                        onProgress?.Invoke($"生成完了: {fname} (残り {pending} 枚)");
                    },
                    (spriteId, err) =>
                    {
                        if (IsAdditionalPose(spriteId) || IsEffectSprite(spriteId))
                            set.Set(spriteId, null);
                        else
                            set.Set(spriteId, baseSprite);
                        Debug.LogWarning($"[AIImage] {spriteId} 生成失敗（フォールバック予定）: {err}");
                        failedCount++;
                        failedNames.Add(spriteId.ToString());
                        pending--;
                    }));
            }

            foreach (var (idx, filename, prompt, size) in extraPoseEntries)
            {
                runner.StartCoroutine(GenerateEditCoroutine(
                    CharacterSpriteId.Idle1, filename, prompt, size, baseRawBytes, key, saveDir, usedModel,
                    false,
                    message => onProgress?.Invoke(message),
                    (_, fname, sprite) =>
                    {
                        extraPoseSprites[idx] = sprite;
                        pending--;
                        onProgress?.Invoke($"生成完了: {fname} (残り {pending} 枚)");
                    },
                    (_, err) =>
                    {
                        Debug.LogWarning($"[AIImage] {filename} 生成失敗（ベース画像で代替）: {err}");
                        extraPoseSprites[idx] = baseSprite;
                        failedCount++;
                        failedNames.Add(filename);
                        pending--;
                    }));
            }

            foreach (var (idx, filename, prompt, size) in extraEffectEntries)
            {
                runner.StartCoroutine(GenerateEditCoroutine(
                    CharacterSpriteId.Idle1, filename, prompt, size, baseRawBytes, key, saveDir, usedModel,
                    true,
                    message => onProgress?.Invoke(message),
                    (_, fname, sprite) =>
                    {
                        extraEffectSprites[idx] = sprite;
                        pending--;
                        onProgress?.Invoke($"生成完了: {fname} (残り {pending} 枚)");
                    },
                    (_, err) =>
                    {
                        Debug.LogWarning($"[AIImage] {filename} 生成失敗（エフェクトなし）: {err}");
                        extraEffectSprites[idx] = null;
                        failedCount++;
                        failedNames.Add(filename);
                        pending--;
                    }));
            }

            const float overallTimeout = 600f;
            float elapsed = 0f;
            while (pending > 0 && elapsed < overallTimeout)
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
            if (pending > 0)
            {
                Debug.LogWarning($"[AIImage] 画像生成が{overallTimeout:F0}秒以内に完了しませんでした（残り{pending}枚）");
                onProgress?.Invoke($"⚠ 画像生成タイムアウト（残り{pending}枚）");
                failedCount += pending;
                pending = 0;
            }

            EnsureAdditionalPoseFallbacks(set, baseSprite);

            if (failedCount > 0)
            {
                string failedList = string.Join(", ", failedNames);
                Debug.LogWarning($"[AIImage] {failedCount}枚の画像生成に失敗しました: {failedList}");
                onProgress?.Invoke($"⚠ {failedCount}枚の画像生成に失敗: {failedList}");
            }

            onSuccess?.Invoke(set, extraPoseSprites, extraEffectSprites);
        }

        static List<(CharacterSpriteId id, string filename, string prompt, string size)> BuildEditEntries(CharacterData data)
        {
            var entries = new List<(CharacterSpriteId id, string filename, string prompt, string size)>(BaseEditEntries);
            if (data?.skills == null) return entries;

            ConfigureActivePose(entries, data.GetSkill(SkillSlot.AttackA), CharacterSpriteId.AttackA, "attack_a");
            ConfigureActivePose(entries, data.GetSkill(SkillSlot.AttackB), CharacterSpriteId.AttackB, "attack_b");
            ConfigureActivePose(entries, data.GetSkill(SkillSlot.AttackC), CharacterSpriteId.AttackC, "attack_c");
            ConfigureActivePose(entries, data.GetSkill(SkillSlot.SmashSide), CharacterSpriteId.SmashSide, "smash_side");
            ConfigureWindup(entries, data.GetSkill(SkillSlot.AttackA), CharacterSpriteId.AttackA_Windup, "attack_a_windup");
            ConfigureWindup(entries, data.GetSkill(SkillSlot.AttackB), CharacterSpriteId.AttackB_Windup, "attack_b_windup");
            ConfigureWindup(entries, data.GetSkill(SkillSlot.AttackC), CharacterSpriteId.AttackC_Windup, "attack_c_windup");
            ConfigureWindup(entries, data.GetSkill(SkillSlot.SmashSide), CharacterSpriteId.Smash_Windup, "smash_windup");
            ConfigureEffect(entries, data.GetSkill(SkillSlot.AttackA), CharacterSpriteId.EffectA, "effect_a");
            ConfigureEffect(entries, data.GetSkill(SkillSlot.AttackB), CharacterSpriteId.EffectB, "effect_b");
            ConfigureEffect(entries, data.GetSkill(SkillSlot.AttackC), CharacterSpriteId.EffectC, "effect_c");
            ConfigureEffect(entries, data.GetSkill(SkillSlot.SmashSide), CharacterSpriteId.EffectSmash, "effect_smash");
            return entries;
        }

        static void ConfigureActivePose(
            List<(CharacterSpriteId id, string filename, string prompt, string size)> entries,
            SkillData skill, CharacterSpriteId id, string filename)
        {
            int index = entries.FindIndex(e => e.id == id);
            if (index < 0 || skill == null) return;

            string action;
            if (HasAction(skill, "beam"))
                action = "the exact instant the beam is released from the existing weapon or hands, without drawing the beam itself";
            else if (HasAction(skill, "projectile"))
                action = "the exact instant a projectile is released or thrown, without drawing the projectile itself";
            else if (HasAction(skill, "uppercut"))
                action = "the rising strike at maximum extension, body driving upward";
            else if (HasAction(skill, "dive_attack"))
                action = "a committed forward-downward diving strike, entire body still safely inside frame";
            else if (HasAction(skill, "summon"))
                action = "the completed summoning gesture, without drawing the summoned being or magic effect";
            else if (HasAction(skill, "wall"))
                action = "the completed groundward gesture that raises a physical wall, without drawing the wall itself";
            else if (HasAction(skill, "counter") || HasAction(skill, "reflector") || HasAction(skill, "barrier"))
                action = "a strong completed defensive technique stance, without drawing a shield, aura, or barrier";
            else
                action = "the strike at maximum readable extension toward the right, with clear body mechanics and silhouette";

            string weight = skill.slot == SkillSlot.SmashSide
                ? "Make the follow-through feel especially heavy and decisive."
                : "Make it read clearly as the active frame following the anticipation pose.";
            entries[index] = (id, filename,
                $"active animation keyframe for '{skill.skill_name}' ({skill.description}): {action}. {weight} " +
                IdentityLockedPoseSuffix,
                CharacterSize);
        }

        static void ConfigureWindup(
            List<(CharacterSpriteId id, string filename, string prompt, string size)> entries,
            SkillData skill, CharacterSpriteId id, string filename)
        {
            int index = entries.FindIndex(e => e.id == id);
            if (index < 0 || skill == null) return;

            string preparation;
            if (HasAction(skill, "projectile") || HasAction(skill, "beam"))
                preparation = "aiming or gathering power before release, with the projectile or beam not created yet";
            else if (HasAction(skill, "summon"))
                preparation = "beginning the summoning gesture before any creature or object appears";
            else if (HasAction(skill, "wall"))
                preparation = "planting their stance and directing power at the ground before a wall rises";
            else if (HasAction(skill, "counter") || HasAction(skill, "reflector") ||
                     HasAction(skill, "barrier") || HasAction(skill, "buff_self"))
                preparation = "entering the technique stance before its defensive or empowering effect appears";
            else
                preparation = "loading the striking limb or existing weapon backward, with the hit not released yet";

            string emphasis = skill.slot == SkillSlot.SmashSide
                ? "Use an exaggerated, heavy anticipation with firmly planted weight and shoulders wound back."
                : "Use a clear, readable anticipation silhouette that naturally leads into the attack to the right.";
            string prompt =
                $"animation wind-up frame immediately BEFORE performing '{skill.skill_name}' ({skill.description}), " +
                $"{preparation}. {emphasis} {IdentityLockedPoseSuffix}";
            entries[index] = (id, filename, prompt, CharacterSize);
        }

        static void ConfigureEffect(List<(CharacterSpriteId id, string filename, string prompt, string size)> entries,
                                    SkillData skill, CharacterSpriteId id, string filename)
        {
            int index = entries.FindIndex(e => e.id == id);
            if (index < 0 || skill == null) return;
            if (!NeedsSeparateEffect(skill))
            {
                entries[index] = (id, filename, null, EffectSize);
                return;
            }

            var (prompt, size) = BuildEffectPrompt(skill);
            entries[index] = (id, filename, prompt, size);
        }

        // エフェクト画像プロンプトを技の内容から組み立てる。
        // 「技種別ごとの形状」×「属性ごとの質感」の組み合わせで、技ごとに見た目のバリエーションを出す。
        static (string prompt, string size) BuildEffectPrompt(SkillData skill)
        {
            bool vertical = PrefersVerticalEffect(skill);
            string shape =
                  HasAction(skill, "wall")        ? "solid destructible wall or block obstacle for a 2D fighting game, broad stable base, clear silhouette"
                : HasAction(skill, "summon")      ? "one individual summoned creature or minion sprite for a 2D fighting game skill, clear full body, exactly one creature, no copies or flock"
                : HasAction(skill, "beam")        ? "long horizontal 2D energy beam visual effect, bright core, no rectangular block"
                : HasAction(skill, "gravity_well")? "large radial vortex and gravity well visual effect, deep spiral center with inward energy flow"
                : HasAction(skill, "uppercut")    ? "tall rising uppercut streak effect, vertical swoosh with strong upward motion"
                : HasAction(skill, "dive_attack") ? "ground impact shockwave effect bursting outward with dust and debris"
                : HasAction(skill, "shockwave")   ? "low wide ground shockwave effect running along the floor"
                : HasEffectShape(skill, "cross")  ? "cross-shaped energy burst with four clearly readable arms"
                : HasEffectShape(skill, "cone")   ? "forward-facing fan or cone-shaped energy wave with a clear origin and widening edge"
                : HasEffectShape(skill, "arc")    ? "curved crescent arc effect with a clearly readable inner and outer edge"
                : HasEffectShape(skill, "column") ? "tall narrow energy column with a clear base and strong vertical motion"
                : HasEffectShape(skill, "line")   ? "long narrow directional energy streak with a clear start and end"
                : HasRadialArea(skill)             ? "radial circular burst effect, expanding energy ring or annular wave"
                : HasExplosion(skill)             ? "round explosion burst effect with bright core and flying sparks"
                : vertical                        ? "tall vertical 2D game visual effect, rising column or upward slash"
                : "wide horizontal 2D game visual effect, side slash, wave, or projectile trail";
            string prompt =
                $"{skill.skill_name} ({skill.description}), {shape}, made of {ElementDescriptor(skill.element)}, {EffectSuffix}";
            return (prompt, vertical ? CharacterSize : EffectSize);
        }

        // 属性→エフェクトの質感表現。物理でも毎回同じ見た目にならないよう技名・説明と組み合わせる。
        static string ElementDescriptor(Element e) => e switch
        {
            Element.Fire      => "blazing fire, flames and embers",
            Element.Ice       => "freezing ice crystals and frost shards",
            Element.Lightning => "crackling electric lightning arcs",
            Element.Dark      => "dark violet shadow energy and wisps",
            Element.Wind      => "swirling wind blades and air currents",
            Element.Physical  => "sharp white and steel-gray impact energy",
            _                 => "bright colorful energy",
        };

        static bool NeedsSeparateEffect(SkillData skill)
        {
            foreach (var a in EnumerateActions(skill))
                if (ActionUsesSeparateEffect(a)) return true;
            return false;
        }

        static bool ActionUsesSeparateEffect(SkillAction a)
        {
            if (a == null || a.hide_effect) return false;
            return a.type == "projectile" || a.type == "area_hitbox" || a.type == "trap_hitbox" || a.type == "wall" ||
                   a.type == "summon" || a.type == "beam" || a.type == "melee_hitbox" ||
                   a.type == "jump_attack" || a.type == "dash+melee_hitbox" || a.type == "multi_hit" ||
                   a.type == "gravity_well" || a.type == "lifesteal" || a.type == "shockwave" ||
                   a.type == "uppercut" || a.type == "dive_attack";
        }

        static bool PrefersVerticalEffect(SkillData skill)
        {
            foreach (var a in EnumerateActions(skill))
            {
                if (a == null) continue;
                if (a.type == "jump_attack" || a.type == "uppercut") return true;
                if (a.size_y > 0f && a.size_y > Mathf.Max(a.size_x, a.range) * 1.15f) return true;
                if (a.knockback_y > 0.7f) return true;
            }
            return false;
        }

        static bool HasRadialArea(SkillData skill)
        {
            return HasEffectShape(skill, "ring") || HasEffectShape(skill, "annulus");
        }

        static bool HasEffectShape(SkillData skill, string shape)
        {
            if (string.IsNullOrEmpty(shape)) return false;
            foreach (var a in EnumerateActions(skill))
                if (ActionUsesSeparateEffect(a) && a.shape == shape) return true;
            return false;
        }

        static bool HasExplosion(SkillData skill)
        {
            foreach (var a in EnumerateActions(skill))
                if (ActionUsesSeparateEffect(a) && a.type == "projectile" && a.explosion_radius > 0f)
                    return true;
            return false;
        }

        static bool HasAction(SkillData skill, string type)
        {
            foreach (var a in EnumerateActions(skill))
                if (a != null && a.type == type) return true;
            return false;
        }

        static IEnumerable<SkillAction> EnumerateActions(SkillData skill)
        {
            if (skill?.actions != null)
                foreach (var action in skill.actions) yield return action;
            if (skill?.follow_up_actions != null)
                foreach (var action in skill.follow_up_actions) yield return action;
        }

        // /v1/images/generations でベース画像を生成し、(Sprite, rawBytes, 使用モデル) を返す。
        // キャラ生成を失敗で終わらせないため、HTTPエラーだけでなく解析・ダウンロード・変換の
        // 失敗もリトライ対象とし、主モデルが全滅したらフォールバックモデルでも試す。
        static IEnumerator GenerateBaseCoroutine(
            string basePrompt, string key,
            Action<string> onRetry,
            Action<Sprite, byte[], string> onSuccess, Action<string> onError)
        {
            string safePrompt = OpenAIRequest.EscapeString(
                basePrompt + $", standing idle, {CharSuffix}");
            string lastErr = null;
            string[] models = { Model, FallbackModel };

            foreach (string model in models)
            {
                string body =
                    $"{{\"model\":\"{model}\"," +
                    $"\"prompt\":\"{safePrompt}\"," +
                    $"\"n\":1,\"size\":\"{CharacterSize}\",\"quality\":\"{Quality}\"}}";

                int attempt = 0;
                int transientRetries = 0;
                while (attempt < MaxImageAttempts)
                {
                    string respText = null;
                    bool transientFailure = false;
                    bool retryTransientFailure = false;
                    using (var req = new UnityWebRequest(GenerationsEndpoint, "POST"))
                    {
                        req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                        req.downloadHandler = new DownloadHandlerBuffer();
                        req.SetRequestHeader("Content-Type", "application/json");
                        req.SetRequestHeader("Authorization", "Bearer " + key);
                        req.timeout = 90;

                        yield return WaitForImageRequestSlot();
                        yield return req.SendWebRequest();

                        if (req.result == UnityWebRequest.Result.Success)
                            respText = req.downloadHandler.text;
                        else
                        {
                            lastErr = $"{req.error}: {req.downloadHandler?.text}";
                            transientFailure = IsTransientRequestFailure(req);
                            if (transientFailure && transientRetries < MaxTransientRetries)
                            {
                                transientRetries++;
                                ApplyTransientCooldown(req, transientRetries);
                                retryTransientFailure = true;
                            }
                        }
                    }
                    if (respText == null)
                    {
                        if (retryTransientFailure)
                        {
                            string retryMessage = $"一時エラーのためベース画像を自動リトライ中 " +
                                                  $"({transientRetries}/{MaxTransientRetries})";
                            onRetry?.Invoke(retryMessage);
                            Debug.LogWarning($"[AIImage] {retryMessage} ({model}): {lastErr}");
                            continue;
                        }

                        // 400系の恒久エラー、または一時障害の再送上限到達。別モデルへ移る。
                        Debug.LogWarning($"[AIImage] ベース生成HTTPエラー({model}): {lastErr}");
                        break;
                    }

                    attempt++;

                    // try-catch の中に yield return は置けないため、レスポンス解析と URL DL を分離する
                    string imageUrl = null;
                    string imageBase64 = null;
                    bool parseOk = true;
                    try { ParseImageResponse(respText, out imageUrl, out imageBase64); }
                    catch (Exception e) { lastErr = "レスポンス解析失敗: " + e.Message; parseOk = false; }
                    if (!parseOk)
                    {
                        if (attempt < MaxImageAttempts) yield return new WaitForSeconds(2f);
                        continue;
                    }

                    byte[] rawBytes = null;
                    if (!string.IsNullOrEmpty(imageBase64))
                    {
                        bool decodeOk = true;
                        try { rawBytes = Convert.FromBase64String(imageBase64); }
                        catch (Exception e)
                        {
                            lastErr = "Base64デコード失敗: " + e.Message;
                            decodeOk = false;
                        }
                        if (!decodeOk)
                        {
                            if (attempt < MaxImageAttempts) yield return new WaitForSeconds(2f);
                            continue;
                        }
                    }
                    else if (!string.IsNullOrEmpty(imageUrl))
                    {
                        // URL ダウンロードは try-catch の外で yield return する
                        var imgReq = UnityWebRequestTexture.GetTexture(imageUrl);
                        imgReq.timeout = 60;
                        yield return imgReq.SendWebRequest();
                        if (imgReq.result != UnityWebRequest.Result.Success)
                        {
                            lastErr = "URL画像のダウンロード失敗: " + imgReq.error;
                            imgReq.Dispose();
                            if (attempt < MaxImageAttempts) yield return new WaitForSeconds(2f);
                            continue;
                        }
                        var urlTex = DownloadHandlerTexture.GetContent(imgReq);
                        rawBytes = ImageConversion.EncodeToPNG(urlTex);
                        imgReq.Dispose();
                    }
                    else
                    {
                        lastErr = "レスポンスにurl/b64_jsonが見つかりません";
                        if (attempt < MaxImageAttempts) yield return new WaitForSeconds(2f);
                        continue;
                    }

                    Sprite sprite = null;
                    bool convertOk = true;
                    try { sprite = RawBytesToSprite(rawBytes); }
                    catch (Exception e)
                    {
                        lastErr = "画像変換失敗: " + e.Message;
                        convertOk = false;
                    }
                    if (!convertOk)
                    {
                        if (attempt < MaxImageAttempts) yield return new WaitForSeconds(2f);
                        continue;
                    }

                    onSuccess?.Invoke(sprite, rawBytes, model);
                    yield break;
                }

                if (model != models[models.Length - 1])
                    Debug.LogWarning($"[AIImage] モデル {model} でベース生成に失敗。{FallbackModel} へフォールバックします");
            }

            onError?.Invoke(lastErr ?? "ベース画像生成に失敗");
        }

        // /v1/images/edits でベース画像を参照してバリエーションを生成する
        static IEnumerator GenerateEditCoroutine(
            CharacterSpriteId id, string filename, string prompt,
            string size, byte[] basePngBytes, string key, string saveDir, string model,
            bool centerPivot,
            Action<string> onRetry,
            Action<CharacterSpriteId, string, Sprite> onSuccess,
            Action<CharacterSpriteId, string> onError)
        {
            string sizeVal = string.IsNullOrEmpty(size) ? CharacterSize : size;
            string lastErr  = null;
            int processingAttempts = 0;
            int transientRetries = 0;

            while (processingAttempts < MaxImageAttempts)
            {
                // multipartはリクエストごとに作り直す必要がある（uploadHandlerが消費されるため）
                var form = new List<IMultipartFormSection>
                {
                    new MultipartFormDataSection("model",   model),
                    new MultipartFormDataSection("prompt",  prompt),
                    new MultipartFormDataSection("size",    sizeVal),
                    new MultipartFormDataSection("quality", Quality),
                    new MultipartFormDataSection("n",       "1"),
                    new MultipartFormFileSection("image[]", basePngBytes, "reference.png", "image/png"),
                };

                using var req = UnityWebRequest.Post(EditsEndpoint, form);
                req.SetRequestHeader("Authorization", "Bearer " + key);
                req.timeout = 180;

                yield return WaitForImageRequestSlot();
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    lastErr = $"{req.error}: {req.downloadHandler?.text}";
                    bool transientFailure = IsTransientRequestFailure(req);
                    if (transientFailure && transientRetries < MaxTransientRetries)
                    {
                        transientRetries++;
                        ApplyTransientCooldown(req, transientRetries);
                        string retryMessage = $"一時エラーのため自動リトライ中: {filename} " +
                                              $"({transientRetries}/{MaxTransientRetries})";
                        onRetry?.Invoke(retryMessage);
                        Debug.LogWarning($"[AIImage] {retryMessage}: {lastErr}");
                        continue;
                    }

                    Debug.LogWarning($"[AIImage] エフェクト/ポーズ生成HTTPエラー {filename}: {lastErr}");
                    break;
                }

                processingAttempts++;
                Sprite processedSprite = null;
                try
                {
                    ParseImageResponse(req.downloadHandler.text, out string url, out string b64);
                    if (string.IsNullOrEmpty(b64))
                        throw new Exception("b64_jsonが見つかりません");

                    byte[] rawBytes = Convert.FromBase64String(b64);
                    processedSprite = RawBytesToSprite(rawBytes, centerPivot);
                }
                catch (Exception e)
                {
                    lastErr = "画像処理失敗: " + e.Message;
                }

                if (processedSprite != null)
                {
                    if (saveDir != null)
                        TrySavePng(saveDir, filename, processedSprite);

                    onSuccess?.Invoke(id, filename, processedSprite);
                    yield break;
                }

                Debug.LogWarning($"[AIImage] 画像処理を再試行します({processingAttempts}/{MaxImageAttempts}) " +
                                 $"{filename}: {lastErr}");
                if (processingAttempts < MaxImageAttempts)
                    yield return new WaitForSeconds(1.5f);
            }

            onError?.Invoke(id, lastErr ?? "画像生成に失敗");
        }

        // バイト列 → WhiteBackgroundRemover適用 → Sprite
        // threshold=0.94: 純白に近い画素のみ除去。キャラの肌・明るい服は保護される。
        static Sprite RawBytesToSprite(byte[] rawBytes, bool centerPivot = false)
        {
            var raw = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(raw, rawBytes))
                throw new Exception("Texture2D.LoadImage failed");

            var processed = WhiteBackgroundRemover.ApplyChromaGreen(raw);
            UnityEngine.Object.Destroy(raw);

            return Sprite.Create(
                processed,
                new Rect(0, 0, processed.width, processed.height),
                centerPivot ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f, 0f),
                processed.height / 2f);
        }

        // 透過済み Sprite を PNG としてディスクに保存する
        static void TrySavePng(string dir, string filename, Sprite sprite)
        {
            if (sprite?.texture == null) return;
            try
            {
                Directory.CreateDirectory(dir);
                byte[] png = ImageConversion.EncodeToPNG(sprite.texture);
                File.WriteAllBytes(Path.Combine(dir, filename + ".png"), png);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AIImage] PNG保存失敗 ({filename}): {e.Message}");
            }
        }

        [Serializable] class ImgResp { public ImgData[] data; }
        [Serializable] class ImgData { public string url; public string b64_json; }

        static void ParseImageResponse(string json, out string url, out string b64Json)
        {
            url = null;
            b64Json = null;
            var resp = JsonUtility.FromJson<ImgResp>(json);
            if (resp?.data == null || resp.data.Length == 0)
                throw new Exception("data[0] が見つかりません");
            url = resp.data[0].url;
            b64Json = resp.data[0].b64_json;
            if (string.IsNullOrEmpty(url) && string.IsNullOrEmpty(b64Json))
                throw new Exception("data[0].url / data[0].b64_json が見つかりません");
        }

        static bool IsAdditionalPose(CharacterSpriteId id) =>
            id == CharacterSpriteId.Guard ||
            id == CharacterSpriteId.Fall ||
            id == CharacterSpriteId.AttackA_Windup ||
            id == CharacterSpriteId.AttackB_Windup ||
            id == CharacterSpriteId.AttackC_Windup ||
            id == CharacterSpriteId.Smash_Windup;

        // 追加画像が失敗・タイムアウト・旧保存データ由来で欠けていても、ポーズの意味を保つ。
        // GetのIdle1自動フォールバックを使わず、生の有無を確認してから対応ポーズを割り当てる。
        static void EnsureAdditionalPoseFallbacks(CharacterSpriteSet set, Sprite idle1)
        {
            if (set == null) return;
            Sprite Raw(CharacterSpriteId id) => set.Get(id, null, fallbackToPrimary: false);
            void Fill(CharacterSpriteId id, CharacterSpriteId fallbackId)
            {
                if (Raw(id) != null) return;
                set.Set(id, Raw(fallbackId) ?? idle1);
            }

            Fill(CharacterSpriteId.Guard, CharacterSpriteId.Idle1);
            Fill(CharacterSpriteId.Fall, CharacterSpriteId.Jump);
            Fill(CharacterSpriteId.AttackA_Windup, CharacterSpriteId.AttackA);
            Fill(CharacterSpriteId.AttackB_Windup, CharacterSpriteId.AttackB);
            Fill(CharacterSpriteId.AttackC_Windup, CharacterSpriteId.AttackC);
            Fill(CharacterSpriteId.Smash_Windup, CharacterSpriteId.SmashSide);
        }

        static bool IsEffectSprite(CharacterSpriteId id) =>
            id == CharacterSpriteId.EffectA ||
            id == CharacterSpriteId.EffectB ||
            id == CharacterSpriteId.EffectC ||
            id == CharacterSpriteId.EffectSmash;
    }
}
