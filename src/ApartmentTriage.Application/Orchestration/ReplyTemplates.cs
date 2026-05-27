using ApartmentTriage.Domain.Entities;
using ApartmentTriage.Domain.Enums;

namespace ApartmentTriage.Application.Orchestration;

/// <summary>
/// Builds bilingual (TR/EN) reply messages sent to residents after triage.
/// Category, priority, and action maps are the single source of truth for display labels.
/// </summary>
public static class ReplyTemplates
{
    public static string BuildTicketReply(Ticket ticket, string lang)
    {
        var shortId = ticket.Id.ToString("N")[..8].ToUpper();
        var category = lang == "en" ? GetCategoryEn(ticket.Category) : GetCategoryTr(ticket.Category);
        var priority = lang == "en" ? GetPriorityEn(ticket.Severity) : GetPriorityTr(ticket.Severity);
        var action = lang == "en" ? GetActionEn(ticket.RoutingAction) : GetActionTr(ticket.RoutingAction);

        return lang == "en"
            ? $"""
               ✅ Your request has been recorded.
               ──────────────────────
               📋 {shortId}
               🏷️  {category}
               ⚡ {priority}
               🔄 {action}
               ──────────────────────
               """
            : $"""
               ✅ Talebiniz kaydedildi.
               ──────────────────────
               📋 {shortId}
               🏷️  {category}
               ⚡ {priority}
               🔄 {action}
               ──────────────────────
               """;
    }

    public static string BuildSimpleAck(string lang) => lang == "en"
        ? "✅ Your message has been received."
        : "✅ Mesajınızı aldık.";

    // ── Category maps ────────────────────────────────────────────────────────

    private static string GetCategoryTr(TicketCategory c) => c switch
    {
        TicketCategory.Elevator        => "Asansör",
        TicketCategory.Plumbing        => "Tesisat",
        TicketCategory.Electrical      => "Elektrik",
        TicketCategory.Structural      => "Yapısal",
        TicketCategory.Gas             => "Gaz",
        TicketCategory.CommonArea      => "Ortak Alan",
        TicketCategory.Security        => "Güvenlik",
        TicketCategory.HeatingCooling  => "Isıtma / Soğutma",
        TicketCategory.Pest            => "Haşere",
        TicketCategory.Noise           => "Gürültü",
        TicketCategory.NeighborDispute => "Komşu Anlaşmazlığı",
        TicketCategory.Billing         => "Fatura / Aidat",
        _                              => "Genel"
    };

    private static string GetCategoryEn(TicketCategory c) => c switch
    {
        TicketCategory.Elevator        => "Elevator",
        TicketCategory.Plumbing        => "Plumbing",
        TicketCategory.Electrical      => "Electrical",
        TicketCategory.Structural      => "Structural",
        TicketCategory.Gas             => "Gas",
        TicketCategory.CommonArea      => "Common Area",
        TicketCategory.Security        => "Security",
        TicketCategory.HeatingCooling  => "Heating / Cooling",
        TicketCategory.Pest            => "Pest Control",
        TicketCategory.Noise           => "Noise",
        TicketCategory.NeighborDispute => "Neighbor Dispute",
        TicketCategory.Billing         => "Billing / Dues",
        _                              => "General"
    };

    // ── Priority maps ────────────────────────────────────────────────────────

    private static string GetPriorityTr(TicketSeverity s) => s switch
    {
        TicketSeverity.Urgent => "🔴 Kritik",
        TicketSeverity.High   => "🟠 Yüksek",
        TicketSeverity.Medium => "🟡 Orta",
        _                     => "🟢 Düşük"
    };

    private static string GetPriorityEn(TicketSeverity s) => s switch
    {
        TicketSeverity.Urgent => "🔴 Critical",
        TicketSeverity.High   => "🟠 High",
        TicketSeverity.Medium => "🟡 Medium",
        _                     => "🟢 Low"
    };

    // ── Action maps ──────────────────────────────────────────────────────────

    private static string GetActionTr(RoutingAction? action) => action switch
    {
        RoutingAction.TriggerEmergency  => "🚨 ACİL DURUM — Yöneticiniz derhal bilgilendiriliyor.",
        RoutingAction.EscalateToManager => "👤 İnsan onayına iletildi — yöneticiniz en kısa sürede dönecek.",
        RoutingAction.AssignTechnician  => "🔧 Teknik ekibe atandı — en kısa sürede ilgilenilecek.",
        RoutingAction.Defer             => "📅 Kaydedildi — planlı bakım kapsamında ele alınacak.",
        RoutingAction.Archive           => "📁 Kaydedildi.",
        _                               => "🔄 Talebiniz değerlendiriliyor."
    };

    private static string GetActionEn(RoutingAction? action) => action switch
    {
        RoutingAction.TriggerEmergency  => "🚨 EMERGENCY — Your building manager is being notified immediately.",
        RoutingAction.EscalateToManager => "👤 Escalated for human review — your manager will follow up shortly.",
        RoutingAction.AssignTechnician  => "🔧 Assigned to maintenance team — will be attended to shortly.",
        RoutingAction.Defer             => "📅 Recorded — will be addressed in scheduled maintenance.",
        RoutingAction.Archive           => "📁 Recorded.",
        _                               => "🔄 Your request is being processed."
    };
}
