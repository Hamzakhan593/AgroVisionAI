using System.ComponentModel.DataAnnotations;

namespace AgroVisionAI.Models
{
    public class Disease
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Crop { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string Description { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string Symptoms { get; set; } = string.Empty;

        [MaxLength(3000)]
        public string Treatment { get; set; } = string.Empty;

        [MaxLength(3000)]
        public string Prevention { get; set; } = string.Empty;
    }
}