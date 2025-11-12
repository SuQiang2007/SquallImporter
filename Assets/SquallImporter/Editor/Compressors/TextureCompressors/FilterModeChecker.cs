using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class FilterModeChecker : EditorWindow
{
    private DefaultAsset searchFolder = null;
    private readonly List<string> trilinearTextures = new List<string>();
    private readonly List<DefaultAsset> excludedFolders = new List<DefaultAsset>();
    private Vector2 scrollPosition;
    private FilterMode targetFilterMode = FilterMode.Bilinear;

    [MenuItem("Tools/SquallTools/Optimise/Texture FilterMode Checker")]
    public static void ShowWindow()
    {
        var window = GetWindow<FilterModeChecker>();
        window.titleContent = new GUIContent("FilterMode Checker");
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
                FindTrilinearTextures();
            }
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Excluded Folders", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
        {
            for (int i = 0; i < excludedFolders.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    excludedFolders[i] = (DefaultAsset)EditorGUILayout.ObjectField($"Exclude {i + 1}", excludedFolders[i], typeof(DefaultAsset), false);
                    if (GUILayout.Button("Remove", GUILayout.Width(70)))
                    {
                        excludedFolders.RemoveAt(i);
                        i--;
                        continue;
                    }
                }

                var excludedPath = excludedFolders.ElementAtOrDefault(i) != null ? AssetDatabase.GetAssetPath(excludedFolders[i]) : string.Empty;
                if (!string.IsNullOrEmpty(excludedPath) && !AssetDatabase.IsValidFolder(excludedPath))
                {
                    EditorGUILayout.HelpBox("Not a valid folder. Select a folder from the Project window.", MessageType.Warning);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Add Exclusion", GUILayout.Width(140)))
                {
                    excludedFolders.Add(null);
                }
            }
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
        targetFilterMode = (FilterMode)EditorGUILayout.EnumPopup("Target Filter Mode", targetFilterMode);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Trilinear Textures", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
        {
            EditorGUILayout.LabelField($"Count: {trilinearTextures.Count}");

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(220));
            foreach (var path in trilinearTextures)
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
        using (new EditorGUI.DisabledScope(trilinearTextures.Count == 0))
        {
            if (GUILayout.Button($"Batch Set Filter Mode to {targetFilterMode}", GUILayout.Height(28)))
            {
                BatchSetFilterMode();
            }
        }
    }

    private void FindTrilinearTextures()
    {
        trilinearTextures.Clear();

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

            var excludedPaths = new List<string>();
            foreach (var excluded in excludedFolders)
            {
                if (excluded == null)
                {
                    continue;
                }

                var excludedPath = AssetDatabase.GetAssetPath(excluded);
                if (string.IsNullOrEmpty(excludedPath))
                {
                    continue;
                }

                if (AssetDatabase.IsValidFolder(excludedPath))
                {
                    excludedPaths.Add(excludedPath.Replace("\\", "/").TrimEnd('/'));
                }
            }

            foreach (var guid in guids)
            {
                currentIndex++;
                var path = AssetDatabase.GUIDToAssetPath(guid);

                var normalizedPath = path.Replace("\\", "/");
                bool isExcluded = false;
                foreach (var excludedPath in excludedPaths)
                {
                    if (normalizedPath.Equals(excludedPath, StringComparison.OrdinalIgnoreCase) ||
                        normalizedPath.StartsWith(excludedPath + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        isExcluded = true;
                        break;
                    }
                }

                if (isExcluded)
                {
                    continue;
                }

                float progress = (float)currentIndex / totalCount;
                string progressText = $"Checking: {path} ({currentIndex}/{totalCount})";
                EditorUtility.DisplayProgressBar("Searching Textures", progressText, progress);

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }

                if (importer.filterMode == FilterMode.Trilinear)
                {
                    trilinearTextures.Add(path);
                }
            }

            EditorUtility.ClearProgressBar();

            if (trilinearTextures.Count == 0)
            {
                EditorUtility.DisplayDialog("Search Complete", $"No textures using Trilinear filter found in:\n{folderPath}", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Search Complete", $"Found {trilinearTextures.Count} texture(s) using Trilinear filter in:\n{folderPath}", "OK");
            }
        }
        catch (Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError($"FilterModeChecker error: {e}");
            EditorUtility.DisplayDialog("Search Error", $"An error occurred during search:\n{e.Message}", "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Repaint();
    }

    private void BatchSetFilterMode()
    {
        if (trilinearTextures.Count == 0)
        {
            EditorUtility.DisplayDialog("No Textures", "No textures to process. Click Search first.", "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog("Confirm", $"This will set Filter Mode to {targetFilterMode} for {trilinearTextures.Count} texture(s). Continue?", "Yes", "No"))
        {
            return;
        }

        try
        {
            AssetDatabase.StartAssetEditing();
            int modified = 0;
            int errors = 0;

            for (int i = 0; i < trilinearTextures.Count; i++)
            {
                var path = trilinearTextures[i];
                EditorUtility.DisplayProgressBar("Applying Filter Mode", path, (float)(i + 1) / trilinearTextures.Count);

                try
                {
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer == null)
                    {
                        continue;
                    }

                    if (importer.filterMode != targetFilterMode)
                    {
                        importer.filterMode = targetFilterMode;
                        importer.SaveAndReimport();
                        modified++;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error setting filter mode for {path}: {e.Message}");
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
                EditorUtility.DisplayDialog("Complete", $"Successfully set Filter Mode to {targetFilterMode} for {modified} texture(s).", "OK");
            }

            // Refresh the list
            FindTrilinearTextures();
        }
        catch (Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError($"FilterModeChecker error: {e}");
            EditorUtility.DisplayDialog("Error", $"An error occurred: {e.Message}", "OK");
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
    }
}
