using System.Reflection;
using UnityEditor;
using UnityEngine;
using PromptFighters.Battle.Skills;
using PromptFighters.Battle.Skills.Json;
using PromptFighters.GameFlow;

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

    [MenuItem("Tools/Test Skill Diversity Extensions")]
    static void RunDiversityExtensions()
    {
        string json = @"{
  ""character_name"": ""多様性テスター"",
  ""skills"": [
    {
      ""slot"": ""attack_a"", ""skill_name"": ""連続危険領域"", ""element"": ""fire"",
      ""parameters"": {""damage"":8,""range"":3,""startup"":0.08,""active_time"":0.2,""recovery"":0.3,""knockback"":4},
      ""actions"": [{""type"":""hazard_field"",""time"":0.08,""duration"":9,""hit_count"":9,
        ""pattern"":""rain"",""pattern_count"":9,""repeat_count"":9,""repeat_interval"":0.01}]
    },
    {
      ""slot"": ""attack_b"", ""skill_name"": ""地空分岐"", ""element"": ""wind"",
      ""parameters"": {""damage"":7,""range"":3,""startup"":0.1,""active_time"":0.2,""recovery"":0.4,""knockback"":5},
      ""actions"": [
        {""type"":""shockwave"",""time"":0.1,""condition"":""grounded""},
        {""type"":""dive_attack"",""time"":0.1,""condition"":""airborne""}
      ]
    },
    {
      ""slot"": ""attack_c"", ""skill_name"": ""予測交換"", ""element"": ""dark"",
      ""parameters"": {""damage"":0,""range"":4,""startup"":0.12,""active_time"":0.1,""recovery"":0.1,""knockback"":3},
      ""actions"": [
        {""type"":""position_swap"",""time"":0.12,""range"":99,""telegraph_time"":0},
        {""type"":""force_field"",""time"":0.12,""spawn_origin"":""enemy"",""telegraph_time"":0,
          ""condition"":""low_hp"",""condition_value"":30}
      ]
    },
    {
      ""slot"": ""smash_side"", ""skill_name"": ""螺旋砲"", ""element"": ""lightning"",
      ""parameters"": {""damage"":24,""range"":12,""startup"":0.3,""active_time"":0.2,""recovery"":0.5,""knockback"":12},
      ""actions"": [{""type"":""projectile"",""time"":0.3,""aim_mode"":""predicted_enemy"",
        ""pattern"":""spiral"",""pattern_count"":8,""pattern_radius"":3,""projectile_speed"":14}]
    }
  ]
}";
        var data = SkillJsonParser.Parse(json);
        bool ok = data != null;
        if (ok)
        {
            var hazard = data.skills[0].actions[0];
            ok &= hazard.type == "hazard_field" && hazard.duration <= 4f &&
                  hazard.hit_count <= 6 && hazard.pattern == "rain" &&
                  hazard.pattern_count <= 4 && hazard.repeat_count == 5 &&
                  hazard.repeat_interval >= 0.08f;
            var ground = data.skills[1].actions[0];
            var air = data.skills[1].actions[1];
            ok &= ground.condition == "grounded" && air.condition == "airborne";
            var swap = data.skills[2].actions[0];
            ok &= swap.type == "position_swap" && swap.range <= 6f &&
                  swap.telegraph_time >= 0.35f && data.skills[2].parameters.recovery >= 0.38f;
            var remoteForce = data.skills[2].actions[1];
            ok &= remoteForce.type == "force_field" && remoteForce.telegraph_time >= 0.3f &&
                  Mathf.Approximately(remoteForce.condition_value, 0.3f);
            var spiral = data.skills[3].actions[0];
            ok &= spiral.aim_mode == "predicted_enemy" && spiral.pattern == "spiral";
        }

        if (ok) Debug.Log("[SkillDiversityTest] PASS: 新action・反復・条件分岐・配置・予測照準");
        else Debug.LogError("[SkillDiversityTest] FAIL: 技多様性拡張の正規化結果が不正");
    }

    [MenuItem("Tools/Test Character Prompt Input")]
    static void RunCharacterPromptInput()
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var panelType = typeof(PreBattlePanel);
        var limitField = panelType.GetField("CharacterPromptCharacterLimit", flags);
        var composeMethod = panelType.GetMethod("ComposeDetailedCharacterPrompt", flags);
        int limit = limitField != null ? (int)limitField.GetRawConstantValue() : -1;
        string prompt = composeMethod?.Invoke(null, new object[]
        {
            "銀髪で軽量。空中移動が速い。",
            "上空から氷柱を5本落とす。",
            "前方へ氷の壁を作る。",
            "相手の背後へ瞬間移動する。",
            "左右から巨大な氷竜で挟撃する。"
        }) as string;
        bool ok = limit == 600 &&
                  prompt != null &&
                  prompt.Contains("【見た目・性能】") &&
                  prompt.Contains("【技A】") &&
                  prompt.Contains("【技B】") &&
                  prompt.Contains("【技X】") &&
                  prompt.Contains("【SMASH】") &&
                  prompt.IndexOf("銀髪", System.StringComparison.Ordinal) <
                  prompt.IndexOf("氷柱", System.StringComparison.Ordinal) &&
                  prompt.IndexOf("氷柱", System.StringComparison.Ordinal) <
                  prompt.IndexOf("氷の壁", System.StringComparison.Ordinal) &&
                  prompt.IndexOf("氷の壁", System.StringComparison.Ordinal) <
                  prompt.IndexOf("瞬間移動", System.StringComparison.Ordinal) &&
                  prompt.IndexOf("瞬間移動", System.StringComparison.Ordinal) <
                  prompt.IndexOf("氷竜", System.StringComparison.Ordinal);
        if (ok) Debug.Log("[CharacterPromptInputTest] PASS: 600字上限・A/B/X/SMASH分割");
        else Debug.LogError("[CharacterPromptInputTest] FAIL: 入力上限または技別統合が不正");
    }
}
