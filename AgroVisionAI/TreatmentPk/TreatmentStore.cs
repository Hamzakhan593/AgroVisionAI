using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AgroVisionAI.TreatmentPk;

public sealed class TreatmentStore
{
    public Catalog Catalog { get; }
    public ProductFile ProductFile { get; }
    public TreatmentSettings Settings { get; }
    public ChemicalOptionsFile ChemicalFile { get; }
    public TreatmentEngine Engine { get; }
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public TreatmentStore(string contentRoot)
    {
        var folder = Path.Combine(contentRoot, "App_Data", "TreatmentPk");
        Catalog = Read<Catalog>(Path.Combine(folder, "catalog.json"));
        ProductFile = Read<ProductFile>(Path.Combine(folder, "products.review.json"));
        Settings = Read<TreatmentSettings>(Path.Combine(folder, "settings.json"));
        ChemicalFile = Read<ChemicalOptionsFile>(Path.Combine(folder, "chemical-options.json"));
        Validate();
        ChemicalOptionsPolicy.Validate(ChemicalFile, Catalog);
        Engine = new TreatmentEngine(Catalog, ProductFile.Products, Settings, chemicalOptions: ChemicalFile.Options);
    }
    private static T Read<T>(string path)
    {
        if (new FileInfo(path).Length > 2_000_000) throw new InvalidDataException("Treatment data exceeds size limit.");
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Empty treatment data.");
    }
    private static void Need(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }
    private void Validate()
    {
        Need(Catalog.SchemaVersion == 1 && ProductFile.SchemaVersion == 1 && Settings.SchemaVersion == 1, "Unsupported schema version.");
        Need(Catalog.Country == "PK" && !string.IsNullOrWhiteSpace(Catalog.CatalogVersion), "Invalid country/version.");
        Need(Catalog.Records != null && Catalog.Sources != null && ProductFile.Products != null && Settings.Paths != null, "Missing collections.");
        Need(Catalog.Sources!.All(s => s != null && !string.IsNullOrWhiteSpace(s.Id) && !string.IsNullOrWhiteSpace(s.Title) &&
            Uri.TryCreate(s.Url, UriKind.Absolute, out var u) && u.Scheme == "https" && string.IsNullOrEmpty(u.UserInfo) &&
            DateOnly.TryParseExact(s.CheckedOn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)), "Invalid source.");
        Need(Catalog.Sources.Select(s => s.Id).Distinct().Count() == Catalog.Sources.Count, "Duplicate source IDs.");
        var sourceIds = Catalog.Sources.Select(s => s.Id).ToHashSet();
        var keys = new HashSet<string>(); var aliases = new HashSet<string>();
        foreach (var r in Catalog.Records!)
        {
            Need(r != null && new[] { "cotton", "wheat", "rice" }.Contains(r.Crop) && !string.IsNullOrWhiteSpace(r.Key) &&
                !string.IsNullOrWhiteSpace(r.Name) && new[] { "healthy", "fungal", "bacterial", "viral" }.Contains(r.Kind), "Invalid condition.");
            Need(r!.Aliases != null && r.SourceIds != null && r.Summary != null && r.Today != null && r.Prevention != null && r.Avoid != null, "Missing condition lists.");
            Need(keys.Add(r.Crop + ":" + r.Key), "Duplicate crop/disease key.");
            foreach (var a in r.Aliases!.Prepend(r.Key).Select(TreatmentEngine.Key).Distinct())
                Need(!string.IsNullOrWhiteSpace(a) && aliases.Add(r.Crop + ":" + a), "Ambiguous crop alias.");
            Need(r.SourceIds!.Count > 0 && r.SourceIds.All(sourceIds.Contains), "Missing record evidence.");
            foreach (var tip in r.Today!.Concat(r.Prevention!).Concat(r.Avoid!).Append(r.Summary!))
                Need(tip != null && !string.IsNullOrWhiteSpace(tip.En) && !string.IsNullOrWhiteSpace(tip.RomanUrdu) &&
                    tip.SourceIds != null && tip.SourceIds.All(id => sourceIds.Contains(id) && r.SourceIds.Contains(id)), "Invalid guidance/evidence link.");
        }
        Need(ProductFile.Products!.Select(p => p.Id).Distinct().Count() == ProductFile.Products.Count, "Duplicate product IDs.");
        foreach (var p in ProductFile.Products)
        {
            Need(p != null && p.Country == "PK" && !string.IsNullOrWhiteSpace(p.Id) && !string.IsNullOrWhiteSpace(p.Brand) &&
                !string.IsNullOrWhiteSpace(p.ActiveIngredient) && !string.IsNullOrWhiteSpace(p.Formulation) &&
                sourceIds.Contains(p.ManufacturerSourceId) && p.DiseaseKeys != null && p.ApprovedDiseaseKeys != null && p.ApprovedProvinces != null && p.ApprovedStages != null, "Invalid product record.");
            Need(p!.DiseaseKeys!.All(k => Catalog.Records.Any(r => r.Crop == p.Crop && r.Key == k && r.Kind == "fungal")), "Product targets a non-fungal/unsupported disease.");
            Need(p.ApprovedDiseaseKeys!.All(p.DiseaseKeys.Contains), "Approved target not in reviewed targets.");
        }
        Need(new[] { "unconfigured", "fraction", "percent" }.Contains(Settings.ConfidenceScale), "Invalid confidence scale.");
        Need(double.IsFinite(Settings.MinimumConfidence) && Settings.MinimumConfidence >= .5 && Settings.MinimumConfidence <= 1 &&
            double.IsFinite(Settings.MinimumMargin) && Settings.MinimumMargin >= 0 && Settings.MinimumMargin <= 1, "Invalid thresholds.");
        foreach (var prop in typeof(FieldPaths).GetProperties())
        {
            var value = prop.GetValue(Settings.Paths) as string;
            Need(value != null && (value.Length == 0 || Regex.IsMatch(value, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$")), "Invalid field path.");
        }
        Need(!string.IsNullOrWhiteSpace(Settings.Paths!.Crop) && !string.IsNullOrWhiteSpace(Settings.Paths.Disease) &&
            !string.IsNullOrWhiteSpace(Settings.Paths.Confidence), "Required field paths missing.");
    }
    public PredictionInput ReadPrediction(object detection) => new()
    {
        Crop = Text(PathValue(detection, Settings.Paths.Crop)),
        Disease = Text(PathValue(detection, Settings.Paths.Disease)),
        Confidence = Number(PathValue(detection, Settings.Paths.Confidence)),
        SecondConfidence = Number(PathValue(detection, Settings.Paths.SecondConfidence)),
        CropAccepted = Boolean(PathValue(detection, Settings.Paths.CropAccepted)),
        ImageUsable = Boolean(PathValue(detection, Settings.Paths.ImageUsable)),
        InferenceSucceeded = Boolean(PathValue(detection, Settings.Paths.InferenceSucceeded)),
        ModelVersion = Text(PathValue(detection, Settings.Paths.ModelVersion))
    };
    private static object? PathValue(object? value, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        foreach (var part in path.Split('.'))
        {
            if (value == null) return null;
            if (value is IDictionary dict) value = dict.Contains(part) ? dict[part] : null;
            else if (value is JsonElement e) value = e.ValueKind == JsonValueKind.Object && e.TryGetProperty(part, out var child) ? child : null;
            else value = value.GetType().GetProperty(part)?.GetValue(value);
        }
        return value;
    }
    private static string? Text(object? v) => v is string s ? s : v is JsonElement e && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
    private static bool? Boolean(object? v) => v is bool b ? b : v is JsonElement e ? e.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null } : null;
    private static double? Number(object? v)
    {
        // Numeric strings are not silently parsed; persist actual JSON/C# numbers.
        if (v is JsonElement e) return e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var x) ? x : null;
        return v switch { double d => d, float f => f, decimal m => (double)m, int i => i, long l => l, _ => null };
    }
}
