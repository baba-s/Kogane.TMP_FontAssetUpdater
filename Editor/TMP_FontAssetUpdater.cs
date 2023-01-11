using TMPro;
using TMPro.EditorUtilities;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.IO;
using System.Threading.Tasks;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using UnityEditor.TextCore.LowLevel;
using Object = UnityEngine.Object;

namespace Kogane.TMP_FontAssetUpdater
{
    public static class TMP_FontAssetUpdater
    {
        // Diagnostics
        static System.Diagnostics.Stopwatch m_StopWatch;
        static double                       m_GlyphPackingGenerationTime;
        static double                       m_GlyphRenderingGenerationTime;

        static int m_PointSizeSamplingMode;
        enum FontPackingModes { Fast = 0, Optimum = 4 };
        static FontPackingModes m_PackingMode = FontPackingModes.Fast;

        static int m_CharacterSetSelectionMode;

        static string m_CharacterSequence = "";
        static string m_OutputFeedback = "";
        static string m_WarningMessage;
        static int m_CharacterCount;
        static Vector2 m_ScrollPosition;
        static Vector2 m_OutputScrollPosition;

        static bool m_IsRepaintNeeded;

        static float m_AtlasGenerationProgress;
        static string m_AtlasGenerationProgressLabel = string.Empty;
        static float m_RenderingProgress;
        static bool m_IsGlyphPackingDone;
        static bool m_IsGlyphRenderingDone;
        static bool m_IsRenderingDone;
        static bool m_IsProcessing;
        static bool m_IsGenerationDisabled;
        static bool m_IsGenerationCancelled;

        static bool          m_IsFontAtlasInvalid;
        static Object        m_SourceFontFile;
        static TMP_FontAsset m_SelectedFontAsset;
        static TMP_FontAsset m_LegacyFontAsset;
        static TMP_FontAsset m_ReferencedFontAsset;

        static TextAsset m_CharactersFromFile;
        static int m_PointSize;
        static int m_Padding = 5;

        static GlyphRenderMode m_GlyphRenderMode = GlyphRenderMode.SDFAA;
        static int m_AtlasWidth = 512;
        static int m_AtlasHeight = 512;
        static byte[] m_AtlasTextureBuffer;
        static Texture2D m_FontAtlasTexture;
        static Texture2D m_GlyphRectPreviewTexture;
        //static Texture2D m_SavedFontAtlas;

        //
        static List<Glyph>         m_FontGlyphTable     = new List<Glyph>();
        static List<TMP_Character> m_FontCharacterTable = new List<TMP_Character>();

        static Dictionary<uint, uint>       m_CharacterLookupMap = new Dictionary<uint, uint>();
        static Dictionary<uint, List<uint>> m_GlyphLookupMap     = new Dictionary<uint, List<uint>>();

        static List<Glyph> m_GlyphsToPack = new List<Glyph>();
        static List<Glyph> m_GlyphsPacked = new List<Glyph>();
        static List<GlyphRect> m_FreeGlyphRects = new List<GlyphRect>();
        static List<GlyphRect> m_UsedGlyphRects = new List<GlyphRect>();
        static List<Glyph> m_GlyphsToRender = new List<Glyph>();
        static List<uint> m_AvailableGlyphsToAdd = new List<uint>();
        static List<uint> m_MissingCharacters = new List<uint>();
        static List<uint> m_ExcludedCharacters = new List<uint>();

        private static FaceInfo m_FaceInfo;

        static bool m_IncludeFontFeatures;

        public static bool   IsProcessing                 => m_IsProcessing;
        public static float  AtlasGenerationProgress      => m_AtlasGenerationProgress;
        public static string AtlasGenerationProgressLabel => m_AtlasGenerationProgressLabel;

