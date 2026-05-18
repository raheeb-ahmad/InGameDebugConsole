// ================================================================
//  InGame Debug Console — UI Redesign Utility  v1.2
//  Run via:  Tools / InGameDebugConsole / Redesign UI
//  Safe to re-run on an already-redesigned prefab.
// ================================================================
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;

namespace RaheebAhmad.DebugConsole.Editor
{
    public static class DebugConsoleRedesign
    {
        const string PREFAB      = "Assets/RaheebAhmad/InGameDebugConsole/Prefabs/InGameDebugConsole.prefab";
        const string SPRITE_PATH = "Assets/RaheebAhmad/InGameDebugConsole/Runtime/PillSprite.png";

        static Color H(string hex) { ColorUtility.TryParseHtmlString(hex, out Color c); return c; }

        // ── Entry point ───────────────────────────────────────────────────────

        [MenuItem("Tools/InGameDebugConsole/Redesign UI")]
        public static void Redesign()
        {
            BuildPillSprite();
            AssetDatabase.Refresh();

            // Re-import with correct sprite settings
            var imp = AssetImporter.GetAtPath(SPRITE_PATH) as TextureImporter;
            if (imp != null)
            {
                imp.textureType         = TextureImporterType.Sprite;
                imp.spriteImportMode    = SpriteImportMode.Single;
                imp.spriteBorder        = new Vector4(16, 16, 16, 16);
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled       = false;
                imp.filterMode          = FilterMode.Bilinear;
                imp.SaveAndReimport();
            }

            var spr    = AssetDatabase.LoadAssetAtPath<Sprite>(SPRITE_PATH);
            var prefab = PrefabUtility.LoadPrefabContents(PREFAB);
            try
            {
                ApplyAll(prefab, spr);
                PrefabUtility.SaveAsPrefabAsset(prefab, PREFAB);
                Debug.Log("[DebugConsoleRedesign] Done.");
            }
            catch (System.Exception e) { Debug.LogError("[DebugConsoleRedesign] " + e); }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            AssetDatabase.Refresh();
        }

        // ── Pill sprite (64×32, r=16, 9-sliced) ──────────────────────────────

