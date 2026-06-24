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

        // 録音中に部分認識を送る間隔（秒）。短いほど反応が速いがAPI呼び出しが増える。
        const float PartialInterval = 1.2f;

        // マイクで recordSeconds 秒録音し、Whisper API で文字起こしする。
        // onPartial を渡すと、録音中に「ここまでの音声」を逐次認識して途中経過を返す（リアルタイム表示用）。
        public static Coroutine RecordAndTranscribe(MonoBehaviour runner,
            float recordSeconds,
            Action<string> onSuccess,
            Action<string> onError = null,
            Action onRecordingStart = null,
            Action<string> onPartial = null)
        {
            return runner.StartCoroutine(
                RecordCoroutine(runner, recordSeconds, onSuccess, onError, onRecordingStart, onPartial));
        }

        static IEnumerator RecordCoroutine(MonoBehaviour runner, float recordSeconds,
            Action<string> onSuccess, Action<string> onError, Action onRecordingStart, Action<string> onPartial)
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
            // 録音しながら一定間隔で「ここまでの音声」を部分認識し、途中経過を onPartial で返す。
            float elapsed     = 0f;
            float nextPartial = PartialInterval;
            bool  partialBusy = false;
            while (elapsed < recordSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                if (onPartial != null && !partialBusy && elapsed >= nextPartial)
                {
                    nextPartial += PartialInterval;
                    int micPos = Microphone.GetPosition(null);
                    if (micPos > sampleRate / 2) // 0.5秒以上たまってから
                    {
                        byte[] chunk = AudioClipToWavSamples(clip, sampleRate, micPos);
                        if (chunk != null)
                        {
                            partialBusy = true;
                            runner.StartCoroutine(TranscribeOnce(chunk, key,
                                t => { if (!string.IsNullOrEmpty(t)) onPartial(t); partialBusy = false; },
                                _ => { partialBusy = false; }));
                        }
                    }
                }
                yield return null;
            }
            Microphone.End(null);

            byte[] wav = AudioClipToWav(clip, sampleRate, recordSeconds);
            if (wav == null) { onError?.Invoke("WAVエンコード失敗"); yield break; }

            // 最終確定の文字起こし（録音全体）。
            yield return TranscribeOnce(wav, key, onSuccess, onError);
        }

        // WAVバイト列を Whisper に1回送って文字起こしする。
        static IEnumerator TranscribeOnce(byte[] wav, string key,
            Action<string> onText, Action<string> onErr)
        {
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
                onErr?.Invoke($"{req.error}: {req.downloadHandler?.text}");
                yield break;
            }

            try
            {
                var resp = JsonUtility.FromJson<WhisperResponse>(req.downloadHandler.text);
                onText?.Invoke(resp?.text ?? "");
            }
            catch (Exception e)
            {
                onErr?.Invoke("解析失敗: " + e.Message);
            }
        }

        // AudioClip → モノラル PCM16 WAV バイト列（指定秒数ぶん）
        static byte[] AudioClipToWav(AudioClip clip, int sampleRate, float duration)
            => AudioClipToWavSamples(clip, sampleRate, Mathf.CeilToInt(duration * sampleRate));

        // AudioClip → モノラル PCM16 WAV バイト列（先頭 wantSamples サンプルぶん。部分認識用）
        static byte[] AudioClipToWavSamples(AudioClip clip, int sampleRate, int wantSamples)
        {
            if (clip == null) return null;

            int sampleCount = Mathf.Clamp(wantSamples, 0, clip.samples);
            if (sampleCount <= 0) return null;
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
