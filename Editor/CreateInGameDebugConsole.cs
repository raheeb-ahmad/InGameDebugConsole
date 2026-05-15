// ================================================================
//  InGame Debug Console  v1.0.0
//  Editor utility — adds the console prefab to the active scene
//  ----------------------------------------------------------------
//  Author  : Raheeb Ahmad
//  License : MIT
//  © 2025 Raheeb Ahmad. All rights reserved.
// ================================================================

using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace RaheebAhmad.DebugConsole.Editor
{
    public static class CreateInGameDebugConsole
    {
        const string PrefabGuid    = "";   // filled by AssetDatabase search at runtime
        const string PrefabSearch  = "InGameDebugConsole t:Prefab";
        static readonly string[] SearchFolders = {
            "Assets/RaheebAhmad/InGameDebugConsole/Prefabs",
            "Packages/com.raheeb-ahmad.ingame-debug-console/Prefabs"
        };

        [MenuItem("Tools/Create InGame Debug Console")]
        public static void Create()
        {
            // Remove any existing instance
            var old = GameObject.Find("InGameDebugConsole");
            if (old != null) Undo.DestroyObjectImmediate(old);

            // Locate prefab inside the package (works whether installed via UPM or as local Assets)
            var guids = AssetDatabase.FindAssets(PrefabSearch, SearchFolders);
            if (guids.Length == 0)
            {
                Debug.LogError("[InGameDebugConsole] Prefab not found. " +
                    "Make sure the package is installed correctly.");
                return;
            }

            var path   = AssetDatabase.GUIDToAssetPath(guids[0]);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(instance, "Create InGame Debug Console");

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Selection.activeGameObject = instance;

            Debug.Log("[InGameDebugConsole] Added to scene — by Raheeb Ahmad v1.0.0");
        }
    }
}
