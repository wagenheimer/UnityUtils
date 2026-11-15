using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Wagenheimer.UnityUtils
{
    /// <summary>
    /// Writes every Unity log line (Debug.Log/Warning/Error and unhandled exceptions) to
    /// <c>persistentDataPath/logs/game-log.txt</c> (the previous run is kept as <c>game-log-prev.txt</c>) and can send
    /// it to the AppDeployHub Forge, which stores it and raises a notification.
    /// <para>
    /// It starts by itself before the first scene (no setup). Upload and the DEBUG button need an
    /// <see cref="AppLogSettings"/> asset with the upload key; see <see cref="AppLogDebugPanel"/>.
    /// </para>
    /// </summary>
    public sealed class AppFileLog : MonoBehaviour
    {
        private const string FolderName = "logs";
        private const string CurrentFile = "game-log.txt";
        private const string PreviousFile = "game-log-prev.txt";
        private const long MaxFileBytes = 2L * 1024 * 1024;
        private const int MaxUploadBytes = 1_500_000;
        private const int RecentCapacity = 400;
        private const float FlushIntervalSeconds = 2f;
        private const float MinSecondsBetweenAutoUploads = 60f;
        private const int MaxAutoUploadsPerSession = 3;
        private const string AutoUploadPrefKey = "Wagenheimer.AppFileLog.AutoUpload";

        private static AppFileLog _instance;

        private readonly object _gate = new object();
        private readonly StringBuilder _pending = new StringBuilder(4096);
        private readonly string[] _recent = new string[RecentCapacity];
        private int _recentCount;
        private int _recentNext;
        private string _path;
        private long _bytesWritten;
        private bool _writeDisabled;
        private float _nextFlush;
        private volatile bool _flushNow;
        private volatile bool _errorSeen;

        private AppLogSettings _settings;
        private bool _uploading;
        private int _autoUploads;
        private float _lastAutoUpload = float.NegativeInfinity;

        /// <summary>Current log file ('' until started).</summary>
        public static string CurrentPath => _instance != null ? _instance._path : string.Empty;

        /// <summary>True when the Forge upload is configured and allowed in this build.</summary>
        public static bool UploadAvailable => _instance != null && _instance._settings != null && _instance._settings.IsUsable;

        public static bool IsUploading => _instance != null && _instance._uploading;

        /// <summary>Last upload result for the panel ('' when none yet).</summary>
        public static string LastUploadStatus { get; private set; } = string.Empty;

        /// <summary>Send the log automatically when an error/exception is logged (persisted per device).</summary>
        public static bool AutoUploadOnError
        {
            get
            {
                if (_instance == null || _instance._settings == null) return false;
                return PlayerPrefs.GetInt(AutoUploadPrefKey, _instance._settings.autoUploadOnError ? 1 : 0) == 1;
            }
            set
            {
                PlayerPrefs.SetInt(AutoUploadPrefKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;

            var go = new GameObject("[AppFileLog]");
            go.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<AppFileLog>();
        }

        /// <summary>The last log lines, oldest first.</summary>
        public static string GetRecent() => _instance == null ? string.Empty : _instance.SnapshotRecent();

        /// <summary>Sends the log to the Forge now. <paramref name="reason"/>: manual, error, pause...</summary>
        public static void Upload(string reason = "manual")
        {
            if (_instance == null) return;
            _instance.StartUpload(reason);
        }

        private void Awake()
        {
            _settings = AppLogSettings.Load();

            try
            {
                string dir = Path.Combine(Application.persistentDataPath, FolderName);
                Directory.CreateDirectory(dir);
                _path = Path.Combine(dir, CurrentFile);

                string previous = Path.Combine(dir, PreviousFile);
                if (File.Exists(_path))
                {
                    if (File.Exists(previous)) File.Delete(previous);
                    File.Move(_path, previous);
                }
            }
            catch (Exception e)
            {
                _writeDisabled = true;
                Debug.LogWarning("[FileLog] Could not prepare the log file, keeping the in-memory log only: " + e.Message);
            }

            Application.logMessageReceivedThreaded += OnLog;

            Append("=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | " + Application.productName + " " +
                   Application.version + " | Unity " + Application.unityVersion + " | " + Application.platform + " | " +
                   SystemInfo.deviceModel + " | " + SystemInfo.operatingSystem + " ===");
            Append("log file: " + (_path ?? "(memory only)") + " | forge upload: " + (UploadAvailable ? "on" : "off"));

            if (UploadAvailable && _settings.showDebugButton)
                gameObject.AddComponent<AppLogDebugPanel>();
        }

        private void OnDestroy()
        {
            Application.logMessageReceivedThreaded -= OnLog;
            Flush();
        }

        private void Update()
        {
            if (_errorSeen)
            {
                _errorSeen = false;
                TryAutoUpload("error");
            }

            if (!_flushNow && Time.unscaledTime < _nextFlush) return;

            _flushNow = false;
            _nextFlush = Time.unscaledTime + FlushIntervalSeconds;
            Flush();
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused) return;

            Flush();
            if (UploadAvailable && _settings.autoUploadOnPause)
                TryAutoUpload("pause");
        }

        private void OnApplicationQuit() => Flush();

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            var line = new StringBuilder(condition.Length + 48);
            line.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(' ').Append(LevelTag(type)).Append(' ').Append(condition);

            // Stack traces only for problems: they make the file huge for plain logs.
            if (type != LogType.Log && type != LogType.Warning && !string.IsNullOrEmpty(stackTrace))
                line.Append('\n').Append(stackTrace.TrimEnd());

            Append(line.ToString());

            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                // Make sure the lines leading up to a crash reach the disk; the upload runs on the main thread.
                _flushNow = true;
                _errorSeen = true;
            }
        }

        private static string LevelTag(LogType type)
        {
            switch (type)
            {
                case LogType.Error: return "[E]";
                case LogType.Assert: return "[A]";
                case LogType.Warning: return "[W]";
                case LogType.Exception: return "[X]";
                default: return "[I]";
            }
        }

        private void Append(string line)
        {
            lock (_gate)
            {
                _recent[_recentNext] = line;
                _recentNext = (_recentNext + 1) % RecentCapacity;
                if (_recentCount < RecentCapacity) _recentCount++;

                if (!_writeDisabled) _pending.Append(line).Append('\n');
            }
        }

        private string SnapshotRecent()
        {
            lock (_gate)
            {
                var sb = new StringBuilder();
                int start = _recentCount < RecentCapacity ? 0 : _recentNext;
                for (int i = 0; i < _recentCount; i++)
                    sb.Append(_recent[(start + i) % RecentCapacity]).Append('\n');
                return sb.ToString();
            }
        }

        private void Flush()
        {
            if (_writeDisabled || string.IsNullOrEmpty(_path)) return;

            string text;
            lock (_gate)
            {
                if (_pending.Length == 0) return;
                text = _pending.ToString();
                _pending.Length = 0;
            }

            try
            {
                if (_bytesWritten > MaxFileBytes)
                {
                    // Runaway log (e.g. an error every frame): stop growing instead of filling the device.
                    _writeDisabled = true;
                    text += "... log file size limit reached, further lines only kept in memory ...\n";
                }

                File.AppendAllText(_path, text, Encoding.UTF8);
                _bytesWritten += text.Length;
            }
            catch (Exception e)
            {
                _writeDisabled = true;
                Debug.LogWarning("[FileLog] Writing the log file failed, switching to memory only: " + e.Message);
            }
        }

        // ── Forge upload ─────────────────────────────────────────────────────

        private void TryAutoUpload(string reason)
        {
            if (!UploadAvailable || _uploading) return;
            if (reason == "error" && !AutoUploadOnError) return;
            if (_autoUploads >= MaxAutoUploadsPerSession) return;
            if (Time.unscaledTime - _lastAutoUpload < MinSecondsBetweenAutoUploads) return;

            _autoUploads++;
            _lastAutoUpload = Time.unscaledTime;
            StartUpload(reason);
        }

        private void StartUpload(string reason)
        {
            if (!UploadAvailable)
            {
                LastUploadStatus = "Upload not configured (AppLogSettings / upload key).";
                return;
            }

            if (_uploading) return;

            _uploading = true;
            LastUploadStatus = "Sending...";
            StartCoroutine(UploadRoutine(reason));
        }

        private IEnumerator UploadRoutine(string reason)
        {
            Flush();
            byte[] body = ReadLogBytes();
            if (body.Length == 0)
            {
                _uploading = false;
                LastUploadStatus = "Nothing to send yet.";
                yield break;
            }

            string url = _settings.serverUrl.TrimEnd('/') + "/api/device-logs" +
                         "?app=" + Uri.EscapeDataString(Application.identifier) +
                         "&platform=" + Uri.EscapeDataString(Application.platform.ToString()) +
                         "&device=" + Uri.EscapeDataString(SystemInfo.deviceModel) +
                         "&version=" + Uri.EscapeDataString(Application.version) +
                         "&reason=" + Uri.EscapeDataString(reason);

            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "text/plain; charset=utf-8");
                request.SetRequestHeader("X-Log-Key", _settings.uploadKey);
                request.timeout = 25;

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    LastUploadStatus = "Sent to the Forge (" + (body.Length / 1024) + " KB) at " + DateTime.Now.ToString("HH:mm:ss");
                    Debug.Log("[FileLog] Log uploaded to the Forge (" + reason + ", " + body.Length + " bytes).");
                }
                else
                {
                    // Warning, not error: an error here would trigger another automatic upload.
                    LastUploadStatus = "Upload failed: " + request.responseCode + " " + request.error;
                    Debug.LogWarning("[FileLog] Log upload failed: " + request.responseCode + " " + request.error);
                }
            }

            _uploading = false;
        }

        /// <summary>The file's tail (at most ~1.5 MB); falls back to the in-memory lines if the file is unavailable.</summary>
        private byte[] ReadLogBytes()
        {
            try
            {
                if (!string.IsNullOrEmpty(_path) && File.Exists(_path))
                {
                    using (var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        long length = stream.Length;
                        long skip = Math.Max(0, length - MaxUploadBytes);
                        stream.Seek(skip, SeekOrigin.Begin);

                        var data = new byte[length - skip];
                        int read = 0;
                        while (read < data.Length)
                        {
                            int n = stream.Read(data, read, data.Length - read);
                            if (n <= 0) break;
                            read += n;
                        }

                        if (read == data.Length && data.Length > 0) return data;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FileLog] Reading the log file failed, sending the in-memory lines: " + e.Message);
            }

            return Encoding.UTF8.GetBytes(SnapshotRecent());
        }
    }
}
