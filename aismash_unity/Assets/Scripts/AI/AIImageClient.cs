using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using PromptFighters.Battle.Skills;

namespace PromptFighters.AI
{
    // OpenAI Images API でキャラクター画像を生成する。
    // visual_prompt を受け取り、Sprite を返す。
    public static class AIImageClient
    {
        const string Endpoint = "https://api.openai.com/v1/images/generations";
        const string Model    = "gpt-image-1";
        const string Size     = "1024x1024";

        static string _cachedApiKey;

        public static string ApiKey
        {
            get
            {
                if (_cachedApiKey != null) return _cachedApiKey;
                _cachedApiKey = LoadApiKey();
                return _cachedApiKey;
            }
            set => _cachedApiKey = value;
        }

        static string LoadApiKey()
        {
            // 優先順位: 環境変数 → config.json
            string fromEnv = System.Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Trim();
            if (!string.IsNullOrEmpty(fromEnv)) return fromEnv;

            string path = System.IO.Path.Combine(Application.streamingAssetsPath, "config.json");
            if (!System.IO.File.Exists(path)) return "";
            try
            {
                string json = System.IO.File.ReadAllText(path);
                var cfg = JsonUtility.FromJson<Config>(json);
                return cfg?.openai_api_key?.Trim() ?? "";
            }
            catch (Exception e)
            {
                Debug.LogWarning("[AIImage] config.json 読み込みエラー: " + e.Message);
                return "";
            }
        }

        [Serializable] class Config { public string openai_api_key; }

        public static bool HasConfiguredApiKey(out string error)
        {
            error = null;
            if (IsConfiguredApiKey(ApiKey)) return true;
            error = "OpenAI APIキーが未設定です。環境変数 OPENAI_API_KEY または StreamingAssets/config.json の openai_api_key に実キーを設定してください。";
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

        static readonly (CharacterSpriteId id, string prompt)[] SpritePrompts =
        {
            (CharacterSpriteId.Idle1, "standing idle pose, full body, white background"),
            (CharacterSpriteId.Idle2, "same character, slightly different idle pose, full body, white background"),
            (CharacterSpriteId.Idle3, "same character, lively idle pose, full body, white background"),
            (CharacterSpriteId.Jump, "same character, jumping pose, full body, white background"),
            (CharacterSpriteId.Damage, "same character, hurt reaction pose, full body, white background"),
            (CharacterSpriteId.Grab, "same character, grabbing pose, full body, white background"),
            (CharacterSpriteId.Dash, "same character, fast dashing pose, full body, white background"),
            (CharacterSpriteId.AttackA, "same character, basic attack A action pose, full body, white background"),
            (CharacterSpriteId.AttackB, "same character, basic attack B action pose, full body, white background"),
            (CharacterSpriteId.AttackC, "same character, basic attack C action pose, full body, white background"),
            (CharacterSpriteId.SmashSide, "same character, powerful side smash attack pose, full body, white background"),
            (CharacterSpriteId.EffectA, "2D game visual effect for attack A only, no character, no text, transparent background"),
            (CharacterSpriteId.EffectB, "2D game visual effect for attack B only, no character, no text, transparent background"),
            (CharacterSpriteId.EffectC, "2D game visual effect for attack C only, no character, no text, transparent background"),
            (CharacterSpriteId.EffectSmash, "large 2D game visual effect for side smash only, no character, no text, transparent background"),
        };

        public static Coroutine Generate(MonoBehaviour runner,
            string visualPrompt,
            Action<Sprite> onSuccess, Action<string> onError)
        {
            return runner.StartCoroutine(GenerateCoroutine(visualPrompt, onSuccess, onError));
        }

        public static Coroutine GenerateSpriteSet(MonoBehaviour runner,
            string baseVisualPrompt,
            Action<string> onProgress,
            Action<CharacterSpriteSet> onSuccess,
            Action<string> onError)
        {
            return runner.StartCoroutine(GenerateSpriteSetCoroutine(baseVisualPrompt, onProgress, onSuccess, onError));
        }

        static IEnumerator GenerateSpriteSetCoroutine(
            string baseVisualPrompt,
            Action<string> onProgress,
            Action<CharacterSpriteSet> onSuccess,
            Action<string> onError)
        {
            if (!HasConfiguredApiKey(out string keyError))
            {
                onError?.Invoke(keyError);
                yield break;
            }

            var set = new CharacterSpriteSet();
            string firstError = null;

            for (int i = 0; i < SpritePrompts.Length; i++)
            {
                var item = SpritePrompts[i];
                onProgress?.Invoke($"スプライト生成中... {i + 1}/{SpritePrompts.Length}");
                bool done = false;
                Sprite generated = null;
                string error = null;
                string prompt = $"{baseVisualPrompt}, {item.prompt}";

                yield return GenerateCoroutine(prompt,
                    sprite => { generated = sprite; done = true; },
                    err => { error = err; done = true; });

                if (!done) yield return new WaitUntil(() => done);

                if (generated != null)
                {
                    set.Set(item.id, generated);
                    if (item.id == CharacterSpriteId.Idle1)
                    {
                        FillMissingCharacterSprites(set, generated);
                    }
                }
                else
                {
                    firstError ??= error;
                    Debug.LogWarning($"[AIImage] {item.id} の生成に失敗: {error}");
                }
            }

            if (set.Get(CharacterSpriteId.Idle1) == null)
            {
                onError?.Invoke(firstError ?? "Idle1画像の生成に失敗しました");
                yield break;
            }

            onSuccess?.Invoke(set);
        }

        static void FillMissingCharacterSprites(CharacterSpriteSet set, Sprite fallback)
        {
            for (int i = (int)CharacterSpriteId.Idle1; i <= (int)CharacterSpriteId.SmashSide; i++)
            {
                if (set.sprites[i] == null)
                    set.sprites[i] = fallback;
            }
        }

        static IEnumerator GenerateCoroutine(
            string visualPrompt,
            Action<Sprite> onSuccess, Action<string> onError)
        {
            string key = ApiKey;
            if (!IsConfiguredApiKey(key))
            {
                onError?.Invoke("OpenAI APIキーが未設定です。環境変数 OPENAI_API_KEY または StreamingAssets/config.json を確認してください。");
                yield break;
            }

            string safePrompt = (visualPrompt + ", white background, no text, no watermark")
                .Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", " ").Replace("\r", "");

            string body = $"{{\"model\":\"{Model}\",\"prompt\":\"{safePrompt}\",\"n\":1,\"size\":\"{Size}\"}}";

            using var req = new UnityWebRequest(Endpoint, "POST");
            req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + key);
            req.timeout = 60;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                string responseText = req.downloadHandler?.text;
                Debug.LogWarning($"[AIImage] 生成エラー: {req.error}\n{responseText}");
                onError?.Invoke($"{req.error}: {responseText}");
                yield break;
            }

