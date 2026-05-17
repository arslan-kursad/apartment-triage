namespace ApartmentTriage.Domain.Enums;

/// <summary>
/// Primary ticket category — maps 1:1 to taxonomy.v3.yaml categories (LOCKED).
/// </summary>
public enum TicketCategory
{
    Plumbing,
    Electrical,
    Gas,
    HeatingCooling,
    Elevator,
    Structural,
    CommonArea,
    Pest,
    Noise,
    NeighborDispute,
    Billing,
    Security,
    Announcement,
    Other
}
