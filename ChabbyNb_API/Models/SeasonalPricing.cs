using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ChabbyNb.Models;

namespace ChabbyNb_API.Models
{
    public class SeasonalPricing
    {
        [Key]
        public int SeasonalPricingID { get; set; }

        public int ApartmentID { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } // e.g., "Summer 2025", "New Year's 2025", "Low Season"

        [Required]
        [DataType(DataType.Date)]
        public DateTime StartDate { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime EndDate { get; set; }

        [Required]
        [Column(TypeName = "decimal(10, 2)")]
        public decimal PricePerNight { get; set; }

        [StringLength(255)]
        public string Description { get; set; }

        // Priority for overlapping seasons - higher number takes precedence
        public int Priority { get; set; } = 0;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        [ForeignKey("ApartmentID")]
        public virtual Apartment Apartment { get; set; }
    }
}