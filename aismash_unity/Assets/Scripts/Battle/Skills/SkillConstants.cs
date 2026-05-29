namespace PromptFighters.Battle.Skills
{
    // 技まわりの横断的なチューニング定数。個別アクションのレイアウト既定値ではなく、
    // 複数箇所で共有される「調整したくなる数値」をここに集約する。
    public static class SkillConstants
    {
        // ヒットボックスの見た目スケール（実判定をスプライトよりやや小さく見せる）
        public const float HitboxVisualScale = 0.9f;

        // 追撃（follow-up）の最大連鎖数と、追撃時のダメージ倍率
        public const int   MaxFollowUpCount = 3;
        public const float FollowUpDamageMultiplier = 0.35f;

        // SmashSideのdash後にmelee/body判定を出すための最小遅延
        public const float SmashHitAfterDashDelay = 0.05f;

        // 空振り音の判定遅延に足す猶予
        public const float WhiffCheckGrace = 0.03f;

        // SmashSideのrecoveryクランプ範囲
        public const float SmashRecoveryMin = 0.20f;
        public const float SmashRecoveryMax = 0.70f;

        // スマッシュ判定とみなすパワー倍率の下限
        public const float SmashPowerThreshold = 1.8f;
    }
}
