// ================================================================
//  InGame Debug Console  v1.1.0
//  Runtime logcat and Unity log overlay for Android & Editor
//  ----------------------------------------------------------------
//  Author  : Raheeb Ahmad
//  License : MIT
//  © 2025 Raheeb Ahmad. All rights reserved.
// ================================================================

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace RaheebAhmad.DebugConsole
{
#if !RAHEEB_RELEASE
    public class InGameDebugConsole : MonoBehaviour
    {
        [Header("UI References")]
        public GameObject panel;
        public Text       logText;
        public ScrollRect scrollRect;
        public Text       toggleLabel;
        public InputField searchInput;
        public Text       statusText;

        [Header("Settings")]
        public int  maxEntries      = 150;
        public bool autoShowOnError = true;

        // ── State ────────────────────────────────────────────────────────────

        private bool   _showLog    = true;
        private bool   _showWarn   = true;
        private bool   _showErr    = true;
        private bool   _showUnity  = true;
        private bool   _showLogcat = true;
        private bool   _paused;
        private string _searchTerm = "";

        private Image _logBtnImg, _warnBtnImg, _errBtnImg;
        private Image _pauseBtnImg, _unityBtnImg, _logcatBtnImg;
        private Text  _logCountText, _warnCountText, _errCountText;
        private Text  _pauseBtnLabel;
        private Text  _errorBadgeText;

        // Pill ON = dark tinted bg matching the pill colour; OFF = neutral dark
        private static readonly Color ColLogOn  = new Color(0.055f, 0.133f, 0.094f, 1f); // #0e2218
        private static readonly Color ColWarnOn = new Color(0.118f, 0.094f, 0.000f, 1f); // #1e1800
        private static readonly Color ColErrOn  = new Color(0.102f, 0.031f, 0.031f, 1f); // #1a0808
        private static readonly Color ColOff      = new Color(0.074f, 0.086f, 0.137f, 1f); // #131623
        private static readonly Color ColSourceOn = new Color(0.055f, 0.102f, 0.188f, 1f); // #0e1a30 active source
        private static readonly Color ColPausedOn = new Color(0.118f, 0.075f, 0.000f, 1f); // #1e1300 paused

        private readonly List<LogEntry> _entries = new List<LogEntry>();
        private bool             _panelOpen;
        private volatile bool    _dirty;
        private volatile bool    _pendingOpen;
        private volatile bool    _badgeDirty;
        private bool             _autoScroll    = true;
        private bool             _ignoreScroll;
        private bool             _scrollPending;
        private float            _rebuildCooldown;
        private int              _errorsSinceClose;

        private const int MaxDisplayLines = 200;

        private static InGameDebugConsole _instance;

        private enum Source { Unity, Logcat }

        private struct LogEntry
        {
            public string  richLine;
            public string  plainText;
            public LogType type;
            public Source  source;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private System.Threading.Thread _logcatThread;
        private volatile bool           _logcatRunning;
        private AndroidJavaObject       _logcatProcess;
#endif

        // ── Lifecycle ────────────────────────────────────────────────────────

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            StartLogcat();
#else
            Application.logMessageReceivedThreaded += HandleUnityLog;
#endif
        }

        private void OnDisable()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            StopLogcat();
#else
            Application.logMessageReceivedThreaded -= HandleUnityLog;
#endif
        }

        private void Start()
        {
            if (panel != null) panel.SetActive(false);

            WireButton    ("ToggleButton",    TogglePanel);
            WireButtonDeep("CloseBtn",        TogglePanel);
            WireButtonDeep("ClearBtn",        ClearLogs);
            WireButtonDeep("LogFilterBtn",    ToggleLogFilter);
            WireButtonDeep("WarnFilterBtn",   ToggleWarnFilter);
            WireButtonDeep("ErrFilterBtn",    ToggleErrFilter);
            WireButtonDeep("ClearSearchBtn",  ClearSearch);
            WireButtonDeep("PauseBtn",        TogglePause);
            WireButtonDeep("ExportBtn",       ExportLogs);
            WireButtonDeep("UnitySourceBtn",  ToggleUnitySource);
            WireButtonDeep("LogcatSourceBtn", ToggleLogcatSource);

            _logBtnImg  = FindChildImage("LogFilterBtn");
            _warnBtnImg = FindChildImage("WarnFilterBtn");
            _errBtnImg  = FindChildImage("ErrFilterBtn");

            _logCountText  = FindCountLabel("LogFilterBtn");
            _warnCountText = FindCountLabel("WarnFilterBtn");
            _errCountText  = FindCountLabel("ErrFilterBtn");

            _pauseBtnLabel  = FindButtonLabel("PauseBtn");
            _errorBadgeText = FindButtonChildText("ToggleButton", "ErrorBadge");

            _pauseBtnImg  = FindChildImage("PauseBtn");
            _unityBtnImg  = FindChildImage("UnitySourceBtn");
            _logcatBtnImg = FindChildImage("LogcatSourceBtn");

            RefreshFilterColors();
            RefreshPauseLabel();
            RefreshRightGroupColors();
            RefreshErrorBadge();

            if (searchInput != null)
                searchInput.onValueChanged.AddListener(v =>
                {
                    _searchTerm      = v;
                    _dirty           = true;
                    _rebuildCooldown = 0f;
                });

            if (scrollRect != null)
                scrollRect.onValueChanged.AddListener(OnScrollMoved);
        }

        private void Update()
        {
            if (_pendingOpen) { _pendingOpen = false; OpenPanel(true); }
            if (_badgeDirty)  { _badgeDirty  = false; RefreshErrorBadge(); }

            _rebuildCooldown -= Time.deltaTime;
            if (_dirty && _panelOpen && _rebuildCooldown <= 0f)
            {
                _rebuildCooldown = 0.25f;
                _dirty           = false;
                Rebuild();
            }
        }

        private void LateUpdate()
        {
            if (!_scrollPending || scrollRect == null) return;
            _scrollPending = false;
            _ignoreScroll  = true;
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
            _ignoreScroll  = false;
        }

        // ── Android logcat ───────────────────────────────────────────────────

#if UNITY_ANDROID && !UNITY_EDITOR
        private void StartLogcat()
        {
            _logcatRunning = true;
            _logcatThread  = new System.Threading.Thread(LogcatReaderLoop) { IsBackground = true };
            _logcatThread.Start();
        }

        private void StopLogcat()
        {
            _logcatRunning = false;
            try { if (_logcatProcess != null) _logcatProcess.Call("destroy"); } catch { }
        }

        private void LogcatReaderLoop()
        {
            AndroidJNI.AttachCurrentThread();
            AndroidJavaObject bufferedReader = null;
            bool reconnect = false;
            try
            {
                AndroidJavaObject runtime;
                using (var rtClass = new AndroidJavaClass("java.lang.Runtime"))
                    runtime = rtClass.CallStatic<AndroidJavaObject>("getRuntime");

                if (runtime == null)
                    throw new System.Exception("Runtime.getRuntime() returned null");

                int myPid;
                using (var procClass = new AndroidJavaClass("android.os.Process"))
                    myPid = procClass.CallStatic<int>("myPid");

                _logcatProcess = runtime.Call<AndroidJavaObject>("exec", "logcat -v time --pid=" + myPid);
                runtime.Dispose();

                if (_logcatProcess == null)
                    throw new System.Exception("exec() returned null");

                var inputStream = _logcatProcess.Call<AndroidJavaObject>("getInputStream");
                if (inputStream == null)
                    throw new System.Exception("getInputStream() returned null");

                var isReader   = new AndroidJavaObject("java.io.InputStreamReader", inputStream);
                bufferedReader = new AndroidJavaObject("java.io.BufferedReader", isReader);

                while (_logcatRunning)
                {
                    string line = bufferedReader.Call<string>("readLine");
                    if (line == null)
                    {
                        // Stream ended — clean up and schedule reconnect
                        try { bufferedReader.Call("close"); } catch { }
                        try { _logcatProcess.Call("destroy"); } catch { }
                        bufferedReader = null;
                        _logcatProcess = null;

                        if (_logcatRunning)
                        {
                            System.Threading.Thread.Sleep(2000);
                            reconnect = _logcatRunning;
                        }
                        break;
                    }
                    if (line.Length > 0) AddLogcatLine(line);
                }
            }
            catch (System.Exception ex)
            {
                AddEntry("<color=#FF6060>[ERR] logcat: " + ex.Message + "</color>",
                         "logcat: " + ex.Message, LogType.Error, Source.Logcat);
            }
            finally
            {
                try { bufferedReader?.Call("close"); }   catch { }
                try { _logcatProcess?.Call("destroy"); } catch { }
                try { AndroidJNI.DetachCurrentThread(); } catch { }
            }

            if (reconnect)
                new System.Threading.Thread(LogcatReaderLoop) { IsBackground = true }.Start();
        }

        private void AddLogcatLine(string line)
        {
            LogType type = LogType.Log;
            if (line.Contains(" E/") || line.Contains(" F/")) type = LogType.Error;
            else if (line.Contains(" W/"))                    type = LogType.Warning;

            string col  = type == LogType.Error   ? "#FF6060"
                        : type == LogType.Warning ? "#FCC733" : "#C8D0E0";
            string safe = line.Replace("<", "<").Replace(">", ">");
            AddEntry(string.Format("<color={0}>{1}</color>", col, safe), line, type, Source.Logcat);
        }
#endif

        // ── Unity log (Editor / non-Android) ────────────────────────────────

        private void HandleUnityLog(string msg, string stack, LogType type)
        {
            string col = (type == LogType.Error || type == LogType.Exception) ? "#FF6060"
                       : type == LogType.Warning ? "#FCC733" : "#C8D0E0";
            string tag = (type == LogType.Error || type == LogType.Exception) ? "ERR"
                       : type == LogType.Warning ? "WRN" : "LOG";
            string time = System.DateTime.Now.ToString("HH:mm:ss");

            var richSb  = new StringBuilder();
            var plainSb = new StringBuilder();

            richSb.AppendFormat("<color={0}>[{1}] {2}  {3}</color>", col, tag, time, msg);
            plainSb.AppendFormat("[{0}] {1}  {2}", tag, time, msg);

            if ((type == LogType.Error || type == LogType.Exception) && !string.IsNullOrEmpty(stack))
            {
                string[] lines = stack.Split('\n');
                int count = Mathf.Min(lines.Length, 8);
                for (int i = 0; i < count; i++)
                {
                    string sl = lines[i].TrimEnd();
                    if (sl.Length == 0) continue;
                    richSb.AppendFormat("\n<color=#556070>  {0}</color>", sl);
                    plainSb.AppendFormat("\n  {0}", sl);
                }
            }

            AddEntry(richSb.ToString(), plainSb.ToString(), type, Source.Unity);
        }

        private void AddEntry(string richLine, string plainText, LogType type, Source source = Source.Unity)
        {
            lock (_entries)
            {
                _entries.Add(new LogEntry { richLine = richLine, plainText = plainText, type = type, source = source });
                if (_entries.Count > maxEntries) _entries.RemoveAt(0);
            }

            if (!_paused)
                _dirty = true;

            if (!_panelOpen && (type == LogType.Error || type == LogType.Exception))
            {
                _errorsSinceClose++;
                _badgeDirty = true;

                if (autoShowOnError)
                {
                    _pendingOpen = true;
                    _autoScroll  = true;
                }
            }
        }

        // ── Display ──────────────────────────────────────────────────────────

        private void Rebuild()
        {
            if (logText == null) return;

            bool searching = !string.IsNullOrEmpty(_searchTerm);
            int  totalLog = 0, totalWarn = 0, totalErr = 0;
            var  matching = new List<string>();

            lock (_entries)
            {
                foreach (var e in _entries)
                {
                    if      (e.type == LogType.Log)     totalLog++;
                    else if (e.type == LogType.Warning) totalWarn++;
                    else                                totalErr++;

                    bool typePass = (e.type == LogType.Log     && _showLog)
                                 || (e.type == LogType.Warning && _showWarn)
                                 || ((e.type == LogType.Error || e.type == LogType.Exception) && _showErr);
                    if (!typePass) continue;

                    bool sourcePass = (e.source == Source.Unity  && _showUnity)
                                   || (e.source == Source.Logcat && _showLogcat);
                    if (!sourcePass) continue;

                    if (searching && e.plainText.IndexOf(_searchTerm, System.StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    matching.Add(e.richLine);
                }
            }

            if (_logCountText  != null) _logCountText.text  = totalLog.ToString();
            if (_warnCountText != null) _warnCountText.text = totalWarn.ToString();
            if (_errCountText  != null) _errCountText.text  = totalErr.ToString();

            if (statusText != null)
                statusText.text = string.Format(
                    "Showing {0} / {1}     LOG {2}  ·  WRN {3}  ·  ERR {4}",
                    Mathf.Min(matching.Count, MaxDisplayLines), matching.Count,
                    totalLog, totalWarn, totalErr);

            var sb    = new StringBuilder();
            int start = Mathf.Max(0, matching.Count - MaxDisplayLines);
            if (start > 0)
                sb.AppendLine(string.Format(
                    "<color=#555E72>──── {0} earlier {1} hidden ────</color>",
                    start, start == 1 ? "entry" : "entries"));

            for (int i = start; i < matching.Count; i++)
                sb.AppendLine(matching[i]);

            logText.text = sb.ToString();
            if (_autoScroll) _scrollPending = true;
        }

        private void OnScrollMoved(Vector2 pos)
        {
            if (_ignoreScroll) return;
            _autoScroll = pos.y <= 0.02f;
        }

        // ── Public API ───────────────────────────────────────────────────────

        public void TogglePanel() => OpenPanel(!_panelOpen);

        public void ClearLogs()
        {
            lock (_entries) _entries.Clear();
            if (logText        != null) logText.text        = string.Empty;
            if (statusText     != null) statusText.text     = "No entries";
            if (_logCountText  != null) _logCountText.text  = "0";
            if (_warnCountText != null) _warnCountText.text = "0";
            if (_errCountText  != null) _errCountText.text  = "0";
        }

        public void ClearSearch()
        {
            _searchTerm = "";
            if (searchInput != null) searchInput.text = "";
            _dirty           = true;
            _rebuildCooldown = 0f;
        }

        public void ToggleLogFilter()  { _showLog  = !_showLog;  RefreshFilterColors(); _dirty = true; _rebuildCooldown = 0f; }
        public void ToggleWarnFilter() { _showWarn = !_showWarn; RefreshFilterColors(); _dirty = true; _rebuildCooldown = 0f; }
        public void ToggleErrFilter()  { _showErr  = !_showErr;  RefreshFilterColors(); _dirty = true; _rebuildCooldown = 0f; }

        public void TogglePause()
        {
            _paused = !_paused;
            if (!_paused) _dirty = true;
            RefreshPauseLabel();
            RefreshRightGroupColors();
        }

        public void ToggleUnitySource()  { _showUnity  = !_showUnity;  _dirty = true; _rebuildCooldown = 0f; RefreshRightGroupColors(); }
        public void ToggleLogcatSource() { _showLogcat = !_showLogcat; _dirty = true; _rebuildCooldown = 0f; RefreshRightGroupColors(); }

        public void ExportLogs()
        {
            bool searching = !string.IsNullOrEmpty(_searchTerm);
            var  sb        = new StringBuilder();
            sb.AppendLine("=== InGame Debug Console Export ===");
            sb.AppendLine(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();

            lock (_entries)
            {
                foreach (var e in _entries)
                {
                    bool typePass = (e.type == LogType.Log     && _showLog)
                                 || (e.type == LogType.Warning && _showWarn)
                                 || ((e.type == LogType.Error || e.type == LogType.Exception) && _showErr);
                    if (!typePass) continue;

                    bool sourcePass = (e.source == Source.Unity  && _showUnity)
                                   || (e.source == Source.Logcat && _showLogcat);
                    if (!sourcePass) continue;

                    if (searching && e.plainText.IndexOf(_searchTerm, System.StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    sb.AppendLine(e.plainText);
                }
            }

            string content   = sb.ToString();
            string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");

#if UNITY_EDITOR
            string path = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, "../debug_export.txt"));
            System.IO.File.WriteAllText(path, content);
            Debug.Log("[InGameDebugConsole] Exported to " + path);
#elif UNITY_ANDROID
            string path = System.IO.Path.Combine(
                Application.persistentDataPath, "debug_log_" + timestamp + ".txt");
            System.IO.File.WriteAllText(path, content);

            using (var intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.SEND"))
            {
                intent.Call<AndroidJavaObject>("setType",  "text/plain");
                intent.Call<AndroidJavaObject>("putExtra", "android.intent.extra.TEXT",    content);
                intent.Call<AndroidJavaObject>("putExtra", "android.intent.extra.SUBJECT", "Debug Log " + timestamp);

                using (var intentClass = new AndroidJavaClass("android.content.Intent"))
                {
                    var chooser = intentClass.CallStatic<AndroidJavaObject>(
                        "createChooser", intent, "Share Debug Log");

                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    {
                        var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                        activity.Call("startActivity", chooser);
                    }
                }
            }
            Debug.Log("[InGameDebugConsole] Exported to " + path);
#endif
        }

        public static void Log(string msg) => Debug.Log(msg);

        // ── Internal helpers ─────────────────────────────────────────────────

        private void OpenPanel(bool open)
        {
            _panelOpen = open;
            if (panel       != null) panel.SetActive(open);
            if (toggleLabel != null) toggleLabel.text = open ? "LOGS  ▼" : "LOGS  ▲";
            if (open)
            {
                _errorsSinceClose = 0;
                _autoScroll       = true;
                _dirty            = true;
                _rebuildCooldown  = 0f;
                RefreshErrorBadge();
            }
        }

        private void RefreshFilterColors()
        {
            if (_logBtnImg  != null) _logBtnImg.color  = _showLog  ? ColLogOn  : ColOff;
            if (_warnBtnImg != null) _warnBtnImg.color = _showWarn ? ColWarnOn : ColOff;
            if (_errBtnImg  != null) _errBtnImg.color  = _showErr  ? ColErrOn  : ColOff;
        }

        private void RefreshPauseLabel()
        {
            if (_pauseBtnLabel != null)
                _pauseBtnLabel.text = _paused ? "▶ RESUME" : "⏸ PAUSE";
        }

        private void RefreshRightGroupColors()
        {
            if (_pauseBtnImg  != null) _pauseBtnImg.color  = _paused     ? ColPausedOn : ColOff;
            if (_unityBtnImg  != null) _unityBtnImg.color  = _showUnity  ? ColSourceOn : ColOff;
            if (_logcatBtnImg != null) _logcatBtnImg.color = _showLogcat ? ColSourceOn : ColOff;
        }

        private void RefreshErrorBadge()
        {
            if (_errorBadgeText == null) return;
            bool show = _errorsSinceClose > 0;
            _errorBadgeText.gameObject.SetActive(show);
            if (show) _errorBadgeText.text = _errorsSinceClose.ToString();
        }

        private void WireButton(string childName, UnityEngine.Events.UnityAction action)
        {
            var t = transform.Find(childName);
            if (t == null) return;
            var b = t.GetComponent<Button>();
            if (b != null) b.onClick.AddListener(action);
        }

        private void WireButtonDeep(string childName, UnityEngine.Events.UnityAction action)
        {
            foreach (var b in GetComponentsInChildren<Button>(true))
                if (b.name == childName) { b.onClick.AddListener(action); return; }
        }

        private Image FindChildImage(string childName)
        {
            foreach (var img in GetComponentsInChildren<Image>(true))
                if (img.name == childName) return img;
            return null;
        }

        private Text FindCountLabel(string btnName)
        {
            foreach (var b in GetComponentsInChildren<Button>(true))
            {
                if (b.name != btnName) continue;
                foreach (var t in b.GetComponentsInChildren<Text>(true))
                    if (t.name == "CountLabel") return t;
            }
            return null;
        }

        private Text FindButtonLabel(string btnName)
        {
            foreach (var b in GetComponentsInChildren<Button>(true))
            {
                if (b.name != btnName) continue;
                var t = b.GetComponentInChildren<Text>(true);
                if (t != null) return t;
            }
            return null;
        }

        private Text FindButtonChildText(string btnName, string textName)
        {
            foreach (var b in GetComponentsInChildren<Button>(true))
            {
                if (b.name != btnName) continue;
                foreach (var t in b.GetComponentsInChildren<Text>(true))
                    if (t.name == textName) return t;
            }
            return null;
        }
    }
#endif // !RAHEEB_RELEASE
}
