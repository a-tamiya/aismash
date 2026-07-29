using System.IO;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace PromptFighters.Utils
{
    // ファイルパスからSprite（透過処理済み）を生成する。
    // Phase 4のAI画像ロードと、テスト画像ロードで共用する。
    // 同期版に加え、ディスク読み込みをバックグラウンドスレッドへ逃がすコルーチン版を提供する
    // （Texture2D/Sprite生成はUnity API制約によりメインスレッドで実行）。
    public static class SpriteLoader
    {
        // path: 絶対パス or StreamingAssets からの相対パス (例: "Sprites/test.jpg")
        public static Sprite LoadWithWhiteBgRemoved(string path,
                                                    float threshold = 0.88f,
                                                    float fadeRange = 0.10f)
        {
            byte[] bytes = ReadAllBytesOrWarn(path);
            if (bytes == null) return null;
            return LoadFromBytesWithWhiteBgRemoved(bytes, threshold, fadeRange);
        }

        // 透過済みPNGをWhiteBackgroundRemoverなしで直接ロードする（保存済みスプライト用）
        public static Sprite LoadDirect(string path, bool centerPivot = false)
        {
            byte[] bytes = ReadAllBytesOrWarn(path);
            if (bytes == null) return null;
            return BuildDirectSprite(bytes, centerPivot);
        }

        // バイト列から直接ロード（Phase 4 API連携用）
        public static Sprite LoadFromBytesWithWhiteBgRemoved(byte[] bytes,
                                                             float threshold = 0.88f,
                                                             float fadeRange = 0.10f)
        {
            var raw = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (!ImageConversion.LoadImage(raw, bytes))
            {
                Object.Destroy(raw);
                return null;
            }

            Texture2D processed = null;
            try
            {
                processed = WhiteBackgroundRemover.Apply(raw, threshold, fadeRange);
                if (processed == null) return null;

                return Sprite.Create(
                    processed,
                    new Rect(0, 0, processed.width, processed.height),
                    new Vector2(0.5f, 0f),
                    processed.height / 2f
                );
            }
            catch (System.Exception e)
            {
                if (processed != null) Object.Destroy(processed);
                Debug.LogWarning($"[SpriteLoader] 画像変換失敗: {e.Message}");
                return null;
            }
            finally
            {
                Object.Destroy(raw);
            }
        }

        // コルーチン版: File.ReadAllBytes をバックグラウンドスレッドで実行し、
        // 完了後にメインスレッドでSpriteを生成して onLoaded で返す。
        public static IEnumerator LoadWithWhiteBgRemovedAsync(string path,
                                                              System.Action<Sprite> onLoaded,
                                                              float threshold = 0.88f,
                                                              float fadeRange = 0.10f)
        {
            byte[] bytes = null;
            yield return ReadAllBytesAsync(path, b => bytes = b);
            onLoaded?.Invoke(bytes == null ? null : LoadFromBytesWithWhiteBgRemoved(bytes, threshold, fadeRange));
        }

        public static IEnumerator LoadDirectAsync(string path, System.Action<Sprite> onLoaded,
                                                  bool centerPivot = false)
        {
            byte[] bytes = null;
            yield return ReadAllBytesAsync(path, b => bytes = b);
            onLoaded?.Invoke(bytes == null ? null : BuildDirectSprite(bytes, centerPivot));
        }

        static Sprite BuildDirectSprite(byte[] bytes, bool centerPivot = false)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, bytes))
            {
                Object.Destroy(tex);
                return null;
            }

            try
            {
                return Sprite.Create(
                    tex,
                    new Rect(0, 0, tex.width, tex.height),
                    centerPivot ? new Vector2(0.5f, 0.5f) : new Vector2(0.5f, 0f),
                    tex.height / 2f);
            }
            catch (System.Exception e)
            {
                Object.Destroy(tex);
                Debug.LogWarning($"[SpriteLoader] Sprite生成失敗: {e.Message}");
                return null;
            }
        }

        static string ResolvePath(string path) => Path.IsPathRooted(path)
            ? path
            : Path.Combine(Application.streamingAssetsPath, path);

        static byte[] ReadAllBytesOrWarn(string path)
        {
            string fullPath = ResolvePath(path);
            if (!File.Exists(fullPath))
            {
                Debug.LogWarning($"[SpriteLoader] ファイルが見つかりません: {fullPath}");
                return null;
            }
            try
            {
                return File.ReadAllBytes(fullPath);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[SpriteLoader] 読み込み失敗: {fullPath} ({e.Message})");
                return null;
            }
        }

        // ディスク読み込みのみバックグラウンドスレッドへ逃がす。
        static IEnumerator ReadAllBytesAsync(string path, System.Action<byte[]> onRead)
        {
            string fullPath = ResolvePath(path);
            if (!File.Exists(fullPath))
            {
                Debug.LogWarning($"[SpriteLoader] ファイルが見つかりません: {fullPath}");
                onRead?.Invoke(null);
                yield break;
            }

            byte[] result = null;
            var task = Task.Run(() => result = File.ReadAllBytes(fullPath));
            while (!task.IsCompleted) yield return null;

            if (task.IsFaulted)
            {
                Debug.LogWarning($"[SpriteLoader] 読み込み失敗: {fullPath} ({task.Exception?.GetBaseException().Message})");
                onRead?.Invoke(null);
                yield break;
            }
            onRead?.Invoke(result);
        }
    }
}
