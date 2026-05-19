using ApartmentTriage.Domain.Enums;

namespace ApartmentTriage.Application.Orchestration;

/// <summary>
/// Maps AmbiguityReasons to Turkish clarification messages sent back to the resident.
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

    private static readonly IReadOnlyDictionary<AmbiguityReason, string> Messages =
        new Dictionary<AmbiguityReason, string>
        {
            [AmbiguityReason.MissingLocation] =
                "Talebinizi aldık. Sorunun hangi daire veya bölgede olduğunu belirtir misiniz?",
            [AmbiguityReason.CategoryAmbiguous] =
                "Talebinizi aldık. Sorununuzu biraz daha açıklar mısınız? " +
                "Hangi tür bir sorunla karşılaştığınızı anlatırsanız daha hızlı yardımcı olabiliriz.",
            [AmbiguityReason.LanguageUnclear] =
                "Mesajınızı tam olarak anlayamadık. " +
                "Sorunu farklı kelimelerle açıklar mısınız?",
            [AmbiguityReason.MissingSeverity] =
                "Talebinizi aldık. Sorun ne zamandır devam ediyor ve acil müdahale gerektiriyor mu? " +
                "Bu bilgi önceliklendirmemize yardımcı olur.",
            [AmbiguityReason.NeedsVisual] =
                "Talebinizi aldık. Mümkünse sorununun fotoğrafını paylaşabilir misiniz? " +
                "Görsel bilgi süreci hızlandırır.",
        };

    /// <summary>
    /// Returns the clarification message to send to the resident, or <c>null</c> if no
    /// message should be sent (empty reasons or NonActionable only).
    /// </summary>
    public static string? BuildMessage(IReadOnlyList<AmbiguityReason> reasons)
    {
        foreach (var candidate in Priority)
        {
            if (reasons.Contains(candidate))
                return Messages[candidate];
        }
        return null;
    }
}
