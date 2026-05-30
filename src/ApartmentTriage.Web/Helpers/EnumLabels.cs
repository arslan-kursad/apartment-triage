using ApartmentTriage.Domain.Enums;

namespace ApartmentTriage.Web.Helpers;

/// <summary>
/// Presentation-layer bilingual labels for domain enums. Single source of truth
/// for the UI (selects, cards, chart legends). Turkish category labels follow
/// taxonomy.v4.yaml. Emitted into markup as data-i18n-tr / data-i18n-en so the
/// shared applyLang() toggle picks them up.
/// </summary>
public static class EnumLabels
{
    public static (string Tr, string En) For(TicketStatus status) => status switch
    {
        TicketStatus.Open       => ("Açık",     "Open"),
        TicketStatus.InProgress => ("İşlemde",  "In Progress"),
        TicketStatus.Resolved   => ("Çözüldü",  "Resolved"),
        TicketStatus.Closed     => ("Kapalı",   "Closed"),
        _                       => (status.ToString(), status.ToString())
    };

    public static (string Tr, string En) For(TicketCategory category) => category switch
    {
        TicketCategory.Plumbing        => ("Tesisat",              "Plumbing"),
        TicketCategory.Electrical      => ("Elektrik",             "Electrical"),
        TicketCategory.Gas             => ("Doğalgaz",             "Gas"),
        TicketCategory.HeatingCooling  => ("Isıtma / Soğutma",     "Heating / Cooling"),
        TicketCategory.Elevator        => ("Asansör",              "Elevator"),
        TicketCategory.Structural      => ("Yapısal",              "Structural"),
        TicketCategory.CommonArea      => ("Ortak Alan",           "Common Area"),
        TicketCategory.Pest            => ("Haşere",               "Pest"),
        TicketCategory.Noise           => ("Gürültü",              "Noise"),
        TicketCategory.NeighborDispute => ("Komşu Anlaşmazlığı",   "Neighbor Dispute"),
        TicketCategory.Billing         => ("Aidat / Fatura",       "Billing"),
        TicketCategory.Security        => ("Güvenlik",             "Security"),
        TicketCategory.Announcement    => ("Duyuru",               "Announcement"),
        TicketCategory.Other           => ("Diğer",                "Other"),
        _                              => (category.ToString(), category.ToString())
    };

    public static (string Tr, string En) For(TicketSeverity severity) => severity switch
    {
        TicketSeverity.Low    => ("Düşük",  "Low"),
        TicketSeverity.Medium => ("Orta",   "Medium"),
        TicketSeverity.High   => ("Yüksek", "High"),
        TicketSeverity.Urgent => ("Acil",   "Urgent"),
        _                     => (severity.ToString(), severity.ToString())
    };

    public static (string Tr, string En) For(RoutingAction action) => action switch
    {
        RoutingAction.NotifyResident    => ("Sakine Bildir",     "Notify Resident"),
        RoutingAction.AssignTechnician  => ("Teknisyen Ata",     "Assign Technician"),
        RoutingAction.EscalateToManager => ("Yöneticiye Yükselt","Escalate to Manager"),
        RoutingAction.TriggerEmergency  => ("Acil Müdahale",     "Trigger Emergency"),
        RoutingAction.Defer             => ("Ertele",            "Defer"),
        RoutingAction.Archive           => ("Arşivle",           "Archive"),
        _                               => (action.ToString(), action.ToString())
    };

    public static (string Tr, string En) For(ConfidenceLevel level) => level switch
    {
        ConfidenceLevel.High   => ("Yüksek", "High"),
        ConfidenceLevel.Medium => ("Orta",   "Medium"),
        ConfidenceLevel.Low    => ("Düşük",  "Low"),
        _                      => (level.ToString(), level.ToString())
    };
}
