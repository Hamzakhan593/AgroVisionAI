using AgroVisionAI.Data;
using AgroVisionAI.Models;
using AgroVisionAI.Models.Reports;
using AgroVisionAI.TreatmentPk;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AgroVisionAI.Services
{
    /// <summary>
    /// Generates a portable report from a saved detection. The report contains the
    /// stored AI result, audit metadata, optional Grad-CAM explanation, human-review
    /// status and the same Pakistan treatment catalogue used by the web care plan.
    /// </summary>
    public sealed class DiagnosticReportService : IDiagnosticReportService
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<DiagnosticReportService> _logger;

        public DiagnosticReportService(
            ApplicationDbContext context,
            IWebHostEnvironment environment,
            ILogger<DiagnosticReportService> logger)
        {
            _context = context;
            _environment = environment;
            _logger = logger;
        }

        public async Task<byte[]> GenerateAsync(
            Detection detection,
            ApplicationUser user,
            int? daysToHarvest = null,
            bool? whiteflyTreatmentNeeded = null,
            CancellationToken cancellationToken = default)
        {
            if (detection.UserId != user.Id)
            {
                throw new InvalidOperationException("The requested report does not belong to this user.");
            }

            if (daysToHarvest is < 0 or > 365)
            {
                throw new ArgumentOutOfRangeException(nameof(daysToHarvest), "Days to harvest must be between 0 and 365.");
            }

            var feedback = await _context.PredictionFeedbacks
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item => item.DetectionId == detection.Id && item.UserId == user.Id,
                    cancellationToken);

            var treatment = BuildTreatment(detection, daysToHarvest, whiteflyTreatmentNeeded);
            var data = new DiagnosticReportData
            {
                Detection = detection,
                User = user,
                Feedback = feedback,
                Treatment = treatment,
                UploadedImage = ReadOptionalImage(Path.Combine(
                    _environment.ContentRootPath,
                    "App_Data",
                    "CropImages",
                    Path.GetFileName(detection.ImagePath))),
                ExplanationImage = ReadOptionalImage(Path.Combine(
                    _environment.ContentRootPath,
                    "App_Data",
                    "Explainability",
                    $"{detection.Id}.jpg")),
                DaysToHarvest = daysToHarvest,
                WhiteflyTreatmentNeeded = whiteflyTreatmentNeeded,
                GeneratedAtUtc = DateTime.UtcNow
            };

            return BuildPdf(data);
        }

        private TreatmentResult BuildTreatment(
            Detection detection,
            int? daysToHarvest,
            bool? whiteflyTreatmentNeeded)
        {
            try
            {
                var store = new TreatmentStore(_environment.ContentRootPath);
                var context = new FarmContext
                {
                    DaysToHarvest = daysToHarvest,
                    WhiteflyTreatmentNeeded = whiteflyTreatmentNeeded
                };
                return store.Engine.Evaluate(store.ReadPrediction(detection), context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Treatment data could not be loaded while generating report {DetectionId}.", detection.Id);
                return new TreatmentResult
                {
                    State = "unavailable",
                    Message = "Treatment guidance could not be loaded for this report. Please use the live care-plan page or contact a local agriculture office.",
                    ChemicalMessage = "Chemical guidance unavailable in this exported report."
                };
            }
        }

        private static byte[]? ReadOptionalImage(string path)
        {
            if (!File.Exists(path)) return null;

            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > 8 * 1024 * 1024) return null;

            try
            {
                return File.ReadAllBytes(path);
            }
            catch
            {
                return null;
            }
        }

        private static byte[] BuildPdf(DiagnosticReportData data)
        {
            var detection = data.Detection;
            var diseaseName = detection.Disease?.Name ?? "Prediction unavailable";
            var diseaseConfidence = Percent(detection.Confidence);
            var cropConfidence = Percent(detection.CropConfidence);
            var secondConfidence = Percent(detection.SecondConfidence);
            var confidenceMargin = Percent(detection.ConfidenceMargin);

            var document = Document.Create(document =>
            {
                document.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(28);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(style => style.FontSize(9).FontColor(Colors.Grey.Darken3));

                    page.Header().Column(header =>
                    {
                        header.Item().Row(row =>
                        {
                            row.RelativeItem().Column(left =>
                            {
                                left.Item().Text("AGROVISION AI").Bold().FontSize(18).FontColor(Colors.Green.Darken2);
                                left.Item().Text("Crop Diagnostic & Care Report").SemiBold().FontSize(12);
                            });

                            row.AutoItem().AlignRight().Column(right =>
                            {
                                right.Item().AlignRight().Text($"Report #{detection.Id}").Bold();
                                right.Item().AlignRight().Text(data.GeneratedAtUtc.ToString("dd MMM yyyy, HH:mm 'UTC'"));
                            });
                        });

                        header.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Green.Lighten2);
                    });

                    page.Content().PaddingVertical(12).Column(column =>
                    {
                        column.Spacing(12);

                        column.Item().Element(Card).Column(card =>
                        {
                            card.Spacing(6);
                            card.Item().Text("Report owner").Bold().FontSize(11).FontColor(Colors.Green.Darken2);
                            card.Item().Row(row =>
                            {
                                row.RelativeItem().Text($"Name: {Safe(data.User.FullName, "Not provided")}");
                                row.RelativeItem().Text($"Email: {Safe(data.User.Email, "Not provided")}");
                            });
                            card.Item().Text($"Analysis date: {detection.CreatedAt:dd MMM yyyy, HH:mm} UTC");
                        });

                        column.Item().Element(Card).Column(card =>
                        {
                            card.Spacing(8);
                            card.Item().Text("AI diagnosis summary").Bold().FontSize(12).FontColor(Colors.Green.Darken2);
                            card.Item().Row(row =>
                            {
                                row.RelativeItem().Column(left =>
                                {
                                    left.Spacing(4);
                                    left.Item().Text($"Crop: {detection.Crop}").Bold();
                                    left.Item().Text($"Suspected condition: {diseaseName}").Bold().FontSize(13);
                                    left.Item().Text($"Disease confidence: {diseaseConfidence}");
                                    left.Item().Text($"Crop confidence: {cropConfidence}");
                                });

                                row.RelativeItem().Column(right =>
                                {
                                    right.Spacing(4);
                                    right.Item().Text($"Second prediction: {Safe(detection.SecondPredictionName, "Unavailable")}");
                                    right.Item().Text($"Second confidence: {secondConfidence}");
                                    right.Item().Text($"Top-1 / Top-2 margin: {confidenceMargin}");
                                    right.Item().Text($"AI processing time: {Milliseconds(detection.ProcessingTimeMs)}");
                                });
                            });

                            card.Item().PaddingTop(3).Text(
                                "Important: model confidence is not disease severity, and a photo-based AI prediction is not a laboratory diagnosis.")
                                .FontSize(8).Italic().FontColor(Colors.Orange.Darken2);
                        });

                        if (data.UploadedImage != null || data.ExplanationImage != null)
                        {
                            column.Item().Row(row =>
                            {
                                row.Spacing(10);
                                if (data.UploadedImage != null)
                                {
                                    row.RelativeItem().Element(Card).Column(imageCard =>
                                    {
                                        imageCard.Spacing(6);
                                        imageCard.Item().Text("Uploaded crop image").Bold();
                                        imageCard.Item().Height(180).Image(data.UploadedImage).FitArea();
                                    });
                                }

                                if (data.ExplanationImage != null)
                                {
                                    row.RelativeItem().Element(Card).Column(imageCard =>
                                    {
                                        imageCard.Spacing(6);
                                        imageCard.Item().Text("Grad-CAM explanation").Bold();
                                        imageCard.Item().Height(180).Image(data.ExplanationImage).FitArea();
                                        imageCard.Item().Text("Shows model attention only; it is not an infected-area or severity map.")
                                            .FontSize(7).Italic();
                                    });
                                }
                            });
                        }

                        if (detection.Disease != null)
                        {
                            column.Item().Element(Card).Column(card =>
                            {
                                card.Spacing(5);
                                card.Item().Text("Condition information").Bold().FontSize(12).FontColor(Colors.Green.Darken2);
                                AddParagraph(card, "Description", detection.Disease.Description);
                                AddParagraph(card, "Common signs to inspect", detection.Disease.Symptoms);
                            });
                        }

                        AddTreatmentSection(column, data);
                        AddFeedbackSection(column, data.Feedback);

                        column.Item().Element(Card).Column(card =>
                        {
                            card.Spacing(4);
                            card.Item().Text("AI audit trail").Bold().FontSize(12).FontColor(Colors.Green.Darken2);
                            AddKeyValue(card, "Request ID", detection.RequestId);
                            AddKeyValue(card, "Crop model", JoinModel(detection.CropModelName, detection.CropModelVersion));
                            AddKeyValue(card, "Disease model", JoinModel(detection.DiseaseModelName, detection.DiseaseModelVersion));
                            AddKeyValue(card, "Internal predicted class", detection.PredictedClass);
                        });

                        column.Item()
                            .Background(Colors.Grey.Lighten4)
                            .Padding(10)
                            .Text("Disclaimer: AgroVisionAI is a decision-support tool. Confirm important field decisions with a qualified agriculture professional and the current product label. Chemical options in this report are not a substitute for local diagnosis, registration checks, or label instructions.")
                            .FontSize(8);
                    });

                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.DefaultTextStyle(style => style.FontSize(8).FontColor(Colors.Grey.Medium));
                        text.Span("AgroVisionAI - Report ");
                        text.CurrentPageNumber();
                        text.Span(" / ");
                        text.TotalPages();
                    });
                });
            });

            return document.GeneratePdf();
        }

        private static void AddTreatmentSection(ColumnDescriptor column, DiagnosticReportData data)
        {
            var result = data.Treatment;
            column.Item().Element(Card).Column(card =>
            {
                card.Spacing(6);
                card.Item().Text("Pakistan crop-care guidance").Bold().FontSize(12).FontColor(Colors.Green.Darken2);
                card.Item().Text(result.Message);

                if (data.DaysToHarvest.HasValue)
                    card.Item().Text($"Field context - days to harvest: {data.DaysToHarvest.Value}").FontSize(8);

                if (data.WhiteflyTreatmentNeeded.HasValue)
                    card.Item().Text($"Whitefly treatment need assessed: {(data.WhiteflyTreatmentNeeded.Value ? "Yes" : "No")}").FontSize(8);

                if (result.Condition != null)
                {
                    AddParagraph(card, "Summary", result.Condition.Summary?.En);
                    AddBulletList(card, "What to do now", result.Condition.Today?.Select(t => t.En));
                    AddBulletList(card, "Prevention", result.Condition.Prevention?.Select(t => t.En));
                    AddBulletList(card, "Avoid", result.Condition.Avoid?.Select(t => t.En));
                }

                if (result.ChemicalOptions.Count > 0)
                {
                    card.Item().PaddingTop(4).Text("Manufacturer-documented chemical options").Bold();
                    foreach (var option in result.ChemicalOptions.Take(4))
                    {
                        var product = option.Product;
                        card.Item().BorderLeft(3).BorderColor(Colors.Green.Lighten2).PaddingLeft(7).Column(item =>
                        {
                            item.Spacing(2);
                            item.Item().Text($"{product.Brand} - {product.ActiveIngredient} {product.Formulation}").SemiBold();
                            item.Item().Text(option.Message).FontSize(8);
                            if (option.ShowPublishedDose)
                            {
                                item.Item().Text($"Published rate: {product.DoseMlPerAcre:0.##} ml/acre | PHI: {product.PhiDays} day(s)")
                                    .FontSize(8);
                            }
                        });
                    }
                }

                if (!string.IsNullOrWhiteSpace(result.ChemicalMessage))
                    card.Item().Text(result.ChemicalMessage).FontSize(8).Italic();

                if (result.Sources.Count > 0)
                {
                    card.Item().PaddingTop(4).Text("Evidence sources").Bold();
                    foreach (var source in result.Sources.Take(8))
                        card.Item().Text($"- {source.Title} ({source.Url})").FontSize(7);
                }
            });
        }

        private static void AddFeedbackSection(ColumnDescriptor column, PredictionFeedback? feedback)
        {
            if (feedback == null) return;

            column.Item().Element(Card).Column(card =>
            {
                card.Spacing(4);
                card.Item().Text("Human feedback & expert review").Bold().FontSize(12).FontColor(Colors.Green.Darken2);
                AddKeyValue(card, "User verdict", feedback.Verdict.ToString());
                AddKeyValue(card, "Suggested condition", feedback.SuggestedDisease);
                AddKeyValue(card, "Review status", feedback.VerificationStatus.ToString());
                AddKeyValue(card, "Verified condition", feedback.VerifiedDisease);
                AddKeyValue(card, "User note", feedback.Comment);
                AddKeyValue(card, "Reviewer note", feedback.ReviewerNote);
                if (feedback.VerifiedAt.HasValue)
                    AddKeyValue(card, "Verified at", feedback.VerifiedAt.Value.ToString("dd MMM yyyy, HH:mm 'UTC'"));
            });
        }

        private static IContainer Card(IContainer container) => container
            .Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .CornerRadius(7)
            .Padding(10);

        private static void AddKeyValue(ColumnDescriptor column, string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            column.Item().Text(text =>
            {
                text.Span(label + ": ").SemiBold();
                text.Span(value);
            });
        }

        private static void AddParagraph(ColumnDescriptor column, string title, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            column.Item().Text(title).SemiBold();
            column.Item().Text(value.Trim());
        }

        private static void AddBulletList(ColumnDescriptor column, string title, IEnumerable<string>? values)
        {
            var items = values?.Where(value => !string.IsNullOrWhiteSpace(value)).Take(8).ToArray() ?? Array.Empty<string>();
            if (items.Length == 0) return;

            column.Item().Text(title).SemiBold();
            foreach (var item in items)
                column.Item().PaddingLeft(6).Text($"- {item}");
        }

        private static string Percent(double? value) =>
            value.HasValue && double.IsFinite(value.Value)
                ? $"{Math.Clamp(value.Value, 0, 1) * 100:0.0}%"
                : "Unavailable";

        private static string Milliseconds(double? value) =>
            value.HasValue && double.IsFinite(value.Value) && value.Value >= 0
                ? $"{value.Value:0} ms"
                : "Unavailable";

        private static string Safe(string? value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

        private static string JoinModel(string? name, string? version)
        {
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(version)) return "Unavailable";
            if (string.IsNullOrWhiteSpace(version)) return name!.Trim();
            if (string.IsNullOrWhiteSpace(name)) return version.Trim();
            return $"{name.Trim()} (v{version.Trim()})";
        }
    }
}
