namespace ApartmentTriage.Domain.Enums;

/// <summary>
/// Authorization role for dashboard access. Additive — default <see cref="None"/>.
/// Only <see cref="Manager"/> is wired to a policy today; the higher values are
/// role-ready placeholders so future Technician/Staff/Team access can be added
/// with new policies + page authorization WITHOUT a schema change.
/// </summary>
public enum ResidentRole
{
    /// <summary>Default. A resident (sakin) — cannot access the dashboard.</summary>
    None = 0,

    /// <summary>Building manager — dashboard access (active today).</summary>
    Manager = 1,

    /// <summary>Placeholder — future technician access.</summary>
    Technician = 2,

    /// <summary>Placeholder — future staff access.</summary>
    Staff = 3,

    /// <summary>Placeholder — future team access.</summary>
    Team = 4
}
