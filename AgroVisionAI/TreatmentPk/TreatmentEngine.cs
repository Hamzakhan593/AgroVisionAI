using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AgroVisionAI.TreatmentPk;

// Pure deterministic engine. Never calls a chatbot or turns a confidence into severity/dose.
public sealed class TreatmentEngine
{
    private readonly Catalog catalog;
    private readonly IReadOnlyList<ProductReview> products;
    private readonly TreatmentSettings settings;
    private readonly DateOnly today;
    private readonly IReadOnlyList<ChemicalOption> chemicalOptions;
    public TreatmentEngine(Catalog catalog, IReadOnlyList<ProductReview> products,
        TreatmentSettings settings, DateOnly? today = null, IReadOnlyList<ChemicalOption>? chemicalOptions = null)
    {
        this.catalog = catalog; this.products = products; this.settings = settings;
        this.chemicalOptions = chemicalOptions ?? Array.Empty<ChemicalOption>();
        this.today = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
    }
    public static string Key(string? text) => Regex.Replace((text ?? "").Trim().ToLowerInvariant(), @"[\s-]+", "_");
    public ConditionRecord? Find(string? crop, string? disease)
    {
        var c = Key(crop); var d = Key(disease);
        // No substring/fuzzy match; aliases are explicitly reviewed within each crop.
        return catalog.Records.SingleOrDefault(r => r.Crop == c &&
            (r.Key == d || r.Aliases.Any(a => Key(a) == d)));
    }
    public TreatmentResult Educational(string? crop, string? disease)
    {
        var r = Find(crop, disease);
        if (r == null) return Result("unsupported", "No matching crop and disease record is available.");
        var result = WithRecord(r, "reference", "Reference information only. Selecting a condition is not an AI diagnosis.");
        AddChemicalOptions(result, new FarmContext(), true);
        return result;
    }
    public TreatmentResult Evaluate(PredictionInput input, FarmContext? farm = null)
    {
        farm ??= new();
        if (farm.Country != "PK") return Result("unsupported", "This catalogue covers Pakistan only.");
        if (input.CropAccepted == false || Key(input.Crop) == "out_of_scope" || Key(input.Disease) == "out_of_scope")
            return Result("out_of_scope", "This image is outside the supported crops. No treatment has been selected.");
        if (input.InferenceSucceeded == false)
            return Result("awaiting_prediction", "AI analysis did not complete. Run detection again before requesting treatment.");
        if (input.ImageUsable == false)
            return Result("uncertain", "Please upload a clear leaf photo and a wider plant photo. No disease-specific treatment was selected.");
        if (string.IsNullOrWhiteSpace(input.Disease))
            return Result("awaiting_prediction", "A saved disease prediction is needed. Your upload alone cannot select a treatment.");
        var record = Find(input.Crop, input.Disease);
        if (record == null) return Result("unsupported", "The saved crop and disease do not match a supported treatment record.");
        var confidence = Normalize(input.Confidence);
        if (confidence == null)
            return Result("uncertain", "Prediction confidence is missing or its format is not configured. Please review the result with an agriculture officer.");
        if (confidence < settings.MinimumConfidence)
            return Result("uncertain", "The model is uncertain. Take clearer photos or request field diagnosis before choosing treatment.");
        var second = Normalize(input.SecondConfidence);
        if (input.SecondConfidence != null && (second == null || second > confidence || confidence + second > 1.000001 || confidence - second < settings.MinimumMargin))
            return Result("uncertain", "The prediction scores are ambiguous or invalid. Please obtain a clearer image or field diagnosis.");
        var result = WithRecord(record, record.Kind == "healthy" ? "healthy" : "guidance",
            record.Kind == "healthy" ? "No supported disease was identified in this image; continue observing the field." :
            "Management guidance for a suspected disease. Confirm the diagnosis in the field before chemical treatment.");
        AddChemicalOptions(result, farm, false);
        result.Confidence = confidence;
        result.CalibrationValidated = CalibrationReady(input);
        if (record.Kind == "healthy")
        { result.ChemicalMessage = "No pesticide is recommended on the basis of this healthy prediction."; return result; }
        if (record.Kind != "fungal" || record.ChemicalPolicy != "review_required") return result;
        if (!result.CalibrationValidated || second == null || input.CropAccepted != true || input.ImageUsable != true || input.InferenceSucceeded != true)
            return result;
        // Province, crop stage, harvest and spray history are explicit; unknown values fail closed.
        foreach (var p in products.Where(p => p.Crop == record.Crop && p.DiseaseKeys.Contains(record.Key)))
        {
            if (!ProductReady(p, record, farm)) continue;
            result.Prescriptions.Add(new Prescription {
                ProductId = p.Id, Brand = p.Brand, ActiveIngredient = p.ActiveIngredient, Formulation = p.Formulation,
                DoseMlPerAcre = p.DoseMlPerAcre!.Value, WaterLitresPerAcre = p.WaterLitresPerAcre!.Value,
                PhiDays = p.PhiDays!.Value, ReiHours = p.ReiHours!.Value,
                Instructions = p.ApplicationInstructions!, Ppe = p.PpeInstructions!, Resistance = p.ResistanceInstructions!,
                ReviewedBy = p.ReviewedBy!, ReviewDueOn = p.ReviewDueOn!,
                LabelUrl = catalog.Sources.Single(s => s.Id == p.LabelSourceId).Url,
                RegistrationUrl = catalog.Sources.Single(s => s.Id == p.RegistrationSourceId).Url
            });
        }
        if (result.Prescriptions.Count > 0)
            result.ChemicalMessage = "Reviewed alternatives, not a tank mix or a repeated-spray schedule. Follow the exact current container label and local field advice.";
        return result;
    }
    private void AddChemicalOptions(TreatmentResult result, FarmContext farm, bool reference)
    {
        if (result.Condition == null) return;
        result.ChemicalOptions = ChemicalOptionsPolicy.Select(chemicalOptions, result.Condition, farm, today, reference);
        if (result.ChemicalOptions.Count > 0)
            result.ChemicalMessage = "Pakistan manufacturer-documented options are shown below with their published rates. They are alternatives, not a tank mix. Verify the exact current container label and field need; these are not independently approved prescriptions.";
        else if (result.Condition.Kind == "bacterial")
            result.ChemicalMessage = "No sufficiently documented Pakistan crop-specific chemical option was established for this bacterial disease. Do not substitute a rice-blast or cotton fungal-blight product.";
        else if (result.Condition.Kind == "viral")
            result.ChemicalMessage = "No virus-curing pesticide is listed. Follow disease-management guidance; insect control is not a cure for an infected plant.";
    }
    private bool CalibrationReady(PredictionInput input) => settings.ThresholdsValidated &&
        !string.IsNullOrWhiteSpace(settings.CalibrationReference) &&
        !string.IsNullOrWhiteSpace(settings.ModelVersion) && input.ModelVersion == settings.ModelVersion &&
        Current(settings.CalibrationReviewedOn, settings.CalibrationReviewDueOn);
    private bool ProductReady(ProductReview p, ConditionRecord r, FarmContext f) =>
        p.Id == f.ProductId && p.Country == "PK" && p.Status == "approved" && p.RegistrationVerified && p.LabelVerified && p.LocalExpertApproved &&
        !string.IsNullOrWhiteSpace(p.RegistrationNumber) && !string.IsNullOrWhiteSpace(p.ReviewedBy) &&
        r.ReviewStatus == "local_expert_reviewed" && !string.IsNullOrWhiteSpace(r.LocalExpert) &&
        Current(p.ReviewedOn, p.ReviewDueOn) && SourceReady(p.RegistrationSourceId, "registration") && SourceReady(p.LabelSourceId, "label") &&
        p.ApprovedDiseaseKeys.Contains(r.Key) && !string.IsNullOrWhiteSpace(f.Province) && p.ApprovedProvinces.Contains(f.Province) &&
        !string.IsNullOrWhiteSpace(f.Stage) && p.ApprovedStages.Contains(f.Stage) &&
        Positive(p.DoseMlPerAcre) && Positive(p.WaterLitresPerAcre) && p.PhiDays >= 0 && p.ReiHours >= 0 &&
        p.MaxApplications > 0 && p.MinIntervalDays > 0 && f.DaysToHarvest >= p.PhiDays &&
        f.PreviousApplicationsOfProduct >= 0 && f.PreviousApplicationsOfProduct < p.MaxApplications &&
        (f.PreviousApplicationsOfProduct == 0 || f.DaysSinceLastApplication >= p.MinIntervalDays) &&
        !string.IsNullOrWhiteSpace(p.ApplicationInstructions) && !string.IsNullOrWhiteSpace(p.PpeInstructions) && !string.IsNullOrWhiteSpace(p.ResistanceInstructions);
    private bool SourceReady(string? id, string kind) => catalog.Sources.Any(s => s.Id == id && s.Kind == kind && s.Access == "page_read" &&
        DateOnly.TryParseExact(s.CheckedOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date <= today &&
        today.DayNumber - date.DayNumber <= 180);
    private bool Current(string? from, string? to) =>
        DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) &&
        DateOnly.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) &&
        start <= today && today <= end && start <= end && end.DayNumber - start.DayNumber <= 180;
    private static bool Positive(double? x) => x.HasValue && double.IsFinite(x.Value) && x > 0;
    private double? Normalize(double? value)
    {
        if (value == null || !double.IsFinite(value.Value)) return null;
        var v = settings.ConfidenceScale switch { "fraction" => value.Value, "percent" => value.Value / 100.0, _ => double.NaN };
        return double.IsFinite(v) && v >= 0 && v <= 1 ? v : null;
    }
    private TreatmentResult Result(string state, string message) => new() { State = state, Message = message, CatalogVersion = catalog.CatalogVersion };
    private TreatmentResult WithRecord(ConditionRecord r, string state, string message)
    {
        var result = Result(state, message); result.Condition = r;
        result.Sources = catalog.Sources.Where(s => r.SourceIds.Contains(s.Id)).ToList();
        return result;
    }
}
