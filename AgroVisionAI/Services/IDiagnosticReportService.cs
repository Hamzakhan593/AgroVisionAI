using AgroVisionAI.Models;

namespace AgroVisionAI.Services
{
    public interface IDiagnosticReportService
    {
        Task<byte[]> GenerateAsync(
            Detection detection,
            ApplicationUser user,
            int? daysToHarvest = null,
            bool? whiteflyTreatmentNeeded = null,
            CancellationToken cancellationToken = default);
    }
}
