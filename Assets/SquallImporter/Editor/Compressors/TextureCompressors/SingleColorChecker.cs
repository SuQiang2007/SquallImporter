using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEditor;

public class SingleColorChecker : EditorWindow
{
    private DefaultAsset searchFolder;
    private readonly List<string> singleColorTextures = new List<string>();
    private Vector2 scrollPosition;
    private readonly string[] supportedPlatforms = { "Android", "iPhone", "Standalone", "WebGL", "tvOS", "PS4", "XboxOne", "Switch" };
    private readonly int[] maxSizeOptions = { 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192 };
    private int selectedTargetMaxSizeIndex = 4;
    private int targetMaxSize = 512;

    [MenuItem("Tools/SquallTools/Optimise/Single Color Texture Checker")]
    public static void ShowWindow()
    {
        var window = GetWindow<SingleColorChecker>();
        window.titleContent = new GUIContent("Single Color Checker");
        window.minSize = new Vector2(520, 360);
        window.Show();
    }

    private void OnGUI()
    {
        if (searchFolder == null)
        {
            searchFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets");
        }

        EditorGUILayout.LabelField("Search", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            searchFolder = (DefaultAsset)EditorGUILayout.ObjectField("Folder", searchFolder, typeof(DefaultAsset), false);
            if (GUILayout.Button("Search", GUILayout.Width(120)))
            {
                FindSingleColorTextures();
            }
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Target Max Size", GUILayout.Width(120));
            selectedTargetMaxSizeIndex = EditorGUILayout.Popup(selectedTargetMaxSizeIndex, GetMaxSizeLabels(), GUILayout.Width(100));
            targetMaxSize = maxSizeOptions[selectedTargetMaxSizeIndex];
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Single Color Textures", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
        {
            EditorGUILayout.LabelField($"Count: {singleColorTextures.Count}");

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(220));
            foreach (var path in singleColorTextures)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(path, EditorStyles.linkLabel))
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                        if (asset != null)
                        {
                            Selection.activeObject = asset;
                            EditorGUIUtility.PingObject(asset);
                        }
                    }

                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Ping", GUILayout.Width(60)))
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                        if (asset != null)
                        {
                            Selection.activeObject = asset;
                            EditorGUIUtility.PingObject(asset);
                        }
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.Space(8);
        using (new EditorGUI.DisabledScope(singleColorTextures.Count == 0))
        {
            if (GUILayout.Button($"Batch Set Max Size to {targetMaxSize}", GUILayout.Height(28)))
            {
                BatchSetMaxSize();
            }
        }
    }

    private string[] GetMaxSizeLabels()
    {
        var labels = new string[maxSizeOptions.Length];
        for (int i = 0; i < maxSizeOptions.Length; i++)
        {
            labels[i] = maxSizeOptions[i].ToString();
        }
        return labels;
    }

    private void FindSingleColorTextures()
    {
        singleColorTextures.Clear();

        var folderPath = searchFolder != null ? AssetDatabase.GetAssetPath(searchFolder) : "Assets";
        if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
        {
            EditorUtility.DisplayDialog("Invalid Folder", "Please select a valid folder in the Project window.", "OK");
            return;
        }

        try
        {
            EditorUtility.DisplayProgressBar("Searching Textures", "Scanning folder...", 0f);
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath });
            int totalCount = guids.Length;
            int currentIndex = 0;
            int skippedUnreadable = 0;

            foreach (var guid in guids)
            {
                currentIndex++;
                var path = AssetDatabase.GUIDToAssetPath(guid);

                float progress = totalCount == 0 ? 1f : (float)currentIndex / totalCount;
                string progressText = $"Checking: {path} ({currentIndex}/{totalCount})";
                EditorUtility.DisplayProgressBar("Searching Textures", progressText, progress);

                try
                {
                    if (IsSingleColorTexture(path))
                    {
                        singleColorTextures.Add(path);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"SingleColorChecker: Unable to process {path}. {e.Message}");
                    skippedUnreadable++;
                }
            }

            EditorUtility.ClearProgressBar();

            string message = singleColorTextures.Count == 0
                ? $"No single color textures found in:\n{folderPath}"
                : $"Found {singleColorTextures.Count} single color texture(s) in:\n{folderPath}";

            if (skippedUnreadable > 0)
            {
                message += $"\n\nSkipped {skippedUnreadable} texture(s) because they could not be analyzed.";
            }

            EditorUtility.DisplayDialog("Search Complete", message, "OK");
        }
        catch (Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError($"SingleColorChecker error: {e}");
            EditorUtility.DisplayDialog("Search Error", $"An error occurred during search:\n{e.Message}", "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Repaint();
    }

    private bool IsSingleColorTexture(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
        {
            return false;
        }

        if (!TryLoadPixels(assetPath, out var pixels, out var width, out var height))
        {
            throw new InvalidOperationException("Unable to read texture data.");
        }

        if (pixels == null || pixels.Length == 0)
        {
            return false;
        }

        if (width < 16 || height < 16)
        {
            return false;
        }

        var firstPixel = pixels[0];
        for (int i = 1; i < pixels.Length; i++)
        {
            if (!PixelsEqual(firstPixel, pixels[i]))
            {
                return false;
            }
        }

        return true;
    }

    private bool PixelsEqual(Color32 a, Color32 b)
    {
        return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
    }

    private bool TryLoadPixels(string assetPath, out Color32[] pixels, out int width, out int height)
    {
        pixels = Array.Empty<Color32>();
        width = 0;
        height = 0;

        try
        {
            string projectRoot = Application.dataPath.Substring(0, Application.dataPath.Length - "Assets".Length);
            string normalizedPath = assetPath.Replace("/", Path.DirectorySeparatorChar.ToString());
            string fullPath = Path.Combine(projectRoot, normalizedPath);

            if (!File.Exists(fullPath))
            {
                return false;
            }

            byte[] fileData = File.ReadAllBytes(fullPath);
            using (var tempTexture = new TempTextureLoader())
            {
                if (!tempTexture.LoadImage(fileData))
                {
                    return false;
                }

                var texture = tempTexture.Texture;
                width = texture.width;
                height = texture.height;
                pixels = texture.GetPixels32();
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    private void BatchSetMaxSize()
    {
        if (singleColorTextures.Count == 0)
        {
            EditorUtility.DisplayDialog("No Textures", "No textures to process. Click Search first.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Confirm", $"This will set Max Size to {targetMaxSize} for {singleColorTextures.Count} texture(s). Continue?", "Yes", "No"))
        {
            return;
        }

        try
        {
            AssetDatabase.StartAssetEditing();
            int modified = 0;
            int errors = 0;

            for (int i = 0; i < singleColorTextures.Count; i++)
            {
                var path = singleColorTextures[i];
                EditorUtility.DisplayProgressBar("Applying Max Size", path, (float)(i + 1) / singleColorTextures.Count);

                try
                {
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer == null)
                    {
                        continue;
                    }

                    bool changed = false;

                    var defaultSettings = importer.GetDefaultPlatformTextureSettings();
                    if (defaultSettings.maxTextureSize != targetMaxSize)
                    {
                        defaultSettings.maxTextureSize = targetMaxSize;
                        importer.SetPlatformTextureSettings(defaultSettings);
                        changed = true;
                    }

                    foreach (var platform in supportedPlatforms)
                    {
                        try
                        {
                            var platformSettings = importer.GetPlatformTextureSettings(platform);
                            if (platformSettings.name == platform)
                            {
                                var overriddenValue = GetOverriddenValue(platformSettings);
                                bool isOverridden = overriddenValue.HasValue && overriddenValue.Value;

                                if (isOverridden && platformSettings.maxTextureSize != targetMaxSize)
                                {
                                    platformSettings.maxTextureSize = targetMaxSize;
                                    importer.SetPlatformTextureSettings(platformSettings);
                                    changed = true;
                                }
                            }
                        }
                        catch
                        {
                            // Platform not available, skip
                        }
                    }

                    if (changed)
                    {
                        importer.SaveAndReimport();
                        modified++;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error setting maxSize for {path}: {e.Message}");
                    errors++;
                }
            }

            EditorUtility.ClearProgressBar();

            if (errors > 0)
            {
                EditorUtility.DisplayDialog("Complete with Errors", $"Updated {modified} texture(s).\n{errors} error(s) occurred. Check Console for details.", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Complete", $"Successfully set Max Size to {targetMaxSize} for {modified} texture(s).", "OK");
            }

            FindSingleColorTextures();
        }
        catch (Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError($"SingleColorChecker error: {e}");
            EditorUtility.DisplayDialog("Error", $"An error occurred: {e.Message}", "OK");
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
    }

    private bool? GetOverriddenValue(object settingsObject)
    {
        if (settingsObject == null) return null;

        var type = settingsObject.GetType();

        var property = type.GetProperty("overridden", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property != null && property.PropertyType == typeof(bool))
        {
            try
            {
                return (bool)property.GetValue(settingsObject);
            }
            catch { }
        }

        var field = type.GetField("overridden", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null && field.FieldType == typeof(bool))
        {
            try
            {
                return (bool)field.GetValue(settingsObject);
            }
            catch { }
        }

        return null;
    }

    private sealed class TempTextureLoader : IDisposable
    {
        public Texture2D Texture { get; }

        public TempTextureLoader()
        {
            Texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        }

        public bool LoadImage(byte[] data)
        {
            return ImageConversion.LoadImage(Texture, data, false);
        }

        public void Dispose()
        {
            if (Texture != null)
            {
                UnityEngine.Object.DestroyImmediate(Texture);
            }
        }
    }
}