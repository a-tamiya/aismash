using System.IO;
using UnityEngine;

namespace PromptFighters.Utils
{
    // ファイルパスからSprite（透過処理済み）を生成する。
    // Phase 4のAI画像ロードと、テスト画像ロードで共用する。
    public static class SpriteLoader
    {
        // path: 絶対パス or StreamingAssets からの相対パス (例: "Sprites/test.jpg")
        public static Sprite LoadWithWhiteBgRemoved(string path,
                                                    float threshold = 0.88f,
                                                    float fadeRange = 0.10f)
        {
            string fullPath = Path.IsPathRooted(path)
                ? path
                : Path.Combine(Application.streamingAssetsPath, path);

            if (!File.Exists(fullPath))
            {
                Debug.LogWarning($"[SpriteLoader] ファイルが見つかりません: {fullPath}");
                return null;
            }

            byte[] bytes = File.ReadAllBytes(fullPath);
            var raw = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (!ImageConversion.LoadImage(raw, bytes))
            {
                Debug.LogWarning($"[SpriteLoader] 画像の読み込みに失敗しました: {fullPath}");
                return null;
            }

            Texture2D processed = WhiteBackgroundRemover.Apply(raw, threshold, fadeRange);
            Object.Destroy(raw); // 元テクスチャを解放

            var sprite = Sprite.Create(
                processed,
                new Rect(0, 0, processed.width, processed.height),
                new Vector2(0.5f, 0f), // ピボット: 横中央・下端
                processed.height / 2f  // PPU: キャラが2Unityユニット高さになるよう調整
            );
            return sprite;
        }

        // 透過済みPNGをWhiteBackgroundRemoverなしで直接ロードする（保存済みスプライト用）
        public static Sprite LoadDirect(string path)
        {
            string fullPath = Path.IsPathRooted(path)
                ? path
                : Path.Combine(Application.streamingAssetsPath, path);

            if (!File.Exists(fullPath))
            {
                Debug.LogWarning($"[SpriteLoader] ファイルが見つかりません: {fullPath}");
                return null;
            }

            byte[] bytes = File.ReadAllBytes(fullPath);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, bytes))
            {
                Debug.LogWarning($"[SpriteLoader] 画像の読み込みに失敗しました: {fullPath}");
                return null;
            }

            return Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0f),
                tex.height / 2f);
        }

        // バイト列から直接ロード（Phase 4 API連携用）
        public static Sprite LoadFromBytesWithWhiteBgRemoved(byte[] bytes,
                                                             float threshold = 0.88f,
                                                             float fadeRange = 0.10f)
        {
            var raw = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (!ImageConversion.LoadImage(raw, bytes)) return null;

            Texture2D processed = WhiteBackgroundRemover.Apply(raw, threshold, fadeRange);
            Object.Destroy(raw);

            var sprite = Sprite.Create(
                processed,
                new Rect(0, 0, processed.width, processed.height),
                new Vector2(0.5f, 0f),
                processed.height / 2f
            );
            return sprite;
        }
    }
}
