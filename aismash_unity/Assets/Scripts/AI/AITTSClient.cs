using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PromptFighters.AI
{
    public static class AITTSClient
    {
        const string Endpoint = "https://api.openai.com/v1/audio/speech";
        const string Model    = "tts-1";
        // 声の演技指示（instructions）に対応した表現力の高いTTSモデル。実況などの感情表現に使う。
        const string ExpressiveModel = "gpt-4o-mini-tts";
        public const string DefaultVoice    = "nova";
        public const string CommentaryVoice = "ash";     // エネルギッシュな男性寄り（gpt-4o-mini-tts対応）
        public const string AngelVoice      = "shimmer"; // 明るく軽やかな印象（ボイスボール用）
        public const float  CommentarySpeed = 1.15f;     // やや速めで迫力を出す（フォールバックのtts-1用）
        // 実況の声の演技指示。台本読みではなく「生の人間の声」に寄せる。
        // 熱い瞬間用：興奮した実況。つなぎ用：落ち着いた解説トーン。使い分けて人間らしさを出す。
        public const string CommentaryInstructionsExcited =
            "日本語のスポーツ実況アナウンサー本人として、目の前の試合に本気で驚き興奮している生の声で話す。" +
            "台本の読み上げには絶対に聞こえないように。自然な日本語のイントネーションで、早口だが聞き取りやすく、" +
            "決め所では声を張り上げ、語尾は叫びっぱなしにせず自然に抜く。息継ぎや間も人間らしく。";
        public const string CommentaryInstructionsCalm =
            "日本語のスポーツ実況アナウンサー本人として、試合の合間を落ち着いた解説トーンでつなぐ。" +
            "自然な会話の速さと抑揚で、台本読みに聞こえないように。静かな中にも試合への熱を感じさせる声で。";

        // 現在AI音声（実況・ボイスボール等）を再生中か。実況側が声の重なりを避けるために参照する。
        // 再生開始時に終了予定時刻を記録する方式（コルーチンが途中停止されてもフラグが残らない）。
        static float _speechEndTime;
        public static bool IsSpeaking => Time.unscaledTime < _speechEndTime;

        public static Coroutine Speak(MonoBehaviour runner, string text,
            AudioSource audioSource,
            Action onComplete = null,
            Action<string> onError = null,
            string voice = DefaultVoice,
            float speed = 1f,
            float volume = 1f,
            string instructions = null)
        {
            return runner.StartCoroutine(SpeakCoroutine(text, audioSource, onComplete, onError, voice, speed, volume, instructions));
        }

        static IEnumerator SpeakCoroutine(string text, AudioSource audioSource,
            Action onComplete, Action<string> onError, string voice, float speed, float volume,
            string instructions)
        {
            string key = AIImageClient.ApiKey;
            if (!AIImageClient.IsConfiguredApiKey(key))
            {
                onError?.Invoke("APIキー未設定");
                yield break;
            }

            string safeText = text
                .Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");
            float clampedSpeed = Mathf.Clamp(speed, 0.25f, 4f);
            string standardBody =
                $"{{\"model\":\"{Model}\"," +
                $"\"input\":\"{safeText}\"," +
                $"\"voice\":\"{SanitizeVoice(voice)}\"," +
                $"\"speed\":{clampedSpeed.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}," +
                $"\"response_format\":\"wav\"}}";

            // 演技指示がある場合は表現対応モデルを使う（speedは非対応のため指示文で速度を表現する）
            string body = standardBody;
            bool expressive = !string.IsNullOrEmpty(instructions);
            if (expressive)
            {
                string safeInst = instructions
                    .Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");
                body =
                    $"{{\"model\":\"{ExpressiveModel}\"," +
                    $"\"input\":\"{safeText}\"," +
                    $"\"voice\":\"{SanitizeVoice(voice)}\"," +
                    $"\"instructions\":\"{safeInst}\"," +
                    $"\"response_format\":\"wav\"}}";
            }

            byte[] wavData = null;
            using (var req = BuildRequest(body, key))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                    wavData = req.downloadHandler.data;
                else if (!expressive)
                {
                    onError?.Invoke(req.error);
                    yield break;
                }
                else
                    Debug.LogWarning($"[TTS] 表現付きTTS失敗（{req.error}）。標準TTSで再試行します");
            }

            // 表現付きモデルが使えない環境では従来のtts-1へフォールバック
            if (wavData == null)
            {
                using var req2 = BuildRequest(standardBody, key);
                yield return req2.SendWebRequest();
                if (req2.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(req2.error);
                    yield break;
                }
                wavData = req2.downloadHandler.data;
            }

            AudioClip clip = WavToAudioClip(wavData, "TTS");
            if (clip == null) { onError?.Invoke("WAV変換失敗"); yield break; }

            if (audioSource != null)
            {
                _speechEndTime = Mathf.Max(_speechEndTime, Time.unscaledTime + clip.length);
                audioSource.PlayOneShot(clip, volume);
                // 音声はスローモーション中も実時間で流れるため、待ちも実時間で行う
                yield return new WaitForSecondsRealtime(clip.length);
            }

            // AudioClip.Createで確保したネイティブリソースはGC対象外。再生完了後に明示破棄してリークを防ぐ。
            UnityEngine.Object.Destroy(clip);

            onComplete?.Invoke();
        }

        static UnityWebRequest BuildRequest(string body, string key)
        {
            var req = new UnityWebRequest(Endpoint, "POST");
            req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + key);
            req.timeout = 20;
            return req;
        }

        static string SanitizeVoice(string voice)
        {
            return string.IsNullOrWhiteSpace(voice) ? DefaultVoice : voice;
        }

        // WAVバイト列（PCM16）を AudioClip に変換する
        public static AudioClip WavToAudioClip(byte[] wav, string clipName)
        {
            try
            {
                int channels   = wav[22] | (wav[23] << 8);
                int sampleRate = wav[24] | (wav[25] << 8) | (wav[26] << 16) | (wav[27] << 24);
                int bitDepth   = wav[34] | (wav[35] << 8);

                // "data" チャンクを探す
                int dataStart = 44;
                for (int i = 12; i < wav.Length - 4; i++)
                {
                    if (wav[i] == 'd' && wav[i+1] == 'a' && wav[i+2] == 't' && wav[i+3] == 'a')
                    {
                        dataStart = i + 8;
                        break;
                    }
                }

                int bytesPerSample = bitDepth / 8;
                int sampleCount    = (wav.Length - dataStart) / bytesPerSample;
                float[] samples    = new float[sampleCount];

                for (int i = 0; i < sampleCount; i++)
                {
                    int idx = dataStart + i * bytesPerSample;
                    samples[i] = bitDepth == 16
                        ? (short)(wav[idx] | (wav[idx + 1] << 8)) / 32768f
                        : (wav[idx] - 128) / 128f;
                }

                var clip = AudioClip.Create(clipName, sampleCount / channels, channels, sampleRate, false);
                clip.SetData(samples, 0);
                return clip;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TTS] WAV解析失敗: {e.Message}");
                return null;
            }
        }
    }
}