        public static Task GenerateAsync( in TMP_FontAssetUpdaterSettings settings )
        {
            var tcs = new TaskCompletionSource<bool>();

            m_SelectedFontAsset         = settings.FontAsset;
            m_SourceFontFile            = settings.SourceFontFile;
            m_PointSizeSamplingMode     = settings.SamplingPointSize == SamplingPointSizeType.AUTO_SIZING ? 0 : 1;
            m_PointSize                 = settings.CustomSize;
            m_Padding                   = settings.Padding;
            m_PackingMode               = settings.PackingMode == PackingMethod.FAST ? FontPackingModes.Fast : FontPackingModes.Optimum;
            m_AtlasWidth                = ( int )settings.AtlasWidth;
            m_AtlasHeight               = ( int )settings.AtlasHeight;
            m_CharacterSetSelectionMode = 7;
            m_GlyphRenderMode           = settings.RenderMode;
            m_CharacterSequence         = settings.CustomCharacterList;

            EditorApplication.update    -= Update;
            EditorApplication.update    += Update;

            if (!m_IsProcessing && m_SourceFontFile != null)
            {
                Object.DestroyImmediate(m_FontAtlasTexture);
                Object.DestroyImmediate(m_GlyphRectPreviewTexture);
                m_FontAtlasTexture = null;
                //m_SavedFontAtlas = null;
                m_OutputFeedback = string.Empty;

                // Initialize font engine
                FontEngineError errorCode = FontEngine.InitializeFontEngine();
                if (errorCode != FontEngineError.Success)
                {
                    Debug.Log("Font Asset Creator - Error [" + errorCode + "] has occurred while Initializing the FreeType Library.");
                }

                // Get file path of the source font file.
                string fontPath = AssetDatabase.GetAssetPath(m_SourceFontFile);

                if (errorCode == FontEngineError.Success)
                {
                    errorCode = FontEngine.LoadFontFace(fontPath);

                    if (errorCode != FontEngineError.Success)
                    {
                        Debug.Log("Font Asset Creator - Error Code [" + errorCode + "] has occurred trying to load the [" + m_SourceFontFile.name + "] font file. This typically results from the use of an incompatible or corrupted font file.", m_SourceFontFile);
                    }
                }


                // Define an array containing the characters we will render.
                if (errorCode == FontEngineError.Success)
                {
                    uint[] characterSet = null;

                    // Get list of characters that need to be packed and rendered to the atlas texture.
                    if (m_CharacterSetSelectionMode == 7 || m_CharacterSetSelectionMode == 8)
                    {
                        List<uint> char_List = new List<uint>();

                        for (int i = 0; i < m_CharacterSequence.Length; i++)
                        {
                            uint unicode = m_CharacterSequence[i];

                            // Handle surrogate pairs
                            if (i < m_CharacterSequence.Length - 1 && char.IsHighSurrogate((char)unicode) && char.IsLowSurrogate(m_CharacterSequence[i + 1]))
                            {
                                unicode = (uint)char.ConvertToUtf32(m_CharacterSequence[i], m_CharacterSequence[i + 1]);
                                i += 1;
                            }

                            // Check to make sure we don't include duplicates
                            if (char_List.FindIndex(item => item == unicode) == -1)
                                char_List.Add(unicode);
                        }

                        characterSet = char_List.ToArray();
                    }
                    else if (m_CharacterSetSelectionMode == 6)
                    {
                        characterSet = ParseHexNumberSequence(m_CharacterSequence);
                    }
                    else
                    {
                        characterSet = ParseNumberSequence(m_CharacterSequence);
                    }

                    m_CharacterCount = characterSet.Length;

                    m_AtlasGenerationProgress = 0;
                    m_IsProcessing = true;
                    m_IsGenerationCancelled = false;

                    GlyphLoadFlags glyphLoadFlags = ((GlyphRasterModes)m_GlyphRenderMode & GlyphRasterModes.RASTER_MODE_HINTED) == GlyphRasterModes.RASTER_MODE_HINTED
                        ? GlyphLoadFlags.LOAD_RENDER
                        : GlyphLoadFlags.LOAD_RENDER | GlyphLoadFlags.LOAD_NO_HINTING;

                    glyphLoadFlags = ((GlyphRasterModes)m_GlyphRenderMode & GlyphRasterModes.RASTER_MODE_MONO) == GlyphRasterModes.RASTER_MODE_MONO
                        ? glyphLoadFlags | GlyphLoadFlags.LOAD_MONOCHROME
                        : glyphLoadFlags;

                    //
                    AutoResetEvent autoEvent = new AutoResetEvent(false);

                    // Worker thread to pack glyphs in the given texture space.
                    ThreadPool.QueueUserWorkItem(PackGlyphs =>
                    {
                        // Start Stop Watch
                        m_StopWatch = System.Diagnostics.Stopwatch.StartNew();

                        // Clear the various lists used in the generation process.
                        m_AvailableGlyphsToAdd.Clear();
                        m_MissingCharacters.Clear();
                        m_ExcludedCharacters.Clear();
                        m_CharacterLookupMap.Clear();
                        m_GlyphLookupMap.Clear();
                        m_GlyphsToPack.Clear();
                        m_GlyphsPacked.Clear();

                        // Check if requested characters are available in the source font file.
                        for (int i = 0; i < characterSet.Length; i++)
                        {
                            uint unicode = characterSet[i];
                            uint glyphIndex;

                            if (FontEngine.TryGetGlyphIndex(unicode, out glyphIndex))
                            {
                                // Skip over potential duplicate characters.
                                if (m_CharacterLookupMap.ContainsKey(unicode))
                                    continue;

                                // Add character to character lookup map.
                                m_CharacterLookupMap.Add(unicode, glyphIndex);

                                // Skip over potential duplicate glyph references.
                                if (m_GlyphLookupMap.ContainsKey(glyphIndex))
                                {
                                    // Add additional glyph reference for this character.
                                    m_GlyphLookupMap[glyphIndex].Add(unicode);
                                    continue;
                                }

                                // Add glyph reference to glyph lookup map.
                                m_GlyphLookupMap.Add(glyphIndex, new List<uint>() { unicode });

                                // Add glyph index to list of glyphs to add to texture.
                                m_AvailableGlyphsToAdd.Add(glyphIndex);
                            }
                            else
                            {
                                // Add Unicode to list of missing characters.
                                m_MissingCharacters.Add(unicode);
                            }
                        }

                        // Pack available glyphs in the provided texture space.
                        if (m_AvailableGlyphsToAdd.Count > 0)
                        {
                            int packingModifier = ((GlyphRasterModes)m_GlyphRenderMode & GlyphRasterModes.RASTER_MODE_BITMAP) == GlyphRasterModes.RASTER_MODE_BITMAP ? 0 : 1;

                            if (m_PointSizeSamplingMode == 0) // Auto-Sizing Point Size Mode
                            {
                                // Estimate min / max range for auto sizing of point size.
                                int minPointSize = 0;
                                int maxPointSize = (int)Mathf.Sqrt((m_AtlasWidth * m_AtlasHeight) / m_AvailableGlyphsToAdd.Count) * 3;

                                m_PointSize = (maxPointSize + minPointSize) / 2;

                                bool optimumPointSizeFound = false;
                                for (int iteration = 0; iteration < 15 && optimumPointSizeFound == false; iteration++)
                                {
                                    m_AtlasGenerationProgressLabel = "Packing glyphs - Pass (" + iteration + ")";

                                    FontEngine.SetFaceSize(m_PointSize);

                                    m_GlyphsToPack.Clear();
                                    m_GlyphsPacked.Clear();

                                    m_FreeGlyphRects.Clear();
                                    m_FreeGlyphRects.Add(new GlyphRect(0, 0, m_AtlasWidth - packingModifier, m_AtlasHeight - packingModifier));
                                    m_UsedGlyphRects.Clear();

                                    for (int i = 0; i < m_AvailableGlyphsToAdd.Count; i++)
                                    {
                                        uint glyphIndex = m_AvailableGlyphsToAdd[i];
                                        Glyph glyph;

                                        if (FontEngine.TryGetGlyphWithIndexValue(glyphIndex, glyphLoadFlags, out glyph))
                                        {
                                            if (glyph.glyphRect.width > 0 && glyph.glyphRect.height > 0)
                                            {
                                                m_GlyphsToPack.Add(glyph);
                                            }
                                            else
                                            {
                                                m_GlyphsPacked.Add(glyph);
                                            }
                                        }
                                    }

                                    FontEngine.TryPackGlyphsInAtlas(m_GlyphsToPack, m_GlyphsPacked, m_Padding, (GlyphPackingMode)m_PackingMode, m_GlyphRenderMode, m_AtlasWidth, m_AtlasHeight, m_FreeGlyphRects, m_UsedGlyphRects);

                                    if (m_IsGenerationCancelled)
                                    {
                                        Object.DestroyImmediate(m_FontAtlasTexture);
                                        m_FontAtlasTexture = null;
                                        return;
                                    }

                                    //Debug.Log("Glyphs remaining to add [" + m_GlyphsToAdd.Count + "]. Glyphs added [" + m_GlyphsAdded.Count + "].");

                                    if (m_GlyphsToPack.Count > 0)
                                    {
                                        if (m_PointSize > minPointSize)
                                        {
                                            maxPointSize = m_PointSize;
                                            m_PointSize = (m_PointSize + minPointSize) / 2;

                                            //Debug.Log("Decreasing point size from [" + maxPointSize + "] to [" + m_PointSize + "].");
                                        }
                                    }
                                    else
                                    {
                                        if (maxPointSize - minPointSize > 1 && m_PointSize < maxPointSize)
                                        {
                                            minPointSize = m_PointSize;
                                            m_PointSize = (m_PointSize + maxPointSize) / 2;

                                            //Debug.Log("Increasing point size from [" + minPointSize + "] to [" + m_PointSize + "].");
                                        }
                                        else
                                        {
                                            //Debug.Log("[" + iteration + "] iterations to find the optimum point size of : [" + m_PointSize + "].");
                                            optimumPointSizeFound = true;
                                        }
                                    }
                                }
                            }
                            else // Custom Point Size Mode
                            {
                                m_AtlasGenerationProgressLabel = "Packing glyphs...";

                                // Set point size
                                FontEngine.SetFaceSize(m_PointSize);

                                m_GlyphsToPack.Clear();
                                m_GlyphsPacked.Clear();

                                m_FreeGlyphRects.Clear();
                                m_FreeGlyphRects.Add(new GlyphRect(0, 0, m_AtlasWidth - packingModifier, m_AtlasHeight - packingModifier));
                                m_UsedGlyphRects.Clear();

                                for (int i = 0; i < m_AvailableGlyphsToAdd.Count; i++)
                                {
                                    uint glyphIndex = m_AvailableGlyphsToAdd[i];
                                    Glyph glyph;

                                    if (FontEngine.TryGetGlyphWithIndexValue(glyphIndex, glyphLoadFlags, out glyph))
                                    {
                                        if (glyph.glyphRect.width > 0 && glyph.glyphRect.height > 0)
                                        {
                                            m_GlyphsToPack.Add(glyph);
                                        }
                                        else
                                        {
                                            m_GlyphsPacked.Add(glyph);
                                        }
                                    }
                                }

                                FontEngine.TryPackGlyphsInAtlas(m_GlyphsToPack, m_GlyphsPacked, m_Padding, (GlyphPackingMode)m_PackingMode, m_GlyphRenderMode, m_AtlasWidth, m_AtlasHeight, m_FreeGlyphRects, m_UsedGlyphRects);

                                if (m_IsGenerationCancelled)
                                {
                                    Object.DestroyImmediate(m_FontAtlasTexture);
                                    m_FontAtlasTexture = null;
                                    return;
                                }
                                //Debug.Log("Glyphs remaining to add [" + m_GlyphsToAdd.Count + "]. Glyphs added [" + m_GlyphsAdded.Count + "].");
                            }

                        }
                        else
                        {
                            int packingModifier = ((GlyphRasterModes)m_GlyphRenderMode & GlyphRasterModes.RASTER_MODE_BITMAP) == GlyphRasterModes.RASTER_MODE_BITMAP ? 0 : 1;

                            FontEngine.SetFaceSize(m_PointSize);

                            m_GlyphsToPack.Clear();
                            m_GlyphsPacked.Clear();

                            m_FreeGlyphRects.Clear();
                            m_FreeGlyphRects.Add(new GlyphRect(0, 0, m_AtlasWidth - packingModifier, m_AtlasHeight - packingModifier));
                            m_UsedGlyphRects.Clear();
                        }

                        //Stop StopWatch
                        m_StopWatch.Stop();
                        m_GlyphPackingGenerationTime = m_StopWatch.Elapsed.TotalMilliseconds;
                        m_IsGlyphPackingDone = true;
                        m_StopWatch.Reset();

                        m_FontCharacterTable.Clear();
                        m_FontGlyphTable.Clear();
                        m_GlyphsToRender.Clear();

                        // Handle Results and potential cancellation of glyph rendering
                        if (m_GlyphRenderMode == GlyphRenderMode.SDF32 && m_PointSize > 512 || m_GlyphRenderMode == GlyphRenderMode.SDF16 && m_PointSize > 1024 || m_GlyphRenderMode == GlyphRenderMode.SDF8 && m_PointSize > 2048)
                        {
                            int upSampling = 1;
                            switch (m_GlyphRenderMode)
                            {
                             case GlyphRenderMode.SDF8:
                                 upSampling = 8;
                                 break;
                             case GlyphRenderMode.SDF16:
                                 upSampling = 16;
                                 break;
                             case GlyphRenderMode.SDF32:
                                 upSampling = 32;
                                 break;
                            }

                            Debug.Log("Glyph rendering has been aborted due to sampling point size of [" + m_PointSize + "] x SDF [" + upSampling + "] up sampling exceeds 16,384 point size. Please revise your generation settings to make sure the sampling point size x SDF up sampling mode does not exceed 16,384.");

                            m_IsRenderingDone = true;
                            m_AtlasGenerationProgress = 0;
                            m_IsGenerationCancelled = true;
                        }

                        // Add glyphs and characters successfully added to texture to their respective font tables.
                        foreach (Glyph glyph in m_GlyphsPacked)
                        {
                            uint glyphIndex = glyph.index;

                            m_FontGlyphTable.Add(glyph);

                            // Add glyphs to list of glyphs that need to be rendered.
                            if (glyph.glyphRect.width > 0 && glyph.glyphRect.height > 0)
                                m_GlyphsToRender.Add(glyph);

                            foreach (uint unicode in m_GlyphLookupMap[glyphIndex])
                            {
                                // Create new Character
                                m_FontCharacterTable.Add(new TMP_Character(unicode, glyph));
                            }
                        }

                        //
                        foreach (Glyph glyph in m_GlyphsToPack)
                        {
                            foreach (uint unicode in m_GlyphLookupMap[glyph.index])
                            {
                                m_ExcludedCharacters.Add(unicode);
                            }
                        }

                        // Get the face info for the current sampling point size.
                        m_FaceInfo = FontEngine.GetFaceInfo();

                        autoEvent.Set();
                    });

                    // Worker thread to render glyphs in texture buffer.
                    ThreadPool.QueueUserWorkItem(RenderGlyphs =>
                    {
                        autoEvent.WaitOne();

                        if (m_IsGenerationCancelled == false)
                        {
                            // Start Stop Watch
                            m_StopWatch = System.Diagnostics.Stopwatch.StartNew();

                            m_IsRenderingDone = false;

                            // Allocate texture data
                            m_AtlasTextureBuffer = new byte[m_AtlasWidth * m_AtlasHeight];

                            m_AtlasGenerationProgressLabel = "Rendering glyphs...";

                            // Render and add glyphs to the given atlas texture.
                            if (m_GlyphsToRender.Count > 0)
                            {
                                FontEngine.RenderGlyphsToTexture(m_GlyphsToRender, m_Padding, m_GlyphRenderMode, m_AtlasTextureBuffer, m_AtlasWidth, m_AtlasHeight);
                            }

                            m_IsRenderingDone = true;

                            // Stop StopWatch
                            m_StopWatch.Stop();
                            m_GlyphRenderingGenerationTime = m_StopWatch.Elapsed.TotalMilliseconds;
                            m_IsGlyphRenderingDone = true;
                            m_StopWatch.Reset();
                        }

                        EditorApplication.delayCall += () =>
                        {
                            Save();
                            tcs.TrySetResult( true );
                        };
                    });
                }
            }

            return tcs.Task;
        }

