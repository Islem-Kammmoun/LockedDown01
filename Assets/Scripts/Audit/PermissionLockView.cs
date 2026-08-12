using LockedDown.Data;
using UnityEngine;
using UnityEngine.UI;

namespace LockedDown.Audit
{
    /// <summary>
    /// A cabinet/lock. Tints itself and labels itself from the SAME PermissionKey
    /// asset the matching key uses, so key and lock can never drift apart.
    ///
    /// Also acts as the registry that KeyInteractable uses to find its beam
    /// target - no manual per-key cabinet assignment.
    /// </summary>
    [ExecuteAlways]
    public class PermissionLockView : MonoBehaviour
    {
        [Tooltip("The permission this cabinet is unlocked by.")]
        public PermissionKey permission;

        [Tooltip("Mesh renderers to tint. Leave empty to auto-collect children.")]
        public Renderer[] targetRenderers;

        [Tooltip("URP/Lit uses _BaseColor. Built-In uses _Color.")]
        public string colorProperty = "_BaseColor";

        [Tooltip("Optional world-space label. Text is redundant encoding so the " +
                 "cabinet is identifiable without colour vision.")]
        public TMPro.TMP_Text label;

        [Tooltip("Optional UI Image behind the label. Canvas UI cannot be tinted " +
                 "with a MaterialPropertyBlock, so it is set directly here.")]
        public Image panelImage;

        [Tooltip("Where a beam should terminate. Defaults to this transform.")]
        public Transform beamAnchor;

        private static readonly System.Collections.Generic.Dictionary<string, PermissionLockView>
            Registry = new System.Collections.Generic.Dictionary<string, PermissionLockView>();

        private MaterialPropertyBlock _mpb;

        public Transform BeamAnchor => beamAnchor != null ? beamAnchor : transform;

        /// <summary>Look up the cabinet a given permission unlocks.</summary>
        public static PermissionLockView Find(PermissionKey key)
        {
            if (key == null) return null;
            return Registry.TryGetValue(key.keyId, out var v) ? v : null;
        }

        private void OnEnable()
        {
            Apply();
            if (Application.isPlaying && permission != null)
                Registry[permission.keyId] = this;
        }

        private void OnDisable()
        {
            if (Application.isPlaying && permission != null)
                Registry.Remove(permission.keyId);
        }

        private void OnValidate() => Apply();

        public void Apply()
        {
            if (permission == null) return;

            if (targetRenderers == null || targetRenderers.Length == 0)
                targetRenderers = GetComponentsInChildren<Renderer>(true);

            _mpb ??= new MaterialPropertyBlock();

            foreach (var r in targetRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(colorProperty, permission.keyColor);
                r.SetPropertyBlock(_mpb);
            }

            if (label != null) label.text = permission.displayName;
            if (panelImage != null) panelImage.color = permission.keyColor;
        }
    }
}