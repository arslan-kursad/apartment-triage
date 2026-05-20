using ApartmentTriage.Domain.Enums;

namespace ApartmentTriage.Application.Agents.Router;

public sealed record RouterOutput(
    RoutingAction   Action,
    string?         AssignedTo,
    string?         NotificationNote,
    ConfidenceLevel ConfidenceLevel
);
