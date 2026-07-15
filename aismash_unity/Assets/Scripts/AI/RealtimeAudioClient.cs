using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace PromptFighters.AI
{
    // OpenAI Realtime API (WebSocket) を使った音声機能クライアント。
    // APIキーの権限変更で whisper-1 / gpt-4o-mini-tts が使えなくなったため、
    // キーで利用可能な gpt-realtime-2 / gpt-realtime-whisper を代替として使う。
    //  ・Transcribe : 録音済みWAVを送って文字起こし（whisper-1 RESTの代替）
    //  ・Synthesize : セリフを演技指示付きで読み上げてAudioClip化（表現付きTTSの代替）
    public static class RealtimeAudioClient
    {
        const string WsEndpoint = "wss://api.openai.com/v1/realtime";
        public const string RealtimeModel   = "gpt-realtime-1.5";
        public const string TranscribeModel = "gpt-realtime-whisper";
        // 読み上げの声（Realtime世代で最も人間らしい2声）。
        //  ・cedar = 男性（実況用） / marin = 女性（ボイスボール用）
        // セッション設定で拒否された場合は近い性別の旧声へフォールバックする。
        public const string MaleVoice   = "cedar";
        public const string FemaleVoice = "marin";

        const int InputRate = 24000; // Realtime APIの audio/pcm 入出力レート

        // ── 文字起こし ───────────────────────────────────────────
        // モノラルPCM16のWAVバイト列を文字起こしする。会話セッションに音声をappend→commitし、
        // 入力音声の自動文字起こし（input_audio_transcription）の完了イベントを受け取る。
        // response.create は送らないため、LLMの応答生成は走らない。
        public static IEnumerator Transcribe(byte[] wav, string apiKey,
            Action<string> onText, Action<string> onErr)
        {
            float[] samples = ParseWavMono(wav, out int rate);
            if (samples == null || samples.Length == 0)
            {
                onErr?.Invoke("WAV解析失敗");
                yield break;
            }
            byte[] pcm = ToPcm16(Resample(samples, rate, InputRate));

            using var sock = new RealtimeSocket();
            yield return sock.Connect($"{WsEndpoint}?model={RealtimeModel}", apiKey, 8f);
            if (!sock.IsOpen)
            {
                onErr?.Invoke("Realtime接続失敗: " + (sock.FatalError ?? "不明"));
                yield break;
            }

            // 入力音声の文字起こしのみを構成（VADなし・応答は作らない）
            string sessionJson =
                "{\"type\":\"session.update\",\"session\":{\"type\":\"realtime\"," +
                "\"output_modalities\":[\"text\"]," +
                "\"audio\":{\"input\":{" +
                "\"format\":{\"type\":\"audio/pcm\",\"rate\":24000}," +
                $"\"transcription\":{{\"model\":\"{TranscribeModel}\",\"language\":\"ja\"}}," +
                "\"turn_detection\":null}}}}";
            sock.SendJson(sessionJson);

            string err = null;
            bool ready = false;
            float deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline && !ready && err == null)
            {
                while (sock.TryDequeue(out string msg))
                {
                    string t = EventType(msg);
                    if (t == "session.updated") { ready = true; break; }
                    if (t == "error") { err = ErrorMessage(msg); break; }
                }
                if (sock.FatalError != null) err ??= sock.FatalError;
                yield return null;
            }
            if (!ready)
            {
                onErr?.Invoke("Realtimeセッション設定失敗: " + (err ?? "タイムアウト"));
                yield break;
            }

            // 音声を約1秒ぶんずつ分割してappend→commit
            const int ChunkBytes = InputRate * 2;
            for (int off = 0; off < pcm.Length; off += ChunkBytes)
            {
                int len = Math.Min(ChunkBytes, pcm.Length - off);
                string b64 = Convert.ToBase64String(pcm, off, len);
                sock.SendJson($"{{\"type\":\"input_audio_buffer.append\",\"audio\":\"{b64}\"}}");
            }
            sock.SendJson("{\"type\":\"input_audio_buffer.commit\"}");

            string transcript = null;
            deadline = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < deadline && transcript == null && err == null)
            {
                while (sock.TryDequeue(out string msg))
                {
                    string t = EventType(msg);
                    if (t == "conversation.item.input_audio_transcription.completed")
                    {
                        transcript = JsonUtility.FromJson<EvtTranscript>(msg)?.transcript ?? "";
                        break;
                    }
                    if (t == "conversation.item.input_audio_transcription.failed" || t == "error")
                    {
                        err = ErrorMessage(msg);
                        break;
                    }
                }
                if (sock.FatalError != null) err ??= sock.FatalError;
                yield return null;
            }

            if (transcript != null) onText?.Invoke(transcript.Trim());
            else onErr?.Invoke("Realtime文字起こし失敗: " + (err ?? "タイムアウト"));
        }

        // ── 音声合成（セリフ読み上げ） ─────────────────────────────
        // Realtimeモデルに「このセリフをそのまま読む」よう指示して喋らせ、AudioClipにして返す。
        // styleInstructions で声の演技（実況調など）、voice で声（cedar=男性/marin=女性）を指定できる。
        public static IEnumerator Synthesize(string text, string styleInstructions, string apiKey,
            Action<AudioClip> onClip, Action<string> onErr, string voice = MaleVoice)
        {
            if (string.IsNullOrWhiteSpace(text)) { onErr?.Invoke("テキストが空"); yield break; }

            using var sock = new RealtimeSocket();
            yield return sock.Connect($"{WsEndpoint}?model={RealtimeModel}", apiKey, 8f);
            if (!sock.IsOpen)
            {
                onErr?.Invoke("Realtime接続失敗: " + (sock.FatalError ?? "不明"));
                yield break;
            }

            string style = string.IsNullOrEmpty(styleInstructions)
                ? "自然な日本語で読み上げる。"
                : styleInstructions;

            // 指定の声が拒否されたら、近い性別の旧声で再試行する
            string fallbackVoice = voice == FemaleVoice ? "shimmer" : "ash";
            string[] voices = { voice, fallbackVoice };
            bool ready = false;
            string err = null;
            for (int v = 0; v < voices.Length && !ready; v++)
            {
                err = null;
                string sessionJson =
                    "{\"type\":\"session.update\",\"session\":{\"type\":\"realtime\"," +
                    "\"output_modalities\":[\"audio\"]," +
                    $"\"instructions\":\"{JsonEscape(style)}\"," +
                    "\"audio\":{\"output\":{" +
                    $"\"voice\":\"{voices[v]}\"," +
                    "\"format\":{\"type\":\"audio/pcm\",\"rate\":24000}}}}}";
                sock.SendJson(sessionJson);

                float dl = Time.realtimeSinceStartup + 8f;
                while (Time.realtimeSinceStartup < dl && !ready && err == null)
                {
                    while (sock.TryDequeue(out string msg))
                    {
                        string t = EventType(msg);
                        if (t == "session.updated") { ready = true; break; }
                        if (t == "error") { err = ErrorMessage(msg); break; }
                    }
                    if (sock.FatalError != null) err ??= sock.FatalError;
                    yield return null;
                }
                // 声の指定エラーのみ次の声で再試行。それ以外は中断。
                if (!ready && (err == null || !err.Contains("voice"))) break;
            }
            if (!ready)
            {
                onErr?.Invoke("Realtimeセッション設定失敗: " + (err ?? "タイムアウト"));
                yield break;
            }

            string ask = "次のセリフを一言一句そのまま日本語で読み上げる。" +
                         "前置き・相づち・言い直し・追加の言葉は一切入れない。セリフ：" + text;
            sock.SendJson($"{{\"type\":\"response.create\",\"response\":{{\"instructions\":\"{JsonEscape(ask)}\"}}}}");

            var pcmStream = new System.IO.MemoryStream();
            bool done = false;
            float deadline = Time.realtimeSinceStartup + 25f;
            while (Time.realtimeSinceStartup < deadline && !done && err == null)
            {
                while (sock.TryDequeue(out string msg))
                {
                    string t = EventType(msg);
                    // GA名(response.output_audio.delta)と旧名(response.audio.delta)の両方を受ける
                    if (t == "response.output_audio.delta" || t == "response.audio.delta")
                    {
                        string b64 = JsonUtility.FromJson<EvtDelta>(msg)?.delta;
                        if (!string.IsNullOrEmpty(b64))
                        {
                            byte[] bytes = Convert.FromBase64String(b64);
                            pcmStream.Write(bytes, 0, bytes.Length);
                        }
                    }
                    else if (t == "response.done") { done = true; break; }
                    else if (t == "error") { err = ErrorMessage(msg); break; }
                }
                if (sock.FatalError != null) err ??= sock.FatalError;
                yield return null;
            }

            if (err != null || pcmStream.Length < 2)
            {
                onErr?.Invoke("Realtime音声合成失敗: " + (err ?? (done ? "音声なし" : "タイムアウト")));
                yield break;
            }

            var clip = Pcm16ToClip(pcmStream.ToArray(), InputRate, "RealtimeTTS");
            if (clip == null) { onErr?.Invoke("PCM変換失敗"); yield break; }
            onClip?.Invoke(clip);
        }

        // ── 音声ユーティリティ ─────────────────────────────────────

        // モノラルPCM16 WAV → float配列（レートはヘッダから読む）
        static float[] ParseWavMono(byte[] wav, out int sampleRate)
        {
            sampleRate = 0;
            try
            {
                if (wav == null || wav.Length < 44) return null;
                int channels = wav[22] | (wav[23] << 8);
                sampleRate   = wav[24] | (wav[25] << 8) | (wav[26] << 16) | (wav[27] << 24);
                int dataStart = 44;
                for (int i = 12; i < wav.Length - 4; i++)
                {
                    if (wav[i] == 'd' && wav[i + 1] == 'a' && wav[i + 2] == 't' && wav[i + 3] == 'a')
                    {
                        dataStart = i + 8;
                        break;
                    }
                }
                int frameCount = (wav.Length - dataStart) / (2 * Math.Max(1, channels));
                var samples = new float[frameCount];
                for (int i = 0; i < frameCount; i++)
                {
                    // マルチチャンネルは先頭チャンネルのみ使用
                    int idx = dataStart + i * 2 * channels;
                    samples[i] = (short)(wav[idx] | (wav[idx + 1] << 8)) / 32768f;
                }
                return samples;
            }
            catch { return null; }
        }

        static float[] Resample(float[] src, int fromRate, int toRate)
        {
            if (src == null || fromRate <= 0 || fromRate == toRate) return src;
            int outLen = (int)((long)src.Length * toRate / fromRate);
            var dst = new float[outLen];
            for (int i = 0; i < outLen; i++)
            {
                float pos = (float)i * fromRate / toRate;
                int i0 = (int)pos;
                int i1 = Mathf.Min(i0 + 1, src.Length - 1);
                dst[i] = Mathf.Lerp(src[i0], src[i1], pos - i0);
            }
            return dst;
        }

        static byte[] ToPcm16(float[] samples)
        {
            var bytes = new byte[samples.Length * 2];
            for (int i = 0; i < samples.Length; i++)
            {
                short s = (short)(Mathf.Clamp(samples[i], -1f, 1f) * 32767f);
                bytes[i * 2]     = (byte)(s & 0xFF);
                bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
            }
            return bytes;
        }

        static AudioClip Pcm16ToClip(byte[] pcm, int rate, string name)
        {
            try
            {
                int count = pcm.Length / 2;
                var samples = new float[count];
                for (int i = 0; i < count; i++)
                    samples[i] = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8)) / 32768f;
                AITTSClient.NormalizeSamples(samples);
                var clip = AudioClip.Create(name, count, 1, rate, false);
                clip.SetData(samples, 0);
                return clip;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Realtime] PCM変換失敗: " + e.Message);
                return null;
            }
        }

        // ── イベントJSONユーティリティ ─────────────────────────────

        static string EventType(string json)
        {
            try { return JsonUtility.FromJson<EvtHead>(json)?.type ?? ""; }
            catch { return ""; }
        }

        static string ErrorMessage(string json)
        {
            try
            {
                var e = JsonUtility.FromJson<EvtError>(json);
                if (!string.IsNullOrEmpty(e?.error?.message)) return e.error.message;
            }
            catch { }
            return json.Length > 200 ? json.Substring(0, 200) : json;
        }

        static string JsonEscape(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");

        [Serializable] class EvtHead       { public string type; }
        [Serializable] class EvtTranscript { public string type; public string transcript; }
        [Serializable] class EvtDelta      { public string type; public string delta; }
        [Serializable] class EvtError      { public string type; public EvtErrorBody error; }
        [Serializable] class EvtErrorBody  { public string message; public string code; }

        // ── WebSocketラッパー ──────────────────────────────────────
        // 受信はバックグラウンドTaskで行い、メッセージをキューに積む。
        // コルーチン側（メインスレッド）はTryDequeueでポーリングする。
        class RealtimeSocket : IDisposable
        {
            readonly ClientWebSocket _ws = new ClientWebSocket();
            readonly ConcurrentQueue<string> _inbox = new ConcurrentQueue<string>();
            readonly CancellationTokenSource _cts = new CancellationTokenSource();
            Task _sendChain = Task.CompletedTask;
            volatile string _fatalError;

            public string FatalError => _fatalError;
            public bool IsOpen => _fatalError == null && _ws.State == WebSocketState.Open;

            public IEnumerator Connect(string url, string apiKey, float timeoutSec)
            {
                Task task;
                try
                {
                    _ws.Options.SetRequestHeader("Authorization", "Bearer " + apiKey);
                    task = _ws.ConnectAsync(new Uri(url), _cts.Token);
                }
                catch (Exception e)
                {
                    _fatalError = e.Message;
                    yield break;
                }

                float deadline = Time.realtimeSinceStartup + timeoutSec;
                while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
                if (!task.IsCompleted) { _fatalError = "接続タイムアウト"; yield break; }
                if (task.IsFaulted)    { _fatalError = Flatten(task.Exception); yield break; }

                _ = ReceiveLoop();
            }

            async Task ReceiveLoop()
            {
                var buf = new byte[16384];
                var ms = new System.IO.MemoryStream();
                try
                {
                    while (_ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                    {
                        var res = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), _cts.Token);
                        if (res.MessageType == WebSocketMessageType.Close) break;
                        ms.Write(buf, 0, res.Count);
                        if (res.EndOfMessage)
                        {
                            _inbox.Enqueue(Encoding.UTF8.GetString(ms.ToArray()));
                            ms.SetLength(0);
                        }
                    }
                }
                catch (Exception e)
                {
                    if (!_cts.IsCancellationRequested) _fatalError = e.Message;
                }
            }

            // 送信は1つずつ順番に行う（ClientWebSocketは同時SendAsync不可のため直列化）
            public void SendJson(string json)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                _sendChain = _sendChain.ContinueWith(async _ =>
                {
                    try
                    {
                        await _ws.SendAsync(new ArraySegment<byte>(bytes),
                            WebSocketMessageType.Text, true, _cts.Token);
                    }
                    catch (Exception e)
                    {
                        if (!_cts.IsCancellationRequested) _fatalError = e.Message;
                    }
                }).Unwrap();
            }

            public bool TryDequeue(out string msg) => _inbox.TryDequeue(out msg);

            static string Flatten(AggregateException e)
                => e?.GetBaseException()?.Message ?? "不明なエラー";

            public void Dispose()
            {
                try { _cts.Cancel(); } catch { }
                try { _ws.Dispose(); } catch { }
            }
        }
    }
}
