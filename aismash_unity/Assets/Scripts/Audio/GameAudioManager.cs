using UnityEngine;
using System.Collections.Generic;
using System.IO;
using PromptFighters.AI;
using PromptFighters.Battle;
using PromptFighters.Battle.Skills;
using PromptFighters.UI;

namespace PromptFighters.Audio
{
    // 設定画面と各音声再生系で共有する永続音量設定。0〜1をそのままユーザー設定値として扱う。
    public static class GameVolumeSettings
    {
        const string BgmKey        = "audio.volume.bgm";
        const string SfxKey        = "audio.volume.sfx";
        const string CharacterKey  = "audio.volume.character_voice";
        const string CommentaryKey = "audio.volume.commentary";

        public static float BgmVolume        => PlayerPrefs.GetFloat(BgmKey, 0.22f);
        public static float SfxVolume        => PlayerPrefs.GetFloat(SfxKey, 0.82f);
        public static float CharacterVolume  => PlayerPrefs.GetFloat(CharacterKey, 1.00f);
        public static float CommentaryVolume => PlayerPrefs.GetFloat(CommentaryKey, 1.00f);

        public static void SetBgmVolume(float value)        => Set(BgmKey, value, true);
        public static void SetSfxVolume(float value)        => Set(SfxKey, value, true);
        public static void SetCharacterVolume(float value)  => Set(CharacterKey, value, false);
        public static void SetCommentaryVolume(float value) => Set(CommentaryKey, value, false);
        public static void Save() => PlayerPrefs.Save();

        static void Set(string key, float value, bool updateManager)
        {
            PlayerPrefs.SetFloat(key, Mathf.Clamp01(value));
            if (updateManager) GameAudioManager.Instance?.ApplyVolumeSettings();
        }
    }

    public class GameAudioManager : MonoBehaviour
    {
        public static GameAudioManager Instance { get; private set; }

        [Range(0f, 1f)] public float bgmVolume = 0.22f;
        [Range(0f, 1f)] public float sfxVolume = 0.82f;

        AudioSource _bgmSource;
        AudioSource _sfxSource;
        AudioSource _moveSource;

        AudioClip _lobbyBgm;
        AudioClip _battleBgm;
        AudioClip _countdown;
        AudioClip _go;
        AudioClip _ko;
        AudioClip _guardBreak;
        AudioClip _guard;
        AudioClip _grab;
        AudioClip _jump;
        AudioClip _land;
        AudioClip _smashHit;
        AudioClip _menu;
        AudioClip _projectile;
        AudioClip _meleeWhiff;
        AudioClip _dodge;
        AudioClip _lightHit;
        AudioClip _moveLoop;
        AudioClip _mediumHit;
        AudioClip _buff;
        AudioClip _heal;
        AudioClip _debuff;
        AudioClip _teleport;

        BattleManager _battle;
        int _lastCountdownNumber;
        bool _fighterEventsBound;
        readonly HashSet<Fighter> _movingFighters = new HashSet<Fighter>();

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            EnsureSources();
            LoadClips();
            ApplyVolumeSettings();
        }

        void Start()
        {
            BindBattle(BattleManager.Instance);
            StartLobbyBgm();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            UnbindBattle();
        }

        public void BindBattle(BattleManager battle)
        {
            if (_battle == battle && _fighterEventsBound) return;
            UnbindBattle();
            _battle = battle;
            if (_battle == null) return;

            _battle.OnCountdownChanged += HandleCountdown;
            _battle.OnBattleStart += HandleBattleStart;
            _battle.OnBattleEnd += HandleBattleEnd;
            _battle.OnReturnedToSetup += HandleReturnedToSetup;
            BindFighterEvents();
        }

        void UnbindBattle()
        {
            if (_battle == null) return;

            _battle.OnCountdownChanged -= HandleCountdown;
            _battle.OnBattleStart -= HandleBattleStart;
            _battle.OnBattleEnd -= HandleBattleEnd;
            _battle.OnReturnedToSetup -= HandleReturnedToSetup;
            UnbindFighterEvents();
            _battle = null;
        }

        void EnsureSources()
        {
            _bgmSource = gameObject.AddComponent<AudioSource>();
            _bgmSource.loop = true;
            _bgmSource.playOnAwake = false;
            _bgmSource.volume = bgmVolume;

            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.loop = false;
            _sfxSource.playOnAwake = false;
            _sfxSource.volume = sfxVolume;

            _moveSource = gameObject.AddComponent<AudioSource>();
            _moveSource.loop = true;
            _moveSource.playOnAwake = false;
            _moveSource.volume = sfxVolume * 0.24f;
        }

        public void ApplyVolumeSettings()
        {
            bgmVolume = GameVolumeSettings.BgmVolume;
            sfxVolume = GameVolumeSettings.SfxVolume;

            if (_bgmSource != null)
                _bgmSource.volume = bgmVolume * (_bgmSource.clip == _lobbyBgm ? 0.85f : 1f);
            if (_sfxSource != null)
                _sfxSource.volume = sfxVolume;
            if (_moveSource != null)
                _moveSource.volume = sfxVolume * 0.24f;
        }

