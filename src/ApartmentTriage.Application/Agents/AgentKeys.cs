namespace ApartmentTriage.Application.Agents;

// Keyed Services registration keys — .NET 8 native, no factory classes needed.
// New agents: add constants here. Pattern stays the same for Day 8 (Enricher).
public static class AgentKeys
{
    public const string ClassifierHaiku  = "classifier-haiku";
    public const string ClassifierSonnet = "classifier-sonnet";

    // Day 9
    public const string RouterHaiku    = "router-haiku";
    public const string EnricherDefault = "enricher-default";
}
