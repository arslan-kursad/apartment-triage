namespace ApartmentTriage.Domain.Enums;

/// <summary>
/// Relation of a secondary issue to the primary issue.
/// Drives orchestrator ticket-splitting logic (taxonomy.v3.yaml § multi_issue).
/// </summary>
public enum CausalRelation
{
    Independent,
    EffectOfPrimary,
    CauseOfPrimary
}