        static void BuildPillSprite()
        {
            const int W = 64, H = 32, R = 16;
            var tex = new Texture2D(W, H, TextureFormat.ARGB32, false);
            var px  = new Color32[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float cx = x + 0.5f, cy = y + 0.5f;
                bool ok = true;
                if      (cx <   R && cy <   R) ok = Dist(cx, cy, R,   R)   <= R;
                else if (cx > W-R && cy <   R) ok = Dist(cx, cy, W-R, R)   <= R;
                else if (cx <   R && cy > H-R) ok = Dist(cx, cy, R,   H-R) <= R;
                else if (cx > W-R && cy > H-R) ok = Dist(cx, cy, W-R, H-R) <= R;
                px[y * W + x] = ok ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            }
            tex.SetPixels32(px);
            tex.Apply();
            string abs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", SPRITE_PATH));
            Directory.CreateDirectory(Path.GetDirectoryName(abs));
            File.WriteAllBytes(abs, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        static float Dist(float ax, float ay, float bx, float by)
        {
            float dx = ax - bx, dy = ay - by;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        // ── Main redesign ─────────────────────────────────────────────────────

        static void ApplyAll(GameObject root, Sprite spr)
        {
            // Safe find helper — returns null without throwing
            Transform TF(string p) => root.transform.Find(p);
            T F<T>(string p) where T : Component => TF(p)?.GetComponent<T>();

            // ── Palette ───────────────────────────────────────────────────────
            Color panelBg  = H("#0b0c10");
            Color headerBg = H("#0e1018");
            Color filterBg = H("#0d0f18");
            Color btnBg    = H("#131623");
            Color btnTxt   = H("#B0BAD0");
            Color mutedTxt = H("#6B7090");
            Color logBg    = H("#0e2218");
            Color warnBg   = H("#1e1800");
            Color errBg    = H("#1a0808");
            Color logTxt   = H("#3EDE67");
            Color warnTxt  = H("#F8C333");
            Color errTxt   = H("#FF6060");

            // ── Panel ─────────────────────────────────────────────────────────
            var panelImg = F<Image>("Panel");
            if (panelImg) panelImg.color = panelBg;

            // ── Header ────────────────────────────────────────────────────────
            var headerImg = F<Image>("Panel/Header");
            if (headerImg) headerImg.color = headerBg;

            // ── FilterBar ─────────────────────────────────────────────────────
            var fbTrans = TF("Panel/FilterBar");
            if (fbTrans == null) { Debug.LogError("FilterBar not found"); return; }

            var fbImg = fbTrans.GetComponent<Image>();
            if (fbImg) fbImg.color = filterBg;

            // Read the current FilterBar sizeDelta to set a consistent 40px height
            var fbRT = fbTrans.GetComponent<RectTransform>();
            // Keep x unchanged; set height via sizeDelta adjustment
            // FilterBar uses anchor-stretch (anchorMin.y=1, anchorMax.y=1) with fixed height
            // Just set the height directly
            fbRT.sizeDelta = new Vector2(fbRT.sizeDelta.x, 40f);

            // ── FilterBar HorizontalLayoutGroup ───────────────────────────────
            // KEY FIX: childForceExpandHeight = true so LeftGroup/RightGroup fill full bar height
            var fbHLG = fbTrans.GetComponent<HorizontalLayoutGroup>();
            if (fbHLG == null) fbHLG = fbTrans.gameObject.AddComponent<HorizontalLayoutGroup>();
            fbHLG.childForceExpandWidth  = false;
            fbHLG.childForceExpandHeight = true;   // ← CRITICAL: fills group height
            fbHLG.childControlWidth      = true;
            fbHLG.childControlHeight     = true;
            fbHLG.childAlignment         = TextAnchor.MiddleLeft;
            fbHLG.padding                = new RectOffset(8, 8, 4, 4);
            fbHLG.spacing                = 6f;

            // ── Ensure LeftGroup and RightGroup exist ─────────────────────────
            var leftTrans  = fbTrans.Find("LeftGroup");
            var rightTrans = fbTrans.Find("RightGroup");

            if (leftTrans == null)
            {
                var go = new GameObject("LeftGroup");
                go.transform.SetParent(fbTrans, false);
                leftTrans = go.transform;
            }
            if (rightTrans == null)
            {
                var go = new GameObject("RightGroup");
                go.transform.SetParent(fbTrans, false);
                rightTrans = go.transform;
            }

            // Ensure correct sibling order: LeftGroup first, RightGroup last
            leftTrans.SetSiblingIndex(0);
            rightTrans.SetSiblingIndex(fbTrans.childCount - 1);

            // ── LeftGroup setup ───────────────────────────────────────────────
            SetupGroup(leftTrans.gameObject,
                flexW: 1f, flexH: 0f,
                alignment: TextAnchor.MiddleLeft,
                spacing: 4f, forceExpandH: false);

            // ── RightGroup setup ──────────────────────────────────────────────
            SetupGroup(rightTrans.gameObject,
                flexW: 0f, flexH: 0f,
                alignment: TextAnchor.MiddleRight,
                spacing: 4f, forceExpandH: false);

            // ── Move filter buttons into LeftGroup (idempotent) ───────────────
            var filterDefs = new (string name, Color bg, Color txt, string label)[]
            {
                ("LogFilterBtn",  logBg,  logTxt,  "LOG"),
                ("WarnFilterBtn", warnBg, warnTxt, "WRN"),
                ("ErrFilterBtn",  errBg,  errTxt,  "ERR"),
            };

            for (int i = 0; i < filterDefs.Length; i++)
            {
                var (fname, fbg, ftxt, flabel) = filterDefs[i];
                // Find in old location (direct child of FilterBar) or new (LeftGroup)
                var btn = fbTrans.Find(fname) ?? leftTrans.Find(fname);
                if (btn == null) { Debug.LogWarning("FilterBtn not found: " + fname); continue; }

                if (btn.parent != leftTrans)
                    btn.SetParent(leftTrans, false);
                btn.SetSiblingIndex(i);

                StyleFilterPill(btn, fbg, ftxt, flabel, spr);
            }

            // ── Move ClearBtn to RightGroup (idempotent) ──────────────────────
            // Check old location (Header) and new location (RightGroup)
            var clearBtn = TF("Panel/Header/ClearBtn") ?? TF("Panel/FilterBar/RightGroup/ClearBtn");
            if (clearBtn != null)
            {
                if (clearBtn.parent != rightTrans)
                    clearBtn.SetParent(rightTrans, false);
                StyleActionBtn(clearBtn, "CLR", btnBg, btnTxt, spr, 46f);
            }

            // ── New action buttons (idempotent) ───────────────────────────────
            EnsureActionBtn(rightTrans, "PauseBtn",        "⏸ PAUSE", btnBg, btnTxt, spr, 70f);
            EnsureActionBtn(rightTrans, "ExportBtn",       "EXP",     btnBg, btnTxt, spr, 46f);
            EnsureActionBtn(rightTrans, "UnitySourceBtn",  "UNITY",   btnBg, btnTxt, spr, 50f);
            EnsureActionBtn(rightTrans, "LogcatSourceBtn", "LOGCAT",  btnBg, btnTxt, spr, 56f);

            // ── SearchBar ─────────────────────────────────────────────────────
            var sbRT = F<RectTransform>("Panel/SearchBar");
            if (sbRT != null) sbRT.sizeDelta = new Vector2(sbRT.sizeDelta.x, 34f);

            var sbImg = F<Image>("Panel/SearchBar");
            if (sbImg) sbImg.color = H("#0a0c14");

            var siImg = F<Image>("Panel/SearchBar/SearchInput");
            if (siImg) siImg.color = H("#0e1120");

            // ── StatusBar ─────────────────────────────────────────────────────
            var statImg = F<Image>("Panel/StatusBar");
            if (statImg) statImg.color = filterBg;

            var stTxt = F<Text>("Panel/StatusBar/StatusText");
            if (stTxt != null) { stTxt.color = mutedTxt; stTxt.fontSize = 11; }

            // ── ScrollView ────────────────────────────────────────────────────
            var svImg = F<Image>("Panel/ScrollView");
            if (svImg) svImg.color = panelBg;

            // ── Log text ──────────────────────────────────────────────────────
            var lt = F<Text>("Panel/ScrollView/Viewport/Content/LogText");
            if (lt != null) { lt.color = H("#C8D0E0"); lt.lineSpacing = 1.3f; lt.fontSize = 14; }

            // ── Scrollbar ─────────────────────────────────────────────────────
            var sbTrack = F<Image>("Panel/ScrollView/Scrollbar");
            if (sbTrack) sbTrack.color = H("#0d0f18");
            var sbHandle = F<Image>("Panel/ScrollView/Scrollbar/Handle");
            if (sbHandle) sbHandle.color = H("#2a2e42");

            // ── ErrorBadge on ToggleButton ────────────────────────────────────
            var toggleBtn = root.transform.Find("ToggleButton");
            if (toggleBtn != null && toggleBtn.Find("ErrorBadge") == null)
            {
                var badgeGO = new GameObject("ErrorBadge");
                badgeGO.transform.SetParent(toggleBtn, false);
                var bRT = badgeGO.AddComponent<RectTransform>();
                bRT.anchorMin       = new Vector2(1f, 1f);
                bRT.anchorMax       = new Vector2(1f, 1f);
                bRT.pivot           = new Vector2(1f, 1f);
                bRT.anchoredPosition = new Vector2(-2f, -2f);
                bRT.sizeDelta       = new Vector2(22f, 16f);
                badgeGO.AddComponent<CanvasRenderer>();
                var bTxt = badgeGO.AddComponent<Text>();
                bTxt.text      = "0";
                bTxt.color     = errTxt;
                bTxt.fontSize  = 10;
                bTxt.fontStyle = FontStyle.Bold;
                bTxt.alignment = TextAnchor.MiddleCenter;
                bTxt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                badgeGO.SetActive(false);
            }
        }

        // ── Layout group helper ───────────────────────────────────────────────

        static void SetupGroup(GameObject go, float flexW, float flexH,
            TextAnchor alignment, float spacing, bool forceExpandH)
        {
            // RectTransform — reset to zero-offset so parent HLG drives it
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
            rt.anchorMin        = Vector2.zero;
            rt.anchorMax        = Vector2.zero;
            rt.pivot            = new Vector2(0f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta        = Vector2.zero;

            // Inner HLG
            var hlg = go.GetComponent<HorizontalLayoutGroup>();
            if (hlg == null) hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = forceExpandH;
            hlg.childControlWidth      = true;
            hlg.childControlHeight     = true;
            hlg.childAlignment         = alignment;
            hlg.spacing                = spacing;
            hlg.padding                = new RectOffset(0, 0, 0, 0);

            // LayoutElement — flexible in width so outer HLG can size this group
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.flexibleWidth  = flexW;
            le.flexibleHeight = flexH;
            le.minHeight      = 0f;
        }

        // ── Filter pill styling ───────────────────────────────────────────────

        static void StyleFilterPill(Transform btn, Color bg, Color accentTxt, string label, Sprite spr)
        {
            var img = btn.GetComponent<Image>();
            if (img) { img.color = bg; ApplyPill(img, spr); }

            var le = btn.GetComponent<LayoutElement>();
            if (le == null) le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth  = 72f;
            le.preferredHeight = 30f;
            le.flexibleWidth   = 0f;
            le.flexibleHeight  = 0f;

            // White label — always readable on both the dark ON and OFF backgrounds
            var typeL = btn.Find("TypeLabel")?.GetComponent<Text>();
            if (typeL != null)
            { typeL.color = Color.white; typeL.fontSize = 11; typeL.fontStyle = FontStyle.Bold; typeL.text = label; }

            // Count uses the accent colour at full opacity so it pops against dark bg
            var countL = btn.Find("CountLabel")?.GetComponent<Text>();
            if (countL != null)
            { countL.color = accentTxt; countL.fontSize = 10; }
        }

        // ── Action button styling (for moved or pre-existing buttons) ─────────

        static void StyleActionBtn(Transform btn, string label, Color bg, Color txt, Sprite spr, float width)
        {
            var img = btn.GetComponent<Image>();
            if (img) { img.color = bg; ApplyPill(img, spr); }

            var le = btn.GetComponent<LayoutElement>();
            if (le == null) le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth  = width;
            le.preferredHeight = 28f;
            le.flexibleWidth   = 0f;
            le.flexibleHeight  = 0f;

            var t = btn.GetComponentInChildren<Text>();
            if (t != null) { t.text = label; t.color = txt; t.fontSize = 11; t.alignment = TextAnchor.MiddleCenter; }
        }

        // ── Create action button if missing ───────────────────────────────────

        static void EnsureActionBtn(Transform parent, string name, string label,
            Color bg, Color txt, Sprite spr, float width)
        {
            var existing = parent.Find(name);
            if (existing != null) { StyleActionBtn(existing, label, bg, txt, spr, width); return; }

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(width, 28f);

            go.AddComponent<CanvasRenderer>();
            var img = go.AddComponent<Image>();
            img.color = bg;
            ApplyPill(img, spr);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth  = width;
            le.preferredHeight = 28f;
            le.flexibleWidth   = 0f;
            le.flexibleHeight  = 0f;

            var lGO = new GameObject("L");
            lGO.transform.SetParent(go.transform, false);
            var lRT = lGO.AddComponent<RectTransform>();
            lRT.anchorMin = Vector2.zero; lRT.anchorMax = Vector2.one;
            lRT.sizeDelta = Vector2.zero; lRT.anchoredPosition = Vector2.zero;
            lGO.AddComponent<CanvasRenderer>();
            var lTxt = lGO.AddComponent<Text>();
            lTxt.text      = label;
            lTxt.color     = txt;
            lTxt.fontSize  = 11;
            lTxt.alignment = TextAnchor.MiddleCenter;
            lTxt.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        // ── Shared helpers ────────────────────────────────────────────────────

        static void ApplyPill(Image img, Sprite spr)
        {
            if (spr == null) return;
            img.sprite = spr;
            img.type   = Image.Type.Sliced;
        }
    }
}
