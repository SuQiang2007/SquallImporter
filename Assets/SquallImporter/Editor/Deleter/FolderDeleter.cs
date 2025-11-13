using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class FolderDeleter : EditorWindow
{
	private DefaultAsset searchFolder;
	private readonly List<string> requiredFolderNames = new List<string>();
	private readonly List<string> matchedParentFolders = new List<string>();
	private readonly HashSet<string> selectedParents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
	private Vector2 resultsScroll;
	private Vector2 namesScroll;

	private const float ROW_HEIGHT = 22f;
	private const int PRELOAD_ROWS = 10;

	[MenuItem("Tools/SquallTools/Deleter/Folder Deleter")]
	public static void ShowWindow()
	{
		var window = GetWindow<FolderDeleter>();
		window.titleContent = new GUIContent("Folder Deleter");
		window.minSize = new Vector2(560, 380);
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
				SearchMatchingParents();
			}
		}

		EditorGUILayout.Space(6);
		EditorGUILayout.LabelField("Names (parent must contain all as direct subfolders)", EditorStyles.boldLabel);
		using (new EditorGUILayout.VerticalScope(GUI.skin.box))
		{
			namesScroll = EditorGUILayout.BeginScrollView(namesScroll, GUILayout.Height(120));
			for (int i = 0; i < requiredFolderNames.Count; i++)
			{
				using (new EditorGUILayout.HorizontalScope())
				{
					requiredFolderNames[i] = EditorGUILayout.TextField($"Name {i + 1}", requiredFolderNames[i] ?? string.Empty);
					if (GUILayout.Button("Remove", GUILayout.Width(70)))
					{
						requiredFolderNames.RemoveAt(i);
						i--;
						continue;
					}
				}
			}
			EditorGUILayout.EndScrollView();

			using (new EditorGUILayout.HorizontalScope())
			{
				GUILayout.FlexibleSpace();
				if (GUILayout.Button("Add Name", GUILayout.Width(120)))
				{
					requiredFolderNames.Add(string.Empty);
				}
			}
		}

		EditorGUILayout.Space(6);
		EditorGUILayout.LabelField("Matched Parent Folders", EditorStyles.boldLabel);
		using (new EditorGUILayout.VerticalScope(GUI.skin.box))
		{
			using (new EditorGUILayout.HorizontalScope())
			{
				EditorGUILayout.LabelField($"Count: {matchedParentFolders.Count}");
				GUILayout.FlexibleSpace();
				if (GUILayout.Button("Select All", GUILayout.Width(90)))
				{
					selectedParents.Clear();
					foreach (var p in matchedParentFolders) selectedParents.Add(p);
				}
				if (GUILayout.Button("Deselect All", GUILayout.Width(110)))
				{
					selectedParents.Clear();
				}
			}

			float listHeight = 240f;
			Rect scrollViewRect = GUILayoutUtility.GetRect(0, listHeight, GUILayout.ExpandWidth(true));
			scrollViewRect.height = listHeight;

			float totalContentHeight = Mathf.Max(matchedParentFolders.Count * ROW_HEIGHT, listHeight);
			Rect contentRect = new Rect(0, 0, scrollViewRect.width - 20f, totalContentHeight);
			resultsScroll = GUI.BeginScrollView(scrollViewRect, resultsScroll, contentRect);

			if (matchedParentFolders.Count == 0)
			{
				var emptyRect = new Rect(0, 0, contentRect.width, ROW_HEIGHT);
				GUI.Label(emptyRect, "No folders found. Adjust names and click Search.", EditorStyles.centeredGreyMiniLabel);
			}
			else
			{
				int startIndex = Mathf.Max(0, Mathf.FloorToInt(resultsScroll.y / ROW_HEIGHT) - PRELOAD_ROWS);
				int endIndex = Mathf.Min(matchedParentFolders.Count, Mathf.CeilToInt((resultsScroll.y + listHeight) / ROW_HEIGHT) + PRELOAD_ROWS);

				for (int i = startIndex; i < endIndex; i++)
				{
					var path = matchedParentFolders[i];
					float yPos = i * ROW_HEIGHT;
					Rect rowRect = new Rect(0, yPos, contentRect.width, ROW_HEIGHT);

					float checkboxWidth = 20f;
					float pingWidth = 60f;
					Rect checkRect = new Rect(rowRect.x + 2, rowRect.y + 2, checkboxWidth, ROW_HEIGHT - 4);
					Rect pingRect = new Rect(rowRect.xMax - pingWidth, rowRect.y, pingWidth, ROW_HEIGHT);
					Rect pathRect = new Rect(checkRect.xMax + 6, rowRect.y, rowRect.width - checkboxWidth - pingWidth - 12, ROW_HEIGHT);

					bool isSelected = selectedParents.Contains(path);
					bool newSelected = GUI.Toggle(checkRect, isSelected, "");
					if (newSelected != isSelected)
					{
						if (newSelected) selectedParents.Add(path);
						else selectedParents.Remove(path);
					}

					if (GUI.Button(pathRect, path, EditorStyles.linkLabel))
					{
						PingPath(path);
					}
					if (GUI.Button(pingRect, "Ping"))
					{
						PingPath(path);
					}
				}
			}

			GUI.EndScrollView();
		}

		EditorGUILayout.Space(6);
		using (new EditorGUI.DisabledScope(selectedParents.Count == 0))
		{
			if (GUILayout.Button($"Batch Delete Selected ({selectedParents.Count})", GUILayout.Height(28)))
			{
				BatchDeleteSelected();
			}
		}
	}

	private void SearchMatchingParents()
	{
		matchedParentFolders.Clear();
		selectedParents.Clear();

		string rootPath = searchFolder != null ? AssetDatabase.GetAssetPath(searchFolder) : "Assets";
		if (string.IsNullOrEmpty(rootPath) || !AssetDatabase.IsValidFolder(rootPath))
		{
			EditorUtility.DisplayDialog("Invalid Folder", "Please select a valid folder in the Project window.", "OK");
			return;
		}

		// Prepare required names
		var names = requiredFolderNames
			.Select(n => (n ?? string.Empty).Trim())
			.Where(n => !string.IsNullOrEmpty(n))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

		if (names.Count == 0)
		{
			EditorUtility.DisplayDialog("No Names", "Please add at least one folder name to search for.", "OK");
			return;
		}

		try
		{
			EditorUtility.DisplayProgressBar("Searching Folders", "Collecting subfolders...", 0f);

			// Traverse all folders under rootPath
			var stack = new Stack<string>();
			stack.Push(rootPath);

			var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			int processed = 0;

			while (stack.Count > 0)
			{
				var current = stack.Pop();
				if (!visited.Add(current)) continue;

				processed++;
				if (processed % 50 == 0)
				{
					EditorUtility.DisplayProgressBar("Searching Folders", $"Scanning: {current}", 0.1f + Mathf.Repeat(processed, 1000) / 1000f);
				}

				// Enqueue subfolders
				var subs = AssetDatabase.GetSubFolders(current);
				foreach (var s in subs)
				{
					stack.Push(s);
				}

				// Check if current contains all required direct subfolders
				bool hasAll = true;
				foreach (var name in names)
				{
					var expected = current.TrimEnd('/') + "/" + name;
					if (!AssetDatabase.IsValidFolder(expected))
					{
						hasAll = false;
						break;
					}
				}

				if (hasAll)
				{
					matchedParentFolders.Add(current.Replace("\\", "/"));
				}
			}

			matchedParentFolders.Sort(StringComparer.OrdinalIgnoreCase);

			EditorUtility.ClearProgressBar();
			EditorUtility.DisplayDialog("Search Complete", $"Found {matchedParentFolders.Count} parent folder(s).", "OK");
		}
		catch (Exception e)
		{
			EditorUtility.ClearProgressBar();
			Debug.LogError($"FolderDeleter search error: {e}");
			EditorUtility.DisplayDialog("Search Error", $"An error occurred during search:\n{e.Message}", "OK");
		}
		finally
		{
			EditorUtility.ClearProgressBar();
		}

		Repaint();
	}

	private void BatchDeleteSelected()
	{
		if (selectedParents.Count == 0)
		{
			EditorUtility.DisplayDialog("No Selection", "Please select folders to delete.", "OK");
			return;
		}

		// Prepare required names
		var names = requiredFolderNames
			.Select(n => (n ?? string.Empty).Trim())
			.Where(n => !string.IsNullOrEmpty(n))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

		if (names.Count == 0)
		{
			EditorUtility.DisplayDialog("No Names", "Please add at least one folder name to delete under selected parents.", "OK");
			return;
		}

		int targetCount = selectedParents.Count * names.Count;
		if (!EditorUtility.DisplayDialog("Confirm Deletion", $"This will delete up to {targetCount} subfolder(s) named [{string.Join(", ", names)}] under {selectedParents.Count} selected parent folder(s). Continue?", "Yes", "No"))
		{
			return;
		}

		var parentsToProcess = matchedParentFolders.Where(p => selectedParents.Contains(p)).ToList();
		int totalOps = Math.Max(1, parentsToProcess.Count * names.Count);
		int deleted = 0;
		int errors = 0;

		try
		{
			AssetDatabase.StartAssetEditing();

			int opIndex = 0;
			for (int i = 0; i < parentsToProcess.Count; i++)
			{
				var parentPath = parentsToProcess[i];
				foreach (var name in names)
				{
					opIndex++;
					var subPath = parentPath.TrimEnd('/') + "/" + name;
					EditorUtility.DisplayProgressBar("Deleting Subfolders", subPath, (float)opIndex / totalOps);

					try
					{
						// Ensure subPath is a folder
						if (!AssetDatabase.IsValidFolder(subPath))
						{
							continue;
						}

						if (AssetDatabase.DeleteAsset(subPath))
						{
							deleted++;
						}
						else
						{
							Debug.LogError($"FolderDeleter: Failed to delete {subPath}");
							errors++;
						}
					}
					catch (Exception e)
					{
						Debug.LogError($"FolderDeleter: Error deleting {subPath}: {e.Message}");
						errors++;
					}
				}
			}
		}
		catch (Exception e)
		{
			Debug.LogError($"FolderDeleter: Batch error: {e}");
			errors++;
		}
		finally
		{
			AssetDatabase.StopAssetEditing();
			AssetDatabase.Refresh();
			EditorUtility.ClearProgressBar();
		}

		EditorUtility.DisplayDialog("Delete Complete", $"Deleted subfolders: {deleted}\nErrors: {errors}", "OK");

		// Refresh list
		SearchMatchingParents();
	}

	private void PingPath(string path)
	{
		var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
		if (obj != null)
		{
			Selection.activeObject = obj;
			EditorGUIUtility.PingObject(obj);
		}
	}
}
