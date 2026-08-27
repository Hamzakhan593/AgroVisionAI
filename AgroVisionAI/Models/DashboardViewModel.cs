namespace AgroVisionAI.Models
{
    public class DashboardViewModel
    {
        public ApplicationUser User { get; set; } = null!;

        public List<Detection> RecentDetections { get; set; } = new();
    }
}