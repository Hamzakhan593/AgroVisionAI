using AgroVisionAI.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AgroVisionAI.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Detection> Detections { get; set; }
        public DbSet<Disease> Diseases { get; set; }
        public DbSet<PredictionFeedback> PredictionFeedbacks { get; set; }
        public DbSet<AiModelRegistryEntry> AiModelRegistryEntries { get; set; }


        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // One current feedback record per saved detection. A user can edit the
            // record, but duplicate feedback rows for the same analysis are blocked.
            builder.Entity<PredictionFeedback>()
                .HasOne(item => item.Detection)
                .WithOne()
                .HasForeignKey<PredictionFeedback>(item => item.DetectionId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<PredictionFeedback>()
                .HasIndex(item => item.DetectionId)
                .IsUnique();

            builder.Entity<PredictionFeedback>()
                .HasIndex(item => item.UserId);

            builder.Entity<AiModelRegistryEntry>()
                .HasIndex(item => new { item.Crop, item.Filename })
                .IsUnique();

            builder.Entity<AiModelRegistryEntry>()
                .HasIndex(item => item.Status);
        }
    }
}