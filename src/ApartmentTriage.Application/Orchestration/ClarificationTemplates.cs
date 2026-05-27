using ApartmentTriage.Domain.Enums;

namespace ApartmentTriage.Application.Orchestration;

/// <summary>
/// Maps AmbiguityReasons to bilingual (TR/EN) clarification messages.
/// Priority: MissingLocation > CategoryAmbiguous > LanguageUnclear > MissingSeverity > NeedsVisual.
/// NonActionable yields null — no reply is sent.
/// </summary>
public static class ClarificationTemplates
{
    private static readonly IReadOnlyList<AmbiguityReason> Priority =
    [
        AmbiguityReason.MissingLocation,
        AmbiguityReason.CategoryAmbiguous,
        AmbiguityReason.LanguageUnclear,
        AmbiguityReason.MissingSeverity,
        AmbiguityReason.NeedsVisual,
        // NonActionable intentionally absent — no message sent
    ];

    private static readonly IReadOnlyDictionary<AmbiguityReason, string> TrMessages =
        new Dictionary<AmbiguityReason, string>
        {
            [AmbiguityReason.MissingLocation] =
                "Sorunun hangi daire veya bölgede olduğunu belirtir misiniz?",
            [AmbiguityReason.CategoryAmbiguous] =
                "Sorununuzu biraz daha açıklar mısınız? " +
                "Hangi tür bir sorunla karşılaştığınızı anlatırsanız daha hızlı yardımcı olabiliriz.",
            [AmbiguityReason.LanguageUnclear] =
                "Mesajınızı tam olarak anlayamadık. " +
                "Sorunu farklı kelimelerle açıklar mısınız?",
            [AmbiguityReason.MissingSeverity] =
                "Sorun ne zamandır devam ediyor ve acil müdahale gerektiriyor mu? " +
                "Bu bilgi önceliklendirmemize yardımcı olur.",
            [AmbiguityReason.NeedsVisual] =
                "Mümkünse sorunun fotoğrafını paylaşabilir misiniz? " +
                "Görsel bilgi süreci hızlandırır.",
        };

    private static readonly IReadOnlyDictionary<AmbiguityReason, string> EnMessages =
        new Dictionary<AmbiguityReason, string>
        {
            [AmbiguityReason.MissingLocation] =
                "Could you specify which apartment or area the issue is in?",
            [AmbiguityReason.CategoryAmbiguous] =
                "Could you describe the problem in a bit more detail? " +
                "Knowing the type of issue helps us assist you faster.",
            [AmbiguityReason.LanguageUnclear] =
                "We couldn't fully understand your message. " +
                "Could you describe the problem in different words?",
            [AmbiguityReason.MissingSeverity] =
                "How long has this been going on, and does it require urgent attention? " +
                "This helps us prioritize your request.",
            [AmbiguityReason.NeedsVisual] =
                "If possible, could you share a photo of the issue? " +
                "Visual information speeds up the process.",
        };

    /// <summary>
    /// Returns the bilingual clarification message wrapped with context header,
    /// or <c>null</c> if no message should be sent (empty reasons or NonActionable only).
    /// </summary>
    public static string? BuildMessage(IReadOnlyList<AmbiguityReason> reasons, string lang = "tr")
    {
        var messages = lang == "en" ? EnMessages : TrMessages;

        foreach (var candidate in Priority)
        {
            if (!reasons.Contains(candidate))
                continue;

            var body = messages[candidate];
            return lang == "en"
                ? $"🤔 Got your request. To help you faster:\n\n{body}"
                : $"🤔 Talebinizi aldık. Daha hızlı yardımcı olabilmek için:\n\n{body}";
        }
        return null;
    }

    public static string BuildAcknowledgementMessage(string lang = "tr") => lang == "en"
        ? "✅ Your request has been received and recorded."
        : "✅ Talebinizi aldık. Sorununuzu sistemimize kaydettik; yöneticiniz en kısa sürede bilgilendirilecektir.";
}
