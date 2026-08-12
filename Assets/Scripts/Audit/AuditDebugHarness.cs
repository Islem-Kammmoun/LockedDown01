using LockedDown.Data;
using UnityEngine;

namespace LockedDown.Audit
{
    /// <summary>
    /// TEMPORARY validation harness. Input-backend independent: invoke from the
    /// component's context menu (the three-dot icon on the component header)
    /// while in Play mode. Works with old Input, new Input System, or neither.
    ///
    /// DELETE BEFORE ANY PILOT BUILD - it commits decisions with no verification,
    /// producing rows indistinguishable from real participant data.
    /// </summary>
    public class AuditDebugHarness : MonoBehaviour
    {
        public RobotAuditStation station;
        public PermissionKey keySupplyCloset;
        public PermissionKey keyCustomerRecords;
        public PermissionKey keyAdminConsole;

        [ContextMenu("0 - Print log path")]
        public void PrintPath()
        {
            Debug.Log("[LockedDown] audit log: " + (AuditLogger.Instance != null
                ? AuditLogger.Instance.FilePath : "NO LOGGER INSTANCE"));
        }

        [ContextMenu("1 - Approach robot")]
        public void Approach() { Require(); station.NotifyApproached(); }

        [ContextMenu("2 - Pull job card")]
        public void PullCard() { Require(); station.NotifyJobCardPulled(); }

        [ContextMenu("3 - Read job card")]
        public void ReadCard() { Require(); station.NotifyJobCardRead(2.5f); }

        [ContextMenu("4 - Beam test customer records key")]
        public void BeamRecords() { Require(); station.NotifyBeamTested(keyCustomerRecords); }

        [ContextMenu("5 - KEEP supply closet")]
        public void KeepSupply() { Require(); station.CommitDecision(keySupplyCloset, Verdict.Keep); }

        [ContextMenu("6 - REVOKE customer records")]
        public void RevokeRecords() { Require(); station.CommitDecision(keyCustomerRecords, Verdict.Revoke); }

        [ContextMenu("7 - REVOKE admin console")]
        public void RevokeAdmin() { Require(); station.CommitDecision(keyAdminConsole, Verdict.Revoke); }

        /// <summary>One click: full ordered sequence, mixed verification states.</summary>
        [ContextMenu("RUN FULL SEQUENCE")]
        public void RunFullSequence()
        {
            Require();
            Approach();
            PullCard();
            ReadCard();
            KeepSupply();        // expect card+nobeam
            BeamRecords();
            RevokeRecords();     // expect card+beam
            RevokeAdmin();       // expect card+nobeam -> also fires RobotResolved
            PrintPath();
        }

        private void Require()
        {
            if (station == null)
                Debug.LogError("[LockedDown] AuditDebugHarness: station is not assigned.", this);
            if (AuditLogger.Instance == null)
                Debug.LogError("[LockedDown] AuditDebugHarness: no AuditLogger in scene.", this);
        }
    }
}
