using LockedDown.Data;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace LockedDown.Audit
{
    /// <summary>
    /// A physical commitment point. Socketing a key here commits that verdict.
    ///
    /// The station is read from the KEY, not configured here - one shared revoke
    /// bin therefore serves all five robots with zero per-robot wiring, and a key
    /// always logs against the robot it came from.
    /// </summary>
    [RequireComponent(typeof(XRSocketInteractor))]
    public class DecisionBin : MonoBehaviour
    {
        [Tooltip("Which verdict placing a key here commits.")]
        public Verdict verdict = Verdict.Revoke;

        [Header("Feedback")]
        public AudioSource clunk;

        private XRSocketInteractor _socket;

        private void Awake() => _socket = GetComponent<XRSocketInteractor>();

        private void OnEnable() => _socket.selectEntered.AddListener(OnSocketed);
        private void OnDisable() => _socket.selectEntered.RemoveListener(OnSocketed);

        private void OnSocketed(SelectEnterEventArgs args)
        {
            var go = args.interactableObject.transform.gameObject;

            var view = go.GetComponent<PermissionKeyView>();
            var key = go.GetComponent<KeyInteractable>();

            // Not a permission key - a test cube, a prop. Ignore silently rather
            // than logging a row that looks like participant behaviour.
            if (view == null || key == null || view.permission == null) return;

            if (key.owningStation == null)
            {
                Debug.LogError($"[Audit] {go.name} socketed into {name} but has no owning station.", this);
                return;
            }

            key.owningStation.CommitDecision(view.permission, verdict);
            if (clunk != null) clunk.Play();
        }
    }
}