            string imageUrl = null;
            string imageBase64 = null;
            try
            {
                ParseImageResponse(req.downloadHandler.text, out imageUrl, out imageBase64);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AIImage] レスポンス解析エラー: {e.Message}");
                onError?.Invoke("レスポンス解析失敗: " + e.Message);
                yield break;
            }

            if (!string.IsNullOrEmpty(imageBase64))
            {
                try
                {
                    byte[] bytes = Convert.FromBase64String(imageBase64);
                    var decodedTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!decodedTex.LoadImage(bytes))
                        throw new Exception("Texture2D.LoadImage failed");
                    onSuccess?.Invoke(Texture2ToSprite(decodedTex));
                }
                catch (Exception e)
                {
                    onError?.Invoke("base64画像の変換に失敗: " + e.Message);
                }
                yield break;
            }

            // 画像URLをダウンロードしてSpriteに変換
            using var imgReq = UnityWebRequestTexture.GetTexture(imageUrl);
            imgReq.timeout = 60;
            yield return imgReq.SendWebRequest();

            if (imgReq.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[AIImage] 画像DLエラー: {imgReq.error}");
                onError?.Invoke("画像ダウンロード失敗: " + imgReq.error);
                yield break;
            }

            var tex = DownloadHandlerTexture.GetContent(imgReq);
            var sprite = Texture2ToSprite(tex);
            onSuccess?.Invoke(sprite);
        }

        // {"data":[{"url":"..."}]} から url を取り出す
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

        static Sprite Texture2ToSprite(Texture2D tex)
        {
            return Sprite.Create(tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
