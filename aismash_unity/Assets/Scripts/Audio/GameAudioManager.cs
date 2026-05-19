using UnityEngine;
using System.Collections.Generic;
using PromptFighters.Battle;
using PromptFighters.Battle.Skills;

namespace PromptFighters.Audio
{
    public class GameAudioManager : MonoBehaviour
    {
        public static GameAudioManager Instance { get; private set; }

        [Range(0f, 1f)] public float bgmVolume = 0.38f;
        [Range(0f, 1f)] public float sfxVolume = 0.82f;

        AudioSource _bgmSource;
        AudioSource _sfxSource;
        AudioSource _moveSource;

        AudioClip _battleBgm;
        AudioClip _countdown;
        AudioClip _go;
        AudioClip _ko;
        AudioClip _guardBreak;
        AudioClip _guard;
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
        }

        void Start()
        {
            BindBattle(BattleManager.Instance);
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
            _battle.OnReturnedToSetup += StopBgm;
            BindFighterEvents();
        }

        void UnbindBattle()
        {
            if (_battle == null) return;

            _battle.OnCountdownChanged -= HandleCountdown;
            _battle.OnBattleStart -= HandleBattleStart;
            _battle.OnBattleEnd -= HandleBattleEnd;
            _battle.OnReturnedToSetup -= StopBgm;
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

        void LoadClips()
        {
            _battleBgm = Load("Audio/BGM/maou_game_battle21");
            _countdown = Load("Audio/SFX/カウントダウン");
            _go = Load("Audio/SFX/GO");
            _ko = Load("Audio/SFX/K.O.");
            _guardBreak = Load("Audio/SFX/ガードが割れる");
            _guard = Load("Audio/SFX/ガード音");
            _jump = Load("Audio/SFX/ジャンプ");
            _land = Load("Audio/SFX/ジャンプの着地");
            _smashHit = Load("Audio/SFX/スマッシュヒット音");
            _menu = Load("Audio/SFX/メニューボタン音");
            _projectile = Load("Audio/SFX/遠距離攻撃");
            _meleeWhiff = Load("Audio/SFX/近距離攻撃空振り");
            _dodge = Load("Audio/SFX/移動回避と空中回避");
            _lightHit = Load("Audio/SFX/小パンチ");
            _moveLoop = Load("Audio/SFX/地上移動");
            _mediumHit = Load("Audio/SFX/中パンチ");
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
            _lastCountdownNumber = n;
            PlayOneShot(_countdown, 0.72f);
        }

        void HandleBattleStart()
        {
            _lastCountdownNumber = 0;
            PlayOneShot(_go, 0.9f);
            StartBgm();
        }

        void HandleBattleEnd(int _)
        {
            StopBgm();
            PlayOneShot(_ko, 0.95f);
        }

        void StartBgm()
        {
            if (_battleBgm == null || _bgmSource == null) return;
            if (_bgmSource.clip != _battleBgm) _bgmSource.clip = _battleBgm;
            _bgmSource.volume = bgmVolume;
            if (!_bgmSource.isPlaying) _bgmSource.Play();
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

            PlayOneShot(damage >= 16f ? _smashHit : _mediumHit, 0.75f);
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

        public void PlayMenu() => PlayOneShot(_menu, 0.62f);

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
            _sfxSource.PlayOneShot(clip, sfxVolume * volumeScale);
        }
    }
}
