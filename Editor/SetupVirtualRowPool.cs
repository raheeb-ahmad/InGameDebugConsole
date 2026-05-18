// ================================================================
//  InGame Debug Console — Virtual Row Pool Setup
//  Editor utility: creates LogRowPrefab and patches the main prefab
//  ----------------------------------------------------------------
//  Author  : Raheeb Ahmad
//  License : MIT
//  © 2025 Raheeb Ahmad. All rights reserved.
// ================================================================

using UnityEngine;
using UnityEditor;
using UnityEngine.UI;

namespace RaheebAhmad.DebugConsole.Editor
{
    public static class SetupVirtualRowPool
    {
        const string MainPrefabPath = "Assets/RaheebAhmad/InGameDebugConsole/Prefabs/InGameDebugConsole.prefab";
        const string RowPrefabPath  = "Assets/RaheebAhmad/InGameDebugConsole/Prefabs/LogRowPrefab.prefab";

        [MenuItem("Tools/InGameDebugConsole/Setup Virtual Row Pool")]
        public static void Run()
        {
            CreateRowPrefab();
            AssetDatabase.Refresh();            // import LogRowPrefab before loading main prefab
            SetupMainPrefab();
            AssetDatabase.SaveAssets();
            Debug.Log("[InGameDebugConsole] Virtual row pool setup complete.");
        }

        // ── Step 1: Create the row prefab ────────────────────────────────────

        static void CreateRowPrefab()
        {
            var go = new GameObject("LogRowPrefab", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();

            // Stretch horizontally, anchor to top of parent (Content)
            rt.anchorMin        = new Vector2(0f, 1f);
            rt.anchorMax        = new Vector2(1f, 1f);
            rt.pivot            = new Vector2(0f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta        = new Vector2(0f, 22f);

            // Text child — fills the row with small horizontal padding
            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var textRt         = textGo.GetComponent<RectTransform>();
            textRt.anchorMin   = Vector2.zero;
            textRt.anchorMax   = Vector2.one;
            textRt.pivot       = new Vector2(0.5f, 0.5f);
            textRt.offsetMin   = new Vector2(4f, 0f);
            textRt.offsetMax   = new Vector2(-4f, 0f);

            var txt                    = textGo.AddComponent<Text>();
            txt.font                   = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize               = 11;
            txt.color                  = Color.white;
            txt.alignment              = TextAnchor.MiddleLeft;
            txt.supportRichText        = true;
            txt.horizontalOverflow     = HorizontalWrapMode.Overflow;
            txt.verticalOverflow       = VerticalWrapMode.Overflow;

            PrefabUtility.SaveAsPrefabAsset(go, RowPrefabPath);
            Object.DestroyImmediate(go);
            Debug.Log("[InGameDebugConsole] LogRowPrefab saved → " + RowPrefabPath);
        }

        // ── Step 2: Patch the main prefab ────────────────────────────────────

        static void SetupMainPrefab()
        {
            var rowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RowPrefabPath);
            if (rowPrefab == null)
            {
                Debug.LogError("[InGameDebugConsole] LogRowPrefab not found at " + RowPrefabPath);
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(MainPrefabPath);
            try
            {
                // Find Content (the ScrollRect's content object)
                var contentT = FindDeep(root.transform, "Content");
                if (contentT == null) { Debug.LogError("[InGameDebugConsole] Content object not found"); return; }

                // Remove layout components that would fight manual sizeDelta / anchoredPosition
                var vlg = contentT.GetComponent<VerticalLayoutGroup>();
                if (vlg != null) Object.DestroyImmediate(vlg);

                var csf = contentT.GetComponent<ContentSizeFitter>();
                if (csf != null) Object.DestroyImmediate(csf);

                // Content RT: top-anchored, full-width stretch, pivot top-left
                var contentRt           = contentT.GetComponent<RectTransform>();
                contentRt.anchorMin     = new Vector2(0f, 1f);
                contentRt.anchorMax     = new Vector2(1f, 1f);
                contentRt.pivot         = new Vector2(0f, 1f);
                contentRt.anchoredPosition = Vector2.zero;
                contentRt.sizeDelta     = Vector2.zero;

                // Disable the old LogText — preserve the object so prefab GUIDs stay intact
                var logTextT = FindDeep(contentT, "LogText");
                if (logTextT != null) logTextT.gameObject.SetActive(false);

                // Wire serialized fields on InGameDebugConsole component
                var console = root.GetComponent<RaheebAhmad.DebugConsole.InGameDebugConsole>();
                if (console == null) { Debug.LogError("[InGameDebugConsole] Component not found on root"); return; }

                var so = new SerializedObject(console);
                so.Update();

                var contentProp = so.FindProperty("_contentRect");
                if (contentProp != null)
                    contentProp.objectReferenceValue = contentRt;
                else
                    Debug.LogWarning("[InGameDebugConsole] _contentRect property not found — recompile first?");

                var rowPrefabProp = so.FindProperty("_rowPrefab");
                if (rowPrefabProp != null)
                    rowPrefabProp.objectReferenceValue = rowPrefab;
                else
                    Debug.LogWarning("[InGameDebugConsole] _rowPrefab property not found — recompile first?");

                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, MainPrefabPath);
                Debug.Log("[InGameDebugConsole] Main prefab patched: Content fixed, _contentRect + _rowPrefab wired.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var result = FindDeep(t.GetChild(i), name);
                if (result != null) return result;
            }
            return null;
        }
    }
}