        void LoadClips()
        {
            _lobbyBgm    = Load("Audio/BGM/ロビーBGM");
            _battleBgm   = Load("Audio/BGM/maou_game_battle21");
            _countdown   = Load("Audio/SFX/カウントダウン");
            _go          = Load("Audio/SFX/GO");
            _ko          = Load("Audio/SFX/K.O.");
            _guardBreak  = Load("Audio/SFX/ガードが割れる");
            _guard       = Load("Audio/SFX/ガード音");
            _grab        = Load("Audio/SFX/つかみ発生");
            _jump        = Load("Audio/SFX/ジャンプ");
            _land        = Load("Audio/SFX/ジャンプの着地");
            _smashHit    = Load("Audio/SFX/スマッシュヒット音");
            _menu        = Load("Audio/SFX/メニューボタン音");
            _projectile  = Load("Audio/SFX/遠距離攻撃");
            _meleeWhiff  = Load("Audio/SFX/近距離攻撃空振り");
            _dodge       = Load("Audio/SFX/移動回避と空中回避");
            _lightHit    = Load("Audio/SFX/小パンチ");
            _moveLoop    = Load("Audio/SFX/地上移動");
            _mediumHit   = Load("Audio/SFX/中パンチ");
            _buff        = Load("Audio/SFX/バフ");
            _heal        = Load("Audio/SFX/回復");
            _debuff      = Load("Audio/SFX/デバフ");
            _teleport    = Load("Audio/SFX/テレポート");
        }

        static AudioClip Load(string path)
        {
            var clip = Resources.Load<AudioClip>(path);
            if (clip == null) Debug.LogWarning($"[Audio] Clip not found: {path}");
            return clip;
        }

        void HandleCountdown(float seconds)
        {
            int n = Mathf.CeilToInt(seconds);
            if (n <= 0 || n == _lastCountdownNumber) return;
            if (_lastCountdownNumber == 0) StopBgm(); // カウントダウン開始でロビーBGM停止
            _lastCountdownNumber = n;
            PlayOneShot(_countdown, 0.72f);
        }

        void HandleBattleStart()
        {
            _lastCountdownNumber = 0;
            PlayOneShot(_go, 0.9f);
            StartBattleBgm();
        }

        void HandleBattleEnd(int _)
        {
            StopBgm();
            PlayOneShot(_ko, 0.95f);
        }

        void HandleReturnedToSetup()
        {
            StopBgm();
            StartLobbyBgm();
            FindAnyObjectByType<CommentaryController>()?.StopVoice();
            FindAnyObjectByType<AngelController>()?.StopVoice();
        }

        void StartLobbyBgm()
        {
            if (_lobbyBgm == null || _bgmSource == null) return;
            _bgmSource.clip = _lobbyBgm;
            _bgmSource.volume = bgmVolume * 0.85f;
            _bgmSource.Play();
        }

        void StartBattleBgm()
        {
            if (_battleBgm == null || _bgmSource == null) return;
            _bgmSource.clip = _battleBgm;
            _bgmSource.volume = bgmVolume;
            _bgmSource.Play();
        }

        void StopBgm()
        {
            if (_bgmSource != null && _bgmSource.isPlaying)
                _bgmSource.Stop();
        }

        void BindFighterEvents()
        {
            if (_battle == null || _fighterEventsBound) return;
            if (_battle.fighter1 != null) BindFighter(_battle.fighter1);
            if (_battle.fighter2 != null) BindFighter(_battle.fighter2);
            _fighterEventsBound = true;
        }

        void UnbindFighterEvents()
        {
            if (_battle == null || !_fighterEventsBound) return;
            if (_battle.fighter1 != null) UnbindFighter(_battle.fighter1);
            if (_battle.fighter2 != null) UnbindFighter(_battle.fighter2);
            _fighterEventsBound = false;
        }

        void BindFighter(Fighter fighter)
        {
            fighter.OnDamageReceived += HandleDamageReceived;
            fighter.OnGuardBroken += HandleGuardBroken;
            fighter.OnJumped += HandleJumped;
            fighter.OnLanded += HandleLanded;
            fighter.OnDodged += HandleDodged;
        }

        void UnbindFighter(Fighter fighter)
        {
            fighter.OnDamageReceived -= HandleDamageReceived;
            fighter.OnGuardBroken -= HandleGuardBroken;
            fighter.OnJumped -= HandleJumped;
            fighter.OnLanded -= HandleLanded;
            fighter.OnDodged -= HandleDodged;
            SetGroundMove(fighter, false);
        }

