using UnityEditor;
using UnityEngine;

namespace PromptFighters.Editor
{
    // GPT生成のフォールバック素材を、環境差に左右されずSpriteとして取り込む。
    [InitializeOnLoad]
    public static class FallbackSpriteImporter
    {
        const string Root = "Assets/Resources/Effects/Fallback";

        static readonly string[] AssetNames =
        {
            "telegraph_radial.png",
            "telegraph_line.png",
            "telegraph_box.png",
            "fallback_projectile.png",
            "fallback_impact.png",
            "fallback_summon.png",
            "fallback_wall.png",
            "fallback_field.png",
        };

        static FallbackSpriteImporter()
        {
            EditorApplication.delayCall += Configure;
        }

        static void Configure()
        {
            bool changed = false;
            for (int i = 0; i < AssetNames.Length; i++)
            {
                string path = $"{Root}/{AssetNames[i]}";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    importer = AssetImporter.GetAtPath(path) as TextureImporter;
                }
                if (importer == null) continue;

                bool needsImport = importer.textureType != TextureImporterType.Sprite ||
                                   importer.spriteImportMode != SpriteImportMode.Single ||
                                   importer.mipmapEnabled ||
                                   !importer.alphaIsTransparency ||
                                   importer.filterMode != FilterMode.Bilinear ||
                                   importer.textureCompression != TextureImporterCompression.Uncompressed;
                if (!needsImport) continue;

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100f;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                changed = true;
            }

            if (changed)
                Debug.Log("[FallbackSpriteImporter] GPT生成フォールバックスプライトを取り込みました。");
        }
    }
}