        /// <summary>
        /// Method which returns the character corresponding to a decimal value.
        /// </summary>
        /// <param name="sequence"></param>
        /// <returns></returns>
        static uint[] ParseNumberSequence(string sequence)
        {
            List<uint> unicodeList = new List<uint>();
            string[]   sequences   = sequence.Split(',');

            foreach (string seq in sequences)
            {
                string[] s1 = seq.Split('-');

                if (s1.Length == 1)
                    try
                    {
                        unicodeList.Add(uint.Parse(s1[0]));
                    }
                    catch
                    {
                        Debug.Log("No characters selected or invalid format.");
                    }
                else
                {
                    for (uint j = uint.Parse(s1[0]); j < uint.Parse(s1[1]) + 1; j++)
                    {
                        unicodeList.Add(j);
                    }
                }
            }

            return unicodeList.ToArray();
        }

        /// <summary>
        /// Method which returns the character (decimal value) from a hex sequence.
        /// </summary>
        /// <param name="sequence"></param>
        /// <returns></returns>
        static uint[] ParseHexNumberSequence(string sequence)
        {
            List<uint> unicodeList = new List<uint>();
            string[]   sequences   = sequence.Split(',');

            foreach (string seq in sequences)
            {
                string[] s1 = seq.Split('-');

                if (s1.Length == 1)
                    try
                    {
                        unicodeList.Add(uint.Parse(s1[0], NumberStyles.AllowHexSpecifier));
                    }
                    catch
                    {
                        Debug.Log("No characters selected or invalid format.");
                    }
                else
                {
                    for (uint j = uint.Parse(s1[0], NumberStyles.AllowHexSpecifier); j < uint.Parse(s1[1], NumberStyles.AllowHexSpecifier) + 1; j++)
                    {
                        unicodeList.Add(j);
                    }
                }
            }

            return unicodeList.ToArray();
        }