        void HandleDamageReceived(float damage, bool wasBlocked)
        {
            if (wasBlocked)
            {
                PlayOneShot(_guard, 0.62f);
                return;
            }

            if      (damage >= 16f) PlayOneShot(_smashHit,  0.75f);
            else if (damage >=  8f) PlayOneShot(_mediumHit, 0.75f);
            else                    PlayOneShot(_lightHit,  0.68f);
        }

        void HandleGuardBroken() => PlayOneShot(_guardBreak, 0.9f);
        void HandleJumped() => PlayOneShot(_jump, 0.58f);
        void HandleLanded() => PlayOneShot(_land, 0.46f);
        void HandleDodged() => PlayOneShot(_dodge, 0.7f);

        public void PlaySkill(SkillData skill)
        {
            if (HasAction(skill, "projectile"))
            {
                PlayOneShot(_projectile, 0.64f);
                return;
            }
        }

        public void PlayMeleeWhiff() => PlayOneShot(_meleeWhiff, 0.62f);
        public void PlayGrab()       => PlayOneShot(_grab,       0.62f);
        public void PlayMenu()       => PlayOneShot(_menu,       0.62f);

        // ボイスボールギミック用
        public void PlayGimmickBuff()     => PlayOneShot(_buff,     0.78f);
        public void PlayGimmickHeal()     => PlayOneShot(_heal,     0.78f);
        public void PlayGimmickDebuff()   => PlayOneShot(_debuff,   0.78f);
        public void PlayTeleport()         => PlayOneShot(_teleport, 0.82f);

        public void SetGroundMove(Fighter fighter, bool active)
        {
            if (fighter == null) return;

            if (active) _movingFighters.Add(fighter);
            else _movingFighters.Remove(fighter);

            if (_moveSource == null || _moveLoop == null) return;
            if (_movingFighters.Count > 0)
            {
                if (_moveSource.clip != _moveLoop) _moveSource.clip = _moveLoop;
                if (!_moveSource.isPlaying) _moveSource.Play();
            }
            else if (_moveSource.isPlaying)
            {
                _moveSource.Stop();
            }
        }

        static bool HasAction(SkillData skill, string type)
        {
            if (skill?.actions == null) return false;
            for (int i = 0; i < skill.actions.Count; i++)
                if (skill.actions[i]?.type == type) return true;
            return false;
        }

        void PlayOneShot(AudioClip clip, float volumeScale)
        {
            if (clip == null || _sfxSource == null) return;
            _sfxSource.PlayOneShot(clip, volumeScale);
        }
    }

    // 保存済みAI生成ボイスをFighterごとに読み込み、登場・技発動時にローカル再生する。
    [RequireComponent(typeof(Fighter))]
    public class CharacterVoicePlayer : MonoBehaviour
    {
        static readonly string[] Filenames = { "intro", "attack_a", "attack_b", "attack_c", "smash_side" };
        readonly AudioClip[] _clips = new AudioClip[5];
        AudioSource _source;
        float _nextSkillVoiceTime;

        void Awake() => EnsureSource();

        public void Configure(CharacterData data)
        {
            EnsureSource();
            StopVoice();
            ReleaseClips();
            if (data?.voiceProfile?.generated != true || string.IsNullOrEmpty(data.voiceDir) ||
                !Directory.Exists(data.voiceDir)) return;

            for (int i = 0; i < Filenames.Length; i++)
            {
                string path = Path.Combine(data.voiceDir, Filenames[i] + ".wav");
                if (!File.Exists(path)) continue;
                try
                {
                    _clips[i] = AITTSClient.WavToAudioClip(File.ReadAllBytes(path),
                        $"{data.characterName}_{Filenames[i]}");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[CharacterVoice] 読み込み失敗 ({path}): {e.Message}");
                }
            }
        }

        public float PlayIntro()
        {
            if (_clips[0] == null || _source == null) return 0f;
            _source.volume = GameVolumeSettings.CharacterVolume;
            _source.PlayOneShot(_clips[0], 1f);
            return _clips[0].length;
        }

        public void PlaySkill(SkillSlot slot)
        {
            int index = (int)slot + 1;
            if (index < 1 || index >= _clips.Length || _clips[index] == null || _source == null) return;
            if (Time.unscaledTime < _nextSkillVoiceTime || _source.isPlaying) return;

            _source.volume = GameVolumeSettings.CharacterVolume;
            _source.PlayOneShot(_clips[index], 1f);
            _nextSkillVoiceTime = Time.unscaledTime + Mathf.Max(0.9f, _clips[index].length * 0.8f);
        }

        public void StopVoice()
        {
            if (_source != null) _source.Stop();
            _nextSkillVoiceTime = 0f;
        }

        void EnsureSource()
        {
            if (_source != null) return;
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 0f;
        }

        void ReleaseClips()
        {
            for (int i = 0; i < _clips.Length; i++)
            {
                if (_clips[i] != null) Destroy(_clips[i]);
                _clips[i] = null;
            }
        }

        void OnDestroy() => ReleaseClips();
    }
}
