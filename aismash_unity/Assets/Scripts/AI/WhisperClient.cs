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
        // これ未満の音量（RMS）は「ほぼ無音」とみなし、録音中の部分認識（途中表示）には送らない。
        // ※実際の音声まで無音扱いにしないよう低めに設定する。最終確定の認識は音量に関わらず必ず行う。
        const float SilenceRmsThreshold = 0.005f;

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
                    // 0.5秒以上たまり、かつ十分な音量（無音でない）のときだけ部分認識する。
                    // 無音チャンクを送るとハルシネーションが出るので、その間は「認識中…」のまま。
                    if (micPos > sampleRate / 2 && HasVoice(clip, micPos))
                    {
                        byte[] chunk = AudioClipToWavSamples(clip, sampleRate, micPos);
                        if (chunk != null)
                        {
                            partialBusy = true;
                            runner.StartCoroutine(TranscribeOnce(chunk, key,
                                t => { if (!IsHallucination(t)) onPartial(t); partialBusy = false; },
                                _ => { partialBusy = false; }));
                        }
                    }
                }
                yield return null;
            }
            Microphone.End(null);

            byte[] wav = AudioClipToWav(clip, sampleRate, recordSeconds);
            if (wav == null) { onError?.Invoke("WAVエンコード失敗"); yield break; }

            // 最終確定の文字起こしは音量に関わらず必ず行う（声を取りこぼさない）。
            // 無音時のハルシネーション定型句だけは「声なし」に変換してフォールバックへ回す。
            yield return TranscribeOnce(wav, key,
                t => onSuccess?.Invoke(IsHallucination(t) ? "" : t),
                onError);
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

        // 無音をWhisperに送ったときに返りがちな定型ハルシネーション（幻聴）。これらは「声なし」とみなす。
        static readonly string[] HallucinationPhrases =
        {
            "ご視聴ありがとうございました", "ご視聴ありがとうございます",
            "ご清聴ありがとうございました", "最後までご視聴",
            "チャンネル登録", "高評価", "次回の動画", "また次の動画で",
        };

        static bool IsHallucination(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            string t = text.Trim();
            for (int i = 0; i < HallucinationPhrases.Length; i++)
                if (t.Contains(HallucinationPhrases[i])) return true;
            return false;
        }

        // 先頭 sampleCount サンプルの音量（RMS）がしきい値以上か（＝声が入っているか）を判定する。
        static bool HasVoice(AudioClip clip, int sampleCount)
        {
            if (clip == null) return false;
            sampleCount = Mathf.Clamp(sampleCount, 0, clip.samples);
            if (sampleCount <= 0) return false;
            float[] buf = new float[sampleCount];
            clip.GetData(buf, 0);
            double sum = 0.0;
            for (int i = 0; i < sampleCount; i++) sum += (double)buf[i] * buf[i];
            float rms = (float)System.Math.Sqrt(sum / sampleCount);
            return rms >= SilenceRmsThreshold;
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
