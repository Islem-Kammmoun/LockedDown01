using LockedDown.Data;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace LockedDown.Audit
{
    /// <summary>
    /// Hooks a grabbable key into the audit event stream.
    ///
    /// Subscribes to XRGrabInteractable in code - NO inspector event wiring.
    /// Inspector UnityEvents have to be re-wired per instance and fail silently
    /// when they are not; with 3-6 keys x 3 events that is a guaranteed source of
    /// missing rows that looks like a logging bug.
    /// </summary>
    [RequireComponent(typeof(PermissionKeyView))]
    [RequireComponent(typeof(XRGrabInteractable))]
    public class KeyInteractable : MonoBehaviour
    {
        [Tooltip("The robot this key currently belongs to.")]
        public RobotAuditStation owningStation;

        [Header("Beam test")]
        [Tooltip("The cabinet this key unlocks. Beam draws to here.")]
        public Transform targetCabinet;
        public LineRenderer beam;
        public float beamDuration = 1.5f;

        private PermissionKeyView _view;
        private XRGrabInteractable _grab;
        private float _beamOffAt;

        // private PermissionKey Permission => _view != null ? _view.permission : null;
        public PermissionKey Permission => _view != null ? _view.permission : null;
        private void Awake()
        {
            _view = GetComponent<PermissionKeyView>();
            _grab = GetComponent<XRGrabInteractable>();
            if (owningStation == null)
                owningStation = GetComponentInParent<RobotAuditStation>();
            if (beam != null) beam.enabled = false;

            if (targetCabinet == null)
            {
                var lockView = PermissionLockView.Find(Permission);
                if (lockView != null) targetCabinet = lockView.BeamAnchor;
            }
        }

        private void OnEnable()
        {
            _grab.selectEntered.AddListener(HandleGrabbed);
            _grab.selectExited.AddListener(HandleReleased);
            _grab.activated.AddListener(HandleActivated);
        }

        private void Start()
        {
            if (targetCabinet == null && Permission != null)
            {
                var lockView = PermissionLockView.Find(Permission);
                if (lockView != null) targetCabinet = lockView.BeamAnchor;
            }
        }

        private void OnDisable()
        {
            _grab.selectEntered.RemoveListener(HandleGrabbed);
            _grab.selectExited.RemoveListener(HandleReleased);
            _grab.activated.RemoveListener(HandleActivated);
        }

        private void HandleGrabbed(SelectEnterEventArgs args)
        {
            // A socket selecting the key is a DECISION, not a participant grab.
            // Without this filter every commit also logs a phantom KeyGrabbed.
            if (args.interactorObject is XRSocketInteractor) return;
            if (!Ready()) return;
            owningStation.NotifyKeyGrabbed(Permission);
        }

        private void HandleReleased(SelectExitEventArgs args)
        {
            if (args.interactorObject is XRSocketInteractor) return;
            if (!Ready()) return;
            AuditLogger.Instance?.Log(AuditEventType.KeyReleased,
                owningStation.profile.robotId, Permission.keyId);
        }


        private void HandleActivated(ActivateEventArgs _) => FireBeam();

        /// <summary>
        /// Player raises the key and fires the reach-beam at its cabinet.
        /// Logged as a first-class event: this is a DISTINCT verification pathway
        /// from reading the job-card and must be separable in analysis.
        /// </summary>
        public void FireBeam()
        {
            if (!Ready()) return;

            // No cabinet assigned = the beam mechanic is not built for this key.
            // Do NOT log a verification event the participant could not have
            // meaningfully performed.
            if (targetCabinet == null) return;

            owningStation.NotifyBeamTested(Permission);


            if (beam == null || targetCabinet == null) return;
            beam.positionCount = 2;
            beam.SetPosition(0, transform.position);
            beam.SetPosition(1, targetCabinet.position);
            beam.startColor = beam.endColor = Permission.keyColor;
            beam.enabled = true;
            _beamOffAt = Time.unscaledTime + beamDuration;
        }

        private bool Ready()
        {
            if (owningStation == null)
            {
                Debug.LogError($"[Audit] {name}: owningStation not assigned.", this);
                return false;
            }
            if (Permission == null)
            {
                Debug.LogError($"[Audit] {name}: PermissionKeyView.permission not assigned.", this);
                return false;
            }
            return true;
        }

        private void Update()
        {
            if (beam != null && beam.enabled)
            {
                beam.SetPosition(0, transform.position);
                beam.SetPosition(1, targetCabinet != null ? targetCabinet.position : transform.position);
                if (Time.unscaledTime >= _beamOffAt) beam.enabled = false;
            }
        }
    }
}