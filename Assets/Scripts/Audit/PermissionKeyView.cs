using LockedDown.Data;
using UnityEngine;

namespace LockedDown.Audit
{
    /// <summary>
    /// Drives a key's appearance from its PermissionKey asset.
    /// ONE prefab serves all six permissions - colour is data, not geometry.
    /// Uses MaterialPropertyBlock so no material instances are created and the
    /// source asset-pack materials are never touched.
    /// </summary>
    [ExecuteAlways]
    public class PermissionKeyView : MonoBehaviour
    {
        [Tooltip("The permission this physical key represents.")]
        public PermissionKey permission;

        [Tooltip("Renderers to tint. Leave empty to auto-collect children.")]
        public Renderer[] targetRenderers;

        [Tooltip("Colour property. URP/Lit uses _BaseColor. Built-In uses _Color.")]
        public string colorProperty = "_BaseColor";

        [Tooltip("Optional world-space label showing the key name.")]
        public TMPro.TMP_Text label;

        private MaterialPropertyBlock _mpb;

        private void OnEnable() { Apply(); }
        private void OnValidate() { Apply(); }

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
            if (Application.isPlaying) gameObject.name = $"Key_{permission.keyId}";
        }
    }
}
