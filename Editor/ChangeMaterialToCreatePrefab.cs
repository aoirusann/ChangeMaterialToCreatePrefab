using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Aoirusann
{
	public class ChangeMaterialToCreatePrefab : EditorWindow
	{
		[System.Serializable]
		public class MaterialReplaceGroup
		{
			public Material dst;
			public List<Material> srcs;
		}
		[System.Serializable]
		public class ObjectChangeMatGroup
		{
			public GameObject objToChangeMat = null;
			public List<Material> originMats = new List<Material>();
			public bool gui_foldout = true;
		}

		static string appDisplayName = "ChangeMaterialToCreatePrefab";

		[SerializeField] private string rootpath = "Assets/";
		[SerializeField] private GameObject objToSaveAsPrefab = null;
		[SerializeField] private List<ObjectChangeMatGroup> objGroupsToChangeMat = new List<ObjectChangeMatGroup>();
		[SerializeField] private List<MaterialReplaceGroup> matGroups = new List<MaterialReplaceGroup>();

		private Vector2 scrollPos = new Vector2(0, 0);

		private void Log(string s)
		{
			Debug.Log($"<{appDisplayName}> {s}");
		}

		private static void DrawSeparator()
		{
			Rect rect = EditorGUILayout.GetControlRect(false, 1f);
			rect.height = 1f;
			EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.6f));
		}

		private DefaultAsset GetRootpathFolderAsset()
		{
			if (string.IsNullOrEmpty(rootpath))
				return null;
			return AssetDatabase.LoadAssetAtPath<DefaultAsset>(rootpath.TrimEnd('/'));
		}

		private string GetRootpathAbsolute()
		{
			string projectRoot = Directory.GetParent(Application.dataPath).FullName;
			string rel = rootpath.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
			return Path.GetFullPath(Path.Combine(projectRoot, rel));
		}

		// Sets rootpath from an absolute folder path chosen via the OS dialog.
		// Returns false (and keeps the current value) when the path is empty (dialog cancelled)
		// or lies outside the project's Assets folder.
		private bool TrySetRootpathFromAbsolute(string absolutePath)
		{
			if (string.IsNullOrEmpty(absolutePath))
				return false;

			string assetsAbs = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
			string chosenAbs = Path.GetFullPath(absolutePath).Replace('\\', '/').TrimEnd('/');

			if (!IsSameOrSubPath(assetsAbs, chosenAbs))
			{
				Log($"'{chosenAbs}' is outside the project's Assets folder. Save folder unchanged.");
				return false;
			}

			string relative = chosenAbs.Substring(assetsAbs.Length).Trim('/');
			SetRootpath(string.IsNullOrEmpty(relative) ? "Assets/" : $"Assets/{relative}/");
			return true;
		}

		private static bool IsSameOrSubPath(string baseAbs, string pathAbs)
		{
			if (string.Equals(baseAbs, pathAbs, System.StringComparison.OrdinalIgnoreCase))
				return true;
			return pathAbs.StartsWith(baseAbs + "/", System.StringComparison.OrdinalIgnoreCase);
		}

		private static void EnsureFolderExists(string folderPath)
		{
			folderPath = folderPath.Replace('\\', '/').TrimEnd('/');
			if (AssetDatabase.IsValidFolder(folderPath))
				return;

			string parent = Path.GetDirectoryName(folderPath).Replace('\\', '/');
			string leaf = Path.GetFileName(folderPath);
			EnsureFolderExists(parent);
			AssetDatabase.CreateFolder(parent, leaf);
		}

		private MaterialReplaceGroup FindOrCreateGroup(Material dst)
		{
			var group = matGroups.Find(g => g.dst == dst);
			if (group == null)
			{
				group = new MaterialReplaceGroup();
				group.dst = dst;
				group.srcs = new List<Material>();
				matGroups.Add(group);
			}
			return group;
		}

		private void SetRootpath(string newRoot)
		{
			if (newRoot == rootpath)
				return;
			Undo.RegisterCompleteObjectUndo(this, "Change save folder");
			rootpath = newRoot;
		}

		private bool HasGroupFor(GameObject go)
		{
			foreach (var g in objGroupsToChangeMat)
			{
				if (g.objToChangeMat == go)
					return true;
			}
			return false;
		}

		private void AddSelectedMaterialsTo(MaterialReplaceGroup group, Material[] selected)
		{
			if (group == null || selected == null || selected.Length == 0)
				return;

			var toAdd = new List<Material>();
			foreach (var m in selected)
			{
				if (m == null)
					continue;
				if (group.srcs.Contains(m) || toAdd.Contains(m))
					continue;
				toAdd.Add(m);
			}

			if (toAdd.Count == 0)
			{
				Log("All selected materials are already in this list.");
				return;
			}

			Undo.RegisterCompleteObjectUndo(this, "Add selected source materials");
			group.srcs.AddRange(toAdd);
		}

		private void AddSelectedObjects(GameObject[] selected)
		{
			var toAdd = new List<GameObject>();
			int skipped = 0;
			foreach (var go in selected)
			{
				if (go == null)
					continue;
				if (HasGroupFor(go) || toAdd.Contains(go))
				{
					skipped++;
					continue;
				}
				toAdd.Add(go);
			}

			if (toAdd.Count == 0)
			{
				Log(skipped > 0
					? $"All {skipped} selected object(s) already have a group."
					: "No scene object selected.");
				return;
			}

			Undo.RegisterCompleteObjectUndo(this, "Add selected object groups");
			foreach (var go in toAdd)
			{
				objGroupsToChangeMat.Add(new ObjectChangeMatGroup { objToChangeMat = go });
				if (go.GetComponent<Renderer>() == null)
					Log($"'{go.name}' has no MeshRenderer or SkinnedMeshRenderer; it was added but Start will skip it until it has one.");
			}
			if (skipped > 0)
				Log($"Added {toAdd.Count} group(s); skipped {skipped} already-present object(s).");
		}

		private void OnEnable()
		{
			Undo.undoRedoPerformed += Repaint;
		}

		private void OnDisable()
		{
			Undo.undoRedoPerformed -= Repaint;
		}

		[MenuItem("Tools/Change Material To Create Prefab")]
		public static void OpenWindow()
		{
			var window = GetWindow<ChangeMaterialToCreatePrefab>();
			window.titleContent = new GUIContent(appDisplayName);
			window.Show();
		}

		private void OnGUI()
		{
			var titleStyle = new GUIStyle(GUI.skin.label)
			{
				fontSize = 20,
			};
			GUILayout.Label(appDisplayName, titleStyle);
			GUILayout.Space(5);

			// GUI part START

			// Folder to save the generated prefabs
			{
				EditorGUILayout.BeginHorizontal();
				var folderLabel = new GUIContent("Save folder", "Folder where the generated prefabs will be stored. Must be inside the project's Assets folder.");
				EditorGUI.BeginChangeCheck();
				var folderAsset = (DefaultAsset)EditorGUILayout.ObjectField(folderLabel, GetRootpathFolderAsset(), typeof(DefaultAsset), false);
				if (EditorGUI.EndChangeCheck())
				{
					if (folderAsset != null)
					{
						string folderPath = AssetDatabase.GetAssetPath(folderAsset);
						if (AssetDatabase.IsValidFolder(folderPath))
							SetRootpath(folderPath.TrimEnd('/') + "/");
						else
							Log($"'{folderPath}' is not a folder. Save folder unchanged.");
					}
					else
					{
						Log("Save folder cleared. Keeping previous folder.");
					}
				}

				if (GUILayout.Button(new GUIContent("Browse...", "Pick the save folder with the system dialog."), GUILayout.Width(80)))
				{
					string chosen = EditorUtility.OpenFolderPanel("Select folder to save prefabs", GetRootpathAbsolute(), "");
					TrySetRootpathFromAbsolute(chosen);
				}
				EditorGUILayout.EndHorizontal();

				using (new EditorGUI.DisabledScope(true))
					EditorGUILayout.TextField(new GUIContent("Resolved path", "The final Assets-relative path where prefabs will be written."), rootpath);
			}

			// Select which object to save as prefab
			EditorGUI.BeginChangeCheck();
			var newObjToSave = (GameObject)EditorGUILayout.ObjectField(
				new GUIContent("Object to save as prefab", "The object that will be saved as prefab(s). Its material assignment is what gets varied."),
				objToSaveAsPrefab, typeof(GameObject), true);
			if (EditorGUI.EndChangeCheck())
			{
				Undo.RegisterCompleteObjectUndo(this, "Change Object to save as prefab");
				objToSaveAsPrefab = newObjToSave;
			}

			// Must select object for further
			if (!objToSaveAsPrefab)
				return;

			// Separator between the top settings and the target list
			EditorGUILayout.Space(5);
			DrawSeparator();
			EditorGUILayout.Space(5);

			// Show all object groups to be changed material
			scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
			var groupsToRemove = new List<ObjectChangeMatGroup>();
			var selectedMats = Selection.GetFiltered<Material>(SelectionMode.Assets);
			foreach (var objGroup in objGroupsToChangeMat)
			{
				var objToChangeMat = objGroup.objToChangeMat;
				string headerName = objToChangeMat ? objToChangeMat.name : "(no target object)";

				EditorGUILayout.Space(5);
				objGroup.gui_foldout = EditorGUILayout.BeginFoldoutHeaderGroup(
					objGroup.gui_foldout,
					new GUIContent(headerName, "Expand to edit this target's material replacement."));
				if (objGroup.gui_foldout)
				{
					// Row 1: the target object whose materials will be replaced
					EditorGUILayout.BeginHorizontal();
					EditorGUILayout.Space(5);
					EditorGUI.BeginChangeCheck();
					var newObjToChange = (GameObject)EditorGUILayout.ObjectField(
						new GUIContent("Target Object", "The object whose materials will be replaced."),
						objGroup.objToChangeMat, typeof(GameObject), true);
					bool objChanged = EditorGUI.EndChangeCheck();
					bool removeClicked = GUILayout.Button(new GUIContent("Remove", "Remove this target from the list."), GUILayout.Width(64));
					EditorGUILayout.EndHorizontal();

					if (removeClicked || (objChanged && newObjToChange == null))
					{
						// Removed explicitly, or the object was cleared -> drop this group
						groupsToRemove.Add(objGroup);
					}
					else if (objChanged)
					{
						Undo.RegisterCompleteObjectUndo(this, "Change Object to change material");
						objGroup.objToChangeMat = newObjToChange;
						objToChangeMat = newObjToChange;
					}

					// Row 2+: the per-material replacement lists
					if (objToChangeMat)
					{
						var renderer = objToChangeMat.GetComponent<Renderer>();
						if (!renderer)
						{
							EditorGUILayout.HelpBox("The target has no MeshRenderer or SkinnedMeshRenderer component.", MessageType.Warning);
						}
						else
						{
							EditorGUILayout.BeginHorizontal();
							EditorGUILayout.Space(5);
							EditorGUILayout.BeginVertical();
							foreach (Material mat in renderer.sharedMaterials)
							{
								// Skip empty material slots
								if (mat == null)
									continue;

								// Find (or create) the group that holds the source materials for the dst
								var group = FindOrCreateGroup(mat);

								// The original material on the target, plus a button to add all selected materials
								EditorGUILayout.BeginHorizontal();
								EditorGUILayout.LabelField(new GUIContent(mat.name, "The original material on this target. The variants below replace it."));
								using (new EditorGUI.DisabledScope(selectedMats.Length == 0))
								{
									if (GUILayout.Button(new GUIContent($"Add selected ({selectedMats.Length})", "Append every material currently selected in the Project to this list."), GUILayout.Width(150)))
										AddSelectedMaterialsTo(group, selectedMats);
								}
								EditorGUILayout.EndHorizontal();

								var srcMats = group.srcs;

								// Clear null source materials
								srcMats.RemoveAll(m => m == null);

								// List all the source materials
								int moveFrom = -1;
								int moveTo = -1;
								for (int i=0; i<srcMats.Count; i++)
								{
									EditorGUILayout.BeginHorizontal();
									EditorGUI.BeginChangeCheck();
									var newSrc = (Material)EditorGUILayout.ObjectField(
										new GUIContent($"{i}", $"Variant {i}: the material that replaces '{mat.name}'."),
										srcMats[i], typeof(Material), true);
									if (EditorGUI.EndChangeCheck())
									{
										Undo.RegisterCompleteObjectUndo(this, "Change source material");
										srcMats[i] = newSrc;
									}
									using (new EditorGUI.DisabledScope(i == 0))
									{
										if (GUILayout.Button(new GUIContent("▲", "Move this variant up."), GUILayout.Width(24)))
										{
											moveFrom = i;
											moveTo = i - 1;
										}
									}
									using (new EditorGUI.DisabledScope(i == srcMats.Count - 1))
									{
										if (GUILayout.Button(new GUIContent("▼", "Move this variant down."), GUILayout.Width(24)))
										{
											moveFrom = i;
											moveTo = i + 1;
										}
									}
									EditorGUILayout.EndHorizontal();
								}

								// Apply a requested move (only one per frame)
								if (moveFrom >= 0 && moveTo >= 0 && moveTo < srcMats.Count)
								{
									Undo.RegisterCompleteObjectUndo(this, "Reorder source materials");
									var moved = srcMats[moveFrom];
									srcMats.RemoveAt(moveFrom);
									srcMats.Insert(moveTo, moved);
								}
								// And allow the user to add more
								var newMatToReplace = (Material)EditorGUILayout.ObjectField(
									new GUIContent($"{srcMats.Count}", "Add one source material variant to this list."),
									null, typeof(Material), true);
								if (newMatToReplace != null)
								{
									Undo.RegisterCompleteObjectUndo(this, "Add source material");
									srcMats.Add(newMatToReplace);
								}
							}
							EditorGUILayout.EndVertical();
							EditorGUILayout.EndHorizontal();
						}
					}
				}
				EditorGUILayout.EndFoldoutHeaderGroup();
			}

			// Remove the groups whose object the user cleared or removed
			if (groupsToRemove.Count > 0)
			{
				Undo.RegisterCompleteObjectUndo(this, "Remove object group");
				foreach (var g in groupsToRemove)
					objGroupsToChangeMat.Remove(g);
			}

			// Separator between the target list and the add controls
			EditorGUILayout.Space(5);
			DrawSeparator();
			EditorGUILayout.Space(5);

			// Always-present empty slot: drag a single object here to add a group
			var objToAdd = (GameObject)EditorGUILayout.ObjectField(
				new GUIContent("Add object (drag here)", "Drag a single scene object here to add it as a new target."),
				null, typeof(GameObject), true);
			if (objToAdd != null)
			{
				Undo.RegisterCompleteObjectUndo(this, "Add object group");
				objGroupsToChangeMat.Add(new ObjectChangeMatGroup { objToChangeMat = objToAdd });
			}

			// Add one group per selected scene object
			var selectedObjects = Selection.gameObjects.Where(go => go != null && go.scene.IsValid()).ToArray();
			using (new EditorGUI.DisabledScope(selectedObjects.Length == 0))
			{
				if (GUILayout.Button(new GUIContent($"Add selected objects ({selectedObjects.Length})", "Add every scene object currently selected in the Hierarchy as a separate target.")))
				{
					AddSelectedObjects(selectedObjects);
				}
			}
			EditorGUILayout.EndScrollView();

			if(objGroupsToChangeMat.Count == 0)
				return;

			EditorGUILayout.Space(10);
			if(GUILayout.Button(new GUIContent("Start", "For every variant index: apply it to all targets and save one prefab.")))
			{
				Undo.IncrementCurrentGroup();
				int undoGroup = Undo.GetCurrentGroup();
				Undo.SetCurrentGroupName("Change Material To Create Prefab");
				Debug.Log($"<{appDisplayName}> Start...");
				Work();
				Debug.Log($"<{appDisplayName}> Finish.");
				Undo.CollapseUndoOperations(undoGroup);
			}

			if(GUILayout.Button(new GUIContent("Clear material cache", "Forget every cached per-material variant list.")))
			{
				Undo.RegisterCompleteObjectUndo(this, "Clear material cache");
				matGroups.Clear();
				Debug.Log("All material cache are cleared.");
				return;
			}

			// GUI part END
		}

		private void Work()
		{
			if(objGroupsToChangeMat.Count == 0)
				return;

			// Validate the save folder before doing anything
			if (string.IsNullOrEmpty(rootpath) || !AssetDatabase.IsValidFolder(rootpath.TrimEnd('/')))
			{
				Log("Invalid save folder. Please select a folder inside the project's Assets folder.");
				return;
			}

			// Save all objects' origin materials
			foreach(var objGroup in objGroupsToChangeMat)
			{
				var objToChangeMat = objGroup.objToChangeMat;
				if(!objToChangeMat)
					continue;
				var renderer = objToChangeMat.GetComponent<Renderer>();
				if(!renderer)
					continue;

				// Save origin materials
				objGroup.originMats.Clear();
				objGroup.originMats.AddRange(renderer.sharedMaterials);
			}

			// Check the number of source materials
			// The number of source materials for every destination material should be the same
			var firstObj = objGroupsToChangeMat[0].objToChangeMat;
			var i = 0;
			while (firstObj == null)
			{
				if(i >= objGroupsToChangeMat.Count)
					return;
				firstObj = objGroupsToChangeMat[i].objToChangeMat;
				i++;
			}
			if(firstObj == null)
				return;
			var firstRenderer = firstObj.GetComponent<Renderer>();
			if(firstRenderer == null)
				return;
			var firstMat = firstRenderer.sharedMaterials[0];
			int srcMatNum = FindOrCreateGroup(firstMat).srcs.Count;
			foreach (var objGroup in objGroupsToChangeMat)
			{
				var obj = objGroup.objToChangeMat;
				if(!obj)
					continue;
				var renderer = obj.GetComponent<Renderer>();
				if(!renderer)
					continue;
				foreach (var mat in renderer.sharedMaterials)
				{
					var current_srcMatNum = FindOrCreateGroup(mat).srcs.Count;
					if (srcMatNum != current_srcMatNum)
					{
						Debug.LogWarning("The number of source materials for each destination material must be the same.");
						Debug.LogWarning($"{firstMat.name} has {srcMatNum} source materials, while {mat.name} has {current_srcMatNum} source materials.");
						return;
					}
				}
			}

			// Replace the materials & Save as prefabs
			for (int srcMatInd=0; srcMatInd<srcMatNum; srcMatInd++)
			{
				// Replace the materials on each GameObject
				foreach (var objGroup in objGroupsToChangeMat)
				{
					var obj = objGroup.objToChangeMat;
					if(!obj)
						continue;
					var renderer = obj.GetComponent<Renderer>();
					if(!renderer)
						continue;

					// Replace the materials on the GameObject
					var newMats = new List<Material>();
					for (int matInd=0; matInd<renderer.sharedMaterials.Count(); matInd++)
					{
						var oriMat = objGroup.originMats[matInd];
						var srcMat = FindOrCreateGroup(oriMat).srcs[srcMatInd];
						newMats.Add(srcMat);
					}
					renderer.sharedMaterials = newMats.ToArray();
				}

				// Build the prefab asset path
				string filename = $"{objToSaveAsPrefab.name}_{srcMatInd}.prefab";
				string path = rootpath + filename;

				// Make sure the folder exists (AssetDatabase.CreateFolder so Unity knows about it)
				EnsureFolderExists(rootpath.TrimEnd('/'));

				// Save the GameObject as a Prefab asset
				var createdPrefab = PrefabUtility.SaveAsPrefabAsset(objToSaveAsPrefab, path);
				if (createdPrefab != null)
					Undo.RegisterCreatedObjectUndo(createdPrefab, "Create prefab");
			}

			// Recover the materials
			foreach(var objGroup in objGroupsToChangeMat)
			{
				var objToChangeMat = objGroup.objToChangeMat;
				if(!objToChangeMat)
					continue;
				var renderer = objToChangeMat.GetComponent<Renderer>();
				if(!renderer)
					continue;

				var newMats = new List<Material>();
				for (int matInd=0; matInd<renderer.sharedMaterials.Count(); matInd++)
				{
					var oriMat = objGroup.originMats[matInd];
					newMats.Add(oriMat);
				}
				renderer.sharedMaterials = newMats.ToArray();
			}
		}
	}
}
