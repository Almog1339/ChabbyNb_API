using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using ChabbyNb_API.Models.DTOs;

namespace ChabbyNb_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PromotionsController : ControllerBase
    {
        private readonly ChabbyNbDbContext _context;

        public PromotionsController(ChabbyNbDbContext context)
        {
            _context = context;
        }

        // GET: api/Promotions
        [HttpGet]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<ActionResult<IEnumerable<PromotionDto>>> GetAllPromotions()
        {
            var promotions = await _context.Promotions
                .Include(p => p.Apartment)
                .OrderByDescending(p => p.CreatedDate)
                .Select(p => new PromotionDto
                {
                    PromotionID = p.PromotionID,
                    Name = p.Name,
                    Code = p.Code,
                    Description = p.Description,
                    DiscountType = p.DiscountType,
                    DiscountValue = p.DiscountValue,
                    MinimumStayNights = p.MinimumStayNights,
                    MinimumBookingAmount = p.MinimumBookingAmount,
                    MaximumDiscountAmount = p.MaximumDiscountAmount,
                    StartDate = p.StartDate,
                    EndDate = p.EndDate,
                    UsageLimit = p.UsageLimit,
                    UsageCount = p.UsageCount,
                    IsActive = p.IsActive,
                    CreatedDate = p.CreatedDate,
                    ApartmentID = p.ApartmentID,
                    ApartmentTitle = p.Apartment != null ? p.Apartment.Title : "All Apartments",
                    StatusDisplay = p.IsActive
                        ? (p.EndDate.HasValue && p.EndDate.Value < DateTime.Today ? "Expired" : "Active")
                        : "Inactive",
                    DateRangeDisplay = BuildDateRangeDisplay(p.StartDate, p.EndDate),
                    DiscountDisplay = BuildDiscountDisplay(p.DiscountType, p.DiscountValue, p.MaximumDiscountAmount)
                })
                .ToListAsync();

            return promotions;
        }

        // GET: api/Promotions/5
        [HttpGet("{id}")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<ActionResult<PromotionDto>> GetPromotion(int id)
        {
            var promotion = await _context.Promotions
                .Include(p => p.Apartment)
                .Where(p => p.PromotionID == id)
                .Select(p => new PromotionDto
                {
                    PromotionID = p.PromotionID,
                    Name = p.Name,
                    Code = p.Code,
                    Description = p.Description,
                    DiscountType = p.DiscountType,
                    DiscountValue = p.DiscountValue,
                    MinimumStayNights = p.MinimumStayNights,
                    MinimumBookingAmount = p.MinimumBookingAmount,
                    MaximumDiscountAmount = p.MaximumDiscountAmount,
                    StartDate = p.StartDate,
                    EndDate = p.EndDate,
                    UsageLimit = p.UsageLimit,
                    UsageCount = p.UsageCount,
                    IsActive = p.IsActive,
                    CreatedDate = p.CreatedDate,
                    ApartmentID = p.ApartmentID,
                    ApartmentTitle = p.Apartment != null ? p.Apartment.Title : "All Apartments",
                    StatusDisplay = p.IsActive
                        ? (p.EndDate.HasValue && p.EndDate.Value < DateTime.Today ? "Expired" : "Active")
                        : "Inactive",
                    DateRangeDisplay = BuildDateRangeDisplay(p.StartDate, p.EndDate),
                    DiscountDisplay = BuildDiscountDisplay(p.DiscountType, p.DiscountValue, p.MaximumDiscountAmount)
                })
                .FirstOrDefaultAsync();

            if (promotion == null)
            {
                return NotFound();
            }

            return promotion;
        }

        // POST: api/Promotions
        [HttpPost]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<ActionResult<PromotionDto>> CreatePromotion(CreatePromotionDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Validate dates
            if (dto.StartDate.HasValue && dto.EndDate.HasValue && dto.StartDate >= dto.EndDate)
            {
                return BadRequest("End date must be after start date");
            }

            // Check if code already exists
            bool codeExists = await _context.Promotions.AnyAsync(p => p.Code == dto.Code);
            if (codeExists)
            {
                return BadRequest("Promotion code already exists");
            }

            // Check if apartment exists if specified
            if (dto.ApartmentID.HasValue)
            {
                var apartment = await _context.Apartments.FindAsync(dto.ApartmentID.Value);
                if (apartment == null)
                {
                    return NotFound("Apartment not found");
                }
            }

            // Validate discount value based on type
            if (dto.DiscountType == "Percentage" && (dto.DiscountValue <= 0 || dto.DiscountValue > 100))
            {
                return BadRequest("Percentage discount must be between 0 and 100");
            }

            // Create the promotion
            var promotion = new Promotion
            {
                Name = dto.Name,
                Code = dto.Code.ToUpper(), // Ensure code is uppercase
                Description = dto.Description,
                DiscountType = dto.DiscountType,
                DiscountValue = dto.DiscountValue,
                MinimumStayNights = dto.MinimumStayNights,
                MinimumBookingAmount = dto.MinimumBookingAmount,
                MaximumDiscountAmount = dto.MaximumDiscountAmount,
                StartDate = dto.StartDate,
                EndDate = dto.EndDate,
                UsageLimit = dto.UsageLimit,
                UsageCount = 0,
                IsActive = true,
                CreatedDate = DateTime.Now,
                ApartmentID = dto.ApartmentID
            };

            _context.Promotions.Add(promotion);
            await _context.SaveChangesAsync();

            // Create response DTO
            var responseDto = new PromotionDto
            {
                PromotionID = promotion.PromotionID,
                Name = promotion.Name,
                Code = promotion.Code,
                Description = promotion.Description,
                DiscountType = promotion.DiscountType,
                DiscountValue = promotion.DiscountValue,
                MinimumStayNights = promotion.MinimumStayNights,
                MinimumBookingAmount = promotion.MinimumBookingAmount,
                MaximumDiscountAmount = promotion.MaximumDiscountAmount,
                StartDate = promotion.StartDate,
                EndDate = promotion.EndDate,
                UsageLimit = promotion.UsageLimit,
                UsageCount = promotion.UsageCount,
                IsActive = promotion.IsActive,
                CreatedDate = promotion.CreatedDate,
                ApartmentID = promotion.ApartmentID,
                ApartmentTitle = promotion.ApartmentID.HasValue ?
                    (await _context.Apartments.FindAsync(promotion.ApartmentID.Value))?.Title : "All Apartments",
                StatusDisplay = "Active",
                DateRangeDisplay = BuildDateRangeDisplay(promotion.StartDate, promotion.EndDate),
                DiscountDisplay = BuildDiscountDisplay(promotion.DiscountType, promotion.DiscountValue, promotion.MaximumDiscountAmount)
            };

            return CreatedAtAction(nameof(GetPromotion), new { id = promotion.PromotionID }, responseDto);
        }

        // Helper method to build date range display
        private string BuildDateRangeDisplay(DateTime? startDate, DateTime? endDate)
        {
            if (startDate.HasValue && endDate.HasValue)
            {
                return $"{startDate.Value.ToShortDateString()} - {endDate.Value.ToShortDateString()}";
            }
            else if (startDate.HasValue)
            {
                return $"From {startDate.Value.ToShortDateString()}";
            }
            else if (endDate.HasValue)
            {
                return $"Until {endDate.Value.ToShortDateString()}";
            }
            else
            {
                return "No time restrictions";
            }
        }

        // Helper method to build discount display
        private string BuildDiscountDisplay(string discountType, decimal discountValue, decimal? maximumDiscountAmount)
        {
            if (discountType == "Percentage")
            {
                string display = $"{discountValue}% off";
                if (maximumDiscountAmount.HasValue)
                {
                    display += $" (max ${maximumDiscountAmount.Value})";
                }
                return display;
            }
            else // FixedAmount
            {
                return $"${discountValue} off";
            }
        }
    }
}