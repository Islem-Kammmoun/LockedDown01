using UnityEngine;

namespace LockedDown.Data
{
    /// <summary>
    /// One permission. Authored as an asset, never hardcoded.
    /// Maps 1:1 to a cabinet in the room.
    /// </summary>
    [CreateAssetMenu(fileName = "Key_", menuName = "LockedDown/Permission Key")]
    public class PermissionKey : ScriptableObject
    {
        [Tooltip("Stable id written to telemetry. NEVER rename after data collection starts.")]
        public string keyId = "key.unset";

        [Tooltip("Shown on the key tag and the cabinet label.")]
        public string displayName = "Unnamed Key";

        [TextArea(2, 3)]
        [Tooltip("Plain-language capability. Slide 3 wording.")]
        public string unlocksDescription = "";

        [Tooltip("Ring + cabinet + beam colour. This is the ONLY colour channel in the room. Robots stay identical.")]
        public Color keyColor = Color.white;

        [Range(0, 3)]
        [Tooltip("0 = mild (supply closet), 3 = destructive (admin console). Analysis covariate only; not shown to player.")]
        public int severity = 1;
    }
}
