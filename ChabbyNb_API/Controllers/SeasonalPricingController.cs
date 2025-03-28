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
using ChabbyNb.Models;

namespace ChabbyNb_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "RequireAdminRole")]
    public class SeasonalPricingController : ControllerBase
    {
        private readonly ChabbyNbDbContext _context;

        public SeasonalPricingController(ChabbyNbDbContext context)
        {
            _context = context;
        }

        // GET: api/SeasonalPricing
        [HttpGet]
        public async Task<ActionResult<IEnumerable<SeasonalPricingDto>>> GetAllSeasonalPricing()
        {
            var seasonalPricing = await _context.SeasonalPricings
                .Include(sp => sp.Apartment)
                .OrderBy(sp => sp.StartDate)
                .Select(sp => new SeasonalPricingDto
                {
                    SeasonalPricingID = sp.SeasonalPricingID,
                    ApartmentID = sp.ApartmentID,
                    ApartmentTitle = sp.Apartment.Title,
                    Name = sp.Name,
                    StartDate = sp.StartDate,
                    EndDate = sp.EndDate,
                    PricePerNight = sp.PricePerNight,
                    BasePrice = sp.Apartment.PricePerNight,
                    PriceDifference = sp.PricePerNight - sp.Apartment.PricePerNight,
                    PriceChangeDisplay = sp.PricePerNight > sp.Apartment.PricePerNight
                        ? $"+{(sp.PricePerNight - sp.Apartment.PricePerNight):C2}"
                        : $"{(sp.PricePerNight - sp.Apartment.PricePerNight):C2}",
                    Description = sp.Description,
                    Priority = sp.Priority,
                    IsActive = sp.IsActive
                })
                .ToListAsync();

            return seasonalPricing;
        }

        // GET: api/SeasonalPricing/5
        [HttpGet("{id}")]
        public async Task<ActionResult<SeasonalPricingDto>> GetSeasonalPricing(int id)
        {
            var seasonalPricing = await _context.SeasonalPricings
                .Include(sp => sp.Apartment)
                .Where(sp => sp.SeasonalPricingID == id)
                .Select(sp => new SeasonalPricingDto
                {
                    SeasonalPricingID = sp.SeasonalPricingID,
                    ApartmentID = sp.ApartmentID,
                    ApartmentTitle = sp.Apartment.Title,
                    Name = sp.Name,
                    StartDate = sp.StartDate,
                    EndDate = sp.EndDate,
                    PricePerNight = sp.PricePerNight,
                    BasePrice = sp.Apartment.PricePerNight,
                    PriceDifference = sp.PricePerNight - sp.Apartment.PricePerNight,
                    PriceChangeDisplay = sp.PricePerNight > sp.Apartment.PricePerNight
                        ? $"+{(sp.PricePerNight - sp.Apartment.PricePerNight):C2}"
                        : $"{(sp.PricePerNight - sp.Apartment.PricePerNight):C2}",
                    Description = sp.Description,
                    Priority = sp.Priority,
                    IsActive = sp.IsActive
                })
                .FirstOrDefaultAsync();

            if (seasonalPricing == null)
            {
                return NotFound();
            }

            return seasonalPricing;
        }

        // GET: api/SeasonalPricing/Apartment/5
        [HttpGet("Apartment/{apartmentId}")]
        public async Task<ActionResult<IEnumerable<SeasonalPricingDto>>> GetSeasonalPricingForApartment(int apartmentId)
        {
            var apartment = await _context.Apartments.FindAsync(apartmentId);
            if (apartment == null)
            {
                return NotFound("Apartment not found");
            }

            var seasonalPricing = await _context.SeasonalPricings
                .Where(sp => sp.ApartmentID == apartmentId)
                .OrderBy(sp => sp.StartDate)
                .Select(sp => new SeasonalPricingDto
                {
                    SeasonalPricingID = sp.SeasonalPricingID,
                    ApartmentID = sp.ApartmentID,
                    ApartmentTitle = apartment.Title,
                    Name = sp.Name,
                    StartDate = sp.StartDate,
                    EndDate = sp.EndDate,
                    PricePerNight = sp.PricePerNight,
                    BasePrice = apartment.PricePerNight,
                    PriceDifference = sp.PricePerNight - apartment.PricePerNight,
                    PriceChangeDisplay = sp.PricePerNight > apartment.PricePerNight
                        ? $"+{(sp.PricePerNight - apartment.PricePerNight):C2}"
                        : $"{(sp.PricePerNight - apartment.PricePerNight):C2}",
                    Description = sp.Description,
                    Priority = sp.Priority,
                    IsActive = sp.IsActive
                })
                .ToListAsync();

            return seasonalPricing;
        }

        // POST: api/SeasonalPricing
        [HttpPost]
        public async Task<ActionResult<SeasonalPricingDto>> CreateSeasonalPricing(CreateSeasonalPricingDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Check if the apartment exists
            var apartment = await _context.Apartments.FindAsync(dto.ApartmentID);
            if (apartment == null)
            {
                return NotFound("Apartment not found");
            }

            // Validate dates
            if (dto.StartDate >= dto.EndDate)
            {
                return BadRequest("End date must be after start date");
            }

            if (dto.EndDate < DateTime.Today)
            {
                return BadRequest("End date cannot be in the past");
            }

            // Create the seasonal pricing
            var seasonalPricing = new SeasonalPricing
            {
                ApartmentID = dto.ApartmentID,
                Name = dto.Name,
                StartDate = dto.StartDate,
                EndDate = dto.EndDate,
                PricePerNight = dto.PricePerNight,
                Description = dto.Description,
                Priority = dto.Priority,
                IsActive = true,
                CreatedDate = DateTime.Now
            };

            _context.SeasonalPricings.Add(seasonalPricing);
            await _context.SaveChangesAsync();

            // Create response DTO
            var responseDto = new SeasonalPricingDto
            {
                SeasonalPricingID = seasonalPricing.SeasonalPricingID,
                ApartmentID = seasonalPricing.ApartmentID,
                ApartmentTitle = apartment.Title,
                Name = seasonalPricing.Name,
                StartDate = seasonalPricing.StartDate,
                EndDate = seasonalPricing.EndDate,
                PricePerNight = seasonalPricing.PricePerNight,
                BasePrice = apartment.PricePerNight,
                PriceDifference = seasonalPricing.PricePerNight - apartment.PricePerNight,
                PriceChangeDisplay = seasonalPricing.PricePerNight > apartment.PricePerNight
                    ? $"+{(seasonalPricing.PricePerNight - apartment.PricePerNight):C2}"
                    : $"{(seasonalPricing.PricePerNight - apartment.PricePerNight):C2}",
                Description = seasonalPricing.Description,
                Priority = seasonalPricing.Priority,
                IsActive = seasonalPricing.IsActive
            };

            return CreatedAtAction(nameof(GetSeasonalPricing), new { id = seasonalPricing.SeasonalPricingID }, responseDto);
        }

        // PUT: api/SeasonalPricing/5
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateSeasonalPricing(int id, UpdateSeasonalPricingDto dto)
        {
            if (id != dto.SeasonalPricingID)
            {
                return BadRequest("ID mismatch");
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Validate dates
            if (dto.StartDate >= dto.EndDate)
            {
                return BadRequest("End date must be after start date");
            }

            var seasonalPricing = await _context.SeasonalPricings.FindAsync(id);
            if (seasonalPricing == null)
            {
                return NotFound();
            }

            // Update properties
            seasonalPricing.Name = dto.Name;
            seasonalPricing.StartDate = dto.StartDate;
            seasonalPricing.EndDate = dto.EndDate;
            seasonalPricing.PricePerNight = dto.PricePerNight;
            seasonalPricing.Description = dto.Description;
            seasonalPricing.Priority = dto.Priority;
            seasonalPricing.IsActive = dto.IsActive;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SeasonalPricingExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // DELETE: api/SeasonalPricing/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSeasonalPricing(int id)
        {
            var seasonalPricing = await _context.SeasonalPricings.FindAsync(id);
            if (seasonalPricing == null)
            {
                return NotFound();
            }

            _context.SeasonalPricings.Remove(seasonalPricing);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool SeasonalPricingExists(int id)
        {
            return _context.SeasonalPricings.Any(e => e.SeasonalPricingID == id);
        }
    }
}