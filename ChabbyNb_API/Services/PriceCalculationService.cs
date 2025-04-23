using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ChabbyNb.Models;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using ChabbyNb_API.Models.DTOs;

namespace ChabbyNb_API.Services
{
    /// <summary>
    /// Interface for pricing calculations throughout the application
    /// </summary>
    public interface IPricingService
    {
        /// <summary>
        /// Calculates the price for each night of a stay
        /// </summary>
        Task<List<DailyPrice>> CalculateDailyPricesAsync(
            int apartmentId,
            DateTime checkIn,
            DateTime checkOut);

        /// <summary>
        /// Calculates the total booking price including taxes, fees, seasonal pricing, and promotions
        /// </summary>
        Task<BookingPriceResult> CalculateBookingPriceAsync(
            int apartmentId,
            DateTime checkIn,
            DateTime checkOut,
            int guestCount = 1,
            int petCount = 0,
            string promotionCode = null);

        /// <summary>
        /// Checks if a promotion code is valid and applicable
        /// </summary>
        Task<PromotionValidationResult> ValidatePromotionCodeAsync(
            string promotionCode,
            int apartmentId,
            DateTime checkIn,
            DateTime checkOut,
            decimal baseAmount);

        /// <summary>
        /// Calculates refund amount based on booking and cancellation policy
        /// </summary>
        Task<RefundCalculationResult> CalculateRefundAmountAsync(
            int bookingId,
            DateTime cancellationDate);

        /// <summary>
        /// Validates if dates are available for booking
        /// </summary>
        Task<bool> AreDatesAvailableAsync(
            int apartmentId,
            DateTime checkIn,
            DateTime checkOut,
            int? excludeBookingId = null);
    }

    /// <summary>
    /// Implementation of the pricing service
    /// </summary>
    public class PricingService : IPricingService
    {
        private readonly ChabbyNbDbContext _context;
        private readonly ILogger<PricingService> _logger;

