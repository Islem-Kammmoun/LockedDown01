using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace LockedDown.Audit
{
    /// <summary>
    /// Bookkeeping component for a container of permission keys — the keep ring or the
    /// revoke bin. NOT an interactor: no collider, no attach transform, never selects.
    ///
    /// The XRSocketInteractors live on hand-placed child Anchor objects and do all the
    /// snapping. This component adopts them at Awake and supplies the two things a socket
    /// cannot know on its own: which robot it belongs to, and which container it is.
    ///
    /// Capacity must be identical on keep and revoke across every robot, so the furniture
    /// never communicates how many keys belong where.
    /// Placement is reversible until the SubmitLever fires decision_submitted.
    /// </summary>
    [DisallowMultipleComponent]
    public class KeyContainer : MonoBehaviour, IXRSelectFilter
    {

        public bool canProcess => isActiveAndEnabled;

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            var key = interactable.transform.GetComponent<KeyInteractable>();
            if (key == null) return false;
            return key.owningStation == station;
        }
        public enum ContainerKind { Keep, Revoke }

        [Header("Identity")]
        [SerializeField] private ContainerKind kind = ContainerKind.Keep;

        [Header("Capacity")]
        [Tooltip("Expected anchor count. Must match on keep AND revoke, on EVERY robot, " +
                 "and be >= the largest key count in the study. Anchors are hand-placed; " +
                 "this is a check, not a generator.")]
        [SerializeField] private int expectedAnchorCount = 4;

        private readonly List<XRSocketInteractor> anchors = new List<XRSocketInteractor>();
        private RobotAuditStation station;

        public ContainerKind Kind => kind;
        public int Capacity => anchors.Count;

        private void Awake()
        {
            station = GetComponentInParent<RobotAuditStation>();
            if (station == null)
            {
                Debug.LogError($"[KeyContainer] {name}: no RobotAuditStation in parents. Disabling.", this);
                enabled = false;
                return;
            }

            AdoptAnchors();
            Validate();
        }

        private void OnDestroy()
        {
            foreach (var socket in anchors)
            {
                if (socket == null) continue;
                socket.selectEntered.RemoveListener(OnSocketSelectEntered);
                socket.selectExited.RemoveListener(OnSocketSelectExited);
                socket.selectFilters.Remove(this);
            }
        }

        private void AdoptAnchors()
        {
            // includeInactive: true so a disabled anchor is still counted and still errors loudly.
            var found = GetComponentsInChildren<XRSocketInteractor>(true);
            foreach (var socket in found)
            {
                // One key per anchor. Never let a single socket hold several keys:
                // XRSocketInteractor drops the extra selection without logging it.
                socket.selectEntered.AddListener(OnSocketSelectEntered);
                socket.selectExited.AddListener(OnSocketSelectExited);

                // Reject keys belonging to another robot before they can snap.
                socket.selectFilters.Add(this);

                anchors.Add(socket);
            }
        }
        private void Validate()
        {
            if (anchors.Count == 0)
            {
                Debug.LogError($"[KeyContainer] {name}: found 0 XRSocketInteractor in children. " +
                               "The hand-placed Anchor objects are missing the component.", this);
                return;
            }

            if (anchors.Count != expectedAnchorCount)
            {
                Debug.LogError($"[KeyContainer] {name}: found {anchors.Count} anchors, expected " +
                               $"{expectedAnchorCount}. Capacity must be uniform across all containers " +
                               "or the furniture leaks the answer.", this);
            }

            int keyCount = station.GetComponentsInChildren<KeyInteractable>(true).Length;
            if (keyCount > anchors.Count)
            {
                Debug.LogError($"[KeyContainer] {station.name} has {keyCount} keys but only " +
                               $"{anchors.Count} anchors. Participant cannot express 'keep all'. " +
                               "Build is invalid.", this);
            }
            else if (keyCount == 0)
            {
                Debug.LogWarning($"[KeyContainer] {station.name}: no KeyInteractable found in children. " +
                                 "Keys are probably not parented under the station.", this);
            }

            // A container scaled away from 1 also scales every child socket's trigger volume,
            // which silently widens the snap radius and can make anchors overlap.
            Vector3 s = transform.lossyScale;
            if (Mathf.Abs(s.x - 1f) > 0.01f || Mathf.Abs(s.y - 1f) > 0.01f || Mathf.Abs(s.z - 1f) > 0.01f)
            {
                Debug.LogWarning($"[KeyContainer] {name}: lossyScale is {s}. Child socket colliders " +
                                 "are scaled by this. Verify anchor trigger radius in world units.", this);
            }
        }

        private void OnSocketSelectEntered(SelectEnterEventArgs args)
        {
            var key = args.interactableObject.transform.GetComponent<KeyInteractable>();
            if (key == null) return;
            station.NotifyKeyPlaced(key, kind);
        }

        private void OnSocketSelectExited(SelectExitEventArgs args)
        {
            var key = args.interactableObject.transform.GetComponent<KeyInteractable>();
            if (key == null) return;
            station.NotifyKeyRetracted(key, kind);
        }

        /// <summary>Keys currently held by this container. Read by the SubmitLever at submit time.</summary>
        public List<KeyInteractable> GetContainedKeys()
        {
            var result = new List<KeyInteractable>();
            foreach (var socket in anchors)
            {
                if (socket == null || !socket.hasSelection) continue;
                foreach (var interactable in socket.interactablesSelected)
                {
                    var key = interactable.transform.GetComponent<KeyInteractable>();
                    if (key != null) result.Add(key);
                }
            }
            return result;
        }

        /// <summary>True when this container holds the given key. Used to detect unassigned keys.</summary>
        public bool Contains(KeyInteractable key)
        {
            foreach (var socket in anchors)
            {
                if (socket == null || !socket.hasSelection) continue;
                foreach (var interactable in socket.interactablesSelected)
                {
                    if (interactable.transform.GetComponent<KeyInteractable>() == key) return true;
                }
            }
            return false;
        }
    }
}
