using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace LockedDown.Audit
{
    /// <summary>
    /// The job-card on a robot's chest. THE evidence object.
    ///
    /// Fires JobCardPulled on grab, and JobCardRead once the card has been held
    /// facing the headset, close enough, for dwellSeconds. That dwell gate is the
    /// difference between "picked it up" and "actually read it" - without it, a
    /// participant who grabs and immediately drops the card would register as
    /// having verified.
    ///
    /// Station and card text are both resolved automatically from the parent
    /// robot. No per-robot wiring.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class JobCardInteractable : MonoBehaviour
    {
        [Tooltip("Leave empty - resolved from the parent robot at Awake.")]
        public RobotAuditStation station;

        [Tooltip("Text object on the card face. Filled from the robot profile.")]
        public TMP_Text cardText;

        [Header("Read detection")]
        [Tooltip("Dot product between card facing and head-to-card direction.")]
        [Range(0.3f, 0.99f)] public float facingThreshold = 0.6f;

        [Tooltip("Max metres from head for the card to count as being read.")]
        public float maxReadDistance = 0.8f;

        private XRGrabInteractable _grab;
        private Transform _head;
        private bool _held;
        private float _dwell;
        private bool _reported;

        private void Awake()
        {
            _grab = GetComponent<XRGrabInteractable>();

            if (station == null)
                station = GetComponentInParent<RobotAuditStation>();

            if (cardText != null && station != null && station.profile != null)
                cardText.text = station.profile.jobCardText;
        }

        private Transform Head()
        {
            if (_head == null && Camera.main != null)
                _head = Camera.main.transform;
            return _head;
        }

        private void OnEnable()
        {
            _grab.selectEntered.AddListener(OnGrabbed);
            _grab.selectExited.AddListener(OnReleased);
        }

        private void OnDisable()
        {
            _grab.selectEntered.RemoveListener(OnGrabbed);
            _grab.selectExited.RemoveListener(OnReleased);
        }

        private void OnGrabbed(SelectEnterEventArgs _)
        {
            _held = true;
            station?.NotifyJobCardPulled();
        }

        private void OnReleased(SelectExitEventArgs _)
        {
            _held = false;
            _dwell = 0f;
            station?.NotifyJobCardReturned();
        }

        private void Update()
        {
            if (!_held || _reported || station == null) return;

            var head = Head();
            if (head == null) return;

            Vector3 toCard = transform.position - head.position;
            if (toCard.magnitude > maxReadDistance) { _dwell = 0f; return; }

            // Accept either face. A Quad's normal direction depends on its
            // rotation, and getting the sign wrong silently kills the DV.
            float facing = Mathf.Abs(Vector3.Dot(transform.forward, toCard.normalized));
            if (facing < facingThreshold) { _dwell = 0f; return; }

            _dwell += Time.unscaledDeltaTime;
            if (_dwell >= station.jobCardDwellSeconds)
            {
                _reported = true;
                station.NotifyJobCardRead(_dwell);
            }
        }

    }
}