namespace PromptFighters
{
    // デバッグ用グローバル設定。ゲームプレイを変えずに可視化・生成コスト削減に使う。
    public static class DebugSettings
    {
        // キャラ生成時に画像生成をスキップする（APIコスト削減用）
        public static bool SkipImageGeneration = false;

        // ヒットボックス・食らい判定をブロック表示する（F3キーで切替）
        public static bool ShowHitboxes = false;
    }
}
