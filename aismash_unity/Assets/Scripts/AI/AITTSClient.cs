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
        public enum CharacterVoiceBackend
        {
            Realtime,
            Realtime21,
            PremiumAudio,
            ExpressiveSpeech,
            HighDefinitionSpeech,
            StandardSpeech,
        }

        const string SpeechEndpoint = "https://api.openai.com/v1/audio/speech";
        const string ChatEndpoint   = "https://api.openai.com/v1/chat/completions";
        const string Model    = "tts-1";
        const string HighDefinitionModel = "tts-1-hd";
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
            CharacterVoiceBackend backend, Action<byte[]> onSuccess, Action<string> onError)
        {
            return runner.StartCoroutine(GenerateWavCoroutine(text, instructions, voice, backend, onSuccess, onError));
        }

        // 保存用5台詞を1つのRealtimeセッションで生成し、同一人物の声色を保ったWAVセットとして返す。
        public static Coroutine GenerateRealtimeWavSet(MonoBehaviour runner, string[] texts, string[] instructions,
            string voice, string model, Action<int> onProgress, Action<byte[][]> onSuccess, Action<string> onError)
        {
            return runner.StartCoroutine(GenerateRealtimeWavSetCoroutine(
                texts, instructions, voice, model, onProgress, onSuccess, onError));
        }

        static IEnumerator GenerateRealtimeWavSetCoroutine(string[] texts, string[] instructions,
            string voice, string model, Action<int> onProgress, Action<byte[][]> onSuccess, Action<string> onError)
        {
            string key = AIImageClient.ApiKey;
            if (!AIImageClient.IsConfiguredApiKey(key))
            {
                onError?.Invoke("APIキー未設定");
                yield break;
            }

            string safeVoice = SanitizeCharacterVoice(voice);
            string lastError = null;
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                AudioClip[] clips = null;
                lastError = null;
                byte[][] successfulWavs = null;
                try
                {
                    yield return RealtimeAudioClient.SynthesizeBatch(texts, instructions, key,
                        result => clips = result,
                        onProgress,
                        error => lastError = error,
                        safeVoice,
                        model);

                    if (clips != null)
                    {
                        var wavs = new byte[clips.Length][];
                        bool valid = clips.Length == texts.Length;
                        if (valid)
                        {
                            for (int i = 0; i < clips.Length; i++)
                            {
                                wavs[i] = AudioClipToWav(clips[i]);
                                if (!IsWav(wavs[i])) valid = false;
                            }
                        }
                        if (valid) successfulWavs = wavs;
                        else lastError = "Realtime音声セットのWAV変換または完全性検証に失敗";
                    }
                }
                finally
                {
                    // nested IEnumeratorの再開境界で親が停止されても、受領済みclipを必ず破棄する。
                    if (clips != null)
                        for (int i = 0; i < clips.Length; i++)
                            if (clips[i] != null) UnityEngine.Object.Destroy(clips[i]);
                }
                if (successfulWavs != null)
                {
                    onSuccess?.Invoke(successfulWavs);
                    yield break;
                }

                if (attempt >= 2 || IsPermanentRealtimeError(lastError)) break;
                yield return new WaitForSecondsRealtime(1.25f);
            }

            onError?.Invoke(lastError ?? "Realtime音声セット生成に失敗");
        }

        static IEnumerator GenerateWavCoroutine(string text, string instructions, string voice,
            CharacterVoiceBackend backend, Action<byte[]> onSuccess, Action<string> onError)
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

            if (backend == CharacterVoiceBackend.Realtime || backend == CharacterVoiceBackend.Realtime21)
            {
                string realtimeModel = backend == CharacterVoiceBackend.Realtime21
                    ? RealtimeAudioClient.RealtimeFallbackModel
                    : RealtimeAudioClient.RealtimeModel;
                AudioClip realtimeClip = null;
                for (int attempt = 1; attempt <= 2 && realtimeClip == null; attempt++)
                {
                    lastError = null;
                    yield return RealtimeAudioClient.Synthesize(text.Trim(), instructions, key,
                        clip => realtimeClip = clip,
                        error => lastError = error,
                        safeVoice,
                        realtimeModel);
                    if (realtimeClip == null && attempt < 2)
                        yield return new WaitForSecondsRealtime(1.25f);
                }

                if (realtimeClip != null)
                {
                    wavData = AudioClipToWav(realtimeClip);
                    UnityEngine.Object.Destroy(realtimeClip);
                }
                lastError ??= "Realtime音声のWAV変換に失敗";
            }
            else if (backend == CharacterVoiceBackend.PremiumAudio)
            {
                if (_premiumAudioUnavailable)
                    lastError = PremiumAudioModel + "はこのセッションで利用できません";
                else
                    yield return RequestPremiumWav(text.Trim(), instructions, safeVoice, key,
                        bytes => wavData = bytes,
                        error => lastError = error);
            }
            else
            {
                bool expressive = backend == CharacterVoiceBackend.ExpressiveSpeech;
                bool highDefinition = backend == CharacterVoiceBackend.HighDefinitionSpeech;
                if (expressive && _expressiveUnavailable)
                    lastError = ExpressiveModel + "はこのセッションで利用できません";
                else
                {
                    string body = expressive
                        ? $"{{\"model\":\"{ExpressiveModel}\",\"input\":\"{safeText}\"," +
                          $"\"voice\":\"{safeVoice}\",\"instructions\":\"{safeInstructions}\"," +
                          $"\"response_format\":\"wav\"}}"
                        : $"{{\"model\":\"{(highDefinition ? HighDefinitionModel : Model)}\",\"input\":\"{safeText}\"," +
                          $"\"voice\":\"{SanitizeLegacyVoice(safeVoice)}\"," +
                          $"\"speed\":1.0,\"response_format\":\"wav\"}}";
                    for (int attempt = 1; attempt <= 2; attempt++)
                    {
                        using var req = BuildRequest(body, key);
                        req.timeout = 30;
                        yield return req.SendWebRequest();
                        if (req.result == UnityWebRequest.Result.Success)
                        {
                            wavData = req.downloadHandler.data;
                            break;
                        }

                        lastError = $"{req.responseCode} {req.error}: {req.downloadHandler?.text}";
                        if (expressive && (req.responseCode == 403 || req.responseCode == 404))
                        {
                            _expressiveUnavailable = true;
                            break;
                        }
                        if (!IsTransient(req) || attempt >= 2) break;
                        yield return new WaitForSecondsRealtime(RetryDelay(req, attempt));
                    }
                }
            }

            if (!IsWav(wavData))
            {
                onError?.Invoke(lastError ?? $"{backend}でキャラボイスWAV生成に失敗");
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
                string[] realtimeModels =
                {
                    RealtimeAudioClient.RealtimeModel,
                    RealtimeAudioClient.RealtimeFallbackModel,
                };
                for (int i = 0; i < realtimeModels.Length && rtClip == null; i++)
                {
                    rtErr = null;
                    yield return RealtimeAudioClient.Synthesize(text, instructions, key,
                        c => rtClip = c, e => rtErr = e,
                        realtimeVoice ?? RealtimeAudioClient.MaleVoice,
                        realtimeModels[i]);
                    if (rtClip == null && IsPermanentRealtimeError(rtErr)) continue;
                }
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
                $"\"voice\":\"{SanitizeLegacyVoice(voice)}\"," +
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
            try
            {
                if (audioSource != null)
                {
                    _speechEndTime = Mathf.Max(_speechEndTime, Time.unscaledTime + clip.length);
                    audioSource.PlayOneShot(clip, volume);
                    // 音声はスローモーション中も実時間で流れるため、待ちも実時間で行う
                    yield return new WaitForSecondsRealtime(clip.length);
                }
            }
            finally
            {
                // StopCoroutineや画面破棄でもAudioClip.Create由来のネイティブ資源を必ず解放する。
                if (clip != null) UnityEngine.Object.Destroy(clip);
            }
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
                : "alloy";
        }

        // gpt-audio / Realtimeで共通利用できる声へ寄せる。
        static string SanitizePremiumVoice(string voice)
        {
            switch (voice?.Trim().ToLowerInvariant())
            {
                case "alloy": case "ash": case "ballad": case "coral": case "echo":
                case "sage": case "shimmer": case "verse": case "marin": case "cedar":
                    return voice.Trim().ToLowerInvariant();
                case "fable": return "sage";
                case "onyx":  return "ash";
                case "nova":  return "coral";
                default:      return "alloy";
            }
        }

        // このプロジェクトのtts-1/tts-1-hd権限ではcedar・marin等の新しい声が拒否される。
        // 高品質モデルでは元の声を維持し、旧Speechモデルへ落ちた場合だけ同性寄りの対応声へ変換する。
        static string SanitizeLegacyVoice(string voice)
        {
            switch (voice?.Trim().ToLowerInvariant())
            {
                case "cedar": return "ash";
                case "marin": return "shimmer";
                case "ballad": case "verse": return "alloy";
                case "alloy": case "ash": case "coral": case "echo": case "fable":
                case "onyx": case "nova": case "sage": case "shimmer":
                    return voice.Trim().ToLowerInvariant();
                default: return "alloy";
            }
        }

        static string EscapeJson(string value) => (value ?? "")
            .Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");

        static bool IsTransient(UnityWebRequest req)
        {
            if (req == null) return true;
            long code = req.responseCode;
            return code == 0 || code == 408 || code == 409 || code == 425 || code == 429 || code >= 500;
        }

        static bool IsPermanentRealtimeError(string error)
        {
            if (string.IsNullOrWhiteSpace(error)) return false;
            string value = error.ToLowerInvariant();
            return value.Contains("not found") || value.Contains("does not exist") ||
                   value.Contains("permission") || value.Contains("not have access") ||
                   value.Contains("unsupported") || value.Contains("invalid api key") ||
                   value.Contains("model_not_found");
        }

        static float RetryDelay(UnityWebRequest req, int attempt)
        {
            string retryAfter = req?.GetResponseHeader("Retry-After");
            if (float.TryParse(retryAfter, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float seconds) && seconds > 0f)
                return Mathf.Min(seconds, 30f);
            return Mathf.Min(Mathf.Pow(2f, attempt), 8f) + UnityEngine.Random.Range(0.1f, 0.6f);
        }

        struct PcmWavInfo
        {
            public int channels;
            public int sampleRate;
            public int bitsPerSample;
            public int blockAlign;
            public int dataOffset;
            public int dataLength;
        }

        static bool IsWav(byte[] data) => TryNormalizeAndParseWav(data, out _);

        // 保存復旧側も生成側と同じ厳密な判定を使い、仕様ずれで正常な旧音声を捨てないようにする。
        // streaming sentinelを持つ入力は、全検証に成功した場合だけ受信済み実長へ書き換える。
        public static bool NormalizeAndValidateWav(byte[] data) => TryNormalizeAndParseWav(data, out _);

        static bool TryNormalizeAndParseWav(byte[] data, out PcmWavInfo info)
        {
            info = default;
            if (data == null || data.Length <= 44 ||
                data[0] != 'R' || data[1] != 'I' || data[2] != 'F' || data[3] != 'F' ||
                data[8] != 'W' || data[9] != 'A' || data[10] != 'V' || data[11] != 'E') return false;

            uint declaredRiffSize = ReadUInt32LittleEndian(data, 4);
            bool streamedRiff = declaredRiffSize == uint.MaxValue;
            ulong declaredEnd = streamedRiff ? (ulong)data.Length : (ulong)declaredRiffSize + 8UL;
            if (!streamedRiff && (declaredRiffSize < 36 || declaredEnd > (ulong)data.Length)) return false;

            bool hasFormat = false;
            bool hasAudioData = false;
            int streamedDataSizeOffset = -1;
            ulong offset = 12;
            while (offset + 8UL <= declaredEnd)
            {
                int index = (int)offset;
                bool isFormat = data[index] == 'f' && data[index + 1] == 'm' &&
                                data[index + 2] == 't' && data[index + 3] == ' ';
                bool isData = data[index] == 'd' && data[index + 1] == 'a' &&
                              data[index + 2] == 't' && data[index + 3] == 'a';
                uint chunkSize = ReadUInt32LittleEndian(data, index + 4);
                bool streamedData = chunkSize == uint.MaxValue;
                if (streamedData)
                {
                    // sentinelは、RIFFも不定長で、このdataが末尾まで続く場合だけ許容する。
                    if (!streamedRiff || !isData || declaredEnd <= offset + 8UL ||
                        declaredEnd - offset - 8UL > uint.MaxValue) return false;
                    chunkSize = (uint)(declaredEnd - offset - 8UL);
                    streamedDataSizeOffset = index + 4;
                }

                ulong chunkEnd = offset + 8UL + chunkSize;
                if (chunkEnd > declaredEnd || chunkEnd > (ulong)data.Length) return false;

                if (isFormat)
                {
                    if (hasFormat || streamedData || chunkSize < 16) return false;
                    int payload = index + 8;
                    int audioFormat = ReadUInt16LittleEndian(data, payload);
                    int channels = ReadUInt16LittleEndian(data, payload + 2);
                    uint sampleRate = ReadUInt32LittleEndian(data, payload + 4);
                    uint byteRate = ReadUInt32LittleEndian(data, payload + 8);
                    int blockAlign = ReadUInt16LittleEndian(data, payload + 12);
                    int bitsPerSample = ReadUInt16LittleEndian(data, payload + 14);
                    int bytesPerSample = bitsPerSample / 8;
                    if (audioFormat != 1 || channels < 1 || channels > 8 ||
                        sampleRate == 0 || sampleRate > 384000 ||
                        (bitsPerSample != 8 && bitsPerSample != 16) ||
                        blockAlign != channels * bytesPerSample ||
                        byteRate != (ulong)sampleRate * (uint)blockAlign) return false;
                    info.channels = channels;
                    info.sampleRate = (int)sampleRate;
                    info.bitsPerSample = bitsPerSample;
                    info.blockAlign = blockAlign;
                    hasFormat = true;
                }
                else if (isData)
                {
                    if (hasAudioData || chunkSize == 0 || chunkSize > int.MaxValue) return false;
                    info.dataOffset = index + 8;
                    info.dataLength = (int)chunkSize;
                    hasAudioData = true;
                }

                offset = streamedData ? declaredEnd : chunkEnd + (chunkSize & 1U);
            }

            if (!hasFormat || !hasAudioData || offset != declaredEnd ||
                info.dataLength % info.blockAlign != 0) return false;

            // Speech APIのストリーミングWAVは、全受信後もRIFF/data長が0xFFFFFFFFのことがある。
            // 新規保存時には通常WAVとして扱えるよう、検証完了後だけ実長で確定する。
            if (streamedRiff)
                WriteUInt32LittleEndian(data, 4, (uint)(data.Length - 8));
            if (streamedDataSizeOffset >= 0)
                WriteUInt32LittleEndian(data, streamedDataSizeOffset, (uint)info.dataLength);
            return true;
        }

        static int ReadUInt16LittleEndian(byte[] data, int offset) =>
            data[offset] | (data[offset + 1] << 8);

        static uint ReadUInt32LittleEndian(byte[] data, int offset) =>
            (uint)(data[offset] | (data[offset + 1] << 8) |
                   (data[offset + 2] << 16) | (data[offset + 3] << 24));

        static void WriteUInt32LittleEndian(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }

        // WAVバイト列（PCM16）を AudioClip に変換する
        public static AudioClip WavToAudioClip(byte[] wav, string clipName)
        {
            try
            {
                if (!TryNormalizeAndParseWav(wav, out PcmWavInfo info)) return null;
                int bytesPerSample = info.bitsPerSample / 8;
                int sampleCount = info.dataLength / bytesPerSample;
                float[] samples    = new float[sampleCount];

                for (int i = 0; i < sampleCount; i++)
                {
                    int idx = info.dataOffset + i * bytesPerSample;
                    samples[i] = info.bitsPerSample == 16
                        ? (short)(wav[idx] | (wav[idx + 1] << 8)) / 32768f
                        : (wav[idx] - 128) / 128f;
                }

                NormalizeSamples(samples);

                var clip = AudioClip.Create(clipName, sampleCount / info.channels,
                    info.channels, info.sampleRate, false);
                clip.SetData(samples, 0);
                return clip;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TTS] WAV解析失敗: {e.Message}");
                return null;
            }
        }

        // Realtime APIが返したAudioClipを、保存・再利用できるPCM16 WAVへ変換する。
        static byte[] AudioClipToWav(AudioClip clip)
        {
            try
            {
                if (clip == null || clip.samples <= 0 || clip.channels <= 0 || clip.frequency <= 0)
                    return null;

                var samples = new float[clip.samples * clip.channels];
                if (!clip.GetData(samples, 0)) return null;

                int dataBytes = samples.Length * 2;
                using var stream = new MemoryStream(44 + dataBytes);
                using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataBytes);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)clip.channels);
                writer.Write(clip.frequency);
                writer.Write(clip.frequency * clip.channels * 2);
                writer.Write((short)(clip.channels * 2));
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataBytes);
                for (int i = 0; i < samples.Length; i++)
                {
                    float clamped = Mathf.Clamp(samples[i], -1f, 1f);
                    writer.Write((short)Mathf.RoundToInt(clamped * 32767f));
                }
                writer.Flush();
                return stream.ToArray();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TTS] AudioClipのWAV変換失敗: " + e.Message);
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
        static readonly AITTSClient.CharacterVoiceBackend[] BackendOrder =
        {
            AITTSClient.CharacterVoiceBackend.Realtime,
            AITTSClient.CharacterVoiceBackend.Realtime21,
            AITTSClient.CharacterVoiceBackend.PremiumAudio,
            AITTSClient.CharacterVoiceBackend.ExpressiveSpeech,
            AITTSClient.CharacterVoiceBackend.HighDefinitionSpeech,
            AITTSClient.CharacterVoiceBackend.StandardSpeech,
        };
        static readonly object ActiveWorkDirectoryLock = new object();
        static readonly System.Collections.Generic.HashSet<string> ActiveWorkDirectories =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public const string VoiceSwapMarkerFilename = "voices_swap_pending";
        const float PerVoiceRequestTimeoutSeconds = 130f;
        // 1接続は8秒+設定8秒+5台詞×30秒。2試行を丸ごと行える予算を各Realtimeモデルへ渡す。
        const float RealtimeSetRequestTimeoutSeconds = 340f;
        // 最後のtts-1（5件×最大約90秒）を必ず試せる時間を、上位モデルより先に予約する。
        const float FinalStandardReserveSeconds = 480f;
        const float VoiceSetTimeoutSeconds = 1200f;
        const float AtomicGenerationWatchdogSeconds = 1220f;

        public static bool IsActiveWorkDirectory(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            lock (ActiveWorkDirectoryLock) return ActiveWorkDirectories.Contains(Path.GetFullPath(path));
        }

        public static string GetVoiceSwapMarkerPath(string targetDir)
        {
            string parentDir = string.IsNullOrEmpty(targetDir) ? null : Path.GetDirectoryName(targetDir);
            return string.IsNullOrEmpty(parentDir) ? null : Path.Combine(parentDir, VoiceSwapMarkerFilename);
        }

        static void SetWorkDirectoryActive(string path, bool active)
        {
            if (string.IsNullOrEmpty(path)) return;
            string fullPath = Path.GetFullPath(path);
            lock (ActiveWorkDirectoryLock)
            {
                if (active) ActiveWorkDirectories.Add(fullPath);
                else ActiveWorkDirectories.Remove(fullPath);
            }
        }

        public static Coroutine GenerateSet(MonoBehaviour runner,
            PromptFighters.Battle.Skills.CharacterData data,
            Action<string> onProgress,
            Action<int> onComplete)
        {
            return runner.StartCoroutine(GenerateSetCoroutine(runner, data, data?.voiceDir, onProgress, onComplete));
        }

        // 保存済みキャラの再生成用。5件を一時ディレクトリへ揃えてから差し替え、
        // 1件でも失敗した場合は従来の音声セットをそのまま残す。
        public static Coroutine RegenerateSetAtomically(MonoBehaviour runner,
            PromptFighters.Battle.Skills.CharacterData data,
            Action<string> onProgress,
            Func<bool> commitMetadata,
            Action<bool, int, string> onComplete)
        {
            return runner.StartCoroutine(RegenerateSetAtomicallyCoroutine(
                runner, data, onProgress, commitMetadata, onComplete));
        }

        static IEnumerator RegenerateSetAtomicallyCoroutine(MonoBehaviour runner,
            PromptFighters.Battle.Skills.CharacterData data,
            Action<string> onProgress,
            Func<bool> commitMetadata,
            Action<bool, int, string> onComplete)
        {
            string targetDir = data?.voiceDir;
            string parentDir = string.IsNullOrEmpty(targetDir) ? null : Path.GetDirectoryName(targetDir);
            if (data == null || string.IsNullOrEmpty(targetDir) || string.IsNullOrEmpty(parentDir))
            {
                onComplete?.Invoke(false, 0, "ボイス保存先がありません");
                yield break;
            }

            data.voiceProfile ??= new PromptFighters.Battle.Skills.CharacterVoiceProfile();
            bool oldGenerated = data.voiceProfile.generated;
            int oldQualityVersion = data.voiceProfile.qualityVersion;
            string oldGenerationId = data.voiceProfile.generationId;
            string newGenerationId = Guid.NewGuid().ToString("N");
            string tempDir = Path.Combine(parentDir, "voices_pending_" + Guid.NewGuid().ToString("N"));
            string backupDir = Path.Combine(parentDir, "voices_backup_" + Guid.NewGuid().ToString("N"));
            string swapMarkerPath = GetVoiceSwapMarkerPath(targetDir);

            int generatedCount = 0;
            bool generationDone = false;
            SetWorkDirectoryActive(tempDir, true);
            Coroutine generationCoroutine = null;
            try
            {
                generationCoroutine = runner.StartCoroutine(GenerateSetCoroutine(runner, data, tempDir, onProgress,
                    count =>
                    {
                        generatedCount = count;
                        generationDone = true;
                    }));
                float watchdogDeadline = Time.realtimeSinceStartup + AtomicGenerationWatchdogSeconds;
                while (!generationDone && Time.realtimeSinceStartup < watchdogDeadline)
                    yield return null;
                if (!generationDone)
                {
                    generatedCount = 0;
                    Debug.LogWarning("[CharacterVoice] ボイスセット生成が監視期限を超えたため中止しました");
                }
            }
            finally
            {
                // 親UIの中断や未知の例外でも、通信コルーチンと作業中マーカーを残さない。
                if (!generationDone && generationCoroutine != null)
                    runner.StopCoroutine(generationCoroutine);
                SetWorkDirectoryActive(tempDir, false);
                if (!generationDone)
                    TryDeleteDirectory(tempDir);
            }

            if (generatedCount != Filenames.Length)
            {
                TryDeleteDirectory(tempDir);
                data.voiceProfile.generated = oldGenerated;
                data.voiceProfile.qualityVersion = oldQualityVersion;
                data.voiceProfile.generationId = oldGenerationId;
                onComplete?.Invoke(false, generatedCount,
                    !generationDone
                        ? "ボイス生成が安全期限を超えました。以前の音声を維持しました"
                        : $"{generatedCount}/{Filenames.Length}件のみ成功。以前の音声を維持しました");
                yield break;
            }

            bool movedOld = false;
            bool movedNew = false;
            bool transactionCommitted = false;
            try
            {
                WriteVoiceSwapMarker(swapMarkerPath, newGenerationId);
                if (Directory.Exists(targetDir))
                {
                    Directory.Move(targetDir, backupDir);
                    movedOld = true;
                }
                Directory.Move(tempDir, targetDir);
                movedNew = true;

                data.voiceProfile.generated = true;
                data.voiceProfile.qualityVersion = PromptFighters.Battle.Skills.CharacterVoiceProfile.CurrentQualityVersion;
                data.voiceProfile.generationId = newGenerationId;
                // JSONも同じトランザクションとして確定する。失敗時は下のcatchで旧WAVと旧メタデータを戻す。
                if (commitMetadata != null && !commitMetadata())
                    throw new IOException("キャラクター設定JSONを保存できませんでした");

                // JSONに新世代IDが確定した後でmarkerを消す。ここで終了してbackupが残っても、
                // 次回ロードはJSONとmarkerの世代一致から新セットを正しく選べる。
                TryDeleteFile(swapMarkerPath, "確定済みボイス差し替えマーカー");
                // 音声とJSONがともに確定してから旧セットを破棄する。
                if (movedOld)
                    TryDeleteDirectory(backupDir);
                transactionCommitted = true;
            }
            catch (Exception e)
            {
                bool newSetRemoved = !movedNew || !Directory.Exists(targetDir);
                if (movedNew && Directory.Exists(targetDir))
                {
                    try
                    {
                        // tempDirはtargetへ移動済みで空いているため、失敗した新セットの退避先として再利用する。
                        Directory.Move(targetDir, tempDir);
                        newSetRemoved = true;
                    }
                    catch (Exception removeNewError)
                    {
                        Debug.LogError("[CharacterVoice] 失敗した新ボイスの退避失敗: " + removeNewError.Message);
                    }
                }
                bool oldSetRestored = !movedOld;
                if (!Directory.Exists(targetDir) && movedOld && Directory.Exists(backupDir))
                {
                    try
                    {
                        Directory.Move(backupDir, targetDir);
                        oldSetRestored = true;
                    }
                    catch (Exception restoreError)
                    {
                        Debug.LogError("[CharacterVoice] 旧ボイス復元失敗: " + restoreError.Message);
                    }
                }
                TryDeleteDirectory(tempDir);
                bool audioStateRestored = movedOld ? oldSetRestored : newSetRemoved;
                data.voiceProfile.generated = audioStateRestored && oldGenerated;
                data.voiceProfile.qualityVersion = audioStateRestored ? oldQualityVersion : 0;
                data.voiceProfile.generationId = audioStateRestored ? oldGenerationId : null;
                // 新メタデータの書込途中で失敗した場合にも、可能な限り旧状態を再保存する。
                try
                {
                    if (commitMetadata != null && !commitMetadata())
                        Debug.LogError("[CharacterVoice] 旧ボイス設定JSONの復元保存に失敗しました");
                }
                catch (Exception restoreMetadataError)
                {
                    Debug.LogError("[CharacterVoice] 旧ボイス設定JSONの復元保存で例外: " + restoreMetadataError.Message);
                }
                if (audioStateRestored)
                    TryDeleteFile(swapMarkerPath, "rollback済みボイス差し替えマーカー");
                onComplete?.Invoke(false, generatedCount, "ボイス差し替え失敗: " + e.Message);
                yield break;
            }

            // トランザクション確定後のUI通知はrollback対象にしない。
            if (transactionCommitted)
            {
                try { onComplete?.Invoke(true, generatedCount, null); }
                catch (Exception callbackError)
                {
                    Debug.LogError("[CharacterVoice] 完了通知で例外: " + callbackError.Message);
                }
            }
        }

        static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            try { Directory.Delete(path, true); }
            catch (Exception e) { Debug.LogWarning("[CharacterVoice] 一時フォルダ削除失敗: " + e.Message); }
        }

        static void WriteVoiceSwapMarker(string path, string generationId)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(generationId))
                throw new IOException("ボイス差し替えマーカーの保存先がありません");
            byte[] bytes = Encoding.UTF8.GetBytes(generationId);
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }

        static void TryDeleteFile(string path, string label)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try { File.Delete(path); }
            catch (Exception e) { Debug.LogWarning($"[CharacterVoice] {label}削除失敗: {e.Message}"); }
        }

        static IEnumerator GenerateSetCoroutine(MonoBehaviour runner,
            PromptFighters.Battle.Skills.CharacterData data,
            string outputDir,
            Action<string> onProgress, Action<int> onComplete)
        {
            if (data == null || string.IsNullOrEmpty(outputDir) ||
                !AIImageClient.HasConfiguredApiKey(out _))
            {
                onComplete?.Invoke(0);
                yield break;
            }

            data.voiceProfile ??= new PromptFighters.Battle.Skills.CharacterVoiceProfile();
            data.voiceProfile.FillDefaults(data);
            // セット生成中にUIや別処理がプロファイルを変更しても、5件は同じ人物の声を維持する。
            string voicePreset = data.voiceProfile.preset;
            string identityInstruction = data.voiceProfile.BuildIdentityInstruction();
            string baseInstructions = data.voiceProfile.instructions;

            try { Directory.CreateDirectory(outputDir); }
            catch (Exception e)
            {
                Debug.LogWarning($"[CharacterVoice] 保存先作成失敗: {e.Message}");
                onComplete?.Invoke(0);
                yield break;
            }

            string[] lines = new string[5];
            lines[0] = data.voiceProfile.introLine;
            for (int i = 0; i < 4; i++) lines[i + 1] = data.voiceProfile.skillLines[i];
            var actingDirections = new string[lines.Length];
            for (int i = 0; i < actingDirections.Length; i++)
            {
                string momentDirection = i == 0
                    ? "登場して相手と観客に存在感を示す瞬間。自信と覚悟を込め、台詞の後に余韻を残す。"
                    : i == 4
                        ? "必殺技を解き放つ最高潮の瞬間。腹から声を出し、全力の気迫と決着をつける覚悟を込める。"
                        : "攻撃を繰り出す一瞬。戦闘中の呼吸と勢いを感じさせ、短く鋭く感情を爆発させる。";
                // 自由記述より後ろに構造化された話者特性を置き、全台詞で同じ人物を固定する。
                actingDirections[i] = baseInstructions + " " + momentDirection + " " + identityInstruction;
            }

            int succeeded = 0;
            float startedAt = Time.realtimeSinceStartup;
            float overallDeadline = startedAt + VoiceSetTimeoutSeconds;
            float nonFinalDeadline = overallDeadline - FinalStandardReserveSeconds;
            bool timedOut = false;
            foreach (AITTSClient.CharacterVoiceBackend backend in BackendOrder)
            {
                bool finalStandardBackend = backend == AITTSClient.CharacterVoiceBackend.StandardSpeech;
                if (!finalStandardBackend && Time.realtimeSinceStartup >= nonFinalDeadline)
                {
                    Debug.LogWarning($"[CharacterVoice] 最終標準音声の再試行時間を確保するため{BackendLabel(backend)}をスキップします");
                    continue;
                }

                ClearVoiceFiles(outputDir);
                int backendSucceeded = 0;
                bool realtimeBackend = backend == AITTSClient.CharacterVoiceBackend.Realtime ||
                                       backend == AITTSClient.CharacterVoiceBackend.Realtime21;
                if (realtimeBackend)
                {
                    byte[][] realtimeWavs = null;
                    string realtimeError = null;
                    bool realtimeDone = false;
                    Coroutine realtimeCoroutine = null;
                    string realtimeModel = backend == AITTSClient.CharacterVoiceBackend.Realtime21
                        ? RealtimeAudioClient.RealtimeFallbackModel
                        : RealtimeAudioClient.RealtimeModel;
                    try
                    {
                        realtimeCoroutine = AITTSClient.GenerateRealtimeWavSet(runner, lines, actingDirections,
                            voicePreset, realtimeModel,
                            step =>
                            {
                                if (step >= 0 && step < lines.Length)
                                    onProgress?.Invoke($"{BackendLabel(backend)}でボイス作成中 {step + 1}/{lines.Length}: {lines[step]}");
                            },
                            wavs => { realtimeWavs = wavs; realtimeDone = true; },
                            err => { realtimeError = err; realtimeDone = true; });
                    }
                    catch (Exception e)
                    {
                        realtimeError = "Realtime音声セット開始失敗: " + e.Message;
                        realtimeDone = true;
                    }

                    float realtimeDeadline = Mathf.Min(
                        Time.realtimeSinceStartup + RealtimeSetRequestTimeoutSeconds,
                        nonFinalDeadline);
                    try
                    {
                        while (!realtimeDone && Time.realtimeSinceStartup < realtimeDeadline)
                            yield return null;
                    }
                    finally
                    {
                        if (!realtimeDone && realtimeCoroutine != null)
                            runner.StopCoroutine(realtimeCoroutine);
                    }
                    if (!realtimeDone)
                        realtimeError = "Realtime音声セットが安全期限を超えました";

                    if (realtimeWavs != null && realtimeWavs.Length == Filenames.Length)
                    {
                        for (int i = 0; i < realtimeWavs.Length; i++)
                        {
                            try
                            {
                                WriteVoiceFileAtomically(Path.Combine(outputDir, Filenames[i] + ".wav"), realtimeWavs[i]);
                                backendSucceeded++;
                            }
                            catch (Exception e)
                            {
                                realtimeError = $"{Filenames[i]}保存失敗: {e.Message}";
                                break;
                            }
                        }
                    }
                    if (backendSucceeded != Filenames.Length)
                        Debug.LogWarning($"[CharacterVoice] {BackendLabel(backend)}のセット生成失敗: {realtimeError}");
                }

                if (realtimeBackend)
                {
                    if (backendSucceeded == Filenames.Length)
                    {
                        succeeded = backendSucceeded;
                        break;
                    }
                    ClearVoiceFiles(outputDir);
                    if (Time.realtimeSinceStartup >= overallDeadline)
                    {
                        timedOut = true;
                        break;
                    }
                    Debug.LogWarning($"[CharacterVoice] {BackendLabel(backend)}では5件を統一できなかったため、次の音声モデルで全件を作り直します");
                    continue;
                }

                bool backendBudgetExhausted = false;
                float backendDeadline = finalStandardBackend ? overallDeadline : nonFinalDeadline;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (Time.realtimeSinceStartup >= backendDeadline)
                    {
                        backendBudgetExhausted = true;
                        if (finalStandardBackend)
                        {
                            timedOut = true;
                            Debug.LogWarning("[CharacterVoice] ボイス生成が20分の安全期限を超えたため中止します");
                        }
                        else
                        {
                            Debug.LogWarning($"[CharacterVoice] 最終標準音声の再試行時間を確保するため{BackendLabel(backend)}を中止します");
                        }
                        break;
                    }

                    onProgress?.Invoke($"{BackendLabel(backend)}でボイス作成中 {i + 1}/{lines.Length}: {lines[i]}");
                    byte[] wav = null;
                    string error = null;
                    bool done = false;
                    Coroutine requestCoroutine = null;
                    try
                    {
                        requestCoroutine = AITTSClient.GenerateWav(runner, lines[i], actingDirections[i], voicePreset, backend,
                            bytes => { wav = bytes; done = true; },
                            err => { error = err; done = true; });
                    }
                    catch (Exception e)
                    {
                        error = "音声リクエスト開始失敗: " + e.Message;
                        done = true;
                    }

                    float requestDeadline = Mathf.Min(
                        Time.realtimeSinceStartup + PerVoiceRequestTimeoutSeconds,
                        backendDeadline);
                    try
                    {
                        while (!done && Time.realtimeSinceStartup < requestDeadline)
                            yield return null;
                    }
                    finally
                    {
                        // セット生成自体が中断された場合も、下位のAPI通信だけが残らないようにする。
                        if (!done && requestCoroutine != null)
                            runner.StopCoroutine(requestCoroutine);
                    }
                    if (!done)
                    {
                        error = "音声リクエストが安全期限を超えました";
                        done = true;
                        if (Time.realtimeSinceStartup >= backendDeadline)
                        {
                            backendBudgetExhausted = true;
                            if (finalStandardBackend) timedOut = true;
                        }
                    }

                    if (wav == null)
                    {
                        Debug.LogWarning($"[CharacterVoice] {BackendLabel(backend)}の{Filenames[i]}生成失敗: {error}");
                        break;
                    }

                    try
                    {
                        WriteVoiceFileAtomically(Path.Combine(outputDir, Filenames[i] + ".wav"), wav);
                        backendSucceeded++;
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[CharacterVoice] {Filenames[i]}保存失敗: {e.Message}");
                        break;
                    }
                }

                if (backendSucceeded == Filenames.Length)
                {
                    succeeded = backendSucceeded;
                    break;
                }

                ClearVoiceFiles(outputDir);
                if (timedOut) break;
                if (backendBudgetExhausted) continue;
                Debug.LogWarning($"[CharacterVoice] {BackendLabel(backend)}では5件を統一できなかったため、次の音声モデルで全件を作り直します");
            }

            data.voiceProfile.generated = succeeded == Filenames.Length;
            if (data.voiceProfile.generated)
            {
                data.voiceProfile.qualityVersion = PromptFighters.Battle.Skills.CharacterVoiceProfile.CurrentQualityVersion;
                data.voiceProfile.generationId = Guid.NewGuid().ToString("N");
            }
            onComplete?.Invoke(succeeded);
        }

        static void ClearVoiceFiles(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;
            foreach (string filename in Filenames)
            {
                string path = Path.Combine(directory, filename + ".wav");
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
                }
                catch (Exception e) { Debug.LogWarning("[CharacterVoice] 不完全な音声の削除失敗: " + e.Message); }
            }
        }

        static void WriteVoiceFileAtomically(string path, byte[] wav)
        {
            string tempPath = path + ".tmp";
            if (File.Exists(tempPath)) File.Delete(tempPath);
            File.WriteAllBytes(tempPath, wav);
            if (File.Exists(path)) File.Delete(path);
            // 同一ディレクトリ内のrenameなので、書込途中のWAVが完成ファイル名で見える時間を作らない。
            File.Move(tempPath, path);
        }

        static string BackendLabel(AITTSClient.CharacterVoiceBackend backend) => backend switch
        {
            AITTSClient.CharacterVoiceBackend.Realtime => "Realtime 2最高品質音声",
            AITTSClient.CharacterVoiceBackend.Realtime21 => "Realtime 2.1高品質音声",
            AITTSClient.CharacterVoiceBackend.PremiumAudio => "高品質音声",
            AITTSClient.CharacterVoiceBackend.ExpressiveSpeech => "表現付き音声",
            AITTSClient.CharacterVoiceBackend.HighDefinitionSpeech => "高精細標準音声",
            _ => "標準音声",
        };
    }
}
