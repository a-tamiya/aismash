using UnityEditor;
using UnityEngine;

namespace PromptFighters.Editor
{
    // GPT生成のプレミアムUI素材を、背景・9-slice枠・ワールド演出の用途別に
    // 常に同じ品質設定で取り込む。
    [InitializeOnLoad]
    public static class PremiumUISpriteImporter
    {
        const string Root = "Assets/Resources/UI/Premium";

        readonly struct ImportSpec
        {
            public readonly string Name;
            public readonly float PixelsPerUnit;
            public readonly Vector4 Border;
            public readonly bool HasAlpha;

            public ImportSpec(string name, float pixelsPerUnit, Vector4 border, bool hasAlpha)
            {
                Name = name;
                PixelsPerUnit = pixelsPerUnit;
                Border = border;
                HasAlpha = hasAlpha;
            }
        }

        static readonly ImportSpec[] Specs =
        {
            new ImportSpec("title_background.png", 100f, Vector4.zero, false),
            new ImportSpec("lobby_background.png", 100f, Vector4.zero, false),
            new ImportSpec("damage_burst.png", 256f, Vector4.zero, true),
        };

        static PremiumUISpriteImporter()
        {
            EditorApplication.delayCall += Configure;
        }

        static void Configure()
        {
            bool changed = false;
            foreach (ImportSpec spec in Specs)
            {
                string path = $"{Root}/{spec.Name}";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    importer = AssetImporter.GetAtPath(path) as TextureImporter;
                }
                if (importer == null) continue;

                bool needsImport = importer.textureType != TextureImporterType.Sprite ||
                                   importer.spriteImportMode != SpriteImportMode.Single ||
                                   importer.spritePixelsPerUnit != spec.PixelsPerUnit ||
                                   importer.spriteBorder != spec.Border ||
                                   importer.mipmapEnabled ||
                                   importer.alphaIsTransparency != spec.HasAlpha ||
                                   importer.filterMode != FilterMode.Bilinear ||
                                   importer.wrapMode != TextureWrapMode.Clamp ||
                                   importer.textureCompression != TextureImporterCompression.Uncompressed;
                if (!needsImport) continue;

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = spec.PixelsPerUnit;
                importer.spriteBorder = spec.Border;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = spec.HasAlpha;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                changed = true;
            }

            if (changed)
                Debug.Log("[PremiumUISpriteImporter] GPT生成のプレミアムUI素材を取り込みました。");
        }
    }
}
