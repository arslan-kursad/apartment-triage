using ApartmentTriage.Domain.Enums;

namespace ApartmentTriage.Application.Agents.Classifier;

public sealed record ClassifierInput(
    Guid ResidentId,
    string RawText,
    ChannelType ChannelType,
    bool EmergencySuspected,
    IReadOnlyList<string> MatchedPhrases);
