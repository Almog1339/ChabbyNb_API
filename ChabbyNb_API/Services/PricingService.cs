using ChabbyNb.Models;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using ChabbyNb_API.Services.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChabbyNb_API.Services
{
    public interface IPricingService
    {
        Task<List<DailyPrice>> CalculateDailyPricesAsync(int apartmentId, DateTime checkIn, DateTime checkOut);
        Task<BookingPriceResult> CalculateBookingPriceAsync(int apartmentId, DateTime checkIn, DateTime checkOut, int guestCount = 1, int petCount = 0, string promotionCode = null);
        Task<PromotionValidationResult> ValidatePromotionCodeAsync(string promotionCode, int apartmentId, DateTime checkIn, DateTime checkOut, decimal baseAmount);
        Task<RefundCalculationResult> CalculateRefundAmountAsync(int bookingId, DateTime cancellationDate);
        Task<bool> AreDatesAvailableAsync(int apartmentId, DateTime checkIn, DateTime checkOut, int? excludeBookingId = null);
    }

    public class PricingService : IPricingService
    {
        private readonly ChabbyNbDbContext _context;
        private readonly ILogger<PricingService> _logger;

        public PricingService(ChabbyNbDbContext context, ILogger<PricingService> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<List<DailyPrice>> CalculateDailyPricesAsync(int apartmentId, DateTime checkIn, DateTime checkOut)
        {
            if (checkIn >= checkOut)
                throw new ArgumentException("Check-out date must be after check-in date");

            var apartment = await _context.Apartments.FindAsync(apartmentId);
            if (apartment == null)
                throw new ArgumentException($"Apartment with ID {apartmentId} not found");

            var seasonalPricings = await GetSeasonalPricingsForPeriod(apartmentId, checkIn, checkOut);
            var dailyPrices = new List<DailyPrice>();
            DateTime currentDate = checkIn;

            while (currentDate < checkOut)
            {
                decimal priceForDate = apartment.PricePerNight;
                string priceType = "Base Price";

                var applicablePricing = seasonalPricings.FirstOrDefault(sp =>
                    sp.StartDate <= currentDate && sp.EndDate >= currentDate);

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

        public async Task<BookingPriceResult> CalculateBookingPriceAsync(
            int apartmentId, DateTime checkIn, DateTime checkOut,
            int guestCount = 1, int petCount = 0, string promotionCode = null)
        {
            // Validation
            var validationResult = await ValidateBookingParameters(apartmentId, checkIn, checkOut, guestCount, petCount);
            if (!validationResult.Success)
                throw new InvalidOperationException(validationResult.Errors.First());

            var apartment = validationResult.Data;

            // Calculate daily prices
            var dailyPrices = await CalculateDailyPricesAsync(apartmentId, checkIn, checkOut);
            decimal basePrice = dailyPrices.Sum(dp => dp.Price);

            // Calculate pet fee
            decimal petFee = CalculatePetFee(apartment, petCount);
            decimal totalBeforeDiscount = basePrice + petFee;

            // Apply promotion
            decimal discountAmount = 0;
            Promotion promotion = null;
            string promotionMessage = null;

            if (!string.IsNullOrEmpty(promotionCode))
            {
                var promotionResult = await ValidatePromotionCodeAsync(
                    promotionCode, apartmentId, checkIn, checkOut, totalBeforeDiscount);

                if (promotionResult.IsValid)
                {
                    promotion = promotionResult.Promotion;
                    discountAmount = promotionResult.DiscountAmount;
                    promotionMessage = promotionResult.Message;
                }
            }

            decimal totalPrice = totalBeforeDiscount - discountAmount;

            return new BookingPriceResult
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
        }

        public async Task<PromotionValidationResult> ValidatePromotionCodeAsync(
            string promotionCode, int apartmentId, DateTime checkIn, DateTime checkOut, decimal baseAmount)
        {
            if (string.IsNullOrEmpty(promotionCode))
                return new PromotionValidationResult { IsValid = false, Message = "No promotion code provided" };

            int stayLengthNights = (checkOut - checkIn).Days;

            var promotion = await _context.Promotions
                .FirstOrDefaultAsync(p =>
                    p.Code == promotionCode.ToUpper() &&
                    p.IsActive &&
                    (!p.StartDate.HasValue || p.StartDate.Value <= checkIn) &&
                    (!p.EndDate.HasValue || p.EndDate.Value >= checkOut) &&
                    (!p.ApartmentID.HasValue || p.ApartmentID.Value == apartmentId) &&
                    (!p.UsageLimit.HasValue || p.UsageCount < p.UsageLimit.Value) &&
                    (!p.MinimumStayNights.HasValue || stayLengthNights >= p.MinimumStayNights.Value) &&
                    (!p.MinimumBookingAmount.HasValue || baseAmount >= p.MinimumBookingAmount.Value));

            if (promotion == null)
                return new PromotionValidationResult { IsValid = false, Message = "Invalid or expired promotion code" };

            decimal discountAmount = CalculateDiscountAmount(promotion, baseAmount);
            string message = BuildDiscountMessage(promotion, discountAmount);

            return new PromotionValidationResult
            {
                IsValid = true,
                Promotion = promotion,
                DiscountAmount = discountAmount,
                Message = message
            };
        }

        public async Task<RefundCalculationResult> CalculateRefundAmountAsync(int bookingId, DateTime cancellationDate)
        {
            var booking = await _context.Bookings
                .Include(b => b.Apartment)
                .Include(b => b.Promotion)
                .FirstOrDefaultAsync(b => b.BookingID == bookingId);

            if (booking == null)
                throw new ArgumentException($"Booking with ID {bookingId} not found");

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

            var daysUntilCheckIn = (booking.CheckInDate - cancellationDate).TotalDays;
            var (refundPercentage, refundPolicy) = DetermineRefundPolicy(daysUntilCheckIn);

            decimal paidAmount = booking.TotalPrice;
            var existingRefunds = await _context.Refunds
                .Where(r => r.Payment.BookingID == bookingId && r.Status == "succeeded")
                .SumAsync(r => r.Amount);

            decimal refundableAmount = paidAmount - existingRefunds;
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
                RefundPercentage = refundPercentage * 100,
                IsRefundable = isRefundable,
                RefundPolicy = refundPolicy,
                CancellationFee = cancellationFee,
                DaysUntilCheckIn = (int)daysUntilCheckIn
            };
        }

        public async Task<bool> AreDatesAvailableAsync(int apartmentId, DateTime checkIn, DateTime checkOut, int? excludeBookingId = null)
        {
            var apartmentExists = await _context.Apartments
                .AnyAsync(a => a.ApartmentID == apartmentId && a.IsActive);

            if (!apartmentExists)
                return false;

            var query = _context.Bookings
                .Where(b =>
                    b.ApartmentID == apartmentId &&
                    b.BookingStatus != "Canceled" &&
                    (
                        (checkIn >= b.CheckInDate && checkIn < b.CheckOutDate) ||
                        (checkOut > b.CheckInDate && checkOut <= b.CheckOutDate) ||
                        (checkIn <= b.CheckInDate && checkOut >= b.CheckOutDate)
                    ));

            if (excludeBookingId.HasValue)
                query = query.Where(b => b.BookingID != excludeBookingId.Value);

            return !await query.AnyAsync();
        }

        #region Private Helper Methods

        private async Task<List<SeasonalPricing>> GetSeasonalPricingsForPeriod(int apartmentId, DateTime checkIn, DateTime checkOut)
        {
            return await _context.SeasonalPricings
                .Where(sp =>
                    sp.ApartmentID == apartmentId &&
                    sp.IsActive &&
                    sp.EndDate >= checkIn &&
                    sp.StartDate <= checkOut)
                .OrderByDescending(sp => sp.Priority)
                .ToListAsync();
        }

        private async Task<ServiceResult<Apartment>> ValidateBookingParameters(
            int apartmentId, DateTime checkIn, DateTime checkOut, int guestCount, int petCount)
        {
            var errors = new List<string>();

            if (checkIn >= checkOut)
                errors.Add("Check-out date must be after check-in date");

            if (guestCount <= 0)
                errors.Add("Guest count must be at least 1");

            if (petCount < 0)
                errors.Add("Pet count cannot be negative");

            var apartment = await _context.Apartments.FindAsync(apartmentId);
            if (apartment == null)
                errors.Add($"Apartment with ID {apartmentId} not found");
            else
            {
                if (guestCount > apartment.MaxOccupancy)
                    errors.Add($"Guest count exceeds maximum occupancy of {apartment.MaxOccupancy}");

                if (petCount > 0 && !apartment.PetFriendly)
                    errors.Add("This apartment does not allow pets");
            }

            if (!await AreDatesAvailableAsync(apartmentId, checkIn, checkOut))
                errors.Add("The selected dates are not available for booking");

            return errors.Any()
                ? ServiceResult<Apartment>.ErrorResult(errors)
                : ServiceResult<Apartment>.SuccessResult(apartment);
        }

        private decimal CalculatePetFee(Apartment apartment, int petCount)
        {
            if (petCount > 0 && apartment.PetFriendly && apartment.PetFee.HasValue)
                return petCount * apartment.PetFee.Value;

            return 0;
        }

        private decimal CalculateDiscountAmount(Promotion promotion, decimal baseAmount)
        {
            decimal discountAmount;

            if (promotion.DiscountType == "Percentage")
            {
                discountAmount = baseAmount * (promotion.DiscountValue / 100m);

                if (promotion.MaximumDiscountAmount.HasValue && discountAmount > promotion.MaximumDiscountAmount.Value)
                    discountAmount = promotion.MaximumDiscountAmount.Value;
            }
            else // FixedAmount
            {
                discountAmount = promotion.DiscountValue;

                if (discountAmount > baseAmount)
                    discountAmount = baseAmount;
            }

            return discountAmount;
        }

        private string BuildDiscountMessage(Promotion promotion, decimal discountAmount)
        {
            if (promotion.DiscountType == "Percentage")
            {
                string message = $"{promotion.DiscountValue}% off";
                if (promotion.MaximumDiscountAmount.HasValue)
                    message += $" (max ${promotion.MaximumDiscountAmount.Value})";
                return message;
            }
            else
            {
                return $"${promotion.DiscountValue} off";
            }
        }

        private (decimal refundPercentage, string refundPolicy) DetermineRefundPolicy(double daysUntilCheckIn)
        {
            return daysUntilCheckIn switch
            {
                >= 30 => (1.0m, "Full refund (30+ days before check-in)"),
                >= 14 => (0.85m, "85% refund (14-29 days before check-in)"),
                >= 7 => (0.50m, "50% refund (7-13 days before check-in)"),
                >= 1 => (0.25m, "25% refund (1-6 days before check-in)"),
                _ => (0m, "No refund (day of check-in or after)")
            };
        }

        #endregion
    }

    // Supporting classes for pricing service
    public class DailyPrice
    {
        public DateTime Date { get; set; }
        public decimal Price { get; set; }
        public string PriceType { get; set; }
    }

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

    public class PromotionValidationResult
    {
        public bool IsValid { get; set; }
        public Promotion Promotion { get; set; }
        public decimal DiscountAmount { get; set; }
        public string Message { get; set; }
    }

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