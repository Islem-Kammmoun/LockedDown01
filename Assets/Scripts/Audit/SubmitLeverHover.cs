using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace LockedDown.Audit
{
    /// <summary>
    /// Swaps the backing panel color while the submit lever is hovered.
    /// Uses MaterialPropertyBlock, so the source material is never instantiated
    /// or edited — safe on purchased asset-pack materials.
    /// </summary>
    public class SubmitLeverHover : MonoBehaviour
    {
        [Tooltip("XRSimpleInteractable on the lever. Leave empty to auto-find in parents.")]
        public XRSimpleInteractable interactable;

        [Tooltip("Renderer of the backing panel behind the lever.")]
        public Renderer panelRenderer;

        public Color idleColor = new Color(0.16f, 0.18f, 0.22f, 1f);
        public Color hoverColor = new Color(0.38f, 0.44f, 0.52f, 1f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private MaterialPropertyBlock _mpb;

        private void Awake()
        {
            if (interactable == null)
                interactable = GetComponentInParent<XRSimpleInteractable>();

            if (panelRenderer == null)
                panelRenderer = GetComponent<Renderer>();

            if (panelRenderer == null || interactable == null)
            {
                Debug.LogError($"[LockedDown] {name}: SubmitLeverHover needs a Renderer and an " +
                               $"XRSimpleInteractable.", this);
                enabled = false;
                return;
            }

            _mpb = new MaterialPropertyBlock();
            Apply(idleColor);
        }

        private void OnEnable()
        {
            interactable.hoverEntered.AddListener(OnHoverEntered);
            interactable.hoverExited.AddListener(OnHoverExited);
        }

        private void OnDisable()
        {
            interactable.hoverEntered.RemoveListener(OnHoverEntered);
            interactable.hoverExited.RemoveListener(OnHoverExited);
        }

        private void OnHoverEntered(HoverEnterEventArgs _) => Apply(hoverColor);
        private void OnHoverExited(HoverExitEventArgs _) => Apply(idleColor);

        private void Apply(Color c)
        {
            panelRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, c);   // URP Lit / Unlit
            _mpb.SetColor(ColorId, c);       // built-in fallback, harmless if absent
            panelRenderer.SetPropertyBlock(_mpb);
        }
    }
}
