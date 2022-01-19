# Kogane TMP_FontAsset Updater

TextMeshPro の FontAsset をスクリプトから更新するエディタ拡張

## 開発環境

* Windows 10
* Unity 2021.2.7f1
* TextMeshPro 3.0.6

## 基本的な使い方

```cs
using Kogane.TMP_FontAssetUpdater;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

public static class Example
{
    [MenuItem( "Tools/Generate" )]
    public static async void Generate()
    {
        var fontAsset      = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>( "Assets/font.asset" );
        var sourceFontFile = AssetDatabase.LoadAssetAtPath<Font>( "Assets/font.otf" );

        var settings = new TMP_FontAssetUpdaterSettings
        (
            fontAsset: fontAsset,
            sourceFontFile: sourceFontFile,
            samplingPointSize: SamplingPointSizeType.CUSTOM_SIZE,
            customSize: 24,
            padding: 5,
            packingMode: PackingMethod.OPTIMUM,
            atlasWidth: AtlasResolution.D_1024,
            atlasHeight: AtlasResolution.D_1024,
            customCharacterList: "abcde",
            renderMode: GlyphRenderMode.SDFAA
        );

        await TMP_FontAssetUpdater.GenerateAsync( settings );

        Debug.Log( "Complete" );
    }
}
```

## プログレスバーを表示するサンプル

```cs
using Kogane.TMP_FontAssetUpdater;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

public static class Example
{
    [MenuItem( "Tools/Generate" )]
    public static async void Generate()
    {
        var fontAsset      = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>( "Assets/font.asset" );
        var sourceFontFile = AssetDatabase.LoadAssetAtPath<Font>( "Assets/font.otf" );

        var settings = new TMP_FontAssetUpdaterSettings
        (
            fontAsset: fontAsset,
            sourceFontFile: sourceFontFile,
            samplingPointSize: SamplingPointSizeType.CUSTOM_SIZE,
            customSize: 24,
            padding: 5,
            packingMode: PackingMethod.OPTIMUM,
            atlasWidth: AtlasResolution.D_1024,
            atlasHeight: AtlasResolution.D_1024,
            customCharacterList: "abcde",
            renderMode: GlyphRenderMode.SDFAA
        );

        static void OnUpdate()
        {
            EditorUtility.DisplayProgressBar
            (
                title: "Update Font Asset",
                info: TMP_FontAssetUpdater.AtlasGenerationProgressLabel,
                progress: TMP_FontAssetUpdater.AtlasGenerationProgress
            );
        }

        try
        {
            EditorApplication.update -= OnUpdate;
            EditorApplication.update += OnUpdate;

            await TMP_FontAssetUpdater.GenerateAsync( settings );

            Debug.Log( "Complete" );
        }
        finally
        {
            EditorApplication.update -= OnUpdate;

            EditorUtility.ClearProgressBar();
        }
    }
}
```