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
    ///
    /// v0.2 changes:
    ///  - app_pause(true) now terminates the session. OnApplicationQuit does NOT fire
    ///    reliably on Quest; the OS kills the process via the pause path. Verified
    ///    empirically: sessions 20260805_152859 and _153051 both ended without a
    ///    session_end row.
    ///  - Participant ID auto-assigned from a persistent counter. No ADB, no rebuild,
    ///    no manual entry per session.
    ///  - Out-of-bounds detection. A fall through world geometry previously logged
    ///    16s of freefall as ordinary heartbeats, indistinguishable from exploration.
    /// </summary>
    public class TelemetryLogger : MonoBehaviour
    {
        public static TelemetryLogger Instance { get; private set; }

        [Header("Sampling")]
        [SerializeField] float heartbeatInterval = 1.0f;
        [SerializeField] float flushInterval = 5.0f;

        [Header("References")]
        [Tooltip("Assign XR Origin > Camera Offset > Main Camera")]
        [SerializeField] Transform headTransform;
        [Tooltip("Assign the XR Origin (XR Rig) root. Needed for out-of-bounds reset.")]
        [SerializeField] Transform rigTransform;

        [Header("Session identity")]
        [Tooltip("TRUE for your own testing. Prefixes IDs with PILOT_ and does not " +
                 "consume a participant number. MUST be false in the study build.")]
        [SerializeField] bool isPilotBuild = true;

        [Tooltip("Fixed condition label. Used when conditionRotation is empty.")]
        [SerializeField] string fixedCondition = "single_config";

        [Tooltip("Leave EMPTY for a single fixed condition (recommended at N=12-20). " +
                 "Populate only if you are genuinely counterbalancing and have the N " +
                 "to support it. Assignment cycles by participant number.")]
        [SerializeField] string[] conditionRotation = new string[0];

        [Header("Out of bounds")]
        [Tooltip("Floor level in world units. Control Room 2 floor sits at ~3.66.")]
        [SerializeField] float floorY = 3.66f;
        [Tooltip("How far below floorY counts as fallen through the world.")]
        [SerializeField] float fallThreshold = 2.0f;
        [Tooltip("Where to put the rig after a fall. Leave null to disable reset " +
                 "(event is still logged).")]
        [SerializeField] Transform respawnPoint;

        const string Header =
            "session_id,participant_id,condition,seq,t_unix_ms,t_session_ms,event," +
            "robot_id,object_id,num_value,str_value,head_x,head_y,head_z,head_yaw";

        const string CounterKey = "lockeddown_session_counter";

        string _sessionId, _participantId = "unset", _condition = "default";
        string _path;
        int _seq;
        float _sessionStart, _nextHeartbeat, _nextFlush;
        readonly List<string> _buffer = new List<string>(256);
        bool _closed;
        bool _outOfBounds;   // latch, so one fall logs one event not sixteen

        // ---------------------------------------------------------------- lifecycle

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

            AssignIdentity();

            Log("session_start", strValue: Application.version);
            Log("build_mode", strValue: isPilotBuild ? "PILOT" : "STUDY");

            if (headTransform == null)
                Log("config_error", strValue: "headTransform_unassigned");
            if (!isPilotBuild && _participantId.StartsWith("PILOT"))
                Log("config_error", strValue: "study_build_with_pilot_id");

            Debug.Log($"[Telemetry] {_participantId} / {_condition} -> {_path}");
        }

        /// <summary>
        /// Assigns participant ID and condition without any per-session manual step.
        /// Pilot builds do not consume participant numbers.
        /// </summary>
        void AssignIdentity()
        {
            if (isPilotBuild)
            {
                _participantId = "PILOT_" + DateTime.UtcNow.ToString("HHmmss");
                _condition = ResolveCondition(0);
                return;
            }

            int n = PlayerPrefs.GetInt(CounterKey, 0) + 1;
            PlayerPrefs.SetInt(CounterKey, n);
            PlayerPrefs.Save();

            _participantId = $"P{n:D3}";
            _condition = ResolveCondition(n);
        }

        string ResolveCondition(int participantNumber)
        {
            if (conditionRotation == null || conditionRotation.Length == 0)
                return Sanitize(fixedCondition);

            int idx = Mathf.Max(0, participantNumber - 1) % conditionRotation.Length;
            return Sanitize(conditionRotation[idx]);
        }

        /// <summary>
        /// Manual override. Only needed if you re-run a participant or need to
        /// correct an assignment mid-study. Logs the change so it is auditable.
        /// </summary>
        public void SetParticipant(string participantId, string condition)
        {
            string oldId = _participantId;
            _participantId = Sanitize(participantId);
            _condition = Sanitize(condition);
            Log("participant_override", strValue: $"{oldId}->{_participantId}");
        }

        /// <summary>
        /// Resets the participant counter. Call only when starting a fresh study.
        /// </summary>
        public static void ResetParticipantCounter()
        {
            PlayerPrefs.DeleteKey(CounterKey);
            PlayerPrefs.Save();
        }

        // ---------------------------------------------------------------- logging

        public void Log(string evt,
                        string robotId = "",
                        string objectId = "",
                        float numValue = float.NaN,
                        string strValue = "")
        {
            if (_closed) return;

            Vector3 p = Vector3.zero; float yaw = 0f;
            if (headTransform != null)
            {
                p = headTransform.position;
                yaw = headTransform.eulerAngles.y;
            }

            var inv = CultureInfo.InvariantCulture;
            long unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int sessMs = Mathf.RoundToInt((Time.realtimeSinceStartup - _sessionStart) * 1000f);

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
            if (t >= _nextFlush) { Flush(); _nextFlush = t + flushInterval; }

            CheckBounds();
        }

        /// <summary>
        /// Detects a fall through world geometry. Without this, freefall samples are
        /// indistinguishable from normal movement in the CSV.
        /// </summary>
        void CheckBounds()
        {
            if (headTransform == null) return;

            bool below = headTransform.position.y < (floorY - fallThreshold);

            if (below && !_outOfBounds)
            {
                _outOfBounds = true;
                Log("out_of_bounds", numValue: headTransform.position.y);

                if (respawnPoint != null && rigTransform != null)
                {
                    var cc = rigTransform.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;
                    rigTransform.position = respawnPoint.position;
                    rigTransform.rotation = respawnPoint.rotation;
                    if (cc != null) cc.enabled = true;

                    Log("respawn", numValue: respawnPoint.position.y);
                }
            }
            else if (!below && _outOfBounds)
            {
                _outOfBounds = false;
            }
        }

        // ------------------------------------------------- app lifecycle boundaries

        void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                // Terminal on Quest. OnApplicationQuit is not guaranteed to run.
                Log("app_pause", numValue: 1f);
                Close();
            }
            else
            {
                Log("app_resume", numValue: 0f);
                Flush();
            }
        }

        void OnApplicationFocus(bool focused)
        {
            Log(focused ? "app_focus_gained" : "app_focus_lost", numValue: focused ? 1f : 0f);
            Flush();
        }

        void OnApplicationQuit() => Close();
        void OnDestroy() { if (Instance == this) Close(); }

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

