using System.Text.RegularExpressions;

namespace ApartmentTriage.Domain;

/// <summary>
/// Canonical E.164 storage for Turkish mobile numbers (+905xxxxxxxxx).
/// Used on write, update, and lookup so UI (+90…) and WhatsApp webhook (905…) forms match.
/// </summary>
public static partial class PhoneNumberNormalizer
{
    private static readonly Regex FormattingChars = FormattingCharsRegex();

    /// <summary>
    /// Returns canonical E.164 (+digits) or null when empty/invalid.
    /// Masked values (containing *) pass through unchanged for admin round-trip.
    /// </summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var trimmed = input.Trim();
        if (trimmed.Contains('*'))
            return trimmed;

        var cleaned = FormattingChars.Replace(trimmed, "");
        if (cleaned.Length == 0)
            return null;

        var hadPlus = cleaned.StartsWith('+');
        var digits = hadPlus ? cleaned[1..] : cleaned;

        if (digits.Length == 0 || !digits.All(char.IsDigit))
            return null;

        // 0506… (11 digits) → drop leading 0
        if (digits.Length == 11 && digits[0] == '0' && digits[1] == '5')
            digits = digits[1..];

        // 506… (10 digits) → prepend country code
        if (digits.Length == 10 && digits[0] == '5')
            digits = "90" + digits;

        // Must be Turkish mobile in E.164 without '+': 90 + 10 digits starting with 5
        if (digits.Length != 12 || !digits.StartsWith("90") || digits[2] != '5')
            return null;

        return "+" + digits;
    }

    /// <summary>WhatsApp Cloud API <c>to</c> field: digits only, no leading +.</summary>
    public static string ToWhatsAppApiRecipient(string normalizedE164)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedE164);
        return normalizedE164.StartsWith('+') ? normalizedE164[1..] : normalizedE164;
    }

    [GeneratedRegex(@"[\s\-().]")]
    private static partial Regex FormattingCharsRegex();
}
