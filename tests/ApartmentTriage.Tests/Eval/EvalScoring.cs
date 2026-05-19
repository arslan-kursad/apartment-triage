using ApartmentTriage.Domain.Enums;

namespace ApartmentTriage.Tests.Eval;

internal sealed record EvalResult(
    string CaseId,
    TicketCategory ExpectedCategory,
    TicketCategory ActualCategory,
    bool ExpectedIsEmergency,
    bool ActualIsEmergency,
    IReadOnlyList<string> Tags);

internal sealed record EvalScore
{
    public required decimal CategoryAccuracy { get; init; }
    public required int CategoryCorrect { get; init; }
    public required int CategoryTotal { get; init; }
    public required decimal EmergencyRecall { get; init; }
    public required int EmergencyTruePositives { get; init; }
    public required int EmergencyActualPositives { get; init; }
    public required decimal EmergencyPrecision { get; init; }
    public required int EmergencyPrecisionCorrect { get; init; }
    public required int EmergencyPredictedPositives { get; init; }
    public required IReadOnlyList<(string CaseId, TicketCategory Expected, TicketCategory Actual)> FailedCases { get; init; }

    public bool Passed =>
        CategoryAccuracy >= 0.80m &&
        EmergencyRecall >= 0.95m &&
        EmergencyPrecision >= 0.70m;

    public void Print()
    {
        Console.WriteLine("=== Eval Results ===");
        Console.WriteLine($"Category Accuracy  : {CategoryCorrect}/{CategoryTotal} ({CategoryAccuracy:P1}) {Mark(CategoryAccuracy >= 0.80m)}");
        Console.WriteLine($"Emergency Recall   : {EmergencyTruePositives}/{EmergencyActualPositives} ({EmergencyRecall:P1}) {Mark(EmergencyRecall >= 0.95m)}");
        Console.WriteLine($"Emergency Precision: {EmergencyPrecisionCorrect}/{EmergencyPredictedPositives} ({EmergencyPrecision:P1}) {Mark(EmergencyPrecision >= 0.70m)}");
        Console.WriteLine($"Overall            : {(Passed ? "PASS" : "FAIL")}");

        if (FailedCases.Count > 0)
        {
            Console.WriteLine("\nFailed cases:");
            foreach (var (caseId, expected, actual) in FailedCases)
                Console.WriteLine($"[{caseId}] expected: {expected}, got: {actual}");
        }
    }

    private static string Mark(bool pass) => pass ? "✓" : "✗";
}

internal static class EvalScoring
{
    public static EvalScore Calculate(IReadOnlyList<EvalResult> results)
    {
        int correct = results.Count(r => r.ActualCategory == r.ExpectedCategory);
        var failed = results
            .Where(r => r.ActualCategory != r.ExpectedCategory)
            .Select(r => (r.CaseId, r.ExpectedCategory, r.ActualCategory))
            .ToList();

        // Recall: TP / (TP + FN)  — how many real emergencies did we catch?
        var actualEmergencies = results.Where(r => r.ExpectedIsEmergency).ToList();
        int truePositives = actualEmergencies.Count(r => r.ActualIsEmergency);
        decimal recall = actualEmergencies.Count == 0 ? 1m
            : (decimal)truePositives / actualEmergencies.Count;

        // Precision: TP / (TP + FP) — of predicted emergencies, how many were real?
        var predictedEmergencies = results.Where(r => r.ActualIsEmergency).ToList();
        decimal precision = predictedEmergencies.Count == 0 ? 1m
            : (decimal)truePositives / predictedEmergencies.Count;

        return new EvalScore
        {
            CategoryAccuracy = results.Count == 0 ? 0m : (decimal)correct / results.Count,
            CategoryCorrect = correct,
            CategoryTotal = results.Count,
            EmergencyRecall = recall,
            EmergencyTruePositives = truePositives,
            EmergencyActualPositives = actualEmergencies.Count,
            EmergencyPrecision = precision,
            EmergencyPrecisionCorrect = truePositives,
            EmergencyPredictedPositives = predictedEmergencies.Count,
            FailedCases = failed
        };
    }
}
