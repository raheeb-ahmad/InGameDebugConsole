// ================================================================
//  InGame Debug Console  v1.0.0
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

        private bool   _showLog  = true;
        private bool   _showWarn = true;
        private bool   _showErr  = true;
        private string _searchTerm = "";

        private Image _logBtnImg, _warnBtnImg, _errBtnImg;
        private Text  _logCountText, _warnCountText, _errCountText;

        private static readonly Color ColLogOn  = new Color(0.25f, 0.87f, 0.40f, 1f);
        private static readonly Color ColWarnOn = new Color(0.97f, 0.76f, 0.20f, 1f);
        private static readonly Color ColErrOn  = new Color(1.00f, 0.38f, 0.38f, 1f);
        private static readonly Color ColOff    = new Color(0.18f, 0.19f, 0.25f, 1f);

        private readonly List<LogEntry> _entries = new List<LogEntry>();
        private bool  _panelOpen;
        private bool  _dirty;
        private bool  _pendingOpen;
        private bool  _autoScroll     = true;
        private bool  _ignoreScroll   = false;
        private bool  _scrollPending  = false;
        private float _rebuildCooldown = 0f;

        private const int MaxDisplayLines = 50;

        private static InGameDebugConsole _instance;

        private struct LogEntry
        {
            public string  richLine;
            public string  plainText;
            public LogType type;
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

            WireButton("ToggleButton",       TogglePanel);
            WireButtonDeep("CloseBtn",       TogglePanel);
            WireButtonDeep("ClearBtn",       ClearLogs);
            WireButtonDeep("LogFilterBtn",   ToggleLogFilter);
            WireButtonDeep("WarnFilterBtn",  ToggleWarnFilter);
            WireButtonDeep("ErrFilterBtn",   ToggleErrFilter);
            WireButtonDeep("ClearSearchBtn", ClearSearch);

            _logBtnImg  = FindChildImage("LogFilterBtn");
            _warnBtnImg = FindChildImage("WarnFilterBtn");
            _errBtnImg  = FindChildImage("ErrFilterBtn");

            _logCountText  = FindCountLabel("LogFilterBtn");
            _warnCountText = FindCountLabel("WarnFilterBtn");
            _errCountText  = FindCountLabel("ErrFilterBtn");

            RefreshFilterColors();

            if (searchInput != null)
                searchInput.onValueChanged.AddListener(v =>
                {
                    _searchTerm = v;
                    _dirty = true;
                    _rebuildCooldown = 0f;
                });

            if (scrollRect != null)
                scrollRect.onValueChanged.AddListener(OnScrollMoved);
        }

        private void Update()
        {
            if (_pendingOpen) { _pendingOpen = false; OpenPanel(true); }

            _rebuildCooldown -= Time.deltaTime;
            if (_dirty && _panelOpen && _rebuildCooldown <= 0f)
            {
                _rebuildCooldown = 0.25f;
                _dirty = false;
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
                    if (line == null) break;
                    if (line.Length > 0) AddLogcatLine(line);
                }
            }
            catch (System.Exception ex)
            {
                AddEntry("<color=#FF6060>[ERR] logcat: " + ex.Message + "</color>",
                         "logcat: " + ex.Message, LogType.Error);
            }
            finally
            {
                try { bufferedReader?.Call("close"); }   catch { }
                try { _logcatProcess?.Call("destroy"); } catch { }
                AndroidJNI.DetachCurrentThread();
            }
        }

        private void AddLogcatLine(string line)
        {
            LogType type = LogType.Log;
            if (line.Contains(" E/") || line.Contains(" F/")) type = LogType.Error;
            else if (line.Contains(" W/"))                    type = LogType.Warning;

            string col  = type == LogType.Error   ? "#FF6060"
                        : type == LogType.Warning ? "#FCC733" : "#C8D0E0";
            string safe = line.Replace("<", "<").Replace(">", ">");
            AddEntry(string.Format("<color={0}>{1}</color>", col, safe), line, type);
        }
