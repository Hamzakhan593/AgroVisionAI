using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AgroVisionAI.TreatmentPk;

public sealed class ChemicalOptionsFile
{
    public int SchemaVersion { get; set; }
    public string Version { get; set; } = "";
    public List<ChemicalOption> Options { get; set; } = new();
}
// Public, attributed manufacturer evidence, distinct from independently reviewed Prescriptions.
public sealed class ChemicalOption
{
    public string Id { get; set; } = "";
    public string Country { get; set; } = "";
    public string Crop { get; set; } = "";
    public List<string> DiseaseKeys { get; set; } = new();
    public string Purpose { get; set; } = "";
    public string Brand { get; set; } = "";
    public string ActiveIngredient { get; set; } = "";
    public string Formulation { get; set; } = "";
    public string SourceTarget { get; set; } = "";
    public double DoseMlPerAcre { get; set; }
    public double? WaterLitresPerAcre { get; set; }
    public int PhiDays { get; set; }
    public int? ReiHours { get; set; }
    public int? RepeatIntervalDays { get; set; }
    public int? MaxApplications { get; set; }
    public string SourceUrl { get; set; } = "";
    public string SourceTitle { get; set; } = "";
    public string CheckedOn { get; set; } = "";
    public string ReviewDueOn { get; set; } = "";
    public string CoverageNote { get; set; } = "";
    public string Timing { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool RegistrationVerified { get; set; }
    public bool LabelVerified { get; set; }
    public bool LocalExpertApproved { get; set; }
}
public sealed class ChemicalOptionCard
{
    public ChemicalOption Product { get; set; } = new();
    public bool ShowPublishedDose { get; set; }
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
}
public static class ChemicalOptionsPolicy
{
    private static bool Date(string s, out DateOnly d) => DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
    public static void Validate(ChemicalOptionsFile file, Catalog catalog)
    {
        if (file.SchemaVersion != 1 || file.Options == null || string.IsNullOrWhiteSpace(file.Version)) throw new InvalidDataException("Invalid chemical-options file.");
        var ids = new HashSet<string>();
        foreach (var p in file.Options)
        {
            if (p == null || string.IsNullOrWhiteSpace(p.Id) || !ids.Add(p.Id) || p.Country != "PK" ||
                string.IsNullOrWhiteSpace(p.Brand) || string.IsNullOrWhiteSpace(p.ActiveIngredient) ||
                !new[] { "EC", "SC" }.Contains(p.Formulation) || !double.IsFinite(p.DoseMlPerAcre) || p.DoseMlPerAcre <= 0 ||
                (p.WaterLitresPerAcre.HasValue && (!double.IsFinite(p.WaterLitresPerAcre.Value) || p.WaterLitresPerAcre <= 0)) ||
                p.PhiDays < 0 || p.ReiHours < 0 || p.RepeatIntervalDays <= 0 || p.MaxApplications <= 0 ||
                !Uri.TryCreate(p.SourceUrl, UriKind.Absolute, out var url) || url.Scheme != "https" || !string.IsNullOrEmpty(url.UserInfo) ||
                string.IsNullOrWhiteSpace(p.SourceTitle) || string.IsNullOrWhiteSpace(p.SourceTarget) ||
                string.IsNullOrWhiteSpace(p.CoverageNote) || string.IsNullOrWhiteSpace(p.Timing) || string.IsNullOrWhiteSpace(p.Notes) ||
                !Date(p.CheckedOn, out var start) || !Date(p.ReviewDueOn, out var end) || end < start || end.DayNumber - start.DayNumber > 180 ||
                p.DiseaseKeys == null || p.DiseaseKeys.Count == 0 || p.DiseaseKeys.Distinct().Count() != p.DiseaseKeys.Count)
                throw new InvalidDataException("Incomplete or invalid manufacturer evidence.");
            foreach (var key in p.DiseaseKeys)
            {
                var r = catalog.Records.SingleOrDefault(r => r.Crop == p.Crop && r.Key == key);
                if (r == null || !(p.Purpose == "fungal_disease" && r.Kind == "fungal" ||
                    p.Purpose == "vector_control" && r.Crop == "cotton" && r.Key == "curl_virus"))
                    throw new InvalidDataException("Chemical option targets an unsupported disease/purpose.");
            }
        }
    }
    public static List<ChemicalOptionCard> Select(IReadOnlyList<ChemicalOption> options, ConditionRecord record,
        FarmContext farm, DateOnly today, bool reference = false)
    {
        var cards = new List<ChemicalOptionCard>();
        if (farm.Country != "PK" || record.Kind == "healthy" || record.Kind == "bacterial") return cards;
        foreach (var p in options.Where(p => p.Country == "PK" && p.Crop == record.Crop && p.DiseaseKeys.Contains(record.Key)))
        {
            if (!(p.Purpose == "fungal_disease" && record.Kind == "fungal" || p.Purpose == "vector_control" && record.Crop == "cotton" && record.Key == "curl_virus")) continue;
            var card = new ChemicalOptionCard { Product = p, ShowPublishedDose = true, Status = "manufacturer_reference",
                Message = "Published Pakistan manufacturer option. Confirm the suspected diagnosis, exact bottle label and current local suitability before applying." };
            if (!Date(p.CheckedOn, out var checkedOn) || !Date(p.ReviewDueOn, out var due) || checkedOn > today || due < today)
            { card.ShowPublishedDose = false; card.Status = "review_due"; card.Message = "This product evidence needs a new source check before displaying its dose."; }
            else if (farm.DaysToHarvest is < 0 or > 365)
            { card.ShowPublishedDose = false; card.Status = "invalid_harvest"; card.Message = "Enter valid days to harvest between 0 and 365, or leave the field blank."; }
            else if (farm.DaysToHarvest.HasValue && farm.DaysToHarvest.Value < p.PhiDays)
            { card.ShowPublishedDose = false; card.Status = "harvest_conflict"; card.Message = "Do not use this option for the entered harvest date: the published minimum waiting period cannot be met."; }
            else if (!reference && p.Purpose == "vector_control" && farm.WhiteflyTreatmentNeeded != true)
            { card.ShowPublishedDose = false; card.Status = "assess_whiteflies"; card.Message = "Whitefly control only, not a virus cure. The leaf image cannot establish a need for insecticide. Confirm treatment need through a local field assessment in the full report."; }
            else if (reference)
            { card.Message = "Reference rate only; choosing a condition is not a field diagnosis or a recommendation to spray."; }
            else if (!farm.DaysToHarvest.HasValue)
            { card.Message = "Published reference rate. Days to harvest are unknown; check the minimum harvest waiting period in the full report before use."; }
            cards.Add(card);
        }
        return cards;
    }
}
