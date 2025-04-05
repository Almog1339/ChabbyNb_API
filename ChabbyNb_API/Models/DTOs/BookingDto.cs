using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace ChabbyNb_API.Models.DTOs
{
    #region Booking Request & Response DTOs

    public class BookingCreateDto
    {
        [Required]
        public int ApartmentID { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime CheckInDate { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime CheckOutDate { get; set; }

        [Required]
        [Range(1, 20, ErrorMessage = "Please enter a valid number of guests between 1 and 20")]
        public int GuestCount { get; set; }

        [Range(0, 5, ErrorMessage = "Please enter a valid number of pets between 0 and 5")]
        public int PetCount { get; set; }

        [StringLength(500, ErrorMessage = "Special requests cannot be longer than 500 characters")]
        public string SpecialRequests { get; set; }

        // Added for promotions
        [StringLength(20)]
        public string PromotionCode { get; set; }
    }

    public class PaymentConfirmationDto
    {
        [Required]
        public string PaymentIntentId { get; set; }

        [Required]
        public string PaymentMethodId { get; set; }
    }

    public class BookingResponseDto
    {
        public int BookingID { get; set; }
        public int ApartmentID { get; set; }
        public string ApartmentTitle { get; set; }
        public string PrimaryImageUrl { get; set; }
        public DateTime CheckInDate { get; set; }
        public DateTime CheckOutDate { get; set; }
        public int GuestCount { get; set; }
        public int PetCount { get; set; }
        public decimal BasePrice { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TotalPrice { get; set; }
        public string PromotionCode { get; set; }
        public string BookingStatus { get; set; }
        public string PaymentStatus { get; set; }
        public string ReservationNumber { get; set; }
        public DateTime CreatedDate { get; set; }
        public string Address { get; set; }
        public string Neighborhood { get; set; }
        public decimal PricePerNight { get; set; }
        public int NightsCount => (CheckOutDate - CheckInDate).Days;
        public bool HasReview { get; set; }
        public string PaymentIntentClientSecret { get; set; }
        public string PaymentIntentId { get; set; }
    }

    #endregion

    #region Booking Cancellation DTOs

    public class BookingCancellationDto
    {
        public int BookingID { get; set; }
        public DateTime CheckInDate { get; set; }
        public DateTime CheckOutDate { get; set; }
        public decimal BasePrice { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TotalPrice { get; set; }
        public string CancellationPolicy { get; set; }

        [Required]
        public string CancellationReason { get; set; }

        public bool FullRefund { get; set; } = true;

        // Only one RefundAmount property
        public decimal? RefundAmount { get; set; }
    }

    #endregion

    #region Pricing and Promotion DTOs

    // For daily price calculations
    public class DailyPriceDto
    {
        public DateTime Date { get; set; }
        public decimal Price { get; set; }
        public string PriceType { get; set; }
    }

    #endregion

    #region Seasonal Pricing DTOs

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

    #endregion

    #region Promotion DTOs

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


    #endregion

    #region Review DTOs

    public class ReviewDto
    {
        public int ReviewID { get; set; }
        public int BookingID { get; set; }
        public int ApartmentID { get; set; }
        public string ApartmentTitle { get; set; }

        [Range(1, 5, ErrorMessage = "Please select a rating between 1 and 5")]
        public int Rating { get; set; }

        [Required(ErrorMessage = "Please provide a comment about your stay")]
        public string Comment { get; set; }

        public DateTime CheckInDate { get; set; }
        public DateTime CheckOutDate { get; set; }
    }

    #endregion
}