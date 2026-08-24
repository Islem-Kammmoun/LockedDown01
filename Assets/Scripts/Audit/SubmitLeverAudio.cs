using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace LockedDown.Audit
{
    /// <summary>
    /// Audio feedback for the submit lever.
    ///
    /// Preferred wiring: hook OnSubmitAccepted / OnSubmitBlocked to the matching
    /// UnityEvents on RobotAuditStation. Those fire on OUTCOME, not on contact.
    ///
    /// DESIGN CONSTRAINT: acceptedClip must be a single clip used for every
    /// submission. onSubmitAccepted fires before any decision is committed or
    /// scored, so a correctness leak is structurally impossible — as long as you
    /// never branch this on CountCorrect().
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class SubmitLeverAudio : MonoBehaviour
    {
        [Header("Clips")]
        [Tooltip("Plays when a submit is ACCEPTED. One clip, always. Never varies.")]
        public AudioClip acceptedClip;

        [Tooltip("Plays when a submit is REJECTED for unassigned keys. Must be clearly " +
                 "different from acceptedClip — a soft double-blip, not an error buzzer.")]
        public AudioClip blockedClip;

        [Header("Optional: raw lever contact")]
        [Tooltip("Plays the instant the lever is pulled, before the outcome is known. " +
                 "Leave EMPTY unless you want a mechanical clunk — a contact sound that " +
                 "resembles a confirmation will read as one.")]
        public AudioClip leverContactClip;

        [Tooltip("XRSimpleInteractable on the lever. Only needed if leverContactClip is set.")]
        public XRSimpleInteractable interactable;

        [Header("Levels")]
        [Range(0f, 1f)] public float volume = 0.8f;

        private AudioSource _source;

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 1f;   // 3D — the sound belongs to the lever, not the head
            _source.dopplerLevel = 0f;

            if (interactable == null)
                interactable = GetComponentInParent<XRSimpleInteractable>();
        }

        private void OnEnable()
        {
            if (leverContactClip != null && interactable != null)
                interactable.selectEntered.AddListener(OnLeverContact);
        }

        private void OnDisable()
        {
            if (interactable != null)
                interactable.selectEntered.RemoveListener(OnLeverContact);
        }

        private void OnLeverContact(SelectEnterEventArgs _) => Play(leverContactClip);

        /// <summary>Hook to RobotAuditStation.onSubmitAccepted.</summary>
        public void OnSubmitAccepted() => Play(acceptedClip);

        /// <summary>Hook to RobotAuditStation.onSubmitBlocked.</summary>
        public void OnSubmitBlocked() => Play(blockedClip);

        private void Play(AudioClip clip)
        {
            if (clip == null || _source == null) return;
            _source.PlayOneShot(clip, volume);
        }
    }
}
