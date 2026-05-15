using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using PromptFighters.Battle.Skills;
using PromptFighters.Utils;

namespace PromptFighters.AI
{
    // OpenAI Images API でキャラクタースプライトセットを生成する。
    // ベース画像(Idle1)を /v1/images/generations で生成後、残り14枚を /v1/images/edits で並列生成する。
    public static class AIImageClient
    {
        const string GenerationsEndpoint = "https://api.openai.com/v1/images/generations";
        const string EditsEndpoint = "https://api.openai.com/v1/images/edits";
        const string Model = "gpt-image-2";
        const string CharacterSize = "1024x1536";
        const string EffectSize = "1536x1024";
        const string Quality = "low";

        static string _cachedApiKey;

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
        const string EffectSuffix = "wide horizontal 2D game visual effect only, no character figure, no text, flat chroma key green background (#00FF00), bright energetic colors, centered in frame";

        // (id, filename, editPrompt) — ベース画像を参照して生成する14枚のバリエーション
        static readonly (CharacterSpriteId id, string filename, string prompt)[] EditEntries =
        {
            (CharacterSpriteId.Idle2,      "idle2",       $"slightly different weight shift idle pose, same character, {CharSuffix}"),
            (CharacterSpriteId.Idle3,      "idle3",       $"lively idle pose with slight arm movement, same character, {CharSuffix}"),
            (CharacterSpriteId.Jump,       "jump",        $"jumping airborne pose feet off ground, same character, {CharSuffix}"),
            (CharacterSpriteId.Damage,     "damage",      $"hurt recoil reaction flinching backward, same character, {CharSuffix}"),
            (CharacterSpriteId.Grab,       "grab",        $"grabbing grappling reach-out pose arms extended forward right, same character, {CharSuffix}"),
            (CharacterSpriteId.Dash,       "dash",        $"fast dashing sprint pose leaning forward to the right, same character, {CharSuffix}"),
            (CharacterSpriteId.AttackA,    "attack_a",    $"attack A punching or slashing to the right, same character, {CharSuffix}"),
            (CharacterSpriteId.AttackB,    "attack_b",    $"attack B projectile launch aiming to the right, same character, {CharSuffix}"),
            (CharacterSpriteId.AttackC,    "attack_c",    $"attack C special technique toward the right, same character, {CharSuffix}"),
            (CharacterSpriteId.SmashSide,  "smash_side",  $"powerful side smash heavy swing to the right, same character, {CharSuffix}"),
            (CharacterSpriteId.EffectA,    "effect_a",    $"attack A visual effect, {EffectSuffix}"),
            (CharacterSpriteId.EffectB,    "effect_b",    $"projectile visual effect, {EffectSuffix}"),
            (CharacterSpriteId.EffectC,    "effect_c",    $"special attack visual effect, {EffectSuffix}"),
            (CharacterSpriteId.EffectSmash,"effect_smash",$"large powerful smash effect, {EffectSuffix}"),
        };

        // saveDir: PNG保存先ディレクトリ（null なら保存しない）
        public static Coroutine GenerateSpriteSet(MonoBehaviour runner,
            string baseVisualPrompt,
            Action<string> onProgress,
            Action<CharacterSpriteSet> onSuccess,
            Action<string> onError,
            string saveDir = null)
        {
            return runner.StartCoroutine(
                GenerateSpriteSetCoroutine(runner, baseVisualPrompt, onProgress, onSuccess, onError, saveDir));
        }

        static IEnumerator GenerateSpriteSetCoroutine(
            MonoBehaviour runner,
            string baseVisualPrompt,
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

            yield return GenerateBaseCoroutine(baseVisualPrompt, key,
                (sprite, rawBytes) => { baseSprite = sprite; baseRawBytes = rawBytes; },
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

            // Step 2: 残り14枚を並列生成 (images/edits)
            onProgress?.Invoke($"バリエーション画像を並列生成中... (14枚)");
            int pending = EditEntries.Length;

            foreach (var (id, filename, editPrompt) in EditEntries)
            {
                // baseVisualPrompt（外見説明）+ ポーズ指示（CharSuffixを既に含む）
                string fullPrompt = baseVisualPrompt + ", " + editPrompt;
                runner.StartCoroutine(GenerateEditCoroutine(
                    id, filename, fullPrompt, baseRawBytes, key, saveDir,
                    (spriteId, fname, sprite) =>
                    {
                        set.Set(spriteId, sprite);
                        pending--;
                        onProgress?.Invoke($"生成完了: {fname} (残り {pending} 枚)");
                    },
                    (spriteId, err) =>
                    {
                        Debug.LogWarning($"[AIImage] {spriteId} 生成失敗（Idle1で代替）: {err}");
                        set.Set(spriteId, baseSprite);
                        pending--;
                    }));
            }

            yield return new WaitUntil(() => pending == 0);

            onSuccess?.Invoke(set);
        }

        // /v1/images/generations でベース画像を生成し、(Sprite, rawBytes) を返す
        static IEnumerator GenerateBaseCoroutine(
            string basePrompt, string key,
            Action<Sprite, byte[]> onSuccess, Action<string> onError)
        {
            string safePrompt = EscapeForJson(
                basePrompt + $", standing idle, {CharSuffix}");
            string body =
                $"{{\"model\":\"{Model}\"," +
                $"\"prompt\":\"{safePrompt}\"," +
                $"\"n\":1,\"size\":\"{CharacterSize}\",\"quality\":\"{Quality}\"}}";

            using var req = new UnityWebRequest(GenerationsEndpoint, "POST");
            req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + key);
            req.timeout = 120;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"{req.error}: {req.downloadHandler?.text}");
                yield break;
            }

            // try-catch の中に yield return は置けないため、レスポンス解析と URL DL を分離する
            string imageUrl = null;
            string imageBase64 = null;
            try
            {
                ParseImageResponse(req.downloadHandler.text, out imageUrl, out imageBase64);
            }
            catch (Exception e)
            {
                onError?.Invoke("レスポンス解析失敗: " + e.Message);
                yield break;
            }

            byte[] rawBytes = null;

            if (!string.IsNullOrEmpty(imageBase64))
            {
                try { rawBytes = Convert.FromBase64String(imageBase64); }
                catch (Exception e) { onError?.Invoke("Base64デコード失敗: " + e.Message); yield break; }
            }
            else if (!string.IsNullOrEmpty(imageUrl))
            {
                // URL ダウンロードは try-catch の外で yield return する
                var imgReq = UnityWebRequestTexture.GetTexture(imageUrl);
                imgReq.timeout = 60;
                yield return imgReq.SendWebRequest();
                if (imgReq.result != UnityWebRequest.Result.Success)
                {
                    string err = imgReq.error;
                    imgReq.Dispose();
                    onError?.Invoke("URL画像のダウンロード失敗: " + err);
                    yield break;
                }
                var urlTex = DownloadHandlerTexture.GetContent(imgReq);
                rawBytes = ImageConversion.EncodeToPNG(urlTex);
                imgReq.Dispose();
            }
            else
            {
                onError?.Invoke("レスポンスにurl/b64_jsonが見つかりません");
                yield break;
            }

            try
            {
                var sprite = RawBytesToSprite(rawBytes);
                onSuccess?.Invoke(sprite, rawBytes);
            }
            catch (Exception e)
            {
                onError?.Invoke("画像変換失敗: " + e.Message);
            }
        }

        // /v1/images/edits でベース画像を参照してバリエーションを生成する
        static IEnumerator GenerateEditCoroutine(
            CharacterSpriteId id, string filename, string prompt,
            byte[] basePngBytes, string key, string saveDir,
            Action<CharacterSpriteId, string, Sprite> onSuccess,
            Action<CharacterSpriteId, string> onError)
        {
            var form = new List<IMultipartFormSection>
            {
                new MultipartFormDataSection("model",   Model),
                new MultipartFormDataSection("prompt",  prompt),
                new MultipartFormDataSection("size",    IsEffectSprite(id) ? EffectSize : CharacterSize),
                new MultipartFormDataSection("quality", Quality),
                new MultipartFormDataSection("n",       "1"),
                new MultipartFormFileSection("image[]", basePngBytes, "reference.png", "image/png"),
            };

            using var req = UnityWebRequest.Post(EditsEndpoint, form);
            req.SetRequestHeader("Authorization", "Bearer " + key);
            req.timeout = 180;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(id, $"{req.error}: {req.downloadHandler?.text}");
                yield break;
            }

            try
            {
                ParseImageResponse(req.downloadHandler.text, out string url, out string b64);
                if (string.IsNullOrEmpty(b64))
                {
                    onError?.Invoke(id, "b64_jsonが見つかりません");
                    yield break;
                }

                byte[] rawBytes = Convert.FromBase64String(b64);
                var sprite = RawBytesToSprite(rawBytes);

                if (saveDir != null)
                    TrySavePng(saveDir, filename, sprite);

                onSuccess?.Invoke(id, filename, sprite);
            }
            catch (Exception e)
            {
                onError?.Invoke(id, "画像処理失敗: " + e.Message);
            }
        }

        // バイト列 → WhiteBackgroundRemover適用 → Sprite
        // threshold=0.94: 純白に近い画素のみ除去。キャラの肌・明るい服は保護される。
        static Sprite RawBytesToSprite(byte[] rawBytes)
        {
            var raw = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(raw, rawBytes))
                throw new Exception("Texture2D.LoadImage failed");

            var processed = WhiteBackgroundRemover.ApplyChromaGreen(raw);
            UnityEngine.Object.Destroy(raw);

            return Sprite.Create(
                processed,
                new Rect(0, 0, processed.width, processed.height),
                new Vector2(0.5f, 0f),
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

        static string EscapeForJson(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "");

        static bool IsEffectSprite(CharacterSpriteId id) =>
            id == CharacterSpriteId.EffectA ||
            id == CharacterSpriteId.EffectB ||
            id == CharacterSpriteId.EffectC ||
            id == CharacterSpriteId.EffectSmash;
    }
}