        public PricingService(
            ChabbyNbDbContext context,
            ILogger<PricingService> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Calculates the price for each night of a stay considering seasonal pricing
        /// </summary>
        public async Task<List<DailyPrice>> CalculateDailyPricesAsync(
            int apartmentId,
            DateTime checkIn,
            DateTime checkOut)
        {
            // Validate inputs
            if (checkIn >= checkOut)
            {
                throw new ArgumentException("Check-out date must be after check-in date");
            }

            // Get the apartment
            var apartment = await _context.Apartments.FindAsync(apartmentId);
            if (apartment == null)
            {
                throw new ArgumentException($"Apartment with ID {apartmentId} not found");
            }

            // Get all seasonal pricing rules that could apply to the stay
            var seasonalPricings = await _context.SeasonalPricings
                .Where(sp =>
                    sp.ApartmentID == apartmentId &&
                    sp.IsActive &&
                    sp.EndDate >= checkIn &&
                    sp.StartDate <= checkOut)
                .OrderByDescending(sp => sp.Priority) // Higher priority rules override lower ones
                .ToListAsync();

            // Calculate price for each night
            var dailyPrices = new List<DailyPrice>();
            DateTime currentDate = checkIn;

            while (currentDate < checkOut)
            {
                // Default to the base price
                decimal priceForDate = apartment.PricePerNight;
                string priceType = "Base Price";

                // Check if a seasonal pricing applies for this date
                var applicablePricing = seasonalPricings.FirstOrDefault(sp =>
                    sp.StartDate <= currentDate &&
                    sp.EndDate >= currentDate);

                if (applicablePricing != null)
                {
                    priceForDate = applicablePricing.PricePerNight;
                    priceType = $"Seasonal: {applicablePricing.Name}";
                }

                dailyPrices.Add(new DailyPrice
                {
                    Date = currentDate,
                    Price = priceForDate,
                    PriceType = priceType
                });

                currentDate = currentDate.AddDays(1);
            }

            return dailyPrices;
        }

        /// <summary>
        /// Calculates the total booking price including seasonal pricing and promotions
        /// </summary>
        public async Task<BookingPriceResult> CalculateBookingPriceAsync(
            int apartmentId,
            DateTime checkIn,
            DateTime checkOut,
            int guestCount = 1,
            int petCount = 0,
            string promotionCode = null)
        {
            // Validate inputs
            if (checkIn >= checkOut)
            {
                throw new ArgumentException("Check-out date must be after check-in date");
            }

            if (guestCount <= 0)
            {
                throw new ArgumentException("Guest count must be at least 1");
            }

            if (petCount < 0)
            {
                throw new ArgumentException("Pet count cannot be negative");
            }

            // Get the apartment
            var apartment = await _context.Apartments
                .FirstOrDefaultAsync(a => a.ApartmentID == apartmentId);

            if (apartment == null)
            {
                throw new ArgumentException($"Apartment with ID {apartmentId} not found");
            }

            // Validate guest count against maximum occupancy
            if (guestCount > apartment.MaxOccupancy)
            {
                throw new ArgumentException($"Guest count exceeds maximum occupancy of {apartment.MaxOccupancy}");
            }

            // Validate pet policy
            if (petCount > 0 && !apartment.PetFriendly)
            {
                throw new ArgumentException("This apartment does not allow pets");
            }

            // Verify dates are available
            bool datesAvailable = await AreDatesAvailableAsync(apartmentId, checkIn, checkOut);
            if (!datesAvailable)
            {
                throw new InvalidOperationException("The selected dates are not available for booking");
            }

            // Get daily prices
            var dailyPrices = await CalculateDailyPricesAsync(apartmentId, checkIn, checkOut);

            // Calculate base price (sum of all daily prices)
            decimal basePrice = dailyPrices.Sum(dp => dp.Price);

            // Calculate pet fee if applicable
            decimal petFee = 0;
            if (petCount > 0 && apartment.PetFriendly && apartment.PetFee.HasValue)
            {
                petFee = petCount * apartment.PetFee.Value;
            }

            // Initial total before any discounts
            decimal totalBeforeDiscount = basePrice + petFee;

            // Apply promotion if provided
            decimal discountAmount = 0;
            Promotion promotion = null;
            string promotionMessage = null;

            if (!string.IsNullOrEmpty(promotionCode))
            {
                // Validate promotion code
                var promotionResult = await ValidatePromotionCodeAsync(
                    promotionCode,
                    apartmentId,
                    checkIn,
                    checkOut,
                    totalBeforeDiscount);

                if (promotionResult.IsValid)
                {
                    promotion = promotionResult.Promotion;
                    discountAmount = promotionResult.DiscountAmount;
                    promotionMessage = promotionResult.Message;
                }
            }

            // Calculate final price
            decimal totalPrice = totalBeforeDiscount - discountAmount;

            // Create result
            var result = new BookingPriceResult
            {
                ApartmentId = apartmentId,
                CheckInDate = checkIn,
                CheckOutDate = checkOut,
                DailyPrices = dailyPrices,
                BasePrice = basePrice,
                PetFee = petFee,
                GuestCount = guestCount,
                PetCount = petCount,
                TotalBeforeDiscount = totalBeforeDiscount,
                DiscountAmount = discountAmount,
                TotalPrice = totalPrice,
                PromotionCode = promotion?.Code,
                PromotionId = promotion?.PromotionID,
                PromotionMessage = promotionMessage,
                NightsCount = (checkOut - checkIn).Days
            };

            return result;
        }

        /// <summary>
        /// Checks if a promotion code is valid and applicable
        /// </summary>
        public async Task<PromotionValidationResult> ValidatePromotionCodeAsync(
            string promotionCode,
            int apartmentId,
            DateTime checkIn,
            DateTime checkOut,
            decimal baseAmount)
        {
            if (string.IsNullOrEmpty(promotionCode))
            {
                return new PromotionValidationResult
                {
                    IsValid = false,
                    Message = "No promotion code provided"
                };
            }

            // Calculate stay length in nights
            int stayLengthNights = (checkOut - checkIn).Days;

            // Try to find a valid promotion with the given code
            var promotion = await _context.Promotions
                .FirstOrDefaultAsync(p =>
                    p.Code == promotionCode.ToUpper() && // Case-insensitive matching
                    p.IsActive &&
                    (!p.StartDate.HasValue || p.StartDate.Value <= checkIn) &&
                    (!p.EndDate.HasValue || p.EndDate.Value >= checkOut) &&
                    (!p.ApartmentID.HasValue || p.ApartmentID.Value == apartmentId) &&
                    (!p.UsageLimit.HasValue || p.UsageCount < p.UsageLimit.Value) &&
                    (!p.MinimumStayNights.HasValue || stayLengthNights >= p.MinimumStayNights.Value) &&
                    (!p.MinimumBookingAmount.HasValue || baseAmount >= p.MinimumBookingAmount.Value)
                );

            if (promotion == null)
            {
                return new PromotionValidationResult
                {
                    IsValid = false,
                    Message = "Invalid or expired promotion code"
                };
            }

            // Calculate discount amount
            decimal discountAmount;
            if (promotion.DiscountType == "Percentage")
            {
                discountAmount = baseAmount * (promotion.DiscountValue / 100m);

                // Apply maximum discount if specified
                if (promotion.MaximumDiscountAmount.HasValue && discountAmount > promotion.MaximumDiscountAmount.Value)
                {
                    discountAmount = promotion.MaximumDiscountAmount.Value;
                }
            }
            else // FixedAmount
            {
                discountAmount = promotion.DiscountValue;

                // Make sure discount doesn't exceed total price
                if (discountAmount > baseAmount)
                {
                    discountAmount = baseAmount;
                }
            }

            string message = promotion.DiscountType == "Percentage"
                ? $"{promotion.DiscountValue}% discount{(promotion.MaximumDiscountAmount.HasValue ? $" (max ${promotion.MaximumDiscountAmount.Value})" : "")}"
                : $"${promotion.DiscountValue} discount";

            return new PromotionValidationResult
            {
                IsValid = true,
                Promotion = promotion,
                DiscountAmount = discountAmount,
                Message = message
            };
        }

        /// <summary>
        /// Calculates refund amount based on booking and cancellation policy
        /// </summary>
        public async Task<RefundCalculationResult> CalculateRefundAmountAsync(
            int bookingId,
            DateTime cancellationDate)
        {
            // Get the booking
            var booking = await _context.Bookings
                .Include(b => b.Apartment)
                .Include(b => b.Promotion)
                .FirstOrDefaultAsync(b => b.BookingID == bookingId);

            if (booking == null)
            {
                throw new ArgumentException($"Booking with ID {bookingId} not found");
            }

            // If booking isn't paid, no refund needed
            if (booking.PaymentStatus != "Paid" && booking.PaymentStatus != "Partially Paid")
            {
                return new RefundCalculationResult
                {
                    BookingId = bookingId,
                    OriginalAmount = booking.TotalPrice,
                    RefundAmount = 0,
                    RefundPercentage = 0,
                    IsRefundable = false,
                    RefundPolicy = "No payment to refund",
                    CancellationFee = 0
                };
            }

            // Calculate days until check-in
            var daysUntilCheckIn = (booking.CheckInDate - cancellationDate).TotalDays;

            // Determine refund percentage based on cancellation policy
            // This is a simple implementation - real apps would have more complex policies
            decimal refundPercentage;
            string refundPolicy;

            if (daysUntilCheckIn >= 30) // 30+ days before check-in
            {
                refundPercentage = 1.0m; // 100% refund
                refundPolicy = "Full refund (30+ days before check-in)";
            }
            else if (daysUntilCheckIn >= 14) // 14-29 days before check-in
            {
                refundPercentage = 0.85m; // 85% refund
                refundPolicy = "85% refund (14-29 days before check-in)";
            }
            else if (daysUntilCheckIn >= 7) // 7-13 days before check-in
            {
                refundPercentage = 0.50m; // 50% refund
                refundPolicy = "50% refund (7-13 days before check-in)";
            }
            else if (daysUntilCheckIn >= 1) // 1-6 days before check-in
            {
                refundPercentage = 0.25m; // 25% refund
                refundPolicy = "25% refund (1-6 days before check-in)";
            }
            else // Day of check-in or after
            {
                refundPercentage = 0m; // No refund
                refundPolicy = "No refund (day of check-in or after)";
            }

            // Find out how much has been paid and can be refunded
            decimal paidAmount = booking.TotalPrice;

            // Check if there are any refunds already processed
            var existingRefunds = await _context.Refunds
                .Where(r => r.Payment.BookingID == bookingId && r.Status == "succeeded")
                .SumAsync(r => r.Amount);

            decimal refundableAmount = paidAmount - existingRefunds;

            // Calculate refund amount based on policy
            decimal refundAmount = Math.Round(refundableAmount * refundPercentage, 2);
            decimal cancellationFee = refundableAmount - refundAmount;

            bool isRefundable = refundAmount > 0 && refundableAmount > 0;

            return new RefundCalculationResult
            {
                BookingId = bookingId,
                OriginalAmount = booking.TotalPrice,
                PaidAmount = paidAmount,
                AlreadyRefunded = existingRefunds,
                RefundableAmount = refundableAmount,
                RefundAmount = refundAmount,
                RefundPercentage = refundPercentage * 100, // Convert to percentage value
                IsRefundable = isRefundable,
                RefundPolicy = refundPolicy,
                CancellationFee = cancellationFee,
                DaysUntilCheckIn = (int)daysUntilCheckIn
            };
        }

        /// <summary>
        /// Validates if dates are available for booking
        /// </summary>
        public async Task<bool> AreDatesAvailableAsync(
            int apartmentId,
            DateTime checkIn,
            DateTime checkOut,
            int? excludeBookingId = null)
        {
            // First check if the apartment exists and is active
            var apartmentExists = await _context.Apartments
                .AnyAsync(a => a.ApartmentID == apartmentId && a.IsActive);

            if (!apartmentExists)
            {
                return false;
            }

            // Build query to check for overlapping bookings
            var query = _context.Bookings
                .Where(b =>
                    b.ApartmentID == apartmentId &&
                    b.BookingStatus != "Canceled" &&
                    (
                        // Check-in date falls within an existing booking
                        (checkIn >= b.CheckInDate && checkIn < b.CheckOutDate) ||
                        // Check-out date falls within an existing booking
                        (checkOut > b.CheckInDate && checkOut <= b.CheckOutDate) ||
                        // New booking completely covers an existing booking
                        (checkIn <= b.CheckInDate && checkOut >= b.CheckOutDate)
                    ));

            // Exclude current booking if specified (for rebooking or extensions)
            if (excludeBookingId.HasValue)
            {
                query = query.Where(b => b.BookingID != excludeBookingId.Value);
            }

            // If there are any overlapping bookings, the dates are not available
            var hasOverlappingBookings = await query.AnyAsync();

            return !hasOverlappingBookings;
        }
    }

