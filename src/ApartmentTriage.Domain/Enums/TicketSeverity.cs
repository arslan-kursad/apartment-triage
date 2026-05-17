namespace ApartmentTriage.Domain.Enums;

/// <summary>
/// Ticket severity / priority — baseline from taxonomy, modified by priority_modifiers signals.
/// Ordering matters: Low &lt; Medium &lt; High &lt; Urgent (used in UpgradeSeverity).
/// </summary>
public enum TicketSeverity
{
    Low = 0,
    Medium = 1,
    High = 2,
    Urgent = 3
}