#endif

        // ── Unity log (Editor / non-Android) ────────────────────────────────

        private void HandleUnityLog(string msg, string stack, LogType type)
        {
            string col  = (type == LogType.Error || type == LogType.Exception) ? "#FF6060"
                        : type == LogType.Warning ? "#FCC733" : "#C8D0E0";
            string tag  = (type == LogType.Error || type == LogType.Exception) ? "ERR"
                        : type == LogType.Warning ? "WRN" : "LOG";
            string time = System.DateTime.Now.ToString("HH:mm:ss");

            AddEntry(string.Format("<color={0}>[{1}] {2}  {3}</color>", col, tag, time, msg),
                     string.Format("[{0}] {1}  {2}", tag, time, msg), type);
        }

        private void AddEntry(string richLine, string plainText, LogType type)
        {
            lock (_entries)
            {
                _entries.Add(new LogEntry { richLine = richLine, plainText = plainText, type = type });
                if (_entries.Count > maxEntries) _entries.RemoveAt(0);
            }
            _dirty = true;
            if (autoShowOnError && !_panelOpen && (type == LogType.Error || type == LogType.Exception))
                _pendingOpen = true;
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
                    if      (e.type == LogType.Log)                                   totalLog++;
                    else if (e.type == LogType.Warning)                               totalWarn++;
                    else                                                               totalErr++;

                    bool typePass = (e.type == LogType.Log     && _showLog)
                                 || (e.type == LogType.Warning && _showWarn)
                                 || ((e.type == LogType.Error || e.type == LogType.Exception) && _showErr);
                    if (!typePass) continue;

                    if (searching && e.plainText.IndexOf(_searchTerm, System.StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    matching.Add(e.richLine);
                }
            }

            // Update count badges on filter buttons
            if (_logCountText  != null) _logCountText.text  = totalLog.ToString();
            if (_warnCountText != null) _warnCountText.text = totalWarn.ToString();
            if (_errCountText  != null) _errCountText.text  = totalErr.ToString();

            // Update status bar
            if (statusText != null)
                statusText.text = string.Format(
                    "Showing {0} / {1}     LOG {2}  ·  WRN {3}  ·  ERR {4}",
                    Mathf.Min(matching.Count, MaxDisplayLines), matching.Count,
                    totalLog, totalWarn, totalErr);

            // Render last MaxDisplayLines of matching entries
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
            if (logText    != null) logText.text    = string.Empty;
            if (statusText != null) statusText.text = "No entries";
            if (_logCountText  != null) _logCountText.text  = "0";
            if (_warnCountText != null) _warnCountText.text = "0";
            if (_errCountText  != null) _errCountText.text  = "0";
        }

        public void ClearSearch()
        {
            _searchTerm = "";
            if (searchInput != null) searchInput.text = "";
            _dirty = true;
            _rebuildCooldown = 0f;
        }

        public void ToggleLogFilter()  { _showLog  = !_showLog;  RefreshFilterColors(); _dirty = true; _rebuildCooldown = 0f; }
        public void ToggleWarnFilter() { _showWarn = !_showWarn; RefreshFilterColors(); _dirty = true; _rebuildCooldown = 0f; }
        public void ToggleErrFilter()  { _showErr  = !_showErr;  RefreshFilterColors(); _dirty = true; _rebuildCooldown = 0f; }

        // ── Internal helpers ─────────────────────────────────────────────────

        private void OpenPanel(bool open)
        {
            _panelOpen = open;
            if (panel != null) panel.SetActive(open);
            if (toggleLabel != null) toggleLabel.text = open ? "LOGS  ▼" : "LOGS  ▲";
            if (open)
            {
                _autoScroll      = true;
                _dirty           = true;
                _rebuildCooldown = 0f;
            }
        }

        private void RefreshFilterColors()
        {
            if (_logBtnImg  != null) _logBtnImg.color  = _showLog  ? ColLogOn  : ColOff;
            if (_warnBtnImg != null) _warnBtnImg.color = _showWarn ? ColWarnOn : ColOff;
            if (_errBtnImg  != null) _errBtnImg.color  = _showErr  ? ColErrOn  : ColOff;
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
    }
}
