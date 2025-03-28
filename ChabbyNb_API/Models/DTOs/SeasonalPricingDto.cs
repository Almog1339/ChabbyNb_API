using System;
using System.ComponentModel.DataAnnotations;

namespace ChabbyNb_API.Models.DTOs
{
    public class SeasonalPricingDto
    {
        public int SeasonalPricingID { get; set; }

        public int ApartmentID { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime StartDate { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime EndDate { get; set; }

        [Required]
        public decimal PricePerNight { get; set; }

        [StringLength(255)]
        public string Description { get; set; }

        public int Priority { get; set; }

        public bool IsActive { get; set; }

        // Helper properties for frontend display
        public string ApartmentTitle { get; set; }
        public decimal BasePrice { get; set; }
        public decimal PriceDifference { get; set; }
        public string PriceChangeDisplay { get; set; }
    }

    public class CreateSeasonalPricingDto
    {
        public int ApartmentID { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime StartDate { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime EndDate { get; set; }

        [Required]
        [Range(1, 10000)]
        public decimal PricePerNight { get; set; }

        [StringLength(255)]
        public string Description { get; set; }

        public int Priority { get; set; } = 0;
    }

    public class UpdateSeasonalPricingDto
    {
        public int SeasonalPricingID { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime StartDate { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime EndDate { get; set; }

        [Required]
        [Range(1, 10000)]
        public decimal PricePerNight { get; set; }

        [StringLength(255)]
        public string Description { get; set; }

        public int Priority { get; set; }

        public bool IsActive { get; set; }
    }
}