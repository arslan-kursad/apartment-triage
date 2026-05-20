namespace ApartmentTriage.Domain.Enums;

public enum RoutingAction
{
    NotifyResident,     // clarification gönder
    AssignTechnician,   // normal bakım
    EscalateToManager,  // yüksek öncelik
    TriggerEmergency,   // acil müdahale
    Defer,              // düşük öncelik, geciktir
    Archive             // NonActionable
}
