using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace LockedDown.Telemetry
{
    /// <summary>
    /// Append-only CSV session logger. One instance, survives scene loads.
    /// Buffers in memory, flushes on an interval and on every lifecycle boundary.
    /// </summary>
    public class TelemetryLogger : MonoBehaviour
    {
        public static TelemetryLogger Instance { get; private set; }

        [SerializeField] float heartbeatInterval = 1.0f;
        [SerializeField] float flushInterval     = 5.0f;
        [SerializeField] Transform headTransform;   // assign XR Main Camera

        const string Header =
            "session_id,participant_id,condition,seq,t_unix_ms,t_session_ms,event," +
            "robot_id,object_id,num_value,str_value,head_x,head_y,head_z,head_yaw";

        string _sessionId, _participantId = "unset", _condition = "default";
        string _path;
        int _seq;
        float _sessionStart, _nextHeartbeat, _nextFlush;
        readonly List<string> _buffer = new List<string>(256);
        bool _closed;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _sessionId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" +
                         UnityEngine.Random.Range(1000, 9999);
            _path = Path.Combine(Application.persistentDataPath, $"session_{_sessionId}.csv");
            File.WriteAllText(_path, Header + "\n", Encoding.UTF8);

            _sessionStart = Time.realtimeSinceStartup;
            Log("session_start", strValue: Application.version);
            Debug.Log($"[Telemetry] writing to {_path}");
        }

        public void SetParticipant(string participantId, string condition)
        {
            _participantId = Sanitize(participantId);
            _condition     = Sanitize(condition);
            Log("participant_set", strValue: _participantId);
        }

        public void Log(string evt,
                        string robotId  = "",
                        string objectId = "",
                        float  numValue = float.NaN,
                        string strValue = "")
        {
            if (_closed) return;

            Vector3 p = Vector3.zero; float yaw = 0f;
            if (headTransform != null)
            {
                p   = headTransform.position;
                yaw = headTransform.eulerAngles.y;
            }

            var inv = CultureInfo.InvariantCulture;
            long unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int  sessMs = Mathf.RoundToInt((Time.realtimeSinceStartup - _sessionStart) * 1000f);

            _buffer.Add(string.Join(",",
                _sessionId, _participantId, _condition,
                (++_seq).ToString(inv), unixMs.ToString(inv), sessMs.ToString(inv),
                Sanitize(evt), Sanitize(robotId), Sanitize(objectId),
                float.IsNaN(numValue) ? "" : numValue.ToString("F3", inv),
                Sanitize(strValue),
                p.x.ToString("F3", inv), p.y.ToString("F3", inv), p.z.ToString("F3", inv),
                yaw.ToString("F2", inv)));
        }

        void Update()
        {
            float t = Time.realtimeSinceStartup;
            if (t >= _nextHeartbeat) { Log("heartbeat"); _nextHeartbeat = t + heartbeatInterval; }
            if (t >= _nextFlush)     { Flush();          _nextFlush     = t + flushInterval; }
        }

        // --- lifecycle: these three hooks are the fix for the heartbeat gap + missing end row ---

        void OnApplicationPause(bool paused)
        {
            Log(paused ? "app_pause" : "app_resume", numValue: paused ? 1f : 0f);
            Flush();
        }

        void OnApplicationFocus(bool focused)
        {
            Log(focused ? "app_focus_gained" : "app_focus_lost", numValue: focused ? 1f : 0f);
            Flush();
        }

        void OnApplicationQuit() => Close();
        void OnDestroy()         { if (Instance == this) Close(); }

        public void Close()
        {
            if (_closed) return;
            Log("session_end", numValue: _seq);
            Flush();
            _closed = true;
        }

        public void Flush()
        {
            if (_buffer.Count == 0) return;
            try
            {
                File.AppendAllLines(_path, _buffer, Encoding.UTF8);
                _buffer.Clear();
            }
            catch (Exception e) { Debug.LogError($"[Telemetry] flush failed: {e.Message}"); }
        }

        static string Sanitize(string s) =>
            string.IsNullOrEmpty(s) ? "" : s.Replace(',', ';').Replace('\n', ' ').Replace('\r', ' ');
    }
}

// using System.Collections;
// using System.Collections.Generic;
// using System;
// using System.IO;
// using UnityEngine;

// public class TelemetryLogger : MonoBehaviour
// {
//     string path;

//     void Start()
//     {
//         path = Path.Combine(Application.persistentDataPath,
//             $"session_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv");
//         try
//         {
//             File.AppendAllText(path, "timestamp_utc,participant_id,event,detail\n");
//             Debug.Log($"[TELEMETRY] OK -> {path}");
//         }
//         catch (Exception e)
//         {
//             Debug.LogError($"[TELEMETRY] WRITE FAILED: {e.Message}");
//         }
//         Log("P000", "app_start", SystemInfo.deviceModel);
//         InvokeRepeating(nameof(Heartbeat), 5f, 5f);
//     }

//     void Heartbeat() => Log("P000", "heartbeat", Time.realtimeSinceStartup.ToString("F1"));
//     static string Csv(string s)
//     {
//         if (string.IsNullOrEmpty(s)) return "";
//         return (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
//             ? "\"" + s.Replace("\"", "\"\"") + "\""
//             : s;
//     }

//     public void Log(string pid, string evt, string detail)
//     {
//         File.AppendAllText(path,
//             $"{DateTime.UtcNow:o},{Csv(pid)},{Csv(evt)},{Csv(detail)}\n");
//     }
// }