// using System;
// using System.Collections.Generic;
// using System.Globalization;
// using System.IO;
// using System.Text;
// using UnityEngine;

// namespace LockedDown.Telemetry
// {
//     /// <summary>
//     /// Append-only CSV session logger. One instance, survives scene loads.
//     /// Buffers in memory, flushes on an interval and on every lifecycle boundary.
//     /// </summary>
//     public class TelemetryLogger : MonoBehaviour
//     {
//         public static TelemetryLogger Instance { get; private set; }

//         [SerializeField] float heartbeatInterval = 1.0f;
//         [SerializeField] float flushInterval     = 5.0f;
//         [SerializeField] Transform headTransform;   // assign XR Main Camera

//         const string Header =
//             "session_id,participant_id,condition,seq,t_unix_ms,t_session_ms,event," +
//             "robot_id,object_id,num_value,str_value,head_x,head_y,head_z,head_yaw";

//         string _sessionId, _participantId = "unset", _condition = "default";
//         string _path;
//         int _seq;
//         float _sessionStart, _nextHeartbeat, _nextFlush;
//         readonly List<string> _buffer = new List<string>(256);
//         bool _closed;

//         void Awake()
//         {
//             if (Instance != null && Instance != this) { Destroy(gameObject); return; }
//             Instance = this;
//             DontDestroyOnLoad(gameObject);

