using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class FbxCompressor : EditorWindow
{
    private DefaultAsset searchFolder = null;
    private readonly List<string> foundFbxAssetPaths = new List<string>();
    private Vector2 scrollPosition;

    private bool readWriteEnabled = false;
    private ModelImporterMeshCompression meshCompression = ModelImporterMeshCompression.Medium;

    [MenuItem("Tools/SquallTools/Optimise/FBX Compressor")]
    public static void ShowWindow()
    {
        var window = GetWindow<FbxCompressor>();
        window.titleContent = new GUIContent("Fbx Compressor");
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
            using (new EditorGUI.DisabledScope(searchFolder == null))
            {
                if (GUILayout.Button("Find", GUILayout.Width(120)))
                {
                    FindFbxAssets();
                }
            }
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Found FBX Assets", EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope(GUI.skin.box))
        {
            EditorGUILayout.LabelField($"Count: {foundFbxAssetPaths.Count}");
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(180));
            foreach (var path in foundFbxAssetPaths)
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

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
        readWriteEnabled = EditorGUILayout.Toggle("Read/Write Enabled", readWriteEnabled);
        meshCompression = (ModelImporterMeshCompression)EditorGUILayout.EnumPopup("Mesh Compression", meshCompression);

        EditorGUILayout.Space(4);
        using (new EditorGUI.DisabledScope(foundFbxAssetPaths.Count == 0))
        {
            if (GUILayout.Button("Compress / Apply", GUILayout.Height(28)))
            {
                ApplySettingsToFound();
            }
        }
    }

    private void FindFbxAssets()
    {
        foundFbxAssetPaths.Clear();

        var folderPath = searchFolder != null ? AssetDatabase.GetAssetPath(searchFolder) : null;
        if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
        {
            EditorUtility.DisplayDialog("Invalid Folder", "Please select a valid folder in the Project window.", "OK");
            return;
        }

        // Search for model assets (includes .fbx). Using t:Model is robust across importers.
        var guids = AssetDatabase.FindAssets("t:Model", new[] { folderPath });
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            {
                foundFbxAssetPaths.Add(path);
            }
        }

        // Fallback: if none via t:Model (rare), enumerate by extension
        if (foundFbxAssetPaths.Count == 0)
        {
            var possibleGuids = AssetDatabase.FindAssets(string.Empty, new[] { folderPath });
            foreach (var guid in possibleGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                {
                    foundFbxAssetPaths.Add(path);
                }
            }
        }

        if (foundFbxAssetPaths.Count == 0)
        {
            EditorUtility.DisplayDialog("No FBX Found", $"No .fbx files found under folder:\n{folderPath}", "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("FBX Found", $"Found {foundFbxAssetPaths.Count} .fbx file(s) under:\n{folderPath}", "OK");
        }

        Repaint();
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

    private void ApplySettingsToFound()
    {
        if (foundFbxAssetPaths.Count == 0)
        {
            EditorUtility.DisplayDialog("No Assets", "No FBX assets found. Click Find first.", "OK");
            return;
        }

        try
        {
            AssetDatabase.StartAssetEditing();
            int modified = 0;
            foreach (var path in foundFbxAssetPaths)
            {
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null)
                {
                    continue;
                }

                bool changed = false;
                if (importer.isReadable != readWriteEnabled)
                {
                    importer.isReadable = readWriteEnabled;
                    changed = true;
                }
                if (importer.meshCompression != meshCompression)
                {
                    importer.meshCompression = meshCompression;
                    changed = true;
                }

                if (changed)
                {
                    importer.SaveAndReimport();
                    modified++;
                }
            }

            EditorUtility.DisplayDialog("Done", $"Applied settings to {modified} FBX asset(s).", "OK");
        }
        catch (Exception e)
        {
            Debug.LogError($"FbxCompressor error: {e}");
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
    }
}
