using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class ReadWriteChecker : EditorWindow
{
    private DefaultAsset searchFolder = null;
    private readonly List<string> readableTexturePaths = new List<string>();
    private Vector2 scroll;

    [MenuItem("Tools/SquallTools/Optimise/Texture Read/Write Checker")]
    public static void ShowWindow()
    {
        var window = GetWindow<ReadWriteChecker>();
        window.titleContent = new GUIContent("Texture R/W Checker");
        window.minSize = new Vector2(560, 360);
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
            using (new EditorGUI.DisabledScope(searchFolder == null))
            {
                if (GUILayout.Button("Search", GUILayout.Width(120)))
                {
                    FindReadableTextures();
                }
            }
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Readable Textures (Read/Write Enabled)", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
        {
            EditorGUILayout.LabelField($"Count: {readableTexturePaths.Count}");
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(220));
            foreach (var path in readableTexturePaths)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(path, EditorStyles.linkLabel))
                    {
                        SelectAndPing(path);
                    }
                    if (GUILayout.Button("Ping", GUILayout.Width(60)))
                    {
                        SelectAndPing(path);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        using (new EditorGUI.DisabledScope(readableTexturePaths.Count == 0))
        {
            if (GUILayout.Button("Batch Cancel Read/Write", GUILayout.Height(28)))
            {
                BatchUnsetReadable();
            }
        }
    }

    private void FindReadableTextures()
    {
        readableTexturePaths.Clear();

        var folderPath = searchFolder != null ? AssetDatabase.GetAssetPath(searchFolder) : null;
        if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
        {
            EditorUtility.DisplayDialog("Invalid Folder", "Please select a valid folder in the Project window.", "OK");
            return;
        }

        // Find all textures under folder; then filter by TextureImporter.isReadable
        var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath });
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && importer.isReadable)
            {
                readableTexturePaths.Add(path);
            }
        }

        if (readableTexturePaths.Count == 0)
        {
            EditorUtility.DisplayDialog("No Readable Textures", $"No textures with Read/Write enabled under:\n{folderPath}", "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("Found Readable Textures", $"Found {readableTexturePaths.Count} texture(s) with Read/Write enabled under:\n{folderPath}", "OK");
        }

        Repaint();
    }

    private void BatchUnsetReadable()
    {
        if (readableTexturePaths.Count == 0)
        {
            EditorUtility.DisplayDialog("No Targets", "Nothing to process. Click Search first.", "OK");
            return;
        }

        int modified = 0;
        var errors = new List<string>();
        try
        {
            AssetDatabase.StartAssetEditing();
            for (int i = 0; i < readableTexturePaths.Count; i++)
            {
                var path = readableTexturePaths[i];
                EditorUtility.DisplayProgressBar("Disabling Read/Write", path, (float)(i + 1) / readableTexturePaths.Count);

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    continue;
                }
                if (importer.isReadable)
                {
                    try
                    {
                        importer.isReadable = false;
                        importer.SaveAndReimport();
                        modified++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{path}: {ex.Message}");
                        Debug.LogError($"[ReadWriteChecker] Failed to unset Read/Write for {path}\n{ex}");
                    }
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.StopAssetEditing();
        }

        if (errors.Count > 0)
        {
            EditorUtility.DisplayDialog("Completed with Errors", $"Updated {modified} texture(s).\nErrors: {errors.Count}\nSee Console for details.", "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("Done", $"Updated {modified} texture(s).", "OK");
        }

        // Refresh results after changes
        FindReadableTextures();
    }

    private void SelectAndPing(string assetPath)
    {
        var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
        if (obj != null)
        {
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
        }
    }
}