        private static void Update()
        {
            if (m_IsRepaintNeeded)
            {
                //Debug.Log("Repainting...");
                m_IsRepaintNeeded = false;
            }

            // Update Progress bar is we are Rendering a Font.
            if (m_IsProcessing)
            {
                m_AtlasGenerationProgress = FontEngine.generationProgress;

                m_IsRepaintNeeded = true;
            }

            if (m_IsGlyphPackingDone)
            {
                if (m_IsGenerationCancelled == false)
                {
                    Debug.Log("Glyph packing completed in: " + m_GlyphPackingGenerationTime.ToString("0.000 ms."));
                }

                m_IsGlyphPackingDone = false;
            }

            if (m_IsGlyphRenderingDone)
            {
                Debug.Log("Font Atlas generation completed in: " + m_GlyphRenderingGenerationTime.ToString("0.000 ms."));
                m_IsGlyphRenderingDone = false;
            }

            // Update Feedback Window & Create Font Texture once Rendering is done.
            if (m_IsRenderingDone)
            {
                m_IsProcessing    = false;
                m_IsRenderingDone = false;

                if (m_IsGenerationCancelled == false)
                {
                    m_AtlasGenerationProgress      = FontEngine.generationProgress;
                    m_AtlasGenerationProgressLabel = "Generation completed in: " + (m_GlyphPackingGenerationTime + m_GlyphRenderingGenerationTime).ToString("0.00 ms.");

                    CreateFontAtlasTexture();

                    // If dynamic make readable ...
                    m_FontAtlasTexture.Apply(false, false);
                }
            }
        }

        static void CreateFontAtlasTexture()
        {
            if (m_FontAtlasTexture != null)
                Object.DestroyImmediate(m_FontAtlasTexture);

            m_FontAtlasTexture = new Texture2D(m_AtlasWidth, m_AtlasHeight, TextureFormat.Alpha8, false, true);

            Color32[] colors = new Color32[m_AtlasWidth * m_AtlasHeight];

            for (int i = 0; i < colors.Length; i++)
            {
                byte c = m_AtlasTextureBuffer[i];
                colors[i] = new Color32(c, c, c, c);
            }

            // Clear allocation of
            m_AtlasTextureBuffer = null;

            if ((m_GlyphRenderMode & GlyphRenderMode.RASTER) == GlyphRenderMode.RASTER || (m_GlyphRenderMode & GlyphRenderMode.RASTER_HINTED) == GlyphRenderMode.RASTER_HINTED)
                m_FontAtlasTexture.filterMode = FilterMode.Point;

            m_FontAtlasTexture.SetPixels32(colors, 0);
            m_FontAtlasTexture.Apply(false, false);

            // Saving File for Debug
            //var pngData = m_FontAtlasTexture.EncodeToPNG();
            //File.WriteAllBytes("Assets/Textures/Debug Font Texture.png", pngData);
        }

        private static void Save()
        {
            if (m_SelectedFontAsset == null)
            {
                if (m_LegacyFontAsset != null)
                    SaveNewFontAssetWithSameName(m_LegacyFontAsset);
                else
                    SaveNewFontAsset(m_SourceFontFile);
            }
            else
            {
                // Save over exiting Font Asset
                string filePath = Path.GetFullPath(AssetDatabase.GetAssetPath(m_SelectedFontAsset)).Replace('\\', '/');

                if (((GlyphRasterModes)m_GlyphRenderMode & GlyphRasterModes.RASTER_MODE_BITMAP) == GlyphRasterModes.RASTER_MODE_BITMAP)
                    Save_Bitmap_FontAsset(filePath);
                else
                    Save_SDF_FontAsset(filePath);
            }
        }

        /// <summary>
        /// Open Save Dialog to provide the option save the font asset using the name of the source font file. This also appends SDF to the name if using any of the SDF Font Asset creation modes.
        /// </summary>
        /// <param name="sourceObject"></param>
        static void SaveNewFontAsset(Object sourceObject)
        {
            string filePath;

            // Save new Font Asset and open save file requester at Source Font File location.
            string saveDirectory = new FileInfo(AssetDatabase.GetAssetPath(sourceObject)).DirectoryName;

            if (((GlyphRasterModes)m_GlyphRenderMode & GlyphRasterModes.RASTER_MODE_BITMAP) == GlyphRasterModes.RASTER_MODE_BITMAP)
            {
                filePath = EditorUtility.SaveFilePanel("Save TextMesh Pro! Font Asset File", saveDirectory, sourceObject.name, "asset");

                if (filePath.Length == 0)
                    return;

                Save_Bitmap_FontAsset(filePath);
            }
            else
            {
                filePath = EditorUtility.SaveFilePanel("Save TextMesh Pro! Font Asset File", saveDirectory, sourceObject.name + " SDF", "asset");

                if (filePath.Length == 0)
                    return;

                Save_SDF_FontAsset(filePath);
            }
        }

        /// <summary>
        /// Open Save Dialog to provide the option to save the font asset under the same name.
        /// </summary>
        /// <param name="sourceObject"></param>
        static void SaveNewFontAssetWithSameName(Object sourceObject)
        {
            string filePath;

            // Save new Font Asset and open save file requester at Source Font File location.
            string saveDirectory = new FileInfo(AssetDatabase.GetAssetPath(sourceObject)).DirectoryName;

            filePath = EditorUtility.SaveFilePanel("Save TextMesh Pro! Font Asset File", saveDirectory, sourceObject.name, "asset");

            if (filePath.Length == 0)
                return;

            if (((GlyphRasterModes)m_GlyphRenderMode & GlyphRasterModes.RASTER_MODE_BITMAP) == GlyphRasterModes.RASTER_MODE_BITMAP)
            {
                Save_Bitmap_FontAsset(filePath);
            }
            else
            {
                Save_SDF_FontAsset(filePath);
            }
        }

