namespace LockedDown.Audit
{
    /// <summary>
    /// Ordered event vocabulary. Ordering IS the measurement - this enum is the
    /// contract between the build and the analysis script. APPEND ONLY.
    /// Never reorder, never rename once data collection begins.
    /// </summary>
    public enum AuditEventType
    {
        SessionMarker,       // session lifecycle only - never a behavioural event
        RobotApproached,
        JobCardPulled,
        JobCardRead,
        JobCardReturned,
        KeyGrabbed,
        BeamTested,
        KeyReleased,
        DecisionCommitted,
        DecisionReversed,
        RobotResolved
    }
}
