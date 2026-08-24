using System.Collections.Generic;
using LockedDown.Data;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace LockedDown.Audit
{
    /// <summary>
    /// Runtime state for ONE robot. Drop on the SciFiDroid03Animated instance,
    /// assign a RobotProfile asset. Five robots = five of these, same prefab.
    ///
    /// NOTE ON KEY LOOKUP: XRGrabInteractable re-parents to the scene root on grab,
    /// so GetComponentsInChildren&lt;KeyInteractable&gt;() is UNSAFE after the first grab.
    /// The key roster is captured once in Start() and used for the rest of the session.
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

        [Tooltip("Fires when a submit is ACCEPTED. Hook a neutral lever latch / light here. " +
                 "Must look identical regardless of correctness.")]
        public UnityEvent onSubmitAccepted;

        [Tooltip("Fires when a submit is REJECTED because keys are unassigned. " +
                 "Neutral 'not ready' cue only — never reveals which key or whether it is right.")]
        public UnityEvent onSubmitBlocked;

        private readonly Dictionary<string, Verdict> _verdicts = new Dictionary<string, Verdict>();
        private bool _resolved;

        // Captured in Start(), before any grab can re-parent a key out of this hierarchy.
        private readonly List<KeyInteractable> _keys = new List<KeyInteractable>();
        private readonly List<XRSocketInteractor> _sockets = new List<XRSocketInteractor>();
        private KeyContainer _keep;
        private KeyContainer _revoke;

        // --- verification state, RECORDED not REWARDED ---
        private bool _jobCardRead;
        private readonly HashSet<string> _beamTestedKeys = new HashSet<string>();

        /// <summary>True once RobotResolved has been logged. Terminal. Read by key/container scripts.</summary>
        public bool IsResolved => _resolved;

        public string RobotId => profile != null ? profile.robotId : "unknown";

        private void Start()
        {
            if (profile == null)
            {
                Debug.LogError($"[LockedDown] {name} has no RobotProfile assigned.", this);
                enabled = false;
                return;
            }

            CaptureRoster();
        }

        /// <summary>
        /// One-time hierarchy scan. Everything after this uses the cached lists.
        /// </summary>
        private void CaptureRoster()
        {
            _keys.Clear();
            foreach (var k in GetComponentsInChildren<KeyInteractable>(true))
            {
                if (k.Permission == null)
                {
                    Debug.LogError($"[LockedDown] {name}: KeyInteractable '{k.name}' has no PermissionKey.", k);
                    continue;
                }
                _keys.Add(k);
            }

            _sockets.Clear();
            _sockets.AddRange(GetComponentsInChildren<XRSocketInteractor>(true));

            _keep = null;
            _revoke = null;
            foreach (var c in GetComponentsInChildren<KeyContainer>(true))
            {
                if (c.Kind == KeyContainer.ContainerKind.Keep) _keep = c;
                else _revoke = c;
            }

            // Fail loudly at scene load, not silently at submit time.
            if (_keep == null || _revoke == null)
                Debug.LogError($"[LockedDown] {name}: missing a Keep or Revoke KeyContainer.", this);

            int expected = profile.assignments != null ? profile.assignments.Count : 0;
            if (_keys.Count != expected)
            {
                Debug.LogError(
                    $"[LockedDown] {name}: found {_keys.Count} keys in hierarchy but profile " +
                    $"'{profile.name}' declares {expected} assignments. Submit will be blocked.", this);
            }
        }

        // ---------------------------------------------------------------------
        // Input notifications. All are hard-guarded: after RobotResolved nothing
        // this robot produces can reach the CSV, whatever the physical lock does.
        // ---------------------------------------------------------------------

        public void NotifyApproached()
        {
            if (_resolved) return;
            AuditLogger.Instance?.Log(AuditEventType.RobotApproached, profile.robotId);
        }

        public void NotifyJobCardPulled()
        {
            if (_resolved) return;
            AuditLogger.Instance?.Log(AuditEventType.JobCardPulled, profile.robotId);
        }

        /// <summary>Called by the card interactable once dwell threshold is met.</summary>
        public void NotifyJobCardRead(float dwellSeconds)
        {
            if (_resolved || _jobCardRead) return;
            _jobCardRead = true;
            AuditLogger.Instance?.Log(AuditEventType.JobCardRead, profile.robotId,
                detail: $"dwell={dwellSeconds:F2}");
        }

        public void NotifyJobCardReturned()
        {
            if (_resolved) return;
            AuditLogger.Instance?.Log(AuditEventType.JobCardReturned, profile.robotId);
        }

        public void NotifyKeyGrabbed(PermissionKey key)
        {
            if (_resolved || key == null) return;
            AuditLogger.Instance?.Log(AuditEventType.KeyGrabbed, profile.robotId, key.keyId);
        }

        public void NotifyBeamTested(PermissionKey key)
        {
            if (_resolved || key == null) return;
            _beamTestedKeys.Add(key.keyId);
            AuditLogger.Instance?.Log(AuditEventType.BeamTested, profile.robotId, key.keyId);
        }

        public void NotifyKeyPlaced(KeyInteractable key, KeyContainer.ContainerKind kind)
        {
            if (_resolved || key == null || key.Permission == null) return;
            AuditLogger.Instance?.Log(AuditEventType.KeyPlaced, profile.robotId, key.Permission.keyId,
                detail: kind.ToString().ToLowerInvariant());
        }

        public void NotifyKeyRetracted(KeyInteractable key, KeyContainer.ContainerKind kind)
        {
            if (_resolved || key == null || key.Permission == null) return;
            AuditLogger.Instance?.Log(AuditEventType.KeyRetracted, profile.robotId, key.Permission.keyId,
                detail: kind.ToString().ToLowerInvariant());
        }

        // ---------------------------------------------------------------------
        // Decisions
        // ---------------------------------------------------------------------

        public void CommitDecision(PermissionKey key, Verdict verdict)
        {
            if (_resolved) return;
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

        // ---------------------------------------------------------------------
        // Submit
        // ---------------------------------------------------------------------

        /// <summary>
        /// True only if every declared key is sitting in exactly one container.
        /// Cannot pass vacuously: the roster count is checked against the profile.
        /// </summary>
        public bool CanSubmit()
        {
            if (_resolved) return false;
            if (_keep == null || _revoke == null) return false;
            if (profile.assignments == null) return false;
            if (_keys.Count != profile.assignments.Count) return false;
            if (_keys.Count == 0) return false;

            foreach (var k in _keys)
            {
                bool inKeep = _keep.Contains(k);
                bool inRevoke = _revoke.Contains(k);
                if (inKeep == inRevoke) return false; // unplaced, or somehow in both
            }
            return true;
        }

        public void SubmitDecisions()
        {
            if (_resolved) return;

            if (_keep == null || _revoke == null)
            {
                Debug.LogError($"[LockedDown] {name}: missing a KeyContainer.", this);
                return;
            }

            if (profile.assignments == null || _keys.Count != profile.assignments.Count || _keys.Count == 0)
            {
                Debug.LogError($"[LockedDown] {name}: key roster ({_keys.Count}) does not match " +
                               $"profile assignments. Refusing to submit.", this);
                AuditLogger.Instance?.Log(AuditEventType.SubmitBlocked, profile.robotId,
                    detail: $"roster_mismatch:{_keys.Count}");
                onSubmitBlocked?.Invoke();
                return;
            }

            // Every key must be assigned. An unplaced key is an uninterpretable third state.
            bool blocked = false;
            foreach (var k in _keys)
            {
                bool inKeep = _keep.Contains(k);
                bool inRevoke = _revoke.Contains(k);
                if (inKeep == inRevoke)
                {
                    AuditLogger.Instance?.Log(AuditEventType.SubmitBlocked, profile.robotId,
                        k.Permission.keyId, detail: inKeep ? "in_both" : "unassigned");
                    blocked = true;
                }
            }
            if (blocked)
            {
                onSubmitBlocked?.Invoke();
                return;
            }

            AuditLogger.Instance?.Log(AuditEventType.DecisionSubmitted, profile.robotId);
            onSubmitAccepted?.Invoke();

            foreach (var k in _keep.GetContainedKeys())
                CommitDecision(k.Permission, Verdict.Keep);
            foreach (var k in _revoke.GetContainedKeys())
                CommitDecision(k.Permission, Verdict.Revoke);

            // Belt and braces: if the profile declared a key that never reached a
            // container, CheckResolved() would never fire. Force terminal state.
            if (!_resolved)
            {
                _resolved = true;
                AuditLogger.Instance?.Log(AuditEventType.RobotResolved, profile.robotId,
                    detail: $"correct={CountCorrect()}/{profile.assignments.Count}");
                onRobotResolved?.Invoke();
            }

            LockKeys();
        }

        /// <summary>
        /// Submit is terminal. Keys stay visible where the participant left them but
        /// stop responding, so no event can post-date the decision it produced.
        /// </summary>
        private void LockKeys()
        {
            foreach (var k in _keys)
            {
                if (k == null) continue;

                var grab = k.GetComponent<XRGrabInteractable>();
                if (grab == null)
                {
                    // Component is not on the same GameObject — search the key subtree.
                    grab = k.GetComponentInChildren<XRGrabInteractable>(true);
                }
                if (grab == null)
                {
                    Debug.LogWarning($"[LockedDown] {name}: no XRGrabInteractable found on key " +
                                     $"'{k.name}'. It will remain interactive.", k);
                    continue;
                }

                Transform t = grab.transform;
                Vector3 pos = t.position;
                Quaternion rot = t.rotation;

                // Disabling unregisters from the interaction manager, which cancels any
                // active selection. That triggers Drop(), which restores the parent and
                // re-enables gravity, so the pose and rigidbody are fixed up afterwards.
                grab.enabled = false;

                var rb = grab.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.useGravity = false;
                    rb.isKinematic = true;
                }

                t.SetPositionAndRotation(pos, rot);
            }

            // Stop the sockets hovering, highlighting, or accepting anything else.
            foreach (var s in _sockets)
            {
                if (s == null) continue;
                s.socketActive = false;
                s.enabled = false;
            }
        }

        // ---------------------------------------------------------------------
        // Scoring — debrief only, never surfaced in-world
        // ---------------------------------------------------------------------

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
// using System.Collections.Generic;
// using LockedDown.Data;
// using UnityEngine;
// using UnityEngine.Events;

// namespace LockedDown.Audit
// {
//     /// <summary>
//     /// Runtime state for ONE robot. Drop on the SciFiDroid03Animated instance,
//     /// assign a RobotProfile asset. Five robots = five of these, same prefab.
//     /// </summary>
//     public class RobotAuditStation : MonoBehaviour
//     {
//         [Header("Data")]
//         public RobotProfile profile;

//         [Header("Verification thresholds")]
//         [Tooltip("Seconds the job-card must be held in view to count as READ.")]
//         public float jobCardDwellSeconds = 2.0f;

//         [Header("Events (hook UI / audio here)")]
//         public UnityEvent<PermissionKey, Verdict> onDecisionCommitted;
//         public UnityEvent onRobotResolved;

//         private readonly Dictionary<string, Verdict> _verdicts = new Dictionary<string, Verdict>();
//         private bool _resolved;

//         // --- verification state, RECORDED not REWARDED ---
//         private bool _jobCardRead;
//         private readonly HashSet<string> _beamTestedKeys = new HashSet<string>();

//         private void Start()
//         {
//             if (profile == null)
//             {
//                 Debug.LogError($"[LockedDown] {name} has no RobotProfile assigned.", this);
//                 enabled = false;
//             }
//         }

//         public void NotifyApproached()
//         {
//             AuditLogger.Instance?.Log(AuditEventType.RobotApproached, profile.robotId);
//         }

//         public void NotifyJobCardPulled()
//         {
//             AuditLogger.Instance?.Log(AuditEventType.JobCardPulled, profile.robotId);
//         }

//         /// <summary>Called by the card interactable once dwell threshold is met.</summary>
//         public void NotifyJobCardRead(float dwellSeconds)
//         {
//             if (_jobCardRead) return;
//             _jobCardRead = true;
//             AuditLogger.Instance?.Log(AuditEventType.JobCardRead, profile.robotId,
//                 detail: $"dwell={dwellSeconds:F2}");
//         }

//         public void NotifyJobCardReturned()
//         {
//             AuditLogger.Instance?.Log(AuditEventType.JobCardReturned, profile.robotId);
//         }

//         public void NotifyKeyGrabbed(PermissionKey key)
//         {
//             AuditLogger.Instance?.Log(AuditEventType.KeyGrabbed, profile.robotId, key.keyId);
//         }

//         public void NotifyBeamTested(PermissionKey key)
//         {
//             _beamTestedKeys.Add(key.keyId);
//             AuditLogger.Instance?.Log(AuditEventType.BeamTested, profile.robotId, key.keyId);
//         }

//         public void CommitDecision(PermissionKey key, Verdict verdict)
//         {
//             if (profile == null || key == null || verdict == Verdict.Undecided) return;

//             bool isReversal = _verdicts.ContainsKey(key.keyId);
//             Verdict previous = isReversal ? _verdicts[key.keyId] : Verdict.Undecided;
//             _verdicts[key.keyId] = verdict;

//             // Verification state at the MOMENT of decision. This is the whole point:
//             // it is only meaningful because it is captured before the outcome is known.
//             string verificationState =
//                 (_jobCardRead ? "card" : "nocard") + "+" +
//                 (_beamTestedKeys.Contains(key.keyId) ? "beam" : "nobeam");

//             if (isReversal)
//             {
//                 AuditLogger.Instance?.Log(AuditEventType.DecisionReversed, profile.robotId, key.keyId,
//                     verdict.ToString(), $"from={previous};{verificationState}");
//             }
//             else
//             {
//                 AuditLogger.Instance?.Log(AuditEventType.DecisionCommitted, profile.robotId, key.keyId,
//                     verdict.ToString(), verificationState);
//             }

//             onDecisionCommitted?.Invoke(key, verdict);
//             CheckResolved();
//         }

//         private void CheckResolved()
//         {
//             if (_resolved) return;
//             foreach (var a in profile.assignments)
//             {
//                 if (a.key == null) continue;
//                 if (!_verdicts.ContainsKey(a.key.keyId)) return;
//             }
//             _resolved = true;
//             AuditLogger.Instance?.Log(AuditEventType.RobotResolved, profile.robotId,
//                 detail: $"correct={CountCorrect()}/{profile.assignments.Count}");
//             onRobotResolved?.Invoke();
//         }

//         public void NotifyKeyPlaced(KeyInteractable key, KeyContainer.ContainerKind kind)
//         {
//             if (key == null || key.Permission == null) return;
//             AuditLogger.Instance?.Log(AuditEventType.KeyPlaced, profile.robotId, key.Permission.keyId,
//                 detail: kind.ToString().ToLowerInvariant());
//         }

//         public void NotifyKeyRetracted(KeyInteractable key, KeyContainer.ContainerKind kind)
//         {
//             if (key == null || key.Permission == null) return;
//             AuditLogger.Instance?.Log(AuditEventType.KeyRetracted, profile.robotId, key.Permission.keyId,
//                 detail: kind.ToString().ToLowerInvariant());
//         }

//         public bool CanSubmit()
//         {
//             if (_resolved) return false;

//             KeyContainer keep = null, revoke = null;
//             foreach (var c in GetComponentsInChildren<KeyContainer>(true))
//             {
//                 if (c.Kind == KeyContainer.ContainerKind.Keep) keep = c;
//                 else revoke = c;
//             }
//             if (keep == null || revoke == null) return false;

//             foreach (var k in GetComponentsInChildren<KeyInteractable>(true))
//             {
//                 if (k.Permission == null) continue;
//                 if (!keep.Contains(k) && !revoke.Contains(k)) return false;
//             }
//             return true;
//         }

//         public void SubmitDecisions()
//         {
//             if (_resolved) return;

//             KeyContainer keep = null, revoke = null;
//             foreach (var c in GetComponentsInChildren<KeyContainer>(true))
//             {
//                 if (c.Kind == KeyContainer.ContainerKind.Keep) keep = c;
//                 else revoke = c;
//             }
//             if (keep == null || revoke == null)
//             {
//                 Debug.LogError($"[LockedDown] {name}: missing a KeyContainer.", this);
//                 return;
//             }

//             // Every key must be assigned. An unplaced key is an uninterpretable third state.
//             foreach (var k in GetComponentsInChildren<KeyInteractable>(true))
//             {
//                 if (k.Permission == null) continue;
//                 if (!keep.Contains(k) && !revoke.Contains(k))
//                 {
//                     AuditLogger.Instance?.Log(AuditEventType.SubmitBlocked, profile.robotId,
//                         k.Permission.keyId, detail: "unassigned");
//                     return;
//                 }
//             }

//             AuditLogger.Instance?.Log(AuditEventType.DecisionSubmitted, profile.robotId);

//             foreach (var k in keep.GetContainedKeys())
//                 CommitDecision(k.Permission, Verdict.Keep);
//             foreach (var k in revoke.GetContainedKeys())
//                 CommitDecision(k.Permission, Verdict.Revoke);

//             // Submit is terminal. Keys stay visible in their containers but stop
//             // responding, so no event can post-date the decision it produced.
//             foreach (var k in GetComponentsInChildren<KeyInteractable>(true))
//             {
//                 var grab = k.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
//                 if (grab != null) grab.enabled = false;
//             }

//         }
//         private int CountCorrect()
//         {
//             int n = 0;
//             foreach (var a in profile.assignments)
//             {
//                 if (a.key == null) continue;
//                 if (_verdicts.TryGetValue(a.key.keyId, out var v) && v == a.correctVerdict) n++;
//             }
//             return n;
//         }

//         /// <summary>Ground truth stays server-side of the player. Debrief screen only.</summary>
//         public bool IsCorrect(PermissionKey key)
//         {
//             if (!profile.TryGetAssignment(key, out var a)) return false;
//             return _verdicts.TryGetValue(key.keyId, out var v) && v == a.correctVerdict;
//         }
//     }
// }