        static void Save_Bitmap_FontAsset(string filePath)
        {
            filePath = filePath.Substring(0, filePath.Length - 6); // Trim file extension from filePath.

            string dataPath = Application.dataPath;

            if (filePath.IndexOf(dataPath, System.StringComparison.InvariantCultureIgnoreCase) == -1)
            {
                Debug.LogError("You're saving the font asset in a directory outside of this project folder. This is not supported. Please select a directory under \"" + dataPath + "\"");
                return;
            }

            string relativeAssetPath = filePath.Substring(dataPath.Length - 6);
            string tex_DirName = Path.GetDirectoryName(relativeAssetPath);
            string tex_FileName = Path.GetFileNameWithoutExtension(relativeAssetPath);
            string tex_Path_NoExt = tex_DirName + "/" + tex_FileName;

            // Check if TextMeshPro font asset already exists. If not, create a new one. Otherwise update the existing one.
            TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath(tex_Path_NoExt + ".asset", typeof(TMP_FontAsset)) as TMP_FontAsset;
            if (fontAsset == null)
            {
                //Debug.Log("Creating TextMeshPro font asset!");
                fontAsset = ScriptableObject.CreateInstance<TMP_FontAsset>(); // Create new TextMeshPro Font Asset.
                AssetDatabase.CreateAsset(fontAsset, tex_Path_NoExt + ".asset");

                // Set version number of font asset
                fontAsset.version = "1.1.0";

                //Set Font Asset Type
                fontAsset.atlasRenderMode = m_GlyphRenderMode;

                // Reference to the source font file GUID.
                fontAsset.m_SourceFontFile_EditorRef = (Font)m_SourceFontFile;
                fontAsset.m_SourceFontFileGUID = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m_SourceFontFile));

                // Add FaceInfo to Font Asset
                fontAsset.faceInfo = m_FaceInfo;

                // Add GlyphInfo[] to Font Asset
                fontAsset.glyphTable = m_FontGlyphTable;

                // Add CharacterTable[] to font asset.
                fontAsset.characterTable = m_FontCharacterTable;

                // Sort glyph and character tables.
                fontAsset.SortAllTables();

                // Get and Add Kerning Pairs to Font Asset
                if (m_IncludeFontFeatures)
                    fontAsset.fontFeatureTable = GetKerningTable();


                // Add Font Atlas as Sub-Asset
                fontAsset.atlasTextures = new Texture2D[] { m_FontAtlasTexture };
                m_FontAtlasTexture.name = tex_FileName + " Atlas";
                fontAsset.atlasWidth = m_AtlasWidth;
                fontAsset.atlasHeight = m_AtlasHeight;
                fontAsset.atlasPadding = m_Padding;

                AssetDatabase.AddObjectToAsset(m_FontAtlasTexture, fontAsset);

                // Create new Material and Add it as Sub-Asset
                Shader default_Shader = Shader.Find("TextMeshPro/Bitmap"); // m_shaderSelection;
                Material tmp_material = new Material(default_Shader);
                tmp_material.name = tex_FileName + " Material";
                tmp_material.SetTexture(ShaderUtilities.ID_MainTex, m_FontAtlasTexture);
                fontAsset.material = tmp_material;

                AssetDatabase.AddObjectToAsset(tmp_material, fontAsset);

            }
            else
            {
                // Find all Materials referencing this font atlas.
                Material[] material_references = TMP_EditorUtility.FindMaterialReferences(fontAsset);

                // Set version number of font asset
                fontAsset.version = "1.1.0";

                // Special handling to remove legacy font asset data
                if (fontAsset.m_glyphInfoList != null && fontAsset.m_glyphInfoList.Count > 0)
                    fontAsset.m_glyphInfoList = null;

                //Set Font Asset Type
                fontAsset.atlasRenderMode = m_GlyphRenderMode;

                // Add FaceInfo to Font Asset
                fontAsset.faceInfo = m_FaceInfo;

                // Add GlyphInfo[] to Font Asset
                fontAsset.glyphTable = m_FontGlyphTable;

                // Add CharacterTable[] to font asset.
                fontAsset.characterTable = m_FontCharacterTable;

                // Sort glyph and character tables.
                fontAsset.SortAllTables();

                // Get and Add Kerning Pairs to Font Asset
                if (m_IncludeFontFeatures)
                    fontAsset.fontFeatureTable = GetKerningTable();

                // Destroy Assets that will be replaced.
                if (fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length > 0)
                {
                    for (int i = 1; i < fontAsset.atlasTextures.Length; i++)
                        Object.DestroyImmediate(fontAsset.atlasTextures[i], true);
                }

                fontAsset.m_AtlasTextureIndex = 0;
                fontAsset.atlasWidth = m_AtlasWidth;
                fontAsset.atlasHeight = m_AtlasHeight;
                fontAsset.atlasPadding = m_Padding;

                // Make sure remaining atlas texture is of the correct size
                Texture2D tex = fontAsset.atlasTextures[0];
                tex.name = tex_FileName + " Atlas";

                // Make texture readable to allow resizing
                bool isReadableState = tex.isReadable;
                if (isReadableState == false)
                    FontEngineEditorUtilities.SetAtlasTextureIsReadable(tex, true);

                if (tex.width != m_AtlasWidth || tex.height != m_AtlasHeight)
                {
                    tex.Reinitialize(m_AtlasWidth, m_AtlasHeight);
                    tex.Apply(false);
                }

                // Copy new texture data to existing texture
                Graphics.CopyTexture(m_FontAtlasTexture, tex);

                // Apply changes to the texture.
                tex.Apply(false);

                // Special handling due to a bug in earlier versions of Unity.
                m_FontAtlasTexture.hideFlags = HideFlags.None;
                fontAsset.material.hideFlags = HideFlags.None;

                // Update the Texture reference on the Material
                //for (int i = 0; i < material_references.Length; i++)
                //{
                //    material_references[i].SetFloat(ShaderUtilities.ID_TextureWidth, tex.width);
                //    material_references[i].SetFloat(ShaderUtilities.ID_TextureHeight, tex.height);

                //    int spread = m_Padding;
                //    material_references[i].SetFloat(ShaderUtilities.ID_GradientScale, spread);

                //    material_references[i].SetFloat(ShaderUtilities.ID_WeightNormal, fontAsset.normalStyle);
                //    material_references[i].SetFloat(ShaderUtilities.ID_WeightBold, fontAsset.boldStyle);
                //}
            }

