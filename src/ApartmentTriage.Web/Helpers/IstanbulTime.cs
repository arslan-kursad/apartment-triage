using System.Globalization;

namespace ApartmentTriage.Web.Helpers;

public static class IstanbulTime
{
    private static readonly TimeZoneInfo? _tz;
    private static readonly CultureInfo _trCulture = new("tr-TR");

    static IstanbulTime()
    {
        try { _tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul"); }
        catch { _tz = null; }
    }

    public static DateTime FromUtc(DateTime utc)
        => _tz is not null
            ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _tz)
            : utc.AddHours(3);

    // "30 May 14:48" — Turkish locale, no year
    public static string Format(DateTime utc)
        => FromUtc(utc).ToString("dd MMM HH:mm", _trCulture);

    // "30 May 2026 14:48" — with year
    public static string FormatFull(DateTime utc)
        => FromUtc(utc).ToString("dd MMM yyyy HH:mm", _trCulture);
}
