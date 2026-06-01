namespace ApartmentTriage.Web.Security;

/// <summary>
/// Parses Telegram bot commands for the login flow (/login and /login@BotName).
/// </summary>
public static class TelegramLoginCommand
{
    public static bool IsLoginCommand(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        if (!trimmed.StartsWith("/login", StringComparison.OrdinalIgnoreCase))
            return false;

        if (trimmed.Length == 6)
            return true;

        // "/login@HanwasBot" or "/login@bot extra" — suffix must start with @
        return trimmed[6] == '@';
    }
}
