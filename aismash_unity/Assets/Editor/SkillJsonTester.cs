using UnityEditor;
using UnityEngine;
using PromptFighters.Battle.Skills;
using PromptFighters.Battle.Skills.Json;

// エディタメニュー Tools > Test Skill JSON でパーサーとバランス補正を動作確認できる。
public static class SkillJsonTester
{
    static readonly string SampleJson = @"{
  ""character_name"": ""シャドウキャット"",
  ""input_features"": ""黒い炎をまとった素早い猫の剣士。近距離で連続攻撃する。"",
  ""base_visual_prompt"": ""2D anime standing character, black cat swordsman, dark flames"",
  ""visual_description"": ""黒い炎をまとった猫耳の剣士。"",
  ""skills"": [
    {
      ""slot"": ""attack_a"",
      ""skill_name"": ""黒炎みだれ斬り"",
      ""description"": ""黒い炎をまとった爪で3回斬りつける。"",
      ""element"": ""fire"",
      ""risk_level"": ""medium"",
      ""parameters"": {
        ""damage"": 4,
        ""hit_count"": 3,
        ""range"": 1.4,
        ""startup"": 0.18,
        ""active_time"": 0.25,
        ""recovery"": 0.45,
        ""knockback"": 3.5,
        ""stun_time"": 0.25,
        ""guard_damage"": 2,
        ""move_force"": 0.4
      },
      ""actions"": [
        { ""type"": ""dash"", ""time"": 0.0, ""power"": 0.4, ""direction"": ""forward"" },
        { ""type"": ""melee_hitbox"", ""time"": 0.18, ""range"": 1.4, ""hit_count"": 3 },
        { ""type"": ""apply_status"", ""time"": 0.2, ""status"": ""stun"", ""duration"": 0.25, ""chance"": 0.25 }
      ]
    },
    {
      ""slot"": ""attack_b"",
      ""skill_name"": ""影火球"",
      ""description"": ""黒い炎球を飛ばす遠距離技。"",
      ""element"": ""dark"",
      ""risk_level"": ""medium"",
      ""parameters"": {
        ""damage"": 10,
        ""range"": 12,
        ""startup"": 0.2,
        ""active_time"": 0.1,
        ""recovery"": 0.8,
        ""knockback"": 4
      },
      ""actions"": [
        { ""type"": ""projectile"", ""time"": 0.2, ""projectile_speed"": 10, ""projectile_lifetime"": 1.5 }
      ]
    },
    {
      ""slot"": ""attack_c"",
      ""skill_name"": ""闇猫ステップ"",
      ""description"": ""素早く踏み込み相手をスローにする。"",
      ""element"": ""dark"",
      ""risk_level"": ""medium"",
      ""parameters"": {
        ""damage"": 7,
        ""range"": 1.5,
        ""startup"": 0.15,
        ""active_time"": 0.15,
        ""recovery"": 1.5,
        ""knockback"": 3,
        ""stun_time"": 0
      },
      ""actions"": [
        { ""type"": ""dash"", ""time"": 0.0, ""power"": 7, ""direction"": ""forward"" },
        { ""type"": ""melee_hitbox"", ""time"": 0.15 },
        { ""type"": ""apply_status"", ""time"": 0.15, ""status"": ""slow"", ""duration"": 2, ""chance"": 1 }
      ]
    },
    {
      ""slot"": ""smash_side"",
      ""skill_name"": ""ナイトメアラッシュ"",
      ""description"": ""黒炎をまとい高速で突進する必殺技。"",
      ""element"": ""fire"",
      ""risk_level"": ""high"",
      ""parameters"": {
        ""damage"": 25,
        ""range"": 2,
        ""startup"": 0.5,
        ""active_time"": 0.25,
        ""recovery"": 4.5,
        ""knockback"": 9,
        ""stun_time"": 0.3
      },
      ""actions"": [
        { ""type"": ""dash"", ""time"": 0.1, ""power"": 12, ""direction"": ""forward"" },
        { ""type"": ""melee_hitbox"", ""time"": 0.5, ""range"": 2 }
      ]
    }
  ]
}";

    [MenuItem("Tools/Test Skill JSON")]
    static void Run()
    {
        var data = SkillJsonParser.Parse(SampleJson);
        if (data == null)
        {
            Debug.LogError("[SkillJsonTester] パース失敗");
            return;
        }

        Debug.Log($"[SkillJsonTester] キャラクター: {data.characterName}");
        for (int i = 0; i < 4; i++)
        {
            var s = data.skills[i];
            if (s == null) { Debug.LogWarning($"  スロット{i}: null"); continue; }
            var p = s.parameters;
            Debug.Log($"  [{(SkillSlot)i}] {s.skill_name} | " +
                      $"dmg={p.damage}x{p.hit_count} recovery={p.recovery:F1}s " +
                      $"range={p.range:F1} element={s.element}");
        }
    }

    [MenuItem("Tools/Test Skill JSON Balance (Extreme Input)")]
    static void RunExtremeBalance()
    {
        // 意図的に上限を超えた値を入れてバランス補正を確認
        string extremeJson = @"{
  ""character_name"": ""無敵の神"",
  ""skills"": [
    {
      ""slot"": ""smash_side"",
      ""skill_name"": ""即死拳"",
      ""element"": ""none"",
      ""risk_level"": ""extreme"",
      ""parameters"": {
        ""damage"": 9999,
        ""stun_time"": 10,
        ""knockback"": 999,
        ""range"": 999,
        ""startup"": 0,
        ""active_time"": 0,
        ""recovery"": 0
      },
      ""actions"": [
        { ""type"": ""melee_hitbox"", ""time"": 0 }
      ]
    }
  ]
}";
        var data = SkillJsonParser.ParseOrFallback(extremeJson, "補正テスト");
        var s = data.skills[(int)SkillSlot.SmashSide];
        var p = s.parameters;
        Debug.Log($"[Balance Test] {s.skill_name}: " +
                  $"dmg={p.damage} (cap=30) | " +
                  $"recovery={p.recovery:F1}s (range=3-6) | " +
                  $"stun={p.stun_time:F2}s (max=1.5) | " +
                  $"kb={p.knockback} (max=15) | " +
                  $"range={p.range} (max=16)");
    }
}
