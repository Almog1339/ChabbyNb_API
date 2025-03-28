using System;
using System.ComponentModel.DataAnnotations;

namespace ChabbyNb_API.Models.DTOs
{
    public class PromotionDto
    {
        public int PromotionID { get; set; }

        public string Name { get; set; }

        public string Code { get; set; }

        public string Description { get; set; }

        public string DiscountType { get; set; }

        public decimal DiscountValue { get; set; }

        public decimal? MinimumStayNights { get; set; }

        public decimal? MinimumBookingAmount { get; set; }

        public decimal? MaximumDiscountAmount { get; set; }

        public DateTime? StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        public int? UsageLimit { get; set; }

        public int UsageCount { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedDate { get; set; }

        public int? ApartmentID { get; set; }

        public string ApartmentTitle { get; set; }

        // Helper properties for display
        public string StatusDisplay { get; set; }
        public string DateRangeDisplay { get; set; }
        public string DiscountDisplay { get; set; }
        public bool IsExpired => EndDate.HasValue && EndDate.Value < DateTime.Today;
        public bool IsUsageLimitReached => UsageLimit.HasValue && UsageCount >= UsageLimit.Value;
    }

    public class CreatePromotionDto
    {
        [Required]
        [StringLength(100)]
        public string Name { get; set; }

        [Required]
        [StringLength(20)]
        [RegularExpression(@"^[A-Z0-9]{3,20}$", ErrorMessage = "Code must be 3-20 uppercase letters or numbers, no spaces or special characters")]
        public string Code { get; set; }

        [StringLength(500)]
        public string Description { get; set; }

        [Required]
        [RegularExpression(@"^(Percentage|FixedAmount)$", ErrorMessage = "Discount type must be either 'Percentage' or 'FixedAmount'")]
        public string DiscountType { get; set; }

        [Required]
        [Range(0.01, 100, ErrorMessage = "For percentage discount, value must be between 0.01 and 100")]
        public decimal DiscountValue { get; set; }

        [Range(1, 365, ErrorMessage = "Minimum stay must be between 1 and 365 nights")]
        public decimal? MinimumStayNights { get; set; }

        [Range(0.01, 100000, ErrorMessage = "Minimum booking amount must be positive")]
        public decimal? MinimumBookingAmount { get; set; }

        [Range(0.01, 10000, ErrorMessage = "Maximum discount amount must be positive")]
        public decimal? MaximumDiscountAmount { get; set; }

        [DataType(DataType.Date)]
        public DateTime? StartDate { get; set; }

        [DataType(DataType.Date)]
        public DateTime? EndDate { get; set; }

        [Range(1, 1000, ErrorMessage = "Usage limit must be between 1 and 1000")]
        public int? UsageLimit { get; set; }

        public int? ApartmentID { get; set; }
    }

    public class UpdatePromotionDto
    {
        public int PromotionID { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; }

        [StringLength(500)]
        public string Description { get; set; }

        [Required]
        [RegularExpression(@"^(Percentage|FixedAmount)$", ErrorMessage = "Discount type must be either 'Percentage' or 'FixedAmount'")]
        public string DiscountType { get; set; }

        [Required]
        [Range(0.01, 100, ErrorMessage = "For percentage discount, value must be between 0.01 and 100")]
        public decimal DiscountValue { get; set; }

        [Range(1, 365, ErrorMessage = "Minimum stay must be between 1 and 365 nights")]
        public decimal? MinimumStayNights { get; set; }

        [Range(0.01, 100000, ErrorMessage = "Minimum booking amount must be positive")]
        public decimal? MinimumBookingAmount { get; set; }

        [Range(0.01, 10000, ErrorMessage = "Maximum discount amount must be positive")]
        public decimal? MaximumDiscountAmount { get; set; }

        [DataType(DataType.Date)]
        public DateTime? StartDate { get; set; }

        [DataType(DataType.Date)]
        public DateTime? EndDate { get; set; }

        [Range(1, 1000, ErrorMessage = "Usage limit must be between 1 and 1000")]
        public int? UsageLimit { get; set; }

        public bool IsActive { get; set; }

        public int? ApartmentID { get; set; }
    }

    public class VerifyPromotionDto
    {
        [Required]
        public string Code { get; set; }

        [Required]
        public int ApartmentID { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime CheckInDate { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime CheckOutDate { get; set; }

        public decimal BookingAmount { get; set; }
    }

    public class PromotionValidationResultDto
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; }
        public PromotionDto Promotion { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal FinalPrice { get; set; }
    }
}