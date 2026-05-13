using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PromptFighters.AI
{
    // OpenAI DALL-E 3 でキャラクター画像を生成する。
    // visual_prompt を受け取り、Sprite を返す。
    public static class AIImageClient
    {
        const string Endpoint = "https://api.openai.com/v1/images/generations";
        const string Model    = "dall-e-3";
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
            string fromEnv = System.Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (!string.IsNullOrEmpty(fromEnv)) return fromEnv;

            string path = System.IO.Path.Combine(Application.streamingAssetsPath, "config.json");
            if (!System.IO.File.Exists(path)) return "";
            try
            {
                string json = System.IO.File.ReadAllText(path);
                var cfg = JsonUtility.FromJson<Config>(json);
                return cfg?.openai_api_key ?? "";
            }
            catch (Exception e)
            {
                Debug.LogWarning("[AIImage] config.json 読み込みエラー: " + e.Message);
                return "";
            }
        }

        [Serializable] class Config { public string openai_api_key; }

        public static Coroutine Generate(MonoBehaviour runner,
            string visualPrompt,
            Action<Sprite> onSuccess, Action<string> onError)
        {
            return runner.StartCoroutine(GenerateCoroutine(visualPrompt, onSuccess, onError));
        }

        static IEnumerator GenerateCoroutine(
            string visualPrompt,
            Action<Sprite> onSuccess, Action<string> onError)
        {
            string key = ApiKey;
            if (string.IsNullOrEmpty(key) || key == "YOUR_API_KEY_HERE")
            {
                onError?.Invoke("OpenAI APIキーが未設定です (StreamingAssets/config.json)");
                yield break;
            }

            string safePrompt = (visualPrompt + ", white background, no text, no watermark")
                .Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", " ").Replace("\r", "");

            string body = $"{{\"model\":\"{Model}\",\"prompt\":\"{safePrompt}\",\"n\":1,\"size\":\"{Size}\",\"response_format\":\"url\"}}";

            using var req = new UnityWebRequest(Endpoint, "POST");
            req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + key);
            req.timeout = 60;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[AIImage] 生成エラー: {req.error}\n{req.downloadHandler.text}");
                onError?.Invoke(req.error);
                yield break;
            }

            string imageUrl;
            try
            {
                imageUrl = ParseUrl(req.downloadHandler.text);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AIImage] レスポンス解析エラー: {e.Message}");
                onError?.Invoke("レスポンス解析失敗: " + e.Message);
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
        [Serializable] class ImgData { public string url; }

        static string ParseUrl(string json)
        {
            var resp = JsonUtility.FromJson<ImgResp>(json);
            if (resp?.data == null || resp.data.Length == 0 || string.IsNullOrEmpty(resp.data[0].url))
                throw new Exception("data[0].url が見つかりません");
            return resp.data[0].url;
        }

        static Sprite Texture2ToSprite(Texture2D tex)
        {
            return Sprite.Create(tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
