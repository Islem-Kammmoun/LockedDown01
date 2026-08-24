using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace LockedDown.Audit
{
    /// <summary>
    /// Per-robot submit control. Terminal action for one station: placements are
    /// reversible until this fires, and locked after.
    ///
    /// Resolves its station via GetComponentInParent - no serialized references,
    /// so the whole robot can be duplicated without rewiring.
    ///
    /// Feedback hooks NEVER indicate correctness. onSubmitBlocked means "a key is
    /// still unassigned", not "wrong answer". Any correctness signal here would leak
    /// the DV to the participant and contaminate every robot after the first.
    /// </summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class SubmitLever : MonoBehaviour
    {
        [Header("Label")]
        [SerializeField] private TMPro.TextMeshPro label;
        [Header("Feedback (NO correctness signals)")]
        [Tooltip("Fired when submit succeeded. Lock visual, confirmation sound.")]
        public UnityEvent onSubmitted;
        [Tooltip("Fired when a key is still unassigned. Buzz, red flash, 'place all keys' label.")]
        public UnityEvent onSubmitBlocked;

        private XRSimpleInteractable _interactable;
        private RobotAuditStation _station;
        private bool _submitted;

        private void Awake()
        {
            _interactable = GetComponent<XRSimpleInteractable>();
            _station = GetComponentInParent<RobotAuditStation>();
            if (_station == null)
            {
                Debug.LogError($"[SubmitLever] {name}: no RobotAuditStation in parents. Disabling.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            _interactable.selectEntered.AddListener(HandlePulled);
            _interactable.hoverEntered.AddListener(HandleHover);
        }

        private void OnDisable()
        {
            _interactable.selectEntered.RemoveListener(HandlePulled);
            _interactable.hoverEntered.RemoveListener(HandleHover);
        }

        private void HandleHover(HoverEnterEventArgs args)
        {
            Debug.Log($"[SubmitLever] hovered by {args.interactorObject}");
        }

        private void HandlePulled(SelectEnterEventArgs args)
        {
            if (label != null) label.text = "PLACE ALL KEYS";
            if (_submitted) return;

            // Ask before acting, so we know which feedback to fire. The station
            // logs SubmitBlocked itself; this is only for the participant-facing cue.
            if (!_station.CanSubmit())
            {
                _station.SubmitDecisions();   // logs SubmitBlocked, changes nothing
                onSubmitBlocked?.Invoke();
                return;
            }

            _station.SubmitDecisions();
            _submitted = true;
            _interactable.enabled = false;    // terminal: no second pull
            onSubmitted?.Invoke();
        }
    }
}
