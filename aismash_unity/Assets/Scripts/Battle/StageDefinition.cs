using System;

namespace PromptFighters.Battle
{
    [Serializable]
    public class PlatformDef
    {
        public float x, y, width;
        public bool  moving;
        public float moveRange   = 0f;   // 片道移動幅（world単位）
        public float movePeriod  = 3f;   // 往復周期（秒）
        public float phaseOffset = 0f;   // 初期位相（0..1）
    }

    [Serializable]
    public class WallDef
    {
        public float x, y, width, height;
    }

    public class StageDefinition
    {
        public string id;
        public string displayName;
        public string backgroundPath;      // Resources.Load<Sprite> パス（"Art/stage1" など）
        public string platformSpritePath;  // null = デフォルト "Stage/platform"
        public string wallSpritePath;      // null = デフォルト "Stage/wall"
        public float  stageHalfWidth = 6.5f;
        public PlatformDef[] platforms;
        public WallDef[]     walls;
    }
}
