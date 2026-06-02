using System.Text.RegularExpressions;

namespace ApartmentTriage.Web.Helpers;

/// <summary>
/// Detects the language a reply should use, from the message CONTENT first.
/// A resident's channel app-language (Telegram language_code) is a poor signal for the
/// language of a given message — a Turkish-locale user often writes in English — so it is
/// only a tiebreak when the content carries no signal.
/// </summary>
public static partial class LanguageDetector
{
    // Turkish-specific letters (NOT i/I, which English shares). Any occurrence ⇒ Turkish.
    private const string TurkishChars = "çğıöşüÇĞİÖŞÜ";

    // High-signal, low-ambiguity tokens. "problem"/"gas" overlaps are deliberately excluded.
    private static readonly HashSet<string> TurkishHints = new(StringComparer.Ordinal)
    {
        "var", "yok", "bozuk", "bozuldu", "ariza", "arizali", "calismiyor", "kesik", "kesildi",
        "kacak", "koku", "su", "sular", "elektrik", "asansor", "kat", "katta", "daire", "kapi",
        "bina", "sorun", "ses", "gurultu", "yanmiyor", "akiyor", "patladi", "tikali", "tikandi",
        "merhaba", "lutfen", "kombi", "kalorifer", "isinmiyor", "sicak", "soguk", "cop", "gaz",
        "yardim", "acil", "calismiyor", "bozulmus", "duzelt", "hala", "var", "kaloriferler"
    };

    private static readonly HashSet<string> EnglishHints = new(StringComparer.Ordinal)
    {
        "the", "is", "are", "not", "no", "working", "work", "there", "near", "please", "help",
        "on", "in", "at", "my", "broken", "break", "leak", "leaking", "smell", "water", "light",
        "lights", "door", "elevator", "lift", "floor", "hello", "hi", "and", "with", "fault",
        "faulty", "stuck", "noise", "gas", "power", "electricity", "hallway", "stairwell",
        "building", "apartment", "flat", "issue", "fix", "fixed", "heating", "cooling", "pipe",
        "wall", "ceiling", "again", "still", "doesnt", "isnt", "arent", "wont"
    };

    public static string Detect(string? text, string? channelLanguageCode = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return channelLanguageCode == "tr" ? "tr" : "en";

        // Strong Turkish signal.
        if (text.Any(c => TurkishChars.Contains(c)))
            return "tr";

        var tokens = TokenSplitter().Split(text.ToLowerInvariant());
        var tr = tokens.Count(TurkishHints.Contains);
        var en = tokens.Count(EnglishHints.Contains);

        if (tr > en) return "tr";
        if (en > tr) return "en";

        // No content signal → fall back to the channel app-language (Telegram); default English.
        return channelLanguageCode == "tr" ? "tr" : "en";
    }

    [GeneratedRegex("[^a-zçğıöşü]+")]
    private static partial Regex TokenSplitter();
}
