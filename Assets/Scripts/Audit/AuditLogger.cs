using System;
using System.Globalization;
using System.IO;
using System.Text;
using LockedDown.Telemetry;
using UnityEngine;

namespace LockedDown.Audit
{
    /// <summary>
    /// Append-only ordered behavioural event log.
    ///
    /// Identity is OWNED BY TelemetryLogger and consumed here. This logger never
    /// invents a participant id - a second source of truth is how you end up with
    /// two files that disagree about how many participants exist.
    ///
    /// File is opened in Start(), not Awake(), so TelemetryLogger.Awake() has
    /// already run and assigned identity regardless of script execution order.
    ///
    /// Kept as a SEPARATE csv from the heartbeat log on purpose: a heartbeat
    /// regression must never be able to corrupt the behavioural record.
    /// </summary>
    public class AuditLogger : MonoBehaviour
    {
        public static AuditLogger Instance { get; private set; }

        private const string Header =
            "session_id,participant_id,condition,seq,utc_iso,session_elapsed_s," +
            "event,robot_id,key_id,verdict,detail";

        private StreamWriter _writer;
        private int _seq;
        private DateTime _sessionStartUtc;

        private string _sessionId = "unset";
        private string _participantId = "unset";
        private string _condition = "unset";

        public string FilePath { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            _sessionStartUtc = DateTime.UtcNow;

            var t = TelemetryLogger.Instance;
            if (t != null)
            {
                _sessionId     = t.SessionId;
                _participantId = t.ParticipantId;
                _condition     = t.Condition;
            }
            else
            {
                // Do NOT silently fabricate an id. Make the failure loud and
                // make it visible in the data itself.
                _sessionId     = "NO_TELEMETRY_" + _sessionStartUtc.ToString("yyyyMMdd_HHmmss");
                _participantId = "NO_TELEMETRY";
                _condition     = "NO_TELEMETRY";
                Debug.LogError("[Audit] TelemetryLogger.Instance is null. " +
                               "Audit rows cannot be joined to the session log.");
            }

            FilePath = Path.Combine(Application.persistentDataPath,
                                    $"audit_{_participantId}_{_sessionId}.csv");

            bool isNew = !File.Exists(FilePath);
            _writer = new StreamWriter(FilePath, append: true, Encoding.UTF8);
            if (isNew) _writer.WriteLine(Header);
            _writer.Flush();

            LogSession("SESSION_START");
            Debug.Log($"[Audit] {_participantId} / {_condition} -> {FilePath}");
        }

        public void Log(AuditEventType type,
                        string robotId,
                        string keyId = null,
                        string verdict = null,
                        string detail = null)
        {
            if (_writer == null) return;

            _seq++;
            DateTime now = DateTime.UtcNow;
            double elapsed = (now - _sessionStartUtc).TotalSeconds;
            var inv = CultureInfo.InvariantCulture;

            _writer.WriteLine(string.Join(",",
                Esc(_sessionId),
                Esc(_participantId),
                Esc(_condition),
                _seq.ToString(inv),
                now.ToString("o", inv),
                elapsed.ToString("F3", inv),
                type.ToString(),
                Esc(robotId),
                Esc(keyId),
                Esc(verdict),
                Esc(detail)));

            _writer.Flush();
        }

        /// <summary>
        /// Session lifecycle marker. Uses a dedicated SessionMarker event type so
        /// lifecycle rows are not mistaken for RobotApproached rows in analysis.
        /// </summary>
        private void LogSession(string detail)
        {
            Log(AuditEventType.SessionMarker, "", null, null, detail);
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        // --- lifecycle. OnApplicationPause(true) is terminal on Quest. ---
        private void OnApplicationPause(bool paused)
        {
            LogSession(paused ? "APP_PAUSED" : "APP_RESUMED");
            if (paused) Close();
        }

        private void OnApplicationFocus(bool focused)
        {
            LogSession(focused ? "APP_FOCUS" : "APP_BLUR");
        }

        private void OnApplicationQuit()
        {
            LogSession("SESSION_END");
            Close();
        }

        private void OnDestroy()
        {
            if (Instance == this) Close();
        }

        private void Close()
        {
            if (_writer == null) return;
            _writer.Flush();
            _writer.Dispose();
            _writer = null;
        }
    }
}