    /// <summary>
    /// Result of price calculation for a single day
    /// </summary>
    public class DailyPrice
    {
        public DateTime Date { get; set; }
        public decimal Price { get; set; }
        public string PriceType { get; set; }
    }

    /// <summary>
    /// Result of a booking price calculation
    /// </summary>
    public class BookingPriceResult
    {
        public int ApartmentId { get; set; }
        public DateTime CheckInDate { get; set; }
        public DateTime CheckOutDate { get; set; }
        public int GuestCount { get; set; }
        public int PetCount { get; set; }
        public List<DailyPrice> DailyPrices { get; set; }
        public decimal BasePrice { get; set; }
        public decimal PetFee { get; set; }
        public decimal TotalBeforeDiscount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TotalPrice { get; set; }
        public string PromotionCode { get; set; }
        public int? PromotionId { get; set; }
        public string PromotionMessage { get; set; }
        public int NightsCount { get; set; }
    }

    /// <summary>
    /// Result of promotion code validation
    /// </summary>
    public class PromotionValidationResult
    {
        public bool IsValid { get; set; }
        public Promotion Promotion { get; set; }
        public decimal DiscountAmount { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Result of refund calculation
    /// </summary>
    public class RefundCalculationResult
    {
        public int BookingId { get; set; }
        public decimal OriginalAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal AlreadyRefunded { get; set; }
        public decimal RefundableAmount { get; set; }
        public decimal RefundAmount { get; set; }
        public decimal RefundPercentage { get; set; }
        public bool IsRefundable { get; set; }
        public string RefundPolicy { get; set; }
        public decimal CancellationFee { get; set; }
        public int DaysUntilCheckIn { get; set; }
    }
}