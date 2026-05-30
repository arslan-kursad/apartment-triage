namespace ApartmentTriage.Web.Helpers;

/// <summary>
/// Resolves a quick date preset ("today" / "week" / "all") to an inclusive UTC
/// window for filtering. Days are interpreted in Istanbul local time (UTC+3, no
/// DST since 2016) so "today" matches what the operator sees on the Istanbul clock.
/// Shared by the Inbox and Tickets date filters.
/// </summary>
public static class DateFilter
{
    public const string Today = "today";
    public const string Week  = "week";
    public const string All   = "all";

    /// <summary>Normalizes an incoming range value; unknown/empty → "all".</summary>
    public static string Normalize(string? range) => range switch
    {
        Today => Today,
        Week  => Week,
        _     => All
    };

    /// <summary>
    /// Returns the inclusive lower bound (FromUtc) and exclusive upper bound (ToUtc)
    /// for the given preset. Null means unbounded on that side.
    /// </summary>
    public static (DateTime? FromUtc, DateTime? ToUtc) Resolve(string? range)
    {
        var todayIst = IstanbulTime.FromUtc(DateTime.UtcNow).Date;
        return Normalize(range) switch
        {
            Today => (IstMidnightToUtc(todayIst), null),
            Week  => (IstMidnightToUtc(todayIst.AddDays(-6)), null),
            _     => (null, null)
        };
    }

    // Istanbul local midnight → UTC instant (UTC = local − 3h).
    private static DateTime IstMidnightToUtc(DateTime istMidnight)
        => DateTime.SpecifyKind(istMidnight.AddHours(-3), DateTimeKind.Utc);
}