//             _sessionId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" +
//                          UnityEngine.Random.Range(1000, 9999);
//             _path = Path.Combine(Application.persistentDataPath, $"session_{_sessionId}.csv");
//             File.WriteAllText(_path, Header + "\n", Encoding.UTF8);

//             _sessionStart = Time.realtimeSinceStartup;
//             Log("session_start", strValue: Application.version);
//             Debug.Log($"[Telemetry] writing to {_path}");
//         }

//         public void SetParticipant(string participantId, string condition)
//         {
//             _participantId = Sanitize(participantId);
//             _condition     = Sanitize(condition);
//             Log("participant_set", strValue: _participantId);
//         }

//         public void Log(string evt,
//                         string robotId  = "",
//                         string objectId = "",
//                         float  numValue = float.NaN,
//                         string strValue = "")
//         {
//             if (_closed) return;

//             Vector3 p = Vector3.zero; float yaw = 0f;
//             if (headTransform != null)
//             {
//                 p   = headTransform.position;
//                 yaw = headTransform.eulerAngles.y;
//             }

//             var inv = CultureInfo.InvariantCulture;
//             long unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
//             int  sessMs = Mathf.RoundToInt((Time.realtimeSinceStartup - _sessionStart) * 1000f);

//             _buffer.Add(string.Join(",",
//                 _sessionId, _participantId, _condition,
//                 (++_seq).ToString(inv), unixMs.ToString(inv), sessMs.ToString(inv),
//                 Sanitize(evt), Sanitize(robotId), Sanitize(objectId),
//                 float.IsNaN(numValue) ? "" : numValue.ToString("F3", inv),
//                 Sanitize(strValue),
//                 p.x.ToString("F3", inv), p.y.ToString("F3", inv), p.z.ToString("F3", inv),
//                 yaw.ToString("F2", inv)));
//         }

//         void Update()
//         {
//             float t = Time.realtimeSinceStartup;
//             if (t >= _nextHeartbeat) { Log("heartbeat"); _nextHeartbeat = t + heartbeatInterval; }
//             if (t >= _nextFlush)     { Flush();          _nextFlush     = t + flushInterval; }
//         }

//         // --- lifecycle: these three hooks are the fix for the heartbeat gap + missing end row ---

//         void OnApplicationPause(bool paused)
//         {
//             Log(paused ? "app_pause" : "app_resume", numValue: paused ? 1f : 0f);
//             Flush();
//         }

//         void OnApplicationFocus(bool focused)
//         {
//             Log(focused ? "app_focus_gained" : "app_focus_lost", numValue: focused ? 1f : 0f);
//             Flush();
//         }

//         void OnApplicationQuit() => Close();
//         void OnDestroy()         { if (Instance == this) Close(); }

//         public void Close()
//         {
//             if (_closed) return;
//             Log("session_end", numValue: _seq);
//             Flush();
//             _closed = true;
//         }

//         public void Flush()
//         {
//             if (_buffer.Count == 0) return;
//             try
//             {
//                 File.AppendAllLines(_path, _buffer, Encoding.UTF8);
//                 _buffer.Clear();
//             }
//             catch (Exception e) { Debug.LogError($"[Telemetry] flush failed: {e.Message}"); }
//         }

//         static string Sanitize(string s) =>
//             string.IsNullOrEmpty(s) ? "" : s.Replace(',', ';').Replace('\n', ' ').Replace('\r', ' ');
//     }
// }
