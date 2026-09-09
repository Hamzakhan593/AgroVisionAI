using System;
using System.Collections.Generic;

namespace AgroVisionAI.TreatmentPk;

public sealed class Tip
{
    public string En { get; set; } = "";
    public string RomanUrdu { get; set; } = "";
    public List<string> SourceIds { get; set; } = new();
}
public sealed class EvidenceSource
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Access { get; set; } = "";
    public string Kind { get; set; } = "reference";
    public string? Published { get; set; }
    public string CheckedOn { get; set; } = "";
    public string Note { get; set; } = "";
}
public sealed class ConditionRecord
{
    public string Crop { get; set; } = "";
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public List<string> Aliases { get; set; } = new();
    public Tip Summary { get; set; } = new();
    public List<Tip> Today { get; set; } = new();
    public List<Tip> Prevention { get; set; } = new();
    public List<Tip> Avoid { get; set; } = new();
    public string Escalation { get; set; } = "";
    public List<string> SourceIds { get; set; } = new();
    public string ReviewStatus { get; set; } = "";
    public string? LocalExpert { get; set; }
    public string LastEvidenceCheck { get; set; } = "";
    public string LocalityNote { get; set; } = "";
    public string ChemicalPolicy { get; set; } = "";
}
public sealed class Catalog
{
    public int SchemaVersion { get; set; }
    public string CatalogVersion { get; set; } = "";
    public string Country { get; set; } = "";
    public List<EvidenceSource> Sources { get; set; } = new();
    public List<ConditionRecord> Records { get; set; } = new();
}
public sealed class ProductFile
{
    public int SchemaVersion { get; set; }
    public List<ProductReview> Products { get; set; } = new();
}
// Observed fields are manufacturer claims, never prescription fields.
public sealed class ProductReview
{
    public string Id { get; set; } = "";
    public string Country { get; set; } = "";
    public string Crop { get; set; } = "";
    public List<string> DiseaseKeys { get; set; } = new();
    public string Brand { get; set; } = "";
    public string ActiveIngredient { get; set; } = "";
    public string Formulation { get; set; } = "";
    public string ManufacturerSourceId { get; set; } = "";
    public double? ObservedDoseMlPerAcre { get; set; }
    public double? ObservedWaterLitresPerAcre { get; set; }
    public int? ObservedPhiDays { get; set; }
    public string CoverageNote { get; set; } = "";
    public string Status { get; set; } = "";
    public bool RegistrationVerified { get; set; }
    public bool LabelVerified { get; set; }
    public bool LocalExpertApproved { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? RegistrationSourceId { get; set; }
    public string? LabelSourceId { get; set; }
    public string? ReviewedBy { get; set; }
    public string? ReviewedOn { get; set; }
    public string? ReviewDueOn { get; set; }
    public List<string> ApprovedDiseaseKeys { get; set; } = new();
    public List<string> ApprovedProvinces { get; set; } = new();
    public List<string> ApprovedStages { get; set; } = new();
    public double? DoseMlPerAcre { get; set; }
    public double? WaterLitresPerAcre { get; set; }
    public int? PhiDays { get; set; }
    public int? ReiHours { get; set; }
    public int? MaxApplications { get; set; }
    public int? MinIntervalDays { get; set; }
    public string? ApplicationInstructions { get; set; }
    public string? PpeInstructions { get; set; }
    public string? ResistanceInstructions { get; set; }
}
public sealed class FieldPaths
{
    public string Crop { get; set; } = "Crop";
    public string Disease { get; set; } = "Disease.Name";
    public string Confidence { get; set; } = "Confidence";
    public string SecondConfidence { get; set; } = "";
    public string CropAccepted { get; set; } = "";
    public string ImageUsable { get; set; } = "";
    public string InferenceSucceeded { get; set; } = "";
    public string ModelVersion { get; set; } = "";
}
public sealed class TreatmentSettings
{
    public int SchemaVersion { get; set; }
    public string ConfidenceScale { get; set; } = "unconfigured";
    public double MinimumConfidence { get; set; } = 0.85;
    public double MinimumMargin { get; set; } = 0.15;
    public bool ThresholdsValidated { get; set; }
    public string? CalibrationReference { get; set; }
    public string? CalibrationReviewedOn { get; set; }
    public string? CalibrationReviewDueOn { get; set; }
    public string? ModelVersion { get; set; }
    public FieldPaths Paths { get; set; } = new();
    public string Notes { get; set; } = "";
}
public sealed class PredictionInput
{
    public string? Crop { get; set; }
    public string? Disease { get; set; }
    public double? Confidence { get; set; }
    public double? SecondConfidence { get; set; }
    public bool? CropAccepted { get; set; }
    public bool? ImageUsable { get; set; }
    public bool? InferenceSucceeded { get; set; }
    public string? ModelVersion { get; set; }
}
public sealed class FarmContext
{
    public bool? WhiteflyTreatmentNeeded { get; set; }
    public string ProductId { get; set; } = "";
    public string Country { get; set; } = "PK";
    public string Province { get; set; } = "";
    public string Stage { get; set; } = "";
    public int? DaysToHarvest { get; set; }
    // Unknown is not equivalent to no previous applications.
    public int? PreviousApplicationsOfProduct { get; set; }
    public int? DaysSinceLastApplication { get; set; }
}
public sealed class Prescription
{
    public string ProductId { get; set; } = "";
    public string Brand { get; set; } = "";
    public string ActiveIngredient { get; set; } = "";
    public string Formulation { get; set; } = "";
    public double DoseMlPerAcre { get; set; }
    public double WaterLitresPerAcre { get; set; }
    public int PhiDays { get; set; }
    public int ReiHours { get; set; }
    public string Instructions { get; set; } = "";
    public string Ppe { get; set; } = "";
    public string Resistance { get; set; } = "";
    public string ReviewedBy { get; set; } = "";
    public string ReviewDueOn { get; set; } = "";
    public string LabelUrl { get; set; } = "";
    public string RegistrationUrl { get; set; } = "";
}
public sealed class TreatmentResult
{
    public List<ChemicalOptionCard> ChemicalOptions { get; set; } = new();
    public string State { get; set; } = "unavailable";
    public string Message { get; set; } = "Treatment guidance is unavailable. Please contact your local agriculture office.";
    public string CatalogVersion { get; set; } = "";
    public ConditionRecord? Condition { get; set; }
    public double? Confidence { get; set; }
    public bool CalibrationValidated { get; set; }
    public List<Prescription> Prescriptions { get; set; } = new();
    public List<EvidenceSource> Sources { get; set; } = new();
    public string ChemicalMessage { get; set; } = "No locally approved chemical plan is available for this result. Ask an agriculture officer to review the diagnosis and current product label.";
}
public sealed class TreatmentPage
{
    public int? DetectionId { get; set; }
    public FarmContext Context { get; set; } = new();
    public TreatmentResult Result { get; set; } = new();
}
public sealed class EvidencePage
{
    public Catalog Catalog { get; set; } = new();
    public List<ProductReview> Products { get; set; } = new();
}
