// ================================================================
//  InGame Debug Console  v1.1.0
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
        const string PrefabSearch  = "InGameDebugConsole t:Prefab";
        static readonly string[] SearchFolders = {
            "Assets/RaheebAhmad/InGameDebugConsole/Prefabs",
            "Packages/com.raheeb-ahmad.ingame-debug-console/Prefabs"
        };

        [MenuItem("Tools/InGameDebugConsole/Add to Scene")]
        public static void Create()
        {
            var old = GameObject.Find("InGameDebugConsole");
            if (old != null) Undo.DestroyObjectImmediate(old);

            var guids = AssetDatabase.FindAssets(PrefabSearch, SearchFolders);
            if (guids.Length == 0)
            {
                Debug.LogError("[InGameDebugConsole] Prefab not found. " +
                    "Make sure the package is installed correctly.");
                return;
            }

            var path     = AssetDatabase.GUIDToAssetPath(guids[0]);
            var prefab   = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(instance, "Create InGame Debug Console");

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Selection.activeGameObject = instance;

            Debug.Log("[InGameDebugConsole] Added to scene — by Raheeb Ahmad v1.1.0");
        }

        [MenuItem("Tools/InGameDebugConsole/Toggle Release Strip")]
        public static void ToggleReleaseStrip()
        {
            var group   = EditorUserBuildSettings.selectedBuildTargetGroup;
            string defs = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);

            const string symbol = "RAHEEB_RELEASE";
            var list = new System.Collections.Generic.List<string>(
                defs.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries));

            if (list.Contains(symbol))
            {
                list.Remove(symbol);
                PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.Join(";", list));
                Debug.Log("[InGameDebugConsole] Debug console ENABLED (RAHEEB_RELEASE removed)");
            }
            else
            {
                list.Add(symbol);
                PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.Join(";", list));
                Debug.Log("[InGameDebugConsole] Debug console STRIPPED from release (RAHEEB_RELEASE added)");
            }
        }
    }
}
