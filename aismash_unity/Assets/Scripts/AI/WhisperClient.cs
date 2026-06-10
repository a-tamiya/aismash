using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PromptFighters.AI
{
    public static class WhisperClient
    {
        const string Endpoint = "https://api.openai.com/v1/audio/transcriptions";

        // マイクで recordSeconds 秒録音し、Whisper API で文字起こしする
        public static Coroutine RecordAndTranscribe(MonoBehaviour runner,
            float recordSeconds,
            Action<string> onSuccess,
            Action<string> onError = null,
            Action onRecordingStart = null)
        {
            return runner.StartCoroutine(
                RecordCoroutine(recordSeconds, onSuccess, onError, onRecordingStart));
        }

        static IEnumerator RecordCoroutine(float recordSeconds,
            Action<string> onSuccess, Action<string> onError, Action onRecordingStart)
        {
            string key = AIImageClient.ApiKey;
            if (!AIImageClient.IsConfiguredApiKey(key))
            {
                onError?.Invoke("APIキー未設定");
                yield break;
            }

            if (Microphone.devices.Length == 0)
            {
                onError?.Invoke("マイクが見つかりません");
                yield break;
            }

            int sampleRate = 16000;
            int clipSeconds = Mathf.CeilToInt(recordSeconds) + 1;
            AudioClip clip = Microphone.Start(null, false, clipSeconds, sampleRate);
            onRecordingStart?.Invoke();

            // 録音はスローモーション（Time.timeScale変更）の影響を受けない実時間で行う。
            // マイクは実時間で録音されるため、ここをスケール時間にすると尺がズレる。
            yield return new WaitForSecondsRealtime(recordSeconds);
            Microphone.End(null);

            byte[] wav = AudioClipToWav(clip, sampleRate, recordSeconds);
            if (wav == null) { onError?.Invoke("WAVエンコード失敗"); yield break; }

            var form = new List<IMultipartFormSection>
            {
                new MultipartFormDataSection("model",    "whisper-1"),
                new MultipartFormDataSection("language", "ja"),
                new MultipartFormFileSection("file", wav, "audio.wav", "audio/wav"),
            };

            using var req = UnityWebRequest.Post(Endpoint, form);
            req.SetRequestHeader("Authorization", "Bearer " + key);
            req.timeout = 30;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"{req.error}: {req.downloadHandler?.text}");
                yield break;
            }

            try
            {
                var resp = JsonUtility.FromJson<WhisperResponse>(req.downloadHandler.text);
                onSuccess?.Invoke(resp?.text ?? "");
            }
            catch (Exception e)
            {
                onError?.Invoke("解析失敗: " + e.Message);
            }
        }

        // AudioClip → モノラル PCM16 WAV バイト列
        static byte[] AudioClipToWav(AudioClip clip, int sampleRate, float duration)
        {
            if (clip == null) return null;

            int sampleCount = Mathf.Min(clip.samples, Mathf.CeilToInt(duration * sampleRate));
            float[] samples = new float[sampleCount];
            clip.GetData(samples, 0);

            int byteCount = sampleCount * 2;
            byte[] wav    = new byte[44 + byteCount];

            Encoding.ASCII.GetBytes("RIFF").CopyTo(wav, 0);
            BitConverter.GetBytes(36 + byteCount).CopyTo(wav, 4);
            Encoding.ASCII.GetBytes("WAVE").CopyTo(wav, 8);
            Encoding.ASCII.GetBytes("fmt ").CopyTo(wav, 12);
            BitConverter.GetBytes(16).CopyTo(wav, 16);
            BitConverter.GetBytes((short)1).CopyTo(wav, 20);   // PCM
            BitConverter.GetBytes((short)1).CopyTo(wav, 22);   // mono
            BitConverter.GetBytes(sampleRate).CopyTo(wav, 24);
            BitConverter.GetBytes(sampleRate * 2).CopyTo(wav, 28);
            BitConverter.GetBytes((short)2).CopyTo(wav, 32);
            BitConverter.GetBytes((short)16).CopyTo(wav, 34);
            Encoding.ASCII.GetBytes("data").CopyTo(wav, 36);
            BitConverter.GetBytes(byteCount).CopyTo(wav, 40);

            for (int i = 0; i < sampleCount; i++)
            {
                short s = (short)(Mathf.Clamp(samples[i], -1f, 1f) * 32767f);
                wav[44 + i * 2]     = (byte)(s & 0xFF);
                wav[44 + i * 2 + 1] = (byte)((s >> 8) & 0xFF);
            }

            return wav;
        }

        [Serializable] class WhisperResponse { public string text; }
    }
}
