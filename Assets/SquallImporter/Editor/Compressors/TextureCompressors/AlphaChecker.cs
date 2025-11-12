using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

public class AlphaChecker : EditorWindow
{
    private DefaultAsset searchFolder;
    private readonly List<string> texturesWithEmptyAlpha = new List<string>();
    private Vector2 scrollPosition;

    [MenuItem("Tools/SquallTools/Optimise/Texture Alpha Checker")]
    public static void ShowWindow()
    {
        var window = GetWindow<AlphaChecker>();
        window.titleContent = new GUIContent("Alpha Checker");
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
                FindTexturesWithEmptyAlpha();
            }
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Textures With Empty Alpha", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
        {
            EditorGUILayout.LabelField($"Count: {texturesWithEmptyAlpha.Count}");

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(220));
            foreach (var path in texturesWithEmptyAlpha)
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
        using (new EditorGUI.DisabledScope(texturesWithEmptyAlpha.Count == 0))
        {
            if (GUILayout.Button("Batch Set Alpha Source to None", GUILayout.Height(28)))
            {
                BatchSetAlphaSourceToNone();
            }
        }
    }

    private void FindTexturesWithEmptyAlpha()
    {
        texturesWithEmptyAlpha.Clear();

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
                    if (IsAlphaChannelEmpty(path))
                    {
                        texturesWithEmptyAlpha.Add(path);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"AlphaChecker: Unable to process {path}. {e.Message}");
                    skippedUnreadable++;
                }
            }

            EditorUtility.ClearProgressBar();

            string message = texturesWithEmptyAlpha.Count == 0
                ? $"No textures with empty alpha found in:\n{folderPath}"
                : $"Found {texturesWithEmptyAlpha.Count} texture(s) with empty alpha in:\n{folderPath}";

            if (skippedUnreadable > 0)
            {
                message += $"\n\nSkipped {skippedUnreadable} texture(s) because they could not be analyzed.";
            }

            EditorUtility.DisplayDialog("Search Complete", message, "OK");
        }
        catch (Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError($"AlphaChecker error: {e}");
            EditorUtility.DisplayDialog("Search Error", $"An error occurred during search:\n{e.Message}", "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Repaint();
    }

    private bool IsAlphaChannelEmpty(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
        {
            return false;
        }

        if (!TryLoadPixels(assetPath, out var pixels))
        {
            throw new InvalidOperationException("Unable to read texture data.");
        }

        if (pixels == null || pixels.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < pixels.Length; i++)
        {
            if (pixels[i].a != 255)
            {
                return false;
            }
        }

        return true;
    }

    private bool TryLoadPixels(string assetPath, out Color32[] pixels)
    {
        pixels = Array.Empty<Color32>();

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

                pixels = tempTexture.Texture.GetPixels32();
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    private void BatchSetAlphaSourceToNone()
    {
        if (texturesWithEmptyAlpha.Count == 0)
        {
            EditorUtility.DisplayDialog("No Textures", "No textures to process. Click Search first.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Confirm", $"This will set Alpha Source to None for {texturesWithEmptyAlpha.Count} texture(s). Continue?", "Yes", "No"))
        {
            return;
        }

        try
        {
            AssetDatabase.StartAssetEditing();
            int modified = 0;
            int errors = 0;

            for (int i = 0; i < texturesWithEmptyAlpha.Count; i++)
            {
                var path = texturesWithEmptyAlpha[i];
                EditorUtility.DisplayProgressBar("Applying Alpha Settings", path, (float)(i + 1) / texturesWithEmptyAlpha.Count);

                try
                {
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer == null)
                    {
                        continue;
                    }

                    bool changed = false;

                    if (importer.alphaSource != TextureImporterAlphaSource.None)
                    {
                        importer.alphaSource = TextureImporterAlphaSource.None;
                        changed = true;
                    }

                    if (importer.alphaIsTransparency)
                    {
                        importer.alphaIsTransparency = false;
                        changed = true;
                    }

                    if (changed)
                    {
                        importer.SaveAndReimport();
                        modified++;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error setting alpha source for {path}: {e.Message}");
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
                EditorUtility.DisplayDialog("Complete", $"Successfully set Alpha Source to None for {modified} texture(s).", "OK");
            }

            FindTexturesWithEmptyAlpha();
        }
        catch (Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError($"AlphaChecker error: {e}");
            EditorUtility.DisplayDialog("Error", $"An error occurred: {e.Message}", "OK");
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
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