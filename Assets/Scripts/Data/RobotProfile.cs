using System;
using System.Collections.Generic;
using UnityEngine;

namespace LockedDown.Data
{
    public enum Verdict { Undecided = 0, Keep = 1, Revoke = 2, Limit = 3 }

    [Serializable]
    public struct KeyAssignment
    {
        public PermissionKey key;

        [Tooltip("Ground truth for scoring. Not exposed to the player at runtime.")]
        public Verdict correctVerdict;

        [Tooltip("Design classification. Drives the decoy analysis - do not skip.")]
        public AssignmentClass classification;

        [TextArea(2, 3)]
        [Tooltip("Debrief rationale. Never shown during the audit.")]
        public string rationale;
    }

    public enum AssignmentClass
    {
        Justified,        // key the job genuinely needs
        ObviousExcess,    // absurd over-permission, tutorial-grade
        PlausibleDecoy,   // feels reasonable, is not. THE construct-validating item.
        DualUse           // needed but must be scoped, not removed
    }

    /// <summary>
    /// One robot. Scaling from 1 to 5 is an authoring task: duplicate this asset.
    /// </summary>
    [CreateAssetMenu(fileName = "Robot_", menuName = "LockedDown/Robot Profile")]
    public class RobotProfile : ScriptableObject
    {
        [Tooltip("Stable id written to telemetry. NEVER rename after data collection starts.")]
        public string robotId = "robot.unset";

        [Tooltip("Internal label only. Never rendered on the robot mesh - all robots are visually identical.")]
        public string internalName = "Unnamed Bot";

        [TextArea(2, 4)]
        [Tooltip("The job-card text. This is the evidence the player must consult.")]
        public string jobCardText = "";

        [Tooltip("Is this the rogue agent? Never drives any visual or audio difference.")]
        public bool isRogueAgent = false;

        public List<KeyAssignment> assignments = new List<KeyAssignment>();

        public bool TryGetAssignment(PermissionKey key, out KeyAssignment result)
        {
            foreach (var a in assignments)
            {
                if (a.key == key) { result = a; return true; }
            }
            result = default;
            return false;
        }
    }
}