            // Set texture to non readable
            FontEngineEditorUtilities.SetAtlasTextureIsReadable(fontAsset.atlasTexture, false);

            // Add list of GlyphRects to font asset.
            fontAsset.freeGlyphRects = m_FreeGlyphRects;
            fontAsset.usedGlyphRects = m_UsedGlyphRects;

            // Save Font Asset creation settings
            m_SelectedFontAsset = fontAsset;
            m_LegacyFontAsset = null;
            fontAsset.creationSettings = SaveFontCreationSettings();

            AssetDatabase.SaveAssets();

            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(fontAsset));  // Re-import font asset to get the new updated version.

            //EditorUtility.SetDirty(font_asset);
            fontAsset.ReadFontAssetDefinition();

            AssetDatabase.Refresh();

            m_FontAtlasTexture = null;

            // NEED TO GENERATE AN EVENT TO FORCE A REDRAW OF ANY TEXTMESHPRO INSTANCES THAT MIGHT BE USING THIS FONT ASSET
            TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, fontAsset);
        }

        static void Save_SDF_FontAsset(string filePath)
        {
            filePath = filePath.Substring(0, filePath.Length - 6); // Trim file extension from filePath.

            string dataPath = Application.dataPath;

            if (filePath.IndexOf(dataPath, System.StringComparison.InvariantCultureIgnoreCase) == -1)
            {
                Debug.LogError("You're saving the font asset in a directory outside of this project folder. This is not supported. Please select a directory under \"" + dataPath + "\"");
                return;
            }

            string relativeAssetPath = filePath.Substring(dataPath.Length - 6);
            string tex_DirName = Path.GetDirectoryName(relativeAssetPath);
            string tex_FileName = Path.GetFileNameWithoutExtension(relativeAssetPath);
            string tex_Path_NoExt = tex_DirName + "/" + tex_FileName;


            // Check if TextMeshPro font asset already exists. If not, create a new one. Otherwise update the existing one.
            TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(tex_Path_NoExt + ".asset");
            if (fontAsset == null)
            {
                //Debug.Log("Creating TextMeshPro font asset!");
                fontAsset = ScriptableObject.CreateInstance<TMP_FontAsset>(); // Create new TextMeshPro Font Asset.
                AssetDatabase.CreateAsset(fontAsset, tex_Path_NoExt + ".asset");

                // Set version number of font asset
                fontAsset.version = "1.1.0";

                // Reference to source font file GUID.
                fontAsset.m_SourceFontFile_EditorRef = (Font)m_SourceFontFile;
                fontAsset.m_SourceFontFileGUID = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m_SourceFontFile));

                //Set Font Asset Type
                fontAsset.atlasRenderMode = m_GlyphRenderMode;

                // Add FaceInfo to Font Asset
                fontAsset.faceInfo = m_FaceInfo;

                // Add GlyphInfo[] to Font Asset
                fontAsset.glyphTable = m_FontGlyphTable;

                // Add CharacterTable[] to font asset.
                fontAsset.characterTable = m_FontCharacterTable;

                // Sort glyph and character tables.
                fontAsset.SortAllTables();

                // Get and Add Kerning Pairs to Font Asset
                if (m_IncludeFontFeatures)
                    fontAsset.fontFeatureTable = GetKerningTable();

                // Add Font Atlas as Sub-Asset
                fontAsset.atlasTextures = new Texture2D[] { m_FontAtlasTexture };
                m_FontAtlasTexture.name = tex_FileName + " Atlas";
                fontAsset.atlasWidth = m_AtlasWidth;
                fontAsset.atlasHeight = m_AtlasHeight;
                fontAsset.atlasPadding = m_Padding;

                AssetDatabase.AddObjectToAsset(m_FontAtlasTexture, fontAsset);

                // Create new Material and Add it as Sub-Asset
                Shader default_Shader = Shader.Find("TextMeshPro/Distance Field");
                Material tmp_material = new Material(default_Shader);

                tmp_material.name = tex_FileName + " Material";
                tmp_material.SetTexture(ShaderUtilities.ID_MainTex, m_FontAtlasTexture);
                tmp_material.SetFloat(ShaderUtilities.ID_TextureWidth, m_FontAtlasTexture.width);
                tmp_material.SetFloat(ShaderUtilities.ID_TextureHeight, m_FontAtlasTexture.height);

                int spread = m_Padding + 1;
                tmp_material.SetFloat(ShaderUtilities.ID_GradientScale, spread); // Spread = Padding for Brute Force SDF.

                tmp_material.SetFloat(ShaderUtilities.ID_WeightNormal, fontAsset.normalStyle);
                tmp_material.SetFloat(ShaderUtilities.ID_WeightBold, fontAsset.boldStyle);

                fontAsset.material = tmp_material;

                AssetDatabase.AddObjectToAsset(tmp_material, fontAsset);
            }
            else
            {
                // Find all Materials referencing this font atlas.
                Material[] material_references = TMP_EditorUtility.FindMaterialReferences(fontAsset);

                // Set version number of font asset
                fontAsset.version = "1.1.0";

                // Special handling to remove legacy font asset data
                if (fontAsset.m_glyphInfoList != null && fontAsset.m_glyphInfoList.Count > 0)
                    fontAsset.m_glyphInfoList = null;

                //Set Font Asset Type
                fontAsset.atlasRenderMode = m_GlyphRenderMode;

                // Add FaceInfo to Font Asset
                fontAsset.faceInfo = m_FaceInfo;

                // Add GlyphInfo[] to Font Asset
                fontAsset.glyphTable = m_FontGlyphTable;

                // Add CharacterTable[] to font asset.
                fontAsset.characterTable = m_FontCharacterTable;

                // Sort glyph and character tables.
                fontAsset.SortAllTables();

                // Get and Add Kerning Pairs to Font Asset
                // TODO: Check and preserve existing adjustment pairs.
                if (m_IncludeFontFeatures)
                    fontAsset.fontFeatureTable = GetKerningTable();

                // Destroy Assets that will be replaced.
                if (fontAsset.atlasTextures != null && fontAsset.atlasTextures.Length > 0)
                {
                    for (int i = 1; i < fontAsset.atlasTextures.Length; i++)
                        Object.DestroyImmediate(fontAsset.atlasTextures[i], true);
                }

                fontAsset.m_AtlasTextureIndex = 0;
                fontAsset.atlasWidth = m_AtlasWidth;
                fontAsset.atlasHeight = m_AtlasHeight;
                fontAsset.atlasPadding = m_Padding;

                // Make sure remaining atlas texture is of the correct size
                Texture2D tex = fontAsset.atlasTextures[0];
                tex.name = tex_FileName + " Atlas";

                // Make texture readable to allow resizing
                bool isReadableState = tex.isReadable;
                if (isReadableState == false)
                    FontEngineEditorUtilities.SetAtlasTextureIsReadable(tex, true);

                if (tex.width != m_AtlasWidth || tex.height != m_AtlasHeight)
                {
                    tex.Reinitialize(m_AtlasWidth, m_AtlasHeight);
                    tex.Apply(false);
                }

                // Copy new texture data to existing texture
                Graphics.CopyTexture(m_FontAtlasTexture, tex);

                // Apply changes to the texture.
                tex.Apply(false);

                // Special handling due to a bug in earlier versions of Unity.
                m_FontAtlasTexture.hideFlags = HideFlags.None;
                fontAsset.material.hideFlags = HideFlags.None;

                // Update the Texture reference on the Material
                for (int i = 0; i < material_references.Length; i++)
                {
                    material_references[i].SetFloat(ShaderUtilities.ID_TextureWidth, tex.width);
                    material_references[i].SetFloat(ShaderUtilities.ID_TextureHeight, tex.height);

                    int spread = m_Padding + 1;
                    material_references[i].SetFloat(ShaderUtilities.ID_GradientScale, spread);

                    material_references[i].SetFloat(ShaderUtilities.ID_WeightNormal, fontAsset.normalStyle);
                    material_references[i].SetFloat(ShaderUtilities.ID_WeightBold, fontAsset.boldStyle);
                }
            }

            // Saving File for Debug
            //var pngData = destination_Atlas.EncodeToPNG();
            //File.WriteAllBytes("Assets/Textures/Debug Distance Field.png", pngData);

            // Set texture to non readable
            FontEngineEditorUtilities.SetAtlasTextureIsReadable(fontAsset.atlasTexture, false);

            // Add list of GlyphRects to font asset.
            fontAsset.freeGlyphRects = m_FreeGlyphRects;
            fontAsset.usedGlyphRects = m_UsedGlyphRects;

            // Save Font Asset creation settings
            m_SelectedFontAsset = fontAsset;
            m_LegacyFontAsset = null;
            fontAsset.creationSettings = SaveFontCreationSettings();

            AssetDatabase.SaveAssets();

            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(fontAsset));  // Re-import font asset to get the new updated version.

            fontAsset.ReadFontAssetDefinition();

            AssetDatabase.Refresh();

            m_FontAtlasTexture = null;

            // NEED TO GENERATE AN EVENT TO FORCE A REDRAW OF ANY TEXTMESHPRO INSTANCES THAT MIGHT BE USING THIS FONT ASSET
            TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, fontAsset);
        }

        // Get Kerning Pairs
        private static TMP_FontFeatureTable GetKerningTable()
        {
            GlyphPairAdjustmentRecord[] adjustmentRecords = FontEngine.GetGlyphPairAdjustmentTable(m_AvailableGlyphsToAdd.ToArray());

            if (adjustmentRecords == null)
                return null;

            TMP_FontFeatureTable fontFeatureTable = new TMP_FontFeatureTable();

            for (int i = 0; i < adjustmentRecords.Length && adjustmentRecords[i].firstAdjustmentRecord.glyphIndex != 0; i++)
            {
                fontFeatureTable.glyphPairAdjustmentRecords.Add(new TMP_GlyphPairAdjustmentRecord(adjustmentRecords[i]));
            }

            fontFeatureTable.SortGlyphPairAdjustmentRecords();

            return fontFeatureTable;
        }

        /// <summary>
        /// Internal method to save the Font Asset Creation Settings
        /// </summary>
        /// <returns></returns>
        static FontAssetCreationSettings SaveFontCreationSettings()
        {
            FontAssetCreationSettings settings = new FontAssetCreationSettings();

            //settings.sourceFontFileName = m_SourceFontFile.name;
            settings.sourceFontFileGUID        = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m_SourceFontFile));
            settings.pointSizeSamplingMode     = m_PointSizeSamplingMode;
            settings.pointSize                 = m_PointSize;
            settings.padding                   = m_Padding;
            settings.packingMode               = (int)m_PackingMode;
            settings.atlasWidth                = m_AtlasWidth;
            settings.atlasHeight               = m_AtlasHeight;
            settings.characterSetSelectionMode = m_CharacterSetSelectionMode;
            settings.characterSequence         = m_CharacterSequence;
            settings.referencedFontAssetGUID   = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m_ReferencedFontAsset));
            settings.referencedTextAssetGUID   = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m_CharactersFromFile));
            //settings.fontStyle = (int)m_FontStyle;
            //settings.fontStyleModifier = m_FontStyleValue;
            settings.renderMode          = (int)m_GlyphRenderMode;
            settings.includeFontFeatures = m_IncludeFontFeatures;

            return settings;
        }
    }
}
