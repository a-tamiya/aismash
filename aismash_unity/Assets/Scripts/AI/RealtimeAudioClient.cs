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
    // OpenAIの現行Realtimeモデルを優先し、利用権限や一時障害時は呼び出し側で別モデルへフォールバックする。
    //  ・Transcribe : 録音済みWAVを送って文字起こし（whisper-1 RESTの代替）
    //  ・Synthesize : セリフを演技指示付きで読み上げてAudioClip化（表現付きTTSの代替）
    public static class RealtimeAudioClient
    {
        const string WsEndpoint = "wss://api.openai.com/v1/realtime";
        // このプロジェクトのAPIキーで利用でき、音声品質も高い2を保存・即時音声の第一候補にする。
        // 2.1は利用権限がある環境向けの第二候補として、同じvoiceを維持したまま使用する。
        public const string RealtimeModel   = "gpt-realtime-2";
        public const string RealtimeFallbackModel = "gpt-realtime-2.1";
        public const string TranscribeModel = "gpt-realtime-whisper";
        // 文字起こしも利用可能な同じ会話セッションモデルを使用する。
        const string TranscriptionSessionModel = RealtimeModel;
        // 読み上げの声（Realtime世代で最も人間らしい2声）。
        //  ・cedar = 男性（実況用） / marin = 女性（ボイスボール用）
        // 別の物理voiceへ自動変換すると性別印象が変わり得るため、voice拒否時はモデル単位で失敗させる。
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
            yield return sock.Connect($"{WsEndpoint}?model={TranscriptionSessionModel}", apiKey, 8f);
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
            Action<AudioClip> onClip, Action<string> onErr, string voice = MaleVoice, string model = RealtimeModel)
        {
            if (string.IsNullOrWhiteSpace(text)) { onErr?.Invoke("テキストが空"); yield break; }

            model = SanitizeSynthesisModel(model);
            using var sock = new RealtimeSocket();
            yield return sock.Connect($"{WsEndpoint}?model={model}", apiKey, 8f);
            if (!sock.IsOpen)
            {
                onErr?.Invoke("Realtime接続失敗: " + (sock.FatalError ?? "不明"));
                yield break;
            }

            string style = string.IsNullOrEmpty(styleInstructions)
                ? "自然な日本語で読み上げる。"
                : styleInstructions;

            bool ready = false;
            string err = null;
            string sessionJson =
                "{\"type\":\"session.update\",\"session\":{\"type\":\"realtime\"," +
                "\"output_modalities\":[\"audio\"]," +
                $"\"instructions\":\"{JsonEscape(style)}\"," +
                "\"audio\":{\"output\":{" +
                $"\"voice\":\"{JsonEscape(voice)}\"," +
                "\"format\":{\"type\":\"audio/pcm\",\"rate\":24000}}}}}";
            sock.SendJson(sessionJson);

            float setupDeadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < setupDeadline && !ready && err == null)
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

            // response.create.instructions はセッションのinstructionsをこの応答だけ上書きするため、
            // 性別・年齢・ピッチ・演技指示も必ず応答側へ含める。
            string ask = style + "\n次のセリフを一言一句そのまま日本語で読み上げる。" +
                         "前置き・相づち・言い直し・追加の言葉は一切入れない。セリフ：" + text;
            sock.SendJson($"{{\"type\":\"response.create\",\"response\":{{\"instructions\":\"{JsonEscape(ask)}\"}}}}");

            var pcmStream = new System.IO.MemoryStream();
            bool done = false;
            float deadline = Time.realtimeSinceStartup + 25f;
            while (Time.realtimeSinceStartup < deadline && !done && err == null)
            {
                while (sock.TryDequeue(out string msg))
                {
                    try
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
                        else if (t == "response.done")
                        {
                            string status = JsonUtility.FromJson<EvtResponseDone>(msg)?.response?.status;
                            if (status == "completed") done = true;
                            else err = "Realtime応答が完了しませんでした: " + (status ?? "status不明");
                            break;
                        }
                        else if (t == "error") { err = ErrorMessage(msg); break; }
                    }
                    catch (Exception e)
                    {
                        // 壊れたdelta等でコルーチンを例外終了させず、次の音声モデルへ確実にフォールバックする。
                        err = "Realtime音声イベント解析失敗: " + e.Message;
                        break;
                    }
                }
                if (sock.FatalError != null) err ??= sock.FatalError;
                yield return null;
            }

            if (err != null || !done || pcmStream.Length < 2)
            {
                onErr?.Invoke("Realtime音声合成失敗: " + (err ?? (done ? "音声なし" : "タイムアウト")));
                yield break;
            }

            var clip = Pcm16ToClip(pcmStream.ToArray(), InputRate, "RealtimeTTS");
            if (clip == null) { onErr?.Invoke("PCM変換失敗"); yield break; }
            onClip?.Invoke(clip);
        }

        // 1キャラクター分の複数台詞を同じRealtimeセッションで順番に生成する。
        // voiceとセッションを固定することで、台詞ごとに接続し直す場合より人物の一貫性を高める。
        public static IEnumerator SynthesizeBatch(string[] texts, string[] styleInstructions, string apiKey,
            Action<AudioClip[]> onClips, Action<int> onProgress, Action<string> onErr,
            string voice = MaleVoice, string model = RealtimeModel)
        {
            if (texts == null || texts.Length == 0)
            {
                onErr?.Invoke("台詞セットが空");
                yield break;
            }
            if (styleInstructions == null || styleInstructions.Length != texts.Length)
            {
                onErr?.Invoke("台詞と演技指示の件数が不一致");
                yield break;
            }
            for (int i = 0; i < texts.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(texts[i])) continue;
                onErr?.Invoke($"{i + 1}件目の台詞が空");
                yield break;
            }

            model = SanitizeSynthesisModel(model);
            using var sock = new RealtimeSocket();
            yield return sock.Connect($"{WsEndpoint}?model={model}", apiKey, 8f);
            if (!sock.IsOpen)
            {
                onErr?.Invoke("Realtime接続失敗: " + (sock.FatalError ?? "不明"));
                yield break;
            }

            string sessionStyle = string.IsNullOrWhiteSpace(styleInstructions[0])
                ? "自然な日本語で読み上げる。"
                : styleInstructions[0];
            string sessionJson =
                "{\"type\":\"session.update\",\"session\":{\"type\":\"realtime\"," +
                "\"output_modalities\":[\"audio\"]," +
                $"\"instructions\":\"{JsonEscape(sessionStyle)}\"," +
                "\"audio\":{\"output\":{" +
                $"\"voice\":\"{JsonEscape(voice)}\"," +
                "\"format\":{\"type\":\"audio/pcm\",\"rate\":24000}}}}}";
            sock.SendJson(sessionJson);

            bool ready = false;
            string err = null;
            float setupDeadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < setupDeadline && !ready && err == null)
            {
                while (sock.TryDequeue(out string msg))
                {
                    string type = EventType(msg);
                    if (type == "session.updated") { ready = true; break; }
                    if (type == "error") { err = ErrorMessage(msg); break; }
                }
                if (sock.FatalError != null) err ??= sock.FatalError;
                yield return null;
            }
            if (!ready)
            {
                onErr?.Invoke("Realtimeセッション設定失敗: " + (err ?? "タイムアウト"));
                yield break;
            }

            var clips = new AudioClip[texts.Length];
            bool ownershipTransferred = false;
            try
            {
                for (int i = 0; i < texts.Length; i++)
                {
                    onProgress?.Invoke(i);
                    string style = string.IsNullOrWhiteSpace(styleInstructions[i])
                        ? "自然な日本語で読み上げる。"
                        : styleInstructions[i];
                    string ask = style + "\n次のセリフを一言一句そのまま日本語で読み上げる。" +
                                 "前置き・相づち・言い直し・追加の言葉は一切入れない。セリフ：" + texts[i];
                    sock.SendJson($"{{\"type\":\"response.create\",\"response\":{{\"instructions\":\"{JsonEscape(ask)}\"}}}}");

                    using var pcmStream = new System.IO.MemoryStream();
                    bool done = false;
                    err = null;
                    float deadline = Time.realtimeSinceStartup + 30f;
                    while (Time.realtimeSinceStartup < deadline && !done && err == null)
                    {
                        while (sock.TryDequeue(out string msg))
                        {
                            try
                            {
                                string type = EventType(msg);
                                if (type == "response.output_audio.delta" || type == "response.audio.delta")
                                {
                                    string b64 = JsonUtility.FromJson<EvtDelta>(msg)?.delta;
                                    if (!string.IsNullOrEmpty(b64))
                                    {
                                        byte[] bytes = Convert.FromBase64String(b64);
                                        pcmStream.Write(bytes, 0, bytes.Length);
                                    }
                                }
                                else if (type == "response.done")
                                {
                                    string status = JsonUtility.FromJson<EvtResponseDone>(msg)?.response?.status;
                                    if (status == "completed") done = true;
                                    else err = "Realtime応答が完了しませんでした: " + (status ?? "status不明");
                                    break;
                                }
                                else if (type == "error")
                                {
                                    err = ErrorMessage(msg);
                                    break;
                                }
                            }
                            catch (Exception e)
                            {
                                err = "Realtime音声イベント解析失敗: " + e.Message;
                                break;
                            }
                        }
                        if (sock.FatalError != null) err ??= sock.FatalError;
                        yield return null;
                    }

                    if (err != null || !done || pcmStream.Length < 2)
                    {
                        onErr?.Invoke($"Realtime音声セット{i + 1}/{texts.Length}生成失敗: " +
                            (err ?? (done ? "音声なし" : "タイムアウト")));
                        yield break;
                    }

                    clips[i] = Pcm16ToClip(pcmStream.ToArray(), InputRate, $"RealtimeTTS_{i}");
                    if (clips[i] == null)
                    {
                        onErr?.Invoke($"Realtime音声セット{i + 1}/{texts.Length}のPCM変換失敗");
                        yield break;
                    }
                }

                onProgress?.Invoke(texts.Length);
                onClips?.Invoke(clips);
                ownershipTransferred = true;
            }
            finally
            {
                // 親コルーチンの期限停止や画面破棄でも、生成途中のネイティブAudioClipを残さない。
                if (!ownershipTransferred) DestroyClips(clips);
            }
        }

        // ── 音声ユーティリティ ─────────────────────────────────────

        static string SanitizeSynthesisModel(string model) =>
            string.Equals(model, RealtimeFallbackModel, StringComparison.OrdinalIgnoreCase)
                ? RealtimeFallbackModel
                : RealtimeModel;

        static void DestroyClips(AudioClip[] clips)
        {
            if (clips == null) return;
            for (int i = 0; i < clips.Length; i++)
                if (clips[i] != null) UnityEngine.Object.Destroy(clips[i]);
        }

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
                // 空音声・極端に短い応答・指示逸脱による異常な長尺を成功扱いにしない。
                if (pcm == null || rate <= 0 || pcm.Length < rate * 2 / 10 || pcm.Length > rate * 2 * 30)
                    return null;
                int count = pcm.Length / 2;
                var samples = new float[count];
                float peak = 0f;
                for (int i = 0; i < count; i++)
                {
                    samples[i] = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8)) / 32768f;
                    peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
                }
                if (peak < 0.002f) return null;
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
                if (!string.IsNullOrEmpty(e?.error?.message))
                    return string.IsNullOrEmpty(e.error.code)
                        ? e.error.message
                        : e.error.code + ": " + e.error.message;
            }
            catch { }
            return json.Length > 200 ? json.Substring(0, 200) : json;
        }

        static string JsonEscape(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"")
             .Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");

        [Serializable] class EvtHead       { public string type; }
        [Serializable] class EvtTranscript { public string type; public string transcript; }
        [Serializable] class EvtDelta      { public string type; public string delta; }
        [Serializable] class EvtResponseDone { public string type; public EvtResponse response; }
        [Serializable] class EvtResponse { public string status; }
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
            Task _receiveTask = Task.CompletedTask;
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

                _receiveTask = ReceiveLoop();
            }

            async Task ReceiveLoop()
            {
                var buf = new byte[16384];
                using var ms = new System.IO.MemoryStream();
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
                // Task内の例外は通常それぞれの処理内で捕捉するが、fault済みならここで必ず観測する。
                if (_receiveTask.IsFaulted) _ = _receiveTask.Exception;
                if (_sendChain.IsFaulted) _ = _sendChain.Exception;
                _cts.Dispose();
            }
        }
    }
}
