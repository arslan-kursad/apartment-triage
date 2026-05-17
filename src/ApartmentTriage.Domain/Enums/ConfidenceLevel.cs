namespace ApartmentTriage.Domain.Enums;

/// <summary>
/// Classifier confidence — used independently for emergency and category decisions.
/// See taxonomy.v3.yaml § confidence.output_fields.
/// </summary>
public enum ConfidenceLevel
{
    Low,
    Medium,
    High
}
