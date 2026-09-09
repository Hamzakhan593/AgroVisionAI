using AgroVisionAI.TreatmentPk;
using System.Text.Json;

int passed = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception("FAILED: " + name); Console.WriteLine("PASS " + name); passed++; }
T Clone<T>(T obj) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(obj))!;
var store = new TreatmentStore(AppContext.BaseDirectory);
var settings = Clone(store.Settings); settings.ConfidenceScale = "fraction";
var day = new DateOnly(2026, 9, 6);
TreatmentEngine Engine() => new(store.Catalog, store.ProductFile.Products, settings, day);
PredictionInput Good(string crop = "rice", string disease = "leaf_blast") => new() { Crop = crop, Disease = disease,
    Confidence = .96, SecondConfidence = .03, CropAccepted = true, ImageUsable = true, InferenceSucceeded = true, ModelVersion = "SYNTHETIC_TEST_ONLY" };
Check(store.Catalog.Records.Count == 11, "11 conditions loaded");
foreach (var r in store.Catalog.Records)
{
    var result = Engine().Evaluate(Good(r.Crop, r.Key));
    Check(result.Condition?.Key == r.Key && result.Condition.Crop == r.Crop && result.Prescriptions.Count == 0, "seeded condition " + r.Crop + ":" + r.Key);
}
Check(Engine().Evaluate(Good("wheat", "bacterial_blight")).State == "unsupported", "cross-crop disease rejected");
Check(Engine().Evaluate(Good("cotton", "fussarium_wilt")).State == "unsupported", "untrained cotton class rejected");
Check(Engine().Evaluate(Good("rice", "blight-like")).State == "unsupported", "fuzzy label rejected");
Check(Engine().Evaluate(Good("rice", "Rice Brown Leaf Spot")).Condition?.Key == "brown_spot", "explicit alias accepted");
var input = Good(); input.CropAccepted = false;
Check(Engine().Evaluate(input).State == "out_of_scope", "out-of-scope gate");
input = Good(); input.ImageUsable = false;
Check(Engine().Evaluate(input).Condition == null, "bad photo gate");
input = Good(); input.InferenceSucceeded = false;
Check(Engine().Evaluate(input).State == "awaiting_prediction", "failed inference gate");
input = Good(); input.Disease = null;
Check(Engine().Evaluate(input).State == "awaiting_prediction", "upload-only record rejected");
foreach (double? invalid in new double?[] { null, double.NaN, double.PositiveInfinity, -1, 95 })
{ input = Good(); input.Confidence = invalid; Check(Engine().Evaluate(input).Condition == null, "invalid fraction " + invalid); }
input = Good(); input.Confidence = .6;
Check(Engine().Evaluate(input).State == "uncertain", "low confidence");
input = Good(); input.SecondConfidence = .9;
Check(Engine().Evaluate(input).State == "uncertain", "invalid probability sum/margin");
settings.ConfidenceScale = "percent"; input = Good(); input.Confidence = 96; input.SecondConfidence = 3;
Check(Engine().Evaluate(input).Condition?.Key == "leaf_blast", "explicit percentage scale");
settings.ConfidenceScale = "unconfigured";
Check(Engine().Evaluate(input).Condition == null, "unconfigured scale fails closed");
settings.ConfidenceScale = "fraction";
Check(Engine().Educational("rice", "leaf_blast").Prescriptions.Count == 0, "reference mode never prescribes");
Check(Engine().Evaluate(Good(), new FarmContext { Country = "IN" }).Condition == null, "country scope");
// Synthetic evidence ONLY. These objects are not persisted to the real catalogue.
var catalog = Clone(store.Catalog);
var record = catalog.Records.Single(r => r.Crop == "rice" && r.Key == "leaf_blast");
record.ReviewStatus = "local_expert_reviewed"; record.LocalExpert = "SYNTHETIC TEST REVIEWER";
catalog.Sources.Add(new() { Id = "test_label", Title = "Synthetic", Url = "https://example.invalid/label", Kind = "label", Access = "page_read", CheckedOn = "2026-09-06" });
catalog.Sources.Add(new() { Id = "test_registration", Title = "Synthetic", Url = "https://example.invalid/registration", Kind = "registration", Access = "page_read", CheckedOn = "2026-09-06" });
var product = Clone(store.ProductFile.Products.Single(p => p.Id == "score_rice_blast"));
product.Status = "approved"; product.RegistrationVerified = product.LabelVerified = product.LocalExpertApproved = true;
product.RegistrationNumber = "SYNTHETIC"; product.RegistrationSourceId = "test_registration"; product.LabelSourceId = "test_label";
product.ReviewedBy = "SYNTHETIC"; product.ReviewedOn = "2026-09-01"; product.ReviewDueOn = "2026-12-01";
product.ApprovedDiseaseKeys = new() { "leaf_blast" }; product.ApprovedProvinces = new() { "Sindh" }; product.ApprovedStages = new() { "vegetative" };
product.DoseMlPerAcre = 1; product.WaterLitresPerAcre = 1; product.PhiDays = 14; product.ReiHours = 24;
product.MaxApplications = 2; product.MinIntervalDays = 10; product.ApplicationInstructions = product.PpeInstructions = product.ResistanceInstructions = "SYNTHETIC TEST VALUE";
settings.ThresholdsValidated = true; settings.CalibrationReference = "SYNTHETIC TEST"; settings.ModelVersion = "SYNTHETIC_TEST_ONLY";
settings.CalibrationReviewedOn = "2026-09-01"; settings.CalibrationReviewDueOn = "2026-12-01";
var farm = new FarmContext { ProductId = product.Id, Province = "Sindh", Stage = "vegetative", DaysToHarvest = 14, PreviousApplicationsOfProduct = 0 };
TreatmentResult Evaluate(ProductReview p, FarmContext f, PredictionInput? i = null) => new TreatmentEngine(catalog, new[] { p }, settings, day).Evaluate(i ?? Good(), f);
Check(Evaluate(product, farm).Prescriptions.Count == 1, "synthetic fully reviewed plan can pass");
foreach (var mutation in new Action<ProductReview>[] {
    p => p.RegistrationVerified = false, p => p.LabelVerified = false, p => p.LocalExpertApproved = false,
    p => p.Status = "manufacturer_page_only", p => p.RegistrationNumber = null, p => p.ReviewedBy = null,
    p => p.ReviewDueOn = "2026-09-05", p => p.ReviewedOn = "2026-09-07", p => p.LabelSourceId = "pk_score",
    p => p.RegistrationSourceId = "pk_registry", p => p.ReiHours = null, p => p.PhiDays = null,
    p => p.DoseMlPerAcre = double.NaN, p => p.WaterLitresPerAcre = 0, p => p.PpeInstructions = null,
    p => p.ApprovedDiseaseKeys.Clear(), p => p.ApprovedProvinces.Clear(), p => p.ApprovedStages.Clear(),
    p => p.ApplicationInstructions = null, p => p.ResistanceInstructions = null })
{ var copy = Clone(product); mutation(copy); Check(Evaluate(copy, farm).Prescriptions.Count == 0, "incomplete product evidence fails closed"); }
foreach (var mutation in new Action<FarmContext>[] {
    f => f.ProductId = "another_product", f => f.Province = "Punjab", f => f.Stage = "", f => f.DaysToHarvest = 13,
    f => f.DaysToHarvest = null, f => f.PreviousApplicationsOfProduct = null,
    f => f.PreviousApplicationsOfProduct = 2, f => { f.PreviousApplicationsOfProduct = 1; f.DaysSinceLastApplication = 9; } })
{ var copy = Clone(farm); mutation(copy); Check(Evaluate(product, copy).Prescriptions.Count == 0, "field/product history gate"); }
input = Good(); input.SecondConfidence = null;
Check(Evaluate(product, farm, input).Prescriptions.Count == 0, "unknown second score");
input = Good(); input.ModelVersion = "other model";
Check(Evaluate(product, farm, input).Prescriptions.Count == 0, "model calibration mismatch");
settings.ThresholdsValidated = false;
Check(Evaluate(product, farm).Prescriptions.Count == 0, "unvalidated thresholds");
Console.WriteLine($"{passed} checks passed. Synthetic approval was never saved to production data.");
// v2 public manufacturer options are distinct from independently reviewed prescriptions.
settings.ConfidenceScale = "fraction";
TreatmentEngine ChemEngine() => new(store.Catalog, store.ProductFile.Products, settings, day, store.ChemicalFile.Options);
var chem = ChemEngine().Evaluate(Good());
Check(chem.ChemicalOptions.Count == 2 && chem.ChemicalOptions.All(c => c.ShowPublishedDose), "rice blast has two visible documented rates without false approval");
Check(chem.Prescriptions.Count == 0, "manufacturer evidence never becomes an approved prescription");
chem = ChemEngine().Evaluate(Good("rice", "brown_spot"));
Check(chem.ChemicalOptions.Single().Product.DoseMlPerAcre == 200, "brown spot uses its own 200 ml rate");
chem = ChemEngine().Evaluate(Good("wheat", "yellow_rust"));
Check(chem.ChemicalOptions.Single().Product.SourceTarget == "Wheat / Rust", "wheat preserves broad source target wording");
chem = ChemEngine().Evaluate(Good(), new FarmContext { DaysToHarvest = 10 });
Check(chem.ChemicalOptions.All(c => c.Status == "harvest_conflict" && !c.ShowPublishedDose), "near harvest withholds all incompatible rates");
chem = ChemEngine().Evaluate(Good(), new FarmContext { DaysToHarvest = 20 });
Check(chem.ChemicalOptions.Single(c => c.Product.Id == "score_rice_blast").ShowPublishedDose &&
    !chem.ChemicalOptions.Single(c => c.Product.Id == "amistar_rice_blast").ShowPublishedDose, "PHI applied separately to each alternative");
chem = ChemEngine().Evaluate(Good("cotton", "curl_virus"));
Check(chem.ChemicalOptions.Single().Status == "assess_whiteflies" && !chem.ChemicalOptions.Single().ShowPublishedDose, "leaf-curl prediction alone does not select insecticide dose");
chem = ChemEngine().Evaluate(Good("cotton", "curl_virus"), new FarmContext { WhiteflyTreatmentNeeded = true, DaysToHarvest = 30 });
Check(chem.ChemicalOptions.Single().ShowPublishedDose && chem.ChemicalOptions.Single().Product.Purpose == "vector_control", "confirmed vector need displays whitefly reference rate");
foreach (var pair in new[] { ("rice", "tungro"), ("rice", "bacterial_leaf_blight"), ("cotton", "bacterial_blight"), ("rice", "healthy") })
    Check(ChemEngine().Evaluate(Good(pair.Item1, pair.Item2)).ChemicalOptions.Count == 0, "no unsupported pesticide for " + pair.Item2);
input = Good(); input.Confidence = .5;
Check(ChemEngine().Evaluate(input).ChemicalOptions.Count == 0, "uncertain image has no chemical options");
chem = new TreatmentEngine(store.Catalog, store.ProductFile.Products, settings, new DateOnly(2027, 3, 6), store.ChemicalFile.Options).Evaluate(Good());
Check(chem.ChemicalOptions.All(c => c.Status == "review_due" && !c.ShowPublishedDose), "outdated evidence withholds rates");
Check(ChemEngine().Educational("rice", "leaf_blast").ChemicalOptions.Count == 2, "reference catalogue also contains chemicals");
Console.WriteLine($"Including v2: {passed} checks passed.");
