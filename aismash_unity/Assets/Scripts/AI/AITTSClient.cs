using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PromptFighters.AI
{
    public static class AITTSClient
    {
        const string SpeechEndpoint = "https://api.openai.com/v1/audio/speech";
        const string ChatEndpoint   = "https://api.openai.com/v1/chat/completions";
        const string Model    = "tts-1";
        // Chat Completionsの高品質音声出力モデル。感情・間・息遣いを含む演技が必要なキャラボイスに使う。
        const string PremiumAudioModel = "gpt-audio-1.5";
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
        // ボイスボール（効果発表）の演技指示。明るい女性の声で魔法の効果を発表するイメージ。
        public const string AngelInstructions =
            "明るく元気な女性の声。魔法の効果を発表するマスコットのように、ワクワク感を込めて。" +
            "自然な日本語のイントネーションで、台本読みに聞こえない生き生きとした声で短く言い切る。";

        // 現在AI音声（実況・ボイスボール等）を再生中か。実況側が声の重なりを避けるために参照する。
        // 再生開始時に終了予定時刻を記録する方式（コルーチンが途中停止されてもフラグが残らない）。
        static float _speechEndTime;
        public static bool IsSpeaking => Time.unscaledTime < _speechEndTime;

        // 表現付きモデルがAPIキーの権限で使えない場合、初回失敗を記憶して以後は最初からtts-1を使う
        // （毎回失敗リクエストを挟む無駄なレイテンシとログを避ける）。
        static bool _expressiveUnavailable;
        static bool _premiumAudioUnavailable;

        // Realtime API音声合成（最も人間らしい・演技指示対応）の連続失敗回数。
        // 2回連続で失敗したらこのセッションでは使わない（一時的な通信エラー1回では諦めない）。
        static int _realtimeTtsFailures;
        const int RealtimeTtsMaxFailures = 2;

        public static Coroutine Speak(MonoBehaviour runner, string text,
            AudioSource audioSource,
            Action onComplete = null,
            Action<string> onError = null,
            string voice = DefaultVoice,
            float speed = 1f,
            float volume = 1f,
            string instructions = null,
            string realtimeVoice = null)
        {
            return runner.StartCoroutine(SpeakCoroutine(text, audioSource, onComplete, onError, voice, speed, volume, instructions, realtimeVoice));
        }

        // キャラクター生成時に再利用可能なWAVを作る。試合中にAPIを呼ばず、保存済み音声を再生するための経路。
        public static Coroutine GenerateWav(MonoBehaviour runner, string text, string instructions, string voice,
            Action<byte[]> onSuccess, Action<string> onError)
        {
            return runner.StartCoroutine(GenerateWavCoroutine(text, instructions, voice, onSuccess, onError));
        }

        static IEnumerator GenerateWavCoroutine(string text, string instructions, string voice,
            Action<byte[]> onSuccess, Action<string> onError)
        {
            string key = AIImageClient.ApiKey;
            if (!AIImageClient.IsConfiguredApiKey(key))
            {
                onError?.Invoke("APIキー未設定");
                yield break;
            }
            if (string.IsNullOrWhiteSpace(text))
            {
                onError?.Invoke("ボイス台詞が空です");
                yield break;
            }

            string safeText = EscapeJson(text.Trim());
            string safeInstructions = EscapeJson(instructions ?? "");
            string safeVoice = SanitizeCharacterVoice(voice);
            byte[] wavData = null;
            string lastError = null;

            // まず高品質なネイティブ音声モデルを使い、プロ声優として台詞を演じさせる。
            if (!_premiumAudioUnavailable)
            {
                yield return RequestPremiumWav(text.Trim(), instructions, safeVoice, key,
                    bytes => wavData = bytes,
                    error => lastError = error);
            }

            string expressiveBody =
                $"{{\"model\":\"{ExpressiveModel}\"," +
                $"\"input\":\"{safeText}\"," +
                $"\"voice\":\"{safeVoice}\"," +
                $"\"instructions\":\"{safeInstructions}\"," +
                $"\"response_format\":\"wav\"}}";

            bool expressiveUnavailable = _expressiveUnavailable;
            for (int attempt = 1; !IsWav(wavData) && !expressiveUnavailable && attempt <= 2; attempt++)
            {
                using var req = BuildRequest(expressiveBody, key);
                req.timeout = 30;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    wavData = req.downloadHandler.data;
                    break;
                }

                lastError = $"{req.responseCode} {req.error}: {req.downloadHandler?.text}";
                if (req.responseCode == 403 || req.responseCode == 404)
                {
                    expressiveUnavailable = true;
                    _expressiveUnavailable = true;
                    break;
                }
                if (!IsTransient(req) || attempt >= 2) break;
                yield return new WaitForSecondsRealtime(RetryDelay(req, attempt));
            }

            // 表現付きモデルが使えない・一時障害が続く場合も、標準TTSで音声生成を試す。
            if (!IsWav(wavData))
            {
                string standardBody =
                    $"{{\"model\":\"{Model}\"," +
                    $"\"input\":\"{safeText}\"," +
                    $"\"voice\":\"{safeVoice}\"," +
                    $"\"speed\":1.0,\"response_format\":\"wav\"}}";
                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    using var req = BuildRequest(standardBody, key);
                    req.timeout = 30;
                    yield return req.SendWebRequest();
                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        wavData = req.downloadHandler.data;
                        break;
                    }

                    lastError = $"{req.responseCode} {req.error}: {req.downloadHandler?.text}";
                    if (!IsTransient(req) || attempt >= 2) break;
                    yield return new WaitForSecondsRealtime(RetryDelay(req, attempt));
                }

                if (expressiveUnavailable)
                    Debug.LogWarning("[TTS] キャラボイスは表現付きモデルを利用できないため標準TTSへフォールバックしました");
            }

            if (!IsWav(wavData))
            {
                onError?.Invoke(lastError ?? "キャラボイスWAV生成に失敗");
                yield break;
            }

            onSuccess?.Invoke(wavData);
        }

        static IEnumerator SpeakCoroutine(string text, AudioSource audioSource,
            Action onComplete, Action<string> onError, string voice, float speed, float volume,
            string instructions, string realtimeVoice)
        {
            string key = AIImageClient.ApiKey;
            if (!AIImageClient.IsConfiguredApiKey(key))
            {
                onError?.Invoke("APIキー未設定");
                yield break;
            }

            // 演技指示がある場合は、まず最も人間らしいRealtime API音声合成を試す。
            // 失敗したら従来のHTTP TTS（表現付き→tts-1）へフォールバックする。
            if (!string.IsNullOrEmpty(instructions) && _realtimeTtsFailures < RealtimeTtsMaxFailures)
            {
                AudioClip rtClip = null;
                string rtErr = null;
                yield return RealtimeAudioClient.Synthesize(text, instructions, key,
                    c => rtClip = c, e => rtErr = e,
                    realtimeVoice ?? RealtimeAudioClient.MaleVoice);
                if (rtClip != null)
                {
                    _realtimeTtsFailures = 0;
                    yield return PlayClip(rtClip, audioSource, volume);
                    onComplete?.Invoke();
                    yield break;
                }
                _realtimeTtsFailures++;
                Debug.LogWarning($"[TTS] Realtime音声合成失敗（{rtErr}）。HTTP TTSへフォールバックします" +
                    (_realtimeTtsFailures >= RealtimeTtsMaxFailures ? "（以後このセッションではHTTP TTSを使用）" : ""));
            }

            // Realtimeが使えない場合も、高品質なgpt-audio音声を優先する。
            if (!string.IsNullOrEmpty(instructions) && !_premiumAudioUnavailable)
            {
                byte[] premiumWav = null;
                string premiumError = null;
                yield return RequestPremiumWav(text, instructions, SanitizeVoice(voice), key,
                    bytes => premiumWav = bytes,
                    error => premiumError = error);
                if (IsWav(premiumWav))
                {
                    AudioClip premiumClip = WavToAudioClip(premiumWav, "PremiumTTS");
                    if (premiumClip != null)
                    {
                        yield return PlayClip(premiumClip, audioSource, volume);
                        onComplete?.Invoke();
                        yield break;
                    }
                }
                Debug.LogWarning($"[TTS] {PremiumAudioModel}音声生成失敗（{premiumError}）。Speech APIへフォールバックします");
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
            bool expressive = !string.IsNullOrEmpty(instructions) && !_expressiveUnavailable;
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
                {
                    // 権限エラー（403/404）はこのセッション中ずっと使えないので記憶し、以後は最初からtts-1を使う
                    if (req.responseCode == 403 || req.responseCode == 404)
                    {
                        _expressiveUnavailable = true;
                        Debug.LogWarning($"[TTS] 表現付きTTSモデル({ExpressiveModel})はこのAPIキーで使用不可。以後はtts-1を使用します");
                    }
                    else
                        Debug.LogWarning($"[TTS] 表現付きTTS失敗（{req.error}）。標準TTSで再試行します");
                }
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

            yield return PlayClip(clip, audioSource, volume);
            onComplete?.Invoke();
        }

        // クリップを再生し、再生完了まで実時間で待って破棄する（発話中フラグも更新）。
        static IEnumerator PlayClip(AudioClip clip, AudioSource audioSource, float volume)
        {
            if (audioSource != null)
            {
                _speechEndTime = Mathf.Max(_speechEndTime, Time.unscaledTime + clip.length);
                audioSource.PlayOneShot(clip, volume);
                // 音声はスローモーション中も実時間で流れるため、待ちも実時間で行う
                yield return new WaitForSecondsRealtime(clip.length);
            }

            // AudioClip.Createで確保したネイティブリソースはGC対象外。再生完了後に明示破棄してリークを防ぐ。
            UnityEngine.Object.Destroy(clip);
        }

        static IEnumerator RequestPremiumWav(string text, string instructions, string voice, string key,
            Action<byte[]> onSuccess, Action<string> onError)
        {
            voice = SanitizePremiumVoice(voice);
            string direction = string.IsNullOrWhiteSpace(instructions)
                ? "自然な日本語で、キャラクター本人として感情を込めて演じる。"
                : instructions.Trim();
            string prompt =
                "あなたは日本の対戦アクションゲームに出演するプロ声優です。" +
                "次の台詞だけを一言一句そのまま発声してください。前置き、説明、相づち、言い直し、別の言葉は絶対に追加しません。" +
                "棒読みを避け、感情の高まり、自然な間、息遣い、声の強弱を使い、実戦の瞬間として臨場感豊かに演じてください。" +
                "\n演技指示: " + direction + "\n台詞: 「" + text.Trim() + "」";
            string body =
                $"{{\"model\":\"{PremiumAudioModel}\"," +
                "\"modalities\":[\"text\",\"audio\"]," +
                $"\"audio\":{{\"voice\":\"{EscapeJson(voice)}\",\"format\":\"wav\"}}," +
                $"\"messages\":[{{\"role\":\"user\",\"content\":\"{EscapeJson(prompt)}\"}}]}}";

            string lastError = null;
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                using var req = BuildRequest(body, key, ChatEndpoint);
                req.timeout = 45;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    byte[] wav = ExtractPremiumWav(req.downloadHandler.text, out string parseError);
                    if (IsWav(wav))
                    {
                        onSuccess?.Invoke(wav);
                        yield break;
                    }
                    lastError = parseError;
                    break;
                }

                lastError = $"{req.responseCode} {req.error}: {req.downloadHandler?.text}";
                if (req.responseCode == 403 || req.responseCode == 404)
                {
                    _premiumAudioUnavailable = true;
                    break;
                }
                if (!IsTransient(req) || attempt >= 2) break;
                yield return new WaitForSecondsRealtime(RetryDelay(req, attempt));
            }
            onError?.Invoke(lastError ?? $"{PremiumAudioModel}音声生成に失敗");
        }

        static byte[] ExtractPremiumWav(string json, out string error)
        {
            error = null;
            try
            {
                var response = JsonUtility.FromJson<AudioChatResponse>(json);
                string data = response?.choices != null && response.choices.Length > 0
                    ? response.choices[0]?.message?.audio?.data
                    : null;
                if (string.IsNullOrEmpty(data))
                {
                    error = "高品質音声レスポンスにaudio.dataがありません";
                    return null;
                }
                return Convert.FromBase64String(data);
            }
            catch (Exception e)
            {
                error = "高品質音声レスポンス解析失敗: " + e.Message;
                return null;
            }
        }

        static UnityWebRequest BuildRequest(string body, string key, string endpoint = SpeechEndpoint)
        {
            var req = new UnityWebRequest(endpoint, "POST");
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

        static string SanitizeCharacterVoice(string voice)
        {
            string value = voice?.Trim().ToLowerInvariant();
            return PromptFighters.Battle.Skills.CharacterVoiceProfile.IsSupportedPreset(value)
                ? value
                : "coral";
        }

        // gpt-audio系で共通利用できる声へ寄せる。旧TTS専用の声は近い印象の声に対応付ける。
        static string SanitizePremiumVoice(string voice)
        {
            switch (voice?.Trim().ToLowerInvariant())
            {
                case "alloy": case "ash": case "coral": case "echo": case "sage": case "shimmer":
                    return voice.Trim().ToLowerInvariant();
                case "fable": return "sage";
                case "onyx":  return "ash";
                case "nova":  return "coral";
                default:      return "coral";
            }
        }

        static string EscapeJson(string value) => (value ?? "")
            .Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");

        static bool IsTransient(UnityWebRequest req)
        {
            if (req == null) return true;
            long code = req.responseCode;
            return code == 0 || code == 408 || code == 409 || code == 425 || code == 429 || code >= 500;
        }

        static float RetryDelay(UnityWebRequest req, int attempt)
        {
            string retryAfter = req?.GetResponseHeader("Retry-After");
            if (float.TryParse(retryAfter, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float seconds) && seconds > 0f)
                return Mathf.Min(seconds, 30f);
            return Mathf.Min(Mathf.Pow(2f, attempt), 8f) + UnityEngine.Random.Range(0.1f, 0.6f);
        }

        static bool IsWav(byte[] data) => data != null && data.Length > 44 &&
            data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F';

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

                NormalizeSamples(samples);

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

        // AI音声の個体差で小さく聞こえる場合に備え、音割れを避けながら最大3倍まで持ち上げる。
        public static void NormalizeSamples(float[] samples)
        {
            if (samples == null || samples.Length == 0) return;
            float peak = 0f;
            for (int i = 0; i < samples.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
            if (peak < 0.001f || peak >= 0.92f) return;
            float gain = Mathf.Min(0.92f / peak, 3f);
            for (int i = 0; i < samples.Length; i++) samples[i] = Mathf.Clamp(samples[i] * gain, -1f, 1f);
        }

        [Serializable] class AudioChatResponse { public AudioChatChoice[] choices; }
        [Serializable] class AudioChatChoice { public AudioChatMessage message; }
        [Serializable] class AudioChatMessage { public AudioChatPayload audio; }
        [Serializable] class AudioChatPayload { public string data; }
    }

    public static class CharacterVoiceGenerator
    {
        static readonly string[] Filenames = { "intro", "attack_a", "attack_b", "attack_c", "smash_side" };

        public static Coroutine GenerateSet(MonoBehaviour runner,
            PromptFighters.Battle.Skills.CharacterData data,
            Action<string> onProgress,
            Action<int> onComplete)
        {
            return runner.StartCoroutine(GenerateSetCoroutine(runner, data, onProgress, onComplete));
        }

        static IEnumerator GenerateSetCoroutine(MonoBehaviour runner,
            PromptFighters.Battle.Skills.CharacterData data,
            Action<string> onProgress, Action<int> onComplete)
        {
            if (data == null || string.IsNullOrEmpty(data.voiceDir) ||
                !AIImageClient.HasConfiguredApiKey(out _))
            {
                onComplete?.Invoke(0);
                yield break;
            }

            data.voiceProfile ??= new PromptFighters.Battle.Skills.CharacterVoiceProfile();
            data.voiceProfile.FillDefaults(data);

            try { Directory.CreateDirectory(data.voiceDir); }
            catch (Exception e)
            {
                Debug.LogWarning($"[CharacterVoice] 保存先作成失敗: {e.Message}");
                onComplete?.Invoke(0);
                yield break;
            }

            string[] lines = new string[5];
            lines[0] = data.voiceProfile.introLine;
            for (int i = 0; i < 4; i++) lines[i + 1] = data.voiceProfile.skillLines[i];

            int succeeded = 0;
            float startedAt = Time.realtimeSinceStartup;
            for (int i = 0; i < lines.Length; i++)
            {
                if (Time.realtimeSinceStartup - startedAt > 240f)
                {
                    Debug.LogWarning("[CharacterVoice] ボイス生成が4分を超えたため、残りをスキップします");
                    break;
                }
                onProgress?.Invoke($"AI生成ボイスを作成中 {i + 1}/{lines.Length}: {lines[i]}");
                byte[] wav = null;
                string error = null;
                bool done = false;
                string momentDirection = i == 0
                    ? "登場して相手と観客に存在感を示す瞬間。自信と覚悟を込め、台詞の後に余韻を残す。"
                    : i == 4
                        ? "必殺技を解き放つ最高潮の瞬間。腹から声を出し、全力の気迫と決着をつける覚悟を込める。"
                        : "攻撃を繰り出す一瞬。戦闘中の呼吸と勢いを感じさせ、短く鋭く感情を爆発させる。";
                string actingDirection = data.voiceProfile.instructions + " " + momentDirection;
                AITTSClient.GenerateWav(runner, lines[i], actingDirection, data.voiceProfile.preset,
                    bytes => { wav = bytes; done = true; },
                    err => { error = err; done = true; });
                yield return new WaitUntil(() => done);

                if (wav == null)
                {
                    Debug.LogWarning($"[CharacterVoice] {Filenames[i]}生成失敗: {error}");
                    continue;
                }

                try
                {
                    File.WriteAllBytes(Path.Combine(data.voiceDir, Filenames[i] + ".wav"), wav);
                    succeeded++;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[CharacterVoice] {Filenames[i]}保存失敗: {e.Message}");
                }
            }

            data.voiceProfile.generated = succeeded > 0;
            onComplete?.Invoke(succeeded);
        }
    }
}
