namespace PromptFighters.Battle
{
    // 全ステージの定義と選択インデックスを管理する。
    public static class StageRegistry
    {
        public static int SelectedIndex = 0;
        public static StageDefinition Current => All[SelectedIndex];

        public static readonly StageDefinition[] All = new[]
        {
            // Stage 1: 天空浮遊アリーナ（既存）
            new StageDefinition
            {
                id             = "stage1",
                displayName    = "天空浮遊アリーナ",
                backgroundPath = "Art/stage1",
                stageHalfWidth = 6.5f,
                platforms = new[]
                {
                    new PlatformDef { x = -3.2f, y = 0.1f, width = 3.0f },
                    new PlatformDef { x =  3.2f, y = 0.1f, width = 3.0f },
                },
            },

            // Stage 2: 熔岩洞窟 — 中央高台＋左右低台の3段構成
            new StageDefinition
            {
                id                 = "stage2",
                displayName        = "熔岩洞窟",
                backgroundPath     = "Art/stage2",
                platformSpritePath = "Stage/stage2_platform",
                stageHalfWidth     = 6.0f,
                platforms = new[]
                {
                    new PlatformDef { x =  0.0f, y =  1.2f, width = 2.5f },
                    new PlatformDef { x = -4.2f, y = -0.3f, width = 2.2f },
                    new PlatformDef { x =  4.2f, y = -0.3f, width = 2.2f },
                },
            },

            // Stage 3: サイバーパンクシティ — 逆位相で往復する2枚の動く台
            new StageDefinition
            {
                id                 = "stage3",
                displayName        = "サイバーパンクシティ",
                backgroundPath     = "Art/stage3",
                platformSpritePath = "Stage/stage3_platform",
                stageHalfWidth     = 6.5f,
                platforms = new[]
                {
                    new PlatformDef { x = -3.0f, y = 0.8f, width = 2.8f,
                        moving = true, moveRange = 2.2f, movePeriod = 4.0f, phaseOffset = 0.0f },
                    new PlatformDef { x =  3.0f, y = 0.8f, width = 2.8f,
                        moving = true, moveRange = 2.2f, movePeriod = 4.0f, phaseOffset = 0.5f },
                },
            },

            // Stage 4: 古代神殿 — 高い左右台＋中央の石柱
            new StageDefinition
            {
                id                 = "stage4",
                displayName        = "古代神殿",
                backgroundPath     = "Art/stage4",
                platformSpritePath = "Stage/stage4_platform",
                wallSpritePath     = "Stage/stage4_wall",
                stageHalfWidth     = 7.0f,
                platforms = new[]
                {
                    new PlatformDef { x = -5.0f, y = 1.5f, width = 2.5f },
                    new PlatformDef { x =  5.0f, y = 1.5f, width = 2.5f },
                },
                walls = new[]
                {
                    new WallDef { x = 0f, y = -1.4f, width = 0.8f, height = 3.2f },
                },
            },

            // Stage 5: 決戦の戦場 — 設置物なしのフラットステージ
            new StageDefinition
            {
                id             = "stage5",
                displayName    = "決戦の戦場",
                backgroundPath = "Art/stage5",
                stageHalfWidth = 7.5f,
                platforms      = System.Array.Empty<PlatformDef>(),
                walls          = System.Array.Empty<WallDef>(),
            },
        };
    }
}
