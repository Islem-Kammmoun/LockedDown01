using System.Collections.Generic;
using LockedDown.Data;
using UnityEngine;
using UnityEngine.Events;

namespace LockedDown.Audit
{
    /// <summary>
    /// Runtime state for ONE robot. Drop on the SciFiDroid03Animated instance,
    /// assign a RobotProfile asset. Five robots = five of these, same prefab.
    /// </summary>
    public class RobotAuditStation : MonoBehaviour
    {
        [Header("Data")]
        public RobotProfile profile;

        [Header("Verification thresholds")]
        [Tooltip("Seconds the job-card must be held in view to count as READ.")]
        public float jobCardDwellSeconds = 2.0f;

        [Header("Events (hook UI / audio here)")]
        public UnityEvent<PermissionKey, Verdict> onDecisionCommitted;
        public UnityEvent onRobotResolved;

        private readonly Dictionary<string, Verdict> _verdicts = new Dictionary<string, Verdict>();
        private bool _resolved;

        // --- verification state, RECORDED not REWARDED ---
        private bool _jobCardRead;
        private readonly HashSet<string> _beamTestedKeys = new HashSet<string>();

        private void Start()
        {
            if (profile == null)
            {
                Debug.LogError($"[LockedDown] {name} has no RobotProfile assigned.", this);
                enabled = false;
            }
        }

        public void NotifyApproached()
        {
            AuditLogger.Instance?.Log(AuditEventType.RobotApproached, profile.robotId);
        }

        public void NotifyJobCardPulled()
        {
            AuditLogger.Instance?.Log(AuditEventType.JobCardPulled, profile.robotId);
        }

        /// <summary>Called by the card interactable once dwell threshold is met.</summary>
        public void NotifyJobCardRead(float dwellSeconds)
        {
            if (_jobCardRead) return;
            _jobCardRead = true;
            AuditLogger.Instance?.Log(AuditEventType.JobCardRead, profile.robotId,
                detail: $"dwell={dwellSeconds:F2}");
        }

        public void NotifyJobCardReturned()
        {
            AuditLogger.Instance?.Log(AuditEventType.JobCardReturned, profile.robotId);
        }

        public void NotifyKeyGrabbed(PermissionKey key)
        {
            AuditLogger.Instance?.Log(AuditEventType.KeyGrabbed, profile.robotId, key.keyId);
        }

        public void NotifyBeamTested(PermissionKey key)
        {
            _beamTestedKeys.Add(key.keyId);
            AuditLogger.Instance?.Log(AuditEventType.BeamTested, profile.robotId, key.keyId);
        }

        public void CommitDecision(PermissionKey key, Verdict verdict)
        {
            if (profile == null || key == null || verdict == Verdict.Undecided) return;

            bool isReversal = _verdicts.ContainsKey(key.keyId);
            Verdict previous = isReversal ? _verdicts[key.keyId] : Verdict.Undecided;
            _verdicts[key.keyId] = verdict;

            // Verification state at the MOMENT of decision. This is the whole point:
            // it is only meaningful because it is captured before the outcome is known.
            string verificationState =
                (_jobCardRead ? "card" : "nocard") + "+" +
                (_beamTestedKeys.Contains(key.keyId) ? "beam" : "nobeam");

            if (isReversal)
            {
                AuditLogger.Instance?.Log(AuditEventType.DecisionReversed, profile.robotId, key.keyId,
                    verdict.ToString(), $"from={previous};{verificationState}");
            }
            else
            {
                AuditLogger.Instance?.Log(AuditEventType.DecisionCommitted, profile.robotId, key.keyId,
                    verdict.ToString(), verificationState);
            }

            onDecisionCommitted?.Invoke(key, verdict);
            CheckResolved();
        }

        private void CheckResolved()
        {
            if (_resolved) return;
            foreach (var a in profile.assignments)
            {
                if (a.key == null) continue;
                if (!_verdicts.ContainsKey(a.key.keyId)) return;
            }
            _resolved = true;
            AuditLogger.Instance?.Log(AuditEventType.RobotResolved, profile.robotId,
                detail: $"correct={CountCorrect()}/{profile.assignments.Count}");
            onRobotResolved?.Invoke();
        }

        private int CountCorrect()
        {
            int n = 0;
            foreach (var a in profile.assignments)
            {
                if (a.key == null) continue;
                if (_verdicts.TryGetValue(a.key.keyId, out var v) && v == a.correctVerdict) n++;
            }
            return n;
        }

        /// <summary>Ground truth stays server-side of the player. Debrief screen only.</summary>
        public bool IsCorrect(PermissionKey key)
        {
            if (!profile.TryGetAssignment(key, out var a)) return false;
            return _verdicts.TryGetValue(key.keyId, out var v) && v == a.correctVerdict;
        }
    }
}
