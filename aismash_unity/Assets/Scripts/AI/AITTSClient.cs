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
        public const string DefaultVoice    = "nova";
        public const string CommentaryVoice = "onyx";    // 男性寄り
        public const string AngelVoice      = "shimmer"; // 明るく軽やかな印象（ボイスボール用）
        public const float  CommentarySpeed = 1.15f;     // やや速めで迫力を出す

        public static Coroutine Speak(MonoBehaviour runner, string text,
            AudioSource audioSource,
            Action onComplete = null,
            Action<string> onError = null,
            string voice = DefaultVoice,
            float speed = 1f,
            float volume = 1f)
        {
            return runner.StartCoroutine(SpeakCoroutine(text, audioSource, onComplete, onError, voice, speed, volume));
        }

        static IEnumerator SpeakCoroutine(string text, AudioSource audioSource,
            Action onComplete, Action<string> onError, string voice, float speed, float volume)
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
            string body =
                $"{{\"model\":\"{Model}\"," +
                $"\"input\":\"{safeText}\"," +
                $"\"voice\":\"{SanitizeVoice(voice)}\"," +
                $"\"speed\":{clampedSpeed.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}," +
                $"\"response_format\":\"wav\"}}";

            using var req = new UnityWebRequest(Endpoint, "POST");
            req.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + key);
            req.timeout = 20;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(req.error);
                yield break;
            }

            AudioClip clip = WavToAudioClip(req.downloadHandler.data, "TTS");
            if (clip == null) { onError?.Invoke("WAV変換失敗"); yield break; }

            if (audioSource != null)
            {
                audioSource.PlayOneShot(clip, volume);
                yield return new WaitForSeconds(clip.length);
            }

            // AudioClip.Createで確保したネイティブリソースはGC対象外。再生完了後に明示破棄してリークを防ぐ。
            UnityEngine.Object.Destroy(clip);

            onComplete?.Invoke();
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
