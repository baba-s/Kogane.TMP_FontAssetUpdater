using System;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

// ReSharper disable UnusedMember.Global
// ReSharper disable InconsistentNaming

namespace Kogane.TMP_FontAssetUpdater
{
    public enum SamplingPointSizeType
    {
        [InspectorName( "AutoSizing" )] AUTO_SIZING,
        [InspectorName( "CustomSize" )] CUSTOM_SIZE,
    }

    public enum PackingMethod
    {
        [InspectorName( "Fast" )]    FAST,
        [InspectorName( "Optimum" )] OPTIMUM,
    }

    public enum AtlasResolution
    {
        [InspectorName( "8" )]    D_8    = 8,
        [InspectorName( "16" )]   D_16   = 16,
        [InspectorName( "32" )]   D_32   = 32,
        [InspectorName( "64" )]   D_64   = 64,
        [InspectorName( "128" )]  D_128  = 128,
        [InspectorName( "256" )]  D_256  = 256,
        [InspectorName( "512" )]  D_512  = 512,
        [InspectorName( "1024" )] D_1024 = 1024,
        [InspectorName( "2048" )] D_2048 = 2048,
        [InspectorName( "4096" )] D_4096 = 4096,
        [InspectorName( "8192" )] D_8192 = 8192,
    }

    [Serializable]
    public readonly struct TMP_FontAssetUpdaterSettings
    {
        public TMP_FontAsset         FontAsset           { get; }
        public Font                  SourceFontFile      { get; }
        public SamplingPointSizeType SamplingPointSize   { get; }
        public int                   CustomSize          { get; }
        public int                   Padding             { get; }
        public PackingMethod         PackingMode         { get; }
        public AtlasResolution       AtlasWidth          { get; }
        public AtlasResolution       AtlasHeight         { get; }
        public string                CustomCharacterList { get; }
        public GlyphRenderMode       RenderMode          { get; }

        public TMP_FontAssetUpdaterSettings
        (
            TMP_FontAsset         fontAsset,
            Font                  sourceFontFile,
            SamplingPointSizeType samplingPointSize,
            int                   customSize,
            int                   padding,
            PackingMethod         packingMode,
            AtlasResolution       atlasWidth,
            AtlasResolution       atlasHeight,
            string                customCharacterList,
            GlyphRenderMode       renderMode
        )
        {
            FontAsset           = fontAsset;
            SourceFontFile      = sourceFontFile;
            SamplingPointSize   = samplingPointSize;
            CustomSize          = customSize;
            Padding             = padding;
            PackingMode         = packingMode;
            AtlasWidth          = atlasWidth;
            AtlasHeight         = atlasHeight;
            CustomCharacterList = customCharacterList;
            RenderMode          = renderMode;
        }
    }
}