using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using PromptFighters.AI;
using PromptFighters.Battle.Skills;
using PromptFighters.Battle.Skills.Json;
using PromptFighters.Utils;

namespace PromptFighters.GameFlow
{
    // 生成されたキャラクターをpersistentDataPathに保存・読み込みする。
    // スプライトPNGは {SaveDir}/{id}/sprites/ 以下に保存する。
    public static class CharacterSaveManager
    {
        static string SaveDir => Path.Combine(Application.persistentDataPath, "SavedChars");
        static readonly object SaveStampLock = new object();
        static long _lastSaveStamp;
        static string _lastRecoveredSpriteDir;
        static readonly string[] VoiceFilenames = { "intro", "attack_a", "attack_b", "attack_c", "smash_side" };

        static readonly (CharacterSpriteId id, string filename)[] SpriteEntries =
        {
            (CharacterSpriteId.Idle1,      "idle1"),
            (CharacterSpriteId.Idle2,      "idle2"),
            (CharacterSpriteId.Idle3,      "idle3"),
            (CharacterSpriteId.Jump,       "jump"),
            (CharacterSpriteId.Damage,     "damage"),
            (CharacterSpriteId.Grab,       "grab"),
            (CharacterSpriteId.Dash,       "dash"),
            (CharacterSpriteId.AttackA,    "attack_a"),
            (CharacterSpriteId.AttackB,    "attack_b"),
            (CharacterSpriteId.AttackC,    "attack_c"),
            (CharacterSpriteId.SmashSide,  "smash_side"),
            (CharacterSpriteId.EffectA,    "effect_a"),
            (CharacterSpriteId.EffectB,    "effect_b"),
            (CharacterSpriteId.EffectC,    "effect_c"),
            (CharacterSpriteId.EffectSmash,"effect_smash"),
            (CharacterSpriteId.Guard,      "guard"),
            (CharacterSpriteId.Fall,       "fall"),
            (CharacterSpriteId.AttackA_Windup, "attack_a_windup"),
            (CharacterSpriteId.AttackB_Windup, "attack_b_windup"),
            (CharacterSpriteId.AttackC_Windup, "attack_c_windup"),
            (CharacterSpriteId.Smash_Windup,   "smash_windup"),
        };

        static bool IsEffectSprite(CharacterSpriteId id) =>
            id == CharacterSpriteId.EffectA ||
            id == CharacterSpriteId.EffectB ||
            id == CharacterSpriteId.EffectC ||
            id == CharacterSpriteId.EffectSmash;

        // 画像生成の前にスプライト保存先ディレクトリだけを確保する。
        // character.json はまだ書かないため、ロスター（LoadAll）には現れない。
        // 生成が完全に成功してから Save() を呼ぶことで、画像のないキャラが一覧に並ぶのを防ぐ。
        public static void PrepareDirectory(CharacterData data)
        {
            if (data == null || string.IsNullOrWhiteSpace(data.characterName)) return;
            if (!string.IsNullOrEmpty(data.spriteDir))
            {
                data.voiceDir ??= VoiceDirFromSpriteDir(data.spriteDir);
                return; // 確保済み
            }
            try
            {
                string id = SanitizeId(data.characterName) + "_" + NextSaveStamp();
                data.spriteDir = Path.Combine(SaveDir, id, "sprites");
                data.voiceDir = Path.Combine(SaveDir, id, "voices");
                Directory.CreateDirectory(data.spriteDir);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] 保存先確保失敗: {e.Message}");
            }
        }

        // 生成に失敗したキャラの確保済みディレクトリ（部分的な画像など）を破棄する。
        public static void DiscardPrepared(CharacterData data)
        {
            if (data == null || string.IsNullOrEmpty(data.spriteDir)) return;
            try
            {
                string characterDir = Directory.GetParent(data.spriteDir)?.FullName;
                if (!string.IsNullOrEmpty(characterDir) && Directory.Exists(characterDir))
                    Directory.Delete(characterDir, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] 生成失敗キャラの破棄失敗: {e.Message}");
            }
            data.spriteDir = null;
            data.voiceDir = null;
        }

        // JSONを保存し、data.spriteDir を設定する。
        // PrepareDirectory 済みの場合は同じIDを使う（画像の保存先とJSONを一致させる）。
        public static bool Save(CharacterData data)
        {
            if (data == null || string.IsNullOrWhiteSpace(data.characterName)) return false;
            try
            {
                Directory.CreateDirectory(SaveDir);
                string id;
                if (!string.IsNullOrEmpty(data.spriteDir))
                {
                    id = Path.GetFileName(Directory.GetParent(data.spriteDir)?.FullName ?? "");
                    if (string.IsNullOrEmpty(id))
                        id = SanitizeId(data.characterName) + "_" + NextSaveStamp();
                }
                else
                {
                    id = SanitizeId(data.characterName) + "_" + NextSaveStamp();
                }
                string path = Path.Combine(SaveDir, id + ".json");
                data.spriteDir = Path.Combine(SaveDir, id, "sprites");
                data.voiceDir = Path.Combine(SaveDir, id, "voices");
                string json = Serialize(data);
                ValidateSerializedJson(json, data.characterName);
                WriteTextAtomically(path, json);
                PresetCharacterLoader.ClearCache();
                Debug.Log($"[Save] 保存完了: {path}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] 保存失敗: {e.Message}");
                return false;
            }
        }

        static void ValidateSerializedJson(string json, string expectedName)
        {
            CharacterJsonRaw raw;
            try
            {
                // SkillJsonParserの旧データ補正を通さず、保存内容そのものを厳密に再読込する。
                raw = JsonUtility.FromJson<CharacterJsonRaw>(json);
            }
            catch (Exception e)
            {
                throw new InvalidDataException("保存JSONの再読込検証に失敗しました", e);
            }
            if (raw == null || string.IsNullOrWhiteSpace(raw.character_name) ||
                !string.Equals(raw.character_name, expectedName, StringComparison.Ordinal))
                throw new InvalidDataException("保存JSONのキャラクター名を再読込できませんでした");
        }

        // 旧形式の不正改行を復旧したキャラを、起動直後に一度だけロスターで見せるための通知。
        public static string ConsumeLastRecoveredSpriteDir()
        {
            string result = _lastRecoveredSpriteDir;
            _lastRecoveredSpriteDir = null;
            return result;
        }

        static void WriteTextAtomically(string path, string contents)
        {
            string tempPath = path + ".tmp";
            string backupPath = path + ".bak";
            if (File.Exists(tempPath)) File.Delete(tempPath);
            File.WriteAllText(tempPath, contents, Encoding.UTF8);
            try
            {
                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, backupPath, true);
                    try { if (File.Exists(backupPath)) File.Delete(backupPath); }
                    catch (Exception e) { Debug.LogWarning("[Save] JSONバックアップ削除失敗: " + e.Message); }
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        static long NextSaveStamp()
        {
            lock (SaveStampLock)
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _lastSaveStamp = Math.Max(now, _lastSaveStamp + 1);
                return _lastSaveStamp;
            }
        }

        // 透過済みスプライトをPNGとして保存する（AIImageClientが保存済みの場合は不要）
        public static bool SaveSprites(CharacterData data)
        {
            if (data?.spriteSet == null || string.IsNullOrEmpty(data.spriteDir)) return false;
            try
            {
                Directory.CreateDirectory(data.spriteDir);
                data.spriteSet.EnsureCapacity();
                foreach (var (id, filename) in SpriteEntries)
                {
                    var sprite = data.spriteSet.sprites[(int)id];
                    if (sprite?.texture == null) continue;
                    byte[] png = ImageConversion.EncodeToPNG(sprite.texture);
                    File.WriteAllBytes(Path.Combine(data.spriteDir, filename + ".png"), png);
                }
                return File.Exists(Path.Combine(data.spriteDir, "idle1.png"));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] スプライト保存失敗: {e.Message}");
                return false;
            }
        }

        // 保存済みキャラから名前が一致する1件だけを探す（無ければnull）。
        // チュートリアル専用キャラ・固定ボスキャラなど、名前で特定の1体を参照したい場面向け。
        public static CharacterData LoadByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return LoadAll().Find(d => d != null && d.characterName == name);
        }

        static bool _defaultCharsSeeded;

        // ゲームが依存する必須キャラ（チュートリアル用「ケンゴ」「サヤ」・固定ボス「冥王ゾルバイン」）を
        // Assets/StreamingAssets/DefaultSavedChars に同梱し、初回起動時にpersistentDataPathへコピーする。
        // これにより他PCへ配布した場合でも、キャラを別途生成し直さなくてもそのまま同じ内容で遊べる。
        // 既にプレイヤー側に同名の保存データがある場合は上書きしない（プレイヤーの生成物を優先）。
        static void EnsureDefaultCharactersSeeded()
        {
            if (_defaultCharsSeeded) return;
            _defaultCharsSeeded = true;

            string srcRoot = Path.Combine(Application.streamingAssetsPath, "DefaultSavedChars");
            if (!Directory.Exists(srcRoot)) return;

            try
            {
                Directory.CreateDirectory(SaveDir);
                foreach (var srcJson in Directory.GetFiles(srcRoot, "*.json"))
                {
                    string id = Path.GetFileNameWithoutExtension(srcJson);
                    string destJson = Path.Combine(SaveDir, id + ".json");
                    if (File.Exists(destJson)) continue; // 既にプレイヤー側にあれば上書きしない

                    File.Copy(srcJson, destJson);

                    string srcSpritesDir = Path.Combine(srcRoot, id, "sprites");
                    if (!Directory.Exists(srcSpritesDir)) continue;
                    string destSpritesDir = Path.Combine(SaveDir, id, "sprites");
                    Directory.CreateDirectory(destSpritesDir);
                    foreach (var file in Directory.GetFiles(srcSpritesDir))
                        File.Copy(file, Path.Combine(destSpritesDir, Path.GetFileName(file)), overwrite: true);

                    string srcVoicesDir = Path.Combine(srcRoot, id, "voices");
                    if (Directory.Exists(srcVoicesDir))
                    {
                        string destVoicesDir = Path.Combine(SaveDir, id, "voices");
                        Directory.CreateDirectory(destVoicesDir);
                        foreach (var file in Directory.GetFiles(srcVoicesDir))
                            File.Copy(file, Path.Combine(destVoicesDir, Path.GetFileName(file)), overwrite: true);
                    }

                    Debug.Log($"[Save] 同梱デフォルトキャラをシードしました: {id}");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] デフォルトキャラのシード失敗: {e.Message}");
            }
        }

        // 保存済みキャラを全件ロードする。通常はIdle1プレビューも読む。
        // ロビー復帰時はloadPreviewSprites=falseで画像デコードを避け、呼び出し側が既存Idle1を再利用する。
        public static List<CharacterData> LoadAll(bool loadPreviewSprites = true)
        {
            EnsureDefaultCharactersSeeded();

            var results = new List<CharacterData>();
            if (!Directory.Exists(SaveDir)) return results;

            // 作成順（古い→新しい）に並べる。保存IDの末尾 "_<unixSeconds>" を作成順キーに使う。
            var files = new List<string>(Directory.GetFiles(SaveDir, "*.json"));
            files.Sort((a, b) => CreationOrderKey(a).CompareTo(CreationOrderKey(b)));

            foreach (var path in files)
            {
                try
                {
                    string originalJson = File.ReadAllText(path, Encoding.UTF8);
                    string json = SkillJsonParser.RepairRawControlCharactersInStrings(
                        originalJson, out bool repairedLegacyControls);
                    var data    = SkillJsonParser.Parse(json);
                    if (data == null) continue;

                    string id        = Path.GetFileNameWithoutExtension(path);
                    string spriteDir = Path.Combine(SaveDir, id, "sprites");
                    string voiceDir  = Path.Combine(SaveDir, id, "voices");
                    RecoverInterruptedVoiceSwap(voiceDir, data.voiceProfile);
                    data.spriteDir   = spriteDir;
                    data.voiceDir    = voiceDir;

                    // 旧保存処理がCRLFのCRを文字列内へ生で残したJSONは、解析成功後に同じIDへ
                    // 原子的に書き直す。画像・ボイス用ディレクトリはそのまま保持する。
                    if (repairedLegacyControls)
                    {
                        try
                        {
                            ValidateSerializedJson(json, data.characterName);
                            WriteTextAtomically(path, json);
                            _lastRecoveredSpriteDir = spriteDir;
                            Debug.Log($"[Save] 旧保存JSONの改行を修復: {Path.GetFileName(path)}");
                        }
                        catch (Exception repairError)
                        {
                            Debug.LogWarning($"[Save] 旧保存JSONの修復保存に失敗 ({Path.GetFileName(path)}): " +
                                repairError.Message);
                        }
                    }

                    // Idle1の絶対パスは常に保持し、必要な画面だけ非同期ロードできるようにする。
                    string idle1 = Path.Combine(spriteDir, "idle1.png");
                    if (File.Exists(idle1))
                    {
                        data.spritePath = idle1;
                    }
                    if (loadPreviewSprites && File.Exists(idle1))
                    {
                        data.characterSprite = SpriteLoader.LoadDirect(idle1);
                        if (data.characterSprite != null)
                        {
                            data.spriteSet ??= new CharacterSpriteSet();
                            data.spriteSet.Set(CharacterSpriteId.Idle1, data.characterSprite);
                        }
                    }

                    results.Add(data);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Save] 読み込み失敗 ({Path.GetFileName(path)}): {e.Message}");
                }
            }
            return results;
        }

        // ボイス一括差し替え中にアプリが終了しても、次回ロード時に完全な旧セットまたは
        // 完全な新セットを復旧する。5件未満のディレクトリは採用しない。
        static void RecoverInterruptedVoiceSwap(string targetDir, CharacterVoiceProfile persistedProfile)
        {
            try
            {
                string parentDir = Path.GetDirectoryName(targetDir);
                if (string.IsNullOrEmpty(parentDir) || !Directory.Exists(parentDir)) return;

                string[] backups = Directory.GetDirectories(parentDir, "voices_backup_*");
                string[] pending = Directory.GetDirectories(parentDir, "voices_pending_*");
                string swapMarkerPath = CharacterVoiceGenerator.GetVoiceSwapMarkerPath(targetDir);
                bool hasSwapMarker = !string.IsNullOrEmpty(swapMarkerPath) && File.Exists(swapMarkerPath);
                string pendingGenerationId = hasSwapMarker ? ReadVoiceSwapMarker(swapMarkerPath) : null;

                bool targetComplete = HasCompleteVoiceSet(targetDir);
                bool profileIsCurrent = persistedProfile != null && persistedProfile.generated &&
                    persistedProfile.qualityVersion >= CharacterVoiceProfile.CurrentQualityVersion;
                // markerが残る場合はq3フラグだけでなく今回の世代ID一致まで確認する。
                // これによりq3→q3再生成でも、JSON保存前と保存後を区別できる。
                bool metadataCommitted = hasSwapMarker
                    ? profileIsCurrent && !string.IsNullOrEmpty(pendingGenerationId) &&
                      string.Equals(persistedProfile.generationId, pendingGenerationId, StringComparison.Ordinal)
                    : profileIsCurrent;

                // 新セットをtargetへ移動した直後、q3 JSONの原子的保存より前に終了すると、
                // targetと完全な旧backupが両方残る。JSONが未確定なら旧セットを優先して戻す。
                if (targetComplete && !metadataCommitted)
                {
                    string oldSet = NewestCompleteDirectory(backups);
                    if (!string.IsNullOrEmpty(oldSet))
                    {
                        string displaced = Path.Combine(parentDir, "voices_incomplete_" + Guid.NewGuid().ToString("N"));
                        Directory.Move(targetDir, displaced);
                        try
                        {
                            Directory.Move(oldSet, targetDir);
                            Debug.LogWarning("[Save] JSON確定前に中断されたボイス差し替えから旧セットを復旧しました: " + targetDir);
                        }
                        catch
                        {
                            if (!Directory.Exists(targetDir) && Directory.Exists(displaced))
                                Directory.Move(displaced, targetDir);
                            throw;
                        }
                    }
                }

                targetComplete = HasCompleteVoiceSet(targetDir);
                if (!targetComplete)
                {
                    string candidate = NewestCompleteDirectory(backups) ?? NewestCompleteDirectory(pending);
                    if (!string.IsNullOrEmpty(candidate))
                    {
                        string displaced = null;
                        if (Directory.Exists(targetDir))
                        {
                            displaced = Path.Combine(parentDir, "voices_incomplete_" + Guid.NewGuid().ToString("N"));
                            Directory.Move(targetDir, displaced);
                        }

                        try
                        {
                            Directory.Move(candidate, targetDir);
                            targetComplete = true;
                            Debug.LogWarning("[Save] 中断されたボイス差し替えを復旧しました: " + targetDir);
                        }
                        catch
                        {
                            if (!Directory.Exists(targetDir) && !string.IsNullOrEmpty(displaced) && Directory.Exists(displaced))
                                Directory.Move(displaced, targetDir);
                            throw;
                        }
                    }
                }

                // 完全なtargetを確保できた場合だけ、残った作業ディレクトリを掃除する。
                if (!targetComplete) return;
                CleanupVoiceWorkDirectories(Directory.GetDirectories(parentDir, "voices_backup_*"), targetDir);
                CleanupVoiceWorkDirectories(Directory.GetDirectories(parentDir, "voices_pending_*"), targetDir);
                CleanupVoiceWorkDirectories(Directory.GetDirectories(parentDir, "voices_incomplete_*"), targetDir);
                if (hasSwapMarker)
                {
                    try { File.Delete(swapMarkerPath); }
                    catch (Exception e) { Debug.LogWarning("[Save] ボイス差し替えマーカー削除失敗: " + e.Message); }
                }
            }
            catch (Exception e)
            {
                // 復旧に失敗してもキャラ本体のロードは継続し、残ったbackupを次回再試行に残す。
                Debug.LogWarning("[Save] ボイス差し替え復旧失敗: " + e.Message);
            }
        }

        static string ReadVoiceSwapMarker(string path)
        {
            try { return File.ReadAllText(path, Encoding.UTF8).Trim(); }
            catch (Exception e)
            {
                // 読めないmarkerは未確定扱いにして、完全なbackupがあれば安全側の旧セットへ戻す。
                Debug.LogWarning("[Save] ボイス差し替えマーカー読込失敗: " + e.Message);
                return null;
            }
        }

        static bool HasCompleteVoiceSet(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return false;
            foreach (string filename in VoiceFilenames)
                if (!IsValidVoiceWav(Path.Combine(directory, filename + ".wav"))) return false;
            return true;
        }

        static bool IsValidVoiceWav(string path)
        {
            try
            {
                var info = new FileInfo(path);
                // キャラ台詞は最大30秒想定。異常に大きいファイルを復旧検証で全読込しない。
                if (!info.Exists || info.Length <= 44 || info.Length > 16 * 1024 * 1024) return false;
                return AITTSClient.NormalizeAndValidateWav(File.ReadAllBytes(path));
            }
            catch { return false; }
        }

        static string NewestCompleteDirectory(string[] directories)
        {
            string newest = null;
            DateTime newestTime = DateTime.MinValue;
            if (directories == null) return null;
            foreach (string directory in directories)
            {
                if (CharacterVoiceGenerator.IsActiveWorkDirectory(directory)) continue;
                if (!HasCompleteVoiceSet(directory)) continue;
                DateTime modified = Directory.GetLastWriteTimeUtc(directory);
                if (newest == null || modified > newestTime)
                {
                    newest = directory;
                    newestTime = modified;
                }
            }
            return newest;
        }

        static void CleanupVoiceWorkDirectories(string[] directories, string targetDir)
        {
            if (directories == null) return;
            foreach (string directory in directories)
            {
                if (string.Equals(directory, targetDir, StringComparison.OrdinalIgnoreCase) ||
                    !Directory.Exists(directory) || CharacterVoiceGenerator.IsActiveWorkDirectory(directory)) continue;
                try { Directory.Delete(directory, true); }
                catch (Exception e) { Debug.LogWarning("[Save] ボイス作業フォルダ削除失敗: " + e.Message); }
            }
        }

        // 保存IDの末尾 "_<unixSeconds>" を作成順の並べ替えキーとして取り出す。
        // 取れない場合はファイルの作成時刻にフォールバックする。
        static long CreationOrderKey(string path)
        {
            string id = Path.GetFileNameWithoutExtension(path);
            int us = id.LastIndexOf('_');
            if (us >= 0 && us < id.Length - 1 && long.TryParse(id.Substring(us + 1), out long unix))
                return unix;
            try { return File.GetCreationTimeUtc(path).Ticks; } catch { return 0L; }
        }

        public static bool Delete(CharacterData data)
        {
            if (data == null || string.IsNullOrEmpty(data.spriteDir)) return false;

            try
            {
                string characterDir = Directory.GetParent(data.spriteDir)?.FullName;
                if (string.IsNullOrEmpty(characterDir)) return false;

                string id = Path.GetFileName(characterDir);
                string jsonPath = Path.Combine(SaveDir, id + ".json");
                if (File.Exists(jsonPath))
                    File.Delete(jsonPath);
                if (Directory.Exists(characterDir))
                    Directory.Delete(characterDir, true);

                PresetCharacterLoader.ClearCache();
                Debug.Log($"[Save] 削除完了: {id}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] 削除失敗: {e.Message}");
                return false;
            }
        }

        // ボス専用の追加技プール用ポーズ/エフェクト画像をロードする（extra_pose_0.png, extra_effect_0.png, ...）。
        // 通常キャラはファイルが存在しないため空リストを返す（無コスト・無変更）。
        public static void LoadExtraSprites(string spriteDir, System.Collections.Generic.List<Sprite> poses, System.Collections.Generic.List<Sprite> effects)
        {
            poses.Clear();
            effects.Clear();
            if (string.IsNullOrEmpty(spriteDir) || !Directory.Exists(spriteDir)) return;

            for (int i = 0; ; i++)
            {
                string posePath = Path.Combine(spriteDir, $"extra_pose_{i}.png");
                if (!File.Exists(posePath)) break;
                poses.Add(SpriteLoader.LoadDirect(posePath));

                string effectPath = Path.Combine(spriteDir, $"extra_effect_{i}.png");
                effects.Add(File.Exists(effectPath) ? SpriteLoader.LoadDirect(effectPath, centerPivot: true) : null);
            }
        }

        // バトル開始時に保存済みスプライトセットをフルロードする。
        public static CharacterSpriteSet LoadSpriteSet(string spriteDir)
        {
            if (string.IsNullOrEmpty(spriteDir) || !Directory.Exists(spriteDir)) return null;

            var set = new CharacterSpriteSet();
            bool anyLoaded = false;

            foreach (var (id, filename) in SpriteEntries)
            {
                string path = Path.Combine(spriteDir, filename + ".png");
                if (!File.Exists(path)) continue;
                var sprite = SpriteLoader.LoadDirect(path, IsEffectSprite(id));
                if (sprite == null) continue;
                set.Set(id, sprite);
                anyLoaded = true;
            }

            return anyLoaded ? set : null;
        }

        // 選択画面ではidleだけを遅延ロードするため、戦闘開始時にディスク上に存在する
        // pose/effectのうち未ロードのものだけを補完する。生成済みのメモリ上スプライトは上書きしない。
        public static bool LoadMissingSprites(CharacterData data)
        {
            if (data == null) return false;

            data.spriteSet ??= new CharacterSpriteSet();
            data.spriteSet.EnsureCapacity();
            bool anyLoaded = false;
            if (data.characterSprite != null &&
                data.spriteSet.Get(CharacterSpriteId.Idle1, null, fallbackToPrimary: false) == null)
            {
                data.spriteSet.Set(CharacterSpriteId.Idle1, data.characterSprite);
                anyLoaded = true;
            }
            if (!HasUnloadedSpriteFiles(data)) return anyLoaded;

            foreach (var (id, filename) in SpriteEntries)
            {
                int index = (int)id;
                if (data.spriteSet.sprites[index] != null) continue;

                string path = Path.Combine(data.spriteDir, filename + ".png");
                if (!File.Exists(path)) continue;
                Sprite sprite = SpriteLoader.LoadDirect(path, IsEffectSprite(id));
                if (sprite == null) continue;
                data.spriteSet.Set(id, sprite);
                anyLoaded = true;
            }

            if (data.characterSprite == null)
            {
                data.characterSprite = data.spriteSet.Get(
                    CharacterSpriteId.Idle1, null, fallbackToPrimary: false);
                if (data.characterSprite == null)
                {
                    string idlePath = Path.Combine(data.spriteDir, "idle1.png");
                    if (File.Exists(idlePath))
                    {
                        data.characterSprite = SpriteLoader.LoadDirect(idlePath);
                        if (data.characterSprite != null)
                            data.spriteSet.Set(CharacterSpriteId.Idle1, data.characterSprite);
                    }
                }
            }
            return anyLoaded;
        }

        public static bool HasUnloadedSpriteFiles(CharacterData data)
        {
            if (data == null || string.IsNullOrEmpty(data.spriteDir) || !Directory.Exists(data.spriteDir))
                return false;
            Sprite[] sprites = data.spriteSet?.sprites;
            foreach (var (id, filename) in SpriteEntries)
            {
                if (id == CharacterSpriteId.Idle1 && data.characterSprite != null) continue;
                string path = Path.Combine(data.spriteDir, filename + ".png");
                if (!File.Exists(path)) continue;

                int index = (int)id;
                if (sprites == null || index >= sprites.Length || sprites[index] == null)
                    return true;
            }
            return false;
        }

        // スプライトセットを1枚ずつフレーム分割で非同期ロードし、data へ反映する。
        // SpriteEntries の並び順（idle1/2/3 が先頭）により待機モーションが最優先で揃う。
        // 1フレームに大量の同期デコードが集中して起きるヒッチを防ぐ。
        // idleOnly=true なら idle1/2/3 のみ読む（pose/effect は戦闘開始時にロード）。
        public static IEnumerator LoadSpriteSetAsync(CharacterData data, bool idleOnly = false)
        {
            if (data == null || string.IsNullOrEmpty(data.spriteDir) || !Directory.Exists(data.spriteDir))
                yield break;

            var set = data.spriteSet ?? new CharacterSpriteSet();
            data.spriteSet = set; // idle1 フォールバックで即アニメ可能にするため先に公開

            int count = idleOnly ? 3 : SpriteEntries.Length; // 先頭3つが idle1/2/3
            for (int i = 0; i < count; i++)
            {
                var (id, filename) = SpriteEntries[i];
                if (set.sprites != null && (int)id < set.sprites.Length && set.sprites[(int)id] != null)
                    continue; // 既にロード済み

                string path = Path.Combine(data.spriteDir, filename + ".png");
                if (!File.Exists(path)) continue;

                Sprite sprite = null;
                yield return SpriteLoader.LoadDirectAsync(path, s => sprite = s, IsEffectSprite(id));
                if (sprite == null) continue;

                set.Set(id, sprite);
                if (data.characterSprite == null && id == CharacterSpriteId.Idle1)
                    data.characterSprite = sprite;
            }
        }

        static string Serialize(CharacterData d)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"character_name\": {Q(d.characterName)},");
            sb.AppendLine($"  \"input_features\": {Q(d.inputFeatures)},");
            sb.AppendLine($"  \"base_visual_prompt\": {Q(d.visualPrompt)},");
            sb.AppendLine($"  \"visual_description\": {Q(d.visualDescription)},");
            sb.AppendLine($"  \"catch_copy\": {Q(d.catchCopy)},");
            d.voiceProfile ??= new CharacterVoiceProfile();
            d.voiceProfile.FillDefaults(d);
            sb.AppendLine("  \"voice_profile\": {");
            sb.AppendLine($"    \"preset\": {Q(d.voiceProfile.preset)},");
            sb.AppendLine($"    \"voice_gender\": {Q(d.voiceProfile.voiceGender)},");
            sb.AppendLine($"    \"voice_age\": {Q(d.voiceProfile.voiceAge)},");
            sb.AppendLine($"    \"voice_pitch\": {Q(d.voiceProfile.voicePitch)},");
            sb.AppendLine($"    \"voice_style\": {Q(d.voiceProfile.voiceStyle)},");
            sb.AppendLine($"    \"voice_variant\": {d.voiceProfile.voiceVariant},");
            sb.AppendLine($"    \"quality_version\": {d.voiceProfile.qualityVersion},");
            sb.AppendLine($"    \"voice_generation_id\": {Q(d.voiceProfile.generationId)},");
            sb.AppendLine($"    \"instructions\": {Q(d.voiceProfile.instructions)},");
            sb.AppendLine($"    \"intro_line\": {Q(d.voiceProfile.introLine)},");
            sb.AppendLine($"    \"skill_lines\": [{Q(d.voiceProfile.skillLines[0])}, {Q(d.voiceProfile.skillLines[1])}, {Q(d.voiceProfile.skillLines[2])}, {Q(d.voiceProfile.skillLines[3])}],");
            sb.AppendLine($"    \"generated\": {(d.voiceProfile.generated ? "true" : "false")}");
            sb.AppendLine("  },");
            sb.AppendLine($"  \"stats\": {{\"maxHP\": {d.stats.maxHP}, \"groundMoveSpeed\": {d.stats.groundMoveSpeed}, \"airMoveSpeed\": {d.stats.airMoveSpeed}, \"jumpForce\": {d.stats.jumpForce}, \"airJumpHeightMultiplier\": {d.stats.airJumpHeightMultiplier}, \"walkSpeedRatio\": {d.stats.walkSpeedRatio}, \"guardDurability\": {d.stats.guardDurability}, \"lightness\": {d.stats.lightness}, \"weight\": {d.stats.weight}, \"groundDodgeDistance\": {d.stats.groundDodgeDistance}, \"airDodgeDistance\": {d.stats.airDodgeDistance}}},");
            sb.AppendLine("  \"skills\": [");

            bool firstSkill = true;
            foreach (var skill in d.skills)
            {
                if (skill == null) continue;
                if (!firstSkill) sb.AppendLine(",");
                firstSkill = false;
                AppendSkill(sb, skill);
            }
            sb.AppendLine();
            sb.AppendLine("  ],");

            if (d.extraSkills != null && d.extraSkills.Count > 0)
            {
                sb.AppendLine("  \"extra_skills\": [");
                bool firstExtra = true;
                foreach (var skill in d.extraSkills)
                {
                    if (skill == null) continue;
                    if (!firstExtra) sb.AppendLine(",");
                    firstExtra = false;
                    AppendSkill(sb, skill);
                }
                sb.AppendLine();
                sb.AppendLine("  ],");
            }

            sb.AppendLine($"  \"grab_parameters\": {{\"range\": {d.grabParameters.range}, \"startup\": {d.grabParameters.startup}, \"recovery\": {d.grabParameters.recovery}}},");
            sb.AppendLine($"  \"throw_parameters\": {{\"front_damage\": {d.throwParameters.front_damage}, \"front_knockback\": {d.throwParameters.front_knockback}, \"back_damage\": {d.throwParameters.back_damage}, \"back_knockback\": {d.throwParameters.back_knockback}, \"up_damage\": {d.throwParameters.up_damage}, \"up_knockback\": {d.throwParameters.up_knockback}}}");
            sb.Append("}");
            return sb.ToString();
        }

        static string VoiceDirFromSpriteDir(string spriteDir)
        {
            if (string.IsNullOrEmpty(spriteDir)) return null;
            string characterDir = Directory.GetParent(spriteDir)?.FullName;
            return string.IsNullOrEmpty(characterDir) ? null : Path.Combine(characterDir, "voices");
        }

        static void AppendSkill(StringBuilder sb, SkillData s)
        {
            var p = s.parameters;
            sb.AppendLine("    {");
            sb.AppendLine($"      \"slot\": {Q(SlotStr(s.slot))},");
            sb.AppendLine($"      \"skill_name\": {Q(s.skill_name)},");
            sb.AppendLine($"      \"description\": {Q(s.description)},");
            sb.AppendLine($"      \"element\": {Q(ElemStr(s.element))},");
            sb.AppendLine($"      \"risk_level\": {Q(RiskStr(s.risk_level))},");
            sb.AppendLine($"      \"parameters\": {{");
            sb.AppendLine($"        \"damage\": {p.damage}, \"hit_count\": {p.hit_count}, \"range\": {p.range},");
            sb.AppendLine($"        \"startup\": {p.startup}, \"active_time\": {p.active_time}, \"recovery\": {p.recovery},");
            sb.AppendLine($"        \"knockback\": {p.knockback}, \"stun_time\": {p.stun_time},");
            sb.AppendLine($"        \"guard_damage\": {p.guard_damage}, \"move_force\": {p.move_force}");
            sb.AppendLine("      },");
            sb.AppendLine("      \"actions\": [");

            bool first = true;
            foreach (var a in s.actions)
            {
                if (!first) sb.AppendLine(",");
                first = false;
                AppendAction(sb, a);
            }
            sb.AppendLine();
            sb.AppendLine("      ],");
            sb.AppendLine($"      \"chargeable\": {(s.chargeable ? "true" : "false")},");
            sb.AppendLine($"      \"max_charge_time\": {s.max_charge_time},");
            if (s.follow_up_actions != null && s.follow_up_actions.Count > 0)
            {
                sb.AppendLine("      \"follow_up_actions\": [");
                bool firstFollow = true;
                foreach (var a in s.follow_up_actions)
                {
                    if (!firstFollow) sb.AppendLine(",");
                    firstFollow = false;
                    AppendAction(sb, a);
                }
                sb.AppendLine();
                sb.AppendLine("      ],");
                sb.AppendLine($"      \"follow_up_window\": {s.follow_up_window}");
            }
            else
            {
                sb.AppendLine("      \"follow_up_actions\": [],");
                sb.AppendLine($"      \"follow_up_window\": {s.follow_up_window}");
            }
            sb.Append("    }");
        }

        static void AppendAction(StringBuilder sb, SkillAction a)
        {
            sb.Append($"        {{\"type\":{Q(a.type)},\"time\":{a.time}");
            if (!string.IsNullOrEmpty(a.direction)) sb.Append($",\"direction\":{Q(a.direction)}");
            if (a.power > 0f)                        sb.Append($",\"power\":{a.power}");
            if (a.range > 0f)                        sb.Append($",\"range\":{a.range}");
            // 新しい空間形式では負方向・0も意味を持つ。未指定の旧形式は従来どおり正値だけを保存する。
            bool explicitSpatial = !string.IsNullOrEmpty(a.spawn_origin) || !string.IsNullOrEmpty(a.spawn_anchor);
            if (explicitSpatial || a.spawn_x > 0f)
                                                        sb.Append($",\"spawn_x\":{a.spawn_x}");
            if (!Mathf.Approximately(a.spawn_y, 0f)) sb.Append($",\"spawn_y\":{a.spawn_y}");
            if (a.size_x > 0f)                       sb.Append($",\"size_x\":{a.size_x}");
            if (a.size_y > 0f)                       sb.Append($",\"size_y\":{a.size_y}");
            if (a.hit_count > 0)                     sb.Append($",\"hit_count\":{a.hit_count}");
            if (a.follow_owner)                      sb.Append(",\"follow_owner\":true");
            if (a.player_controlled)                 sb.Append(",\"player_controlled\":true");
            if (a.hide_effect)                       sb.Append(",\"hide_effect\":true");
            if (a.repeat_count > 1)                  sb.Append($",\"repeat_count\":{a.repeat_count}");
            if (a.repeat_interval > 0f)              sb.Append($",\"repeat_interval\":{a.repeat_interval}");
            if (!string.IsNullOrEmpty(a.condition))  sb.Append($",\"condition\":{Q(a.condition)}");
            if (a.condition_value > 0f)              sb.Append($",\"condition_value\":{a.condition_value}");
            if (!Mathf.Approximately(a.knockback_x, 0f)) sb.Append($",\"knockback_x\":{a.knockback_x}");
            if (!Mathf.Approximately(a.knockback_y, 0f)) sb.Append($",\"knockback_y\":{a.knockback_y}");
            if (!string.IsNullOrEmpty(a.knockback_direction)) sb.Append($",\"knockback_direction\":{Q(a.knockback_direction)}");
            if (!string.IsNullOrEmpty(a.spawn_origin)) sb.Append($",\"spawn_origin\":{Q(a.spawn_origin)}");
            if (!string.IsNullOrEmpty(a.spawn_anchor)) sb.Append($",\"spawn_anchor\":{Q(a.spawn_anchor)}");
            if (!string.IsNullOrEmpty(a.aim_mode))     sb.Append($",\"aim_mode\":{Q(a.aim_mode)}");
            if (!Mathf.Approximately(a.vector_x, 0f))  sb.Append($",\"vector_x\":{a.vector_x}");
            if (!Mathf.Approximately(a.vector_y, 0f))  sb.Append($",\"vector_y\":{a.vector_y}");
            if (!Mathf.Approximately(a.rotation_angle, 0f)) sb.Append($",\"rotation_angle\":{a.rotation_angle}");
            if (!string.IsNullOrEmpty(a.pattern))      sb.Append($",\"pattern\":{Q(a.pattern)}");
            if (a.pattern_count > 0)                   sb.Append($",\"pattern_count\":{a.pattern_count}");
            if (a.pattern_spacing > 0f)                sb.Append($",\"pattern_spacing\":{a.pattern_spacing}");
            if (a.pattern_radius > 0f)                 sb.Append($",\"pattern_radius\":{a.pattern_radius}");
            if (a.burst_interval > 0f)                 sb.Append($",\"burst_interval\":{a.burst_interval}");
            if (a.telegraph_time > 0f)                 sb.Append($",\"telegraph_time\":{a.telegraph_time}");
            if (a.inner_radius > 0f)                   sb.Append($",\"inner_radius\":{a.inner_radius}");
            if (a.arc_angle > 0f)                      sb.Append($",\"arc_angle\":{a.arc_angle}");
            if (a.projectile_speed > 0f)             sb.Append($",\"projectile_speed\":{a.projectile_speed}");
            if (a.projectile_lifetime > 0f)          sb.Append($",\"projectile_lifetime\":{a.projectile_lifetime}");
            if (!Mathf.Approximately(a.projectile_angle, 0f)) sb.Append($",\"projectile_angle\":{a.projectile_angle}");
            if (a.homing)                            sb.Append(",\"homing\":true");
            if (!Mathf.Approximately(a.homing_strength, 0f)) sb.Append($",\"homing_strength\":{a.homing_strength}");
            if (a.boomerang)                         sb.Append(",\"boomerang\":true");
            if (a.projectile_count > 1)               sb.Append($",\"projectile_count\":{a.projectile_count}");
            if (a.spread_angle > 0f)                  sb.Append($",\"spread_angle\":{a.spread_angle}");
            if (!Mathf.Approximately(a.gravity_scale, 0f)) sb.Append($",\"gravity_scale\":{a.gravity_scale}");
            // 拡張バリエーション（保存しないと再読み込みで跳弾・爆発などの挙動が消える）
            if (a.explosion_radius > 0f)              sb.Append($",\"explosion_radius\":{a.explosion_radius}");
            if (a.bounce_count > 0)                   sb.Append($",\"bounce_count\":{a.bounce_count}");
            if (a.wave_amplitude > 0f)                sb.Append($",\"wave_amplitude\":{a.wave_amplitude}");
            if (a.pierce)                             sb.Append(",\"pierce\":true");
            if (a.split_count > 0)                    sb.Append($",\"split_count\":{a.split_count}");
            if (a.split_angle > 0f)                   sb.Append($",\"split_angle\":{a.split_angle}");
            if (a.orbit)                              sb.Append(",\"orbit\":true");
            if (a.spawn_at_enemy)                     sb.Append(",\"spawn_at_enemy\":true");
            if (!string.IsNullOrEmpty(a.shape))       sb.Append($",\"shape\":{Q(a.shape)}");
            if (a.lifesteal_ratio > 0f)               sb.Append($",\"lifesteal_ratio\":{a.lifesteal_ratio}");
            if (!string.IsNullOrEmpty(a.status))     sb.Append($",\"status\":{Q(a.status)},\"duration\":{a.duration},\"status_duration\":{a.status_duration},\"chance\":{a.chance}");
            else if (a.duration > 0f)                sb.Append($",\"duration\":{a.duration}"); // trap/summon等の寿命（statusなしでも保持）
            if (a.damage_override >= 0f)             sb.Append($",\"damage_override\":{a.damage_override}");
            sb.Append("}");
        }

        static string Q(string s)
        {
            if (s == null) return "\"\"";
            var escaped = new StringBuilder(s.Length + 8);
            escaped.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"':  escaped.Append("\\\""); break;
                    case '\\': escaped.Append("\\\\"); break;
                    case '\b': escaped.Append("\\b"); break;
                    case '\f': escaped.Append("\\f"); break;
                    case '\n': escaped.Append("\\n"); break;
                    case '\r': escaped.Append("\\r"); break;
                    case '\t': escaped.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            escaped.Append("\\u");
                            escaped.Append(((int)c).ToString("x4"));
                        }
                        else escaped.Append(c);
                        break;
                }
            }
            escaped.Append('"');
            return escaped.ToString();
        }

        static string SlotStr(SkillSlot s) => s switch {
            SkillSlot.AttackA   => "attack_a",
            SkillSlot.AttackB   => "attack_b",
            SkillSlot.AttackC   => "attack_c",
            SkillSlot.SmashSide => "smash_side",
            _                   => "attack_a",
        };
        static string ElemStr(Element e) => e switch {
            Element.Physical  => "physical",
            Element.Fire      => "fire",
            Element.Ice       => "ice",
            Element.Lightning => "lightning",
            Element.Dark      => "dark",
            Element.Wind      => "wind",
            _                 => "none",
        };
        static string RiskStr(RiskLevel r) => r switch {
            RiskLevel.Low     => "low",
            RiskLevel.Medium  => "medium",
            RiskLevel.High    => "high",
            RiskLevel.Extreme => "extreme",
            _                 => "medium",
        };

        static string SanitizeId(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name)
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
            return sb.Length > 0 ? sb.ToString() : "char";
        }
    }
}
