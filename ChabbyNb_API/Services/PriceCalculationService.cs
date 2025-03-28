using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ChabbyNb.Models;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;

namespace ChabbyNb_API.Services
{
    public class PriceCalculationService
    {
        private readonly ChabbyNbDbContext _context;

        public PriceCalculationService(ChabbyNbDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Calculates the price for each night of a stay
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
                throw new ArgumentException("Apartment not found");
            }

            // Get all seasonal pricing rules that could apply to the stay
            var seasonalPricings = await _context.SeasonalPricings
                .Where(sp =>
                    sp.ApartmentID == apartmentId &&
                    sp.IsActive &&
                    sp.EndDate >= checkIn &&
                    sp.StartDate <= checkOut)
                .OrderByDescending(sp => sp.Priority)
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
            int petCount,
            string promotionCode = null)
        {
            // Get daily prices
            var dailyPrices = await CalculateDailyPricesAsync(apartmentId, checkIn, checkOut);

            // Get the apartment for pet fee calculation
            var apartment = await _context.Apartments.FindAsync(apartmentId);
            if (apartment == null)
            {
                throw new ArgumentException("Apartment not found");
            }

            // Calculate base price (sum of all daily prices)
            decimal basePrice = dailyPrices.Sum(dp => dp.Price);

            // Calculate pet fee if applicable
            decimal petFee = 0;
            if (petCount > 0 && apartment.PetFriendly && apartment.PetFee.HasValue)
            {
                petFee = petCount * apartment.PetFee.Value;
            }

            // Add pet fee to base price
            decimal totalBeforeDiscount = basePrice + petFee;

            // Apply promotion if provided
            decimal discountAmount = 0;
            Promotion promotion = null;

            if (!string.IsNullOrEmpty(promotionCode))
            {
                // Calculate stay length in nights
                int stayLengthNights = (checkOut - checkIn).Days;

                // Try to find a valid promotion with the given code
                promotion = await _context.Promotions
                    .FirstOrDefaultAsync(p =>
                        p.Code == promotionCode.ToUpper() &&
                        p.IsActive &&
                        (!p.StartDate.HasValue || p.StartDate.Value <= checkIn) &&
                        (!p.EndDate.HasValue || p.EndDate.Value >= checkOut) &&
                        (!p.ApartmentID.HasValue || p.ApartmentID.Value == apartmentId) &&
                        (!p.UsageLimit.HasValue || p.UsageCount < p.UsageLimit.Value) &&
                        (!p.MinimumStayNights.HasValue || stayLengthNights >= p.MinimumStayNights.Value) &&
                        (!p.MinimumBookingAmount.HasValue || totalBeforeDiscount >= p.MinimumBookingAmount.Value)
                    );

                if (promotion != null)
                {
                    // Calculate discount amount
                    if (promotion.DiscountType == "Percentage")
                    {
                        discountAmount = totalBeforeDiscount * (promotion.DiscountValue / 100m);

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
                        if (discountAmount > totalBeforeDiscount)
                        {
                            discountAmount = totalBeforeDiscount;
                        }
                    }
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
                TotalBeforeDiscount = totalBeforeDiscount,
                DiscountAmount = discountAmount,
                TotalPrice = totalPrice,
                PromotionCode = promotion?.Code,
                PromotionId = promotion?.PromotionID
            };

            return result;
        }
    }

    /// <summary>
    /// Represents the price for a single day
    /// </summary>
    public class DailyPrice
    {
        public DateTime Date { get; set; }
        public decimal Price { get; set; }
        public string PriceType { get; set; }
    }

    /// <summary>
    /// Represents the full booking price calculation result
    /// </summary>
    public class BookingPriceResult
    {
        public int ApartmentId { get; set; }
        public DateTime CheckInDate { get; set; }
        public DateTime CheckOutDate { get; set; }
        public List<DailyPrice> DailyPrices { get; set; }
        public decimal BasePrice { get; set; }
        public decimal PetFee { get; set; }
        public decimal TotalBeforeDiscount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TotalPrice { get; set; }
        public string PromotionCode { get; set; }
        public int? PromotionId { get; set; }
    }
}