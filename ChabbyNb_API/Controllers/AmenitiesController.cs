using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using ChabbyNb_API.Models.DTOs;

namespace ChabbyNb_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AmenitiesController : ControllerBase
    {
        private readonly ChabbyNbDbContext _context;

        public AmenitiesController(ChabbyNbDbContext context)
        {
            _context = context;
        }

        // GET: api/Amenities
        [HttpGet]
        public async Task<ActionResult<IEnumerable<AmenityDto>>> GetAmenities()
        {
            var amenities = await _context.Amenities
                .OrderBy(a => a.Category)
                .ThenBy(a => a.Name)
                .Select(a => new AmenityDto
                {
                    AmenityID = a.AmenityID,
                    Name = a.Name,
                    IconBase64 = a.Icon != null ? Convert.ToBase64String(a.Icon) : null,
                    Category = a.Category,
                    UsageCount = a.ApartmentAmenities.Count
                })
                .ToListAsync();

            return amenities;
        }

        // GET: api/Amenities/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<AmenityDto>> GetAmenity(int id)
        {
            var amenity = await _context.Amenities
                .Where(a => a.AmenityID == id)
                .Select(a => new AmenityDto
                {
                    AmenityID = a.AmenityID,
                    Name = a.Name,
                    IconBase64 = a.Icon != null ? Convert.ToBase64String(a.Icon) : null,
                    Category = a.Category,
                    UsageCount = a.ApartmentAmenities.Count
                })
                .FirstOrDefaultAsync();

            if (amenity == null)
            {
                return NotFound();
            }

            return amenity;
        }

        // GET: api/Amenities/Categories
        [HttpGet("Categories")]
        public async Task<ActionResult<IEnumerable<string>>> GetCategories()
        {
            var categories = await _context.Amenities
                .Select(a => a.Category)
                .Distinct()
                .Where(c => !string.IsNullOrEmpty(c))
                .OrderBy(c => c)
                .ToListAsync();

            return categories;
        }

        // GET: api/Amenities/Category/{category}
        [HttpGet("Category/{category}")]
        public async Task<ActionResult<IEnumerable<AmenityDto>>> GetAmenitiesByCategory(string category)
        {
            var amenities = await _context.Amenities
                .Where(a => a.Category == category)
                .OrderBy(a => a.Name)
                .Select(a => new AmenityDto
                {
                    AmenityID = a.AmenityID,
                    Name = a.Name,
                    IconBase64 = a.Icon != null ? Convert.ToBase64String(a.Icon) : null,
                    Category = a.Category,
                    UsageCount = a.ApartmentAmenities.Count
                })
                .ToListAsync();

            return amenities;
        }

        // GET: api/Amenities/Popular
        [HttpGet("Popular")]
        public async Task<ActionResult<IEnumerable<AmenityDto>>> GetPopularAmenities(int count = 10)
        {
            // Limit the maximum number that can be requested
            if (count > 50) count = 50;

            var amenities = await _context.Amenities
                .OrderByDescending(a => a.ApartmentAmenities.Count)
                .Take(count)
                .Select(a => new AmenityDto
                {
                    AmenityID = a.AmenityID,
                    Name = a.Name,
                    IconBase64 = a.Icon != null ? Convert.ToBase64String(a.Icon) : null,
                    Category = a.Category,
                    UsageCount = a.ApartmentAmenities.Count
                })
                .ToListAsync();

            return amenities;
        }

        // GET: api/Amenities/Search
        [HttpGet("Search")]
        public async Task<ActionResult<IEnumerable<AmenityDto>>> SearchAmenities(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return BadRequest("Search query cannot be empty");
            }

            var amenities = await _context.Amenities
                .Where(a => a.Name.Contains(query) ||
                            a.Category.Contains(query))
                .OrderBy(a => a.Category)
                .ThenBy(a => a.Name)
                .Select(a => new AmenityDto
                {
                    AmenityID = a.AmenityID,
                    Name = a.Name,
                    IconBase64 = a.Icon != null ? Convert.ToBase64String(a.Icon) : null,
                    Category = a.Category,
                    UsageCount = a.ApartmentAmenities.Count
                })
                .ToListAsync();

            return amenities;
        }

        // GET: api/Amenities/ForApartment/{id}
        [HttpGet("ForApartment/{id}")]
        public async Task<ActionResult<IEnumerable<AmenityDto>>> GetAmenitiesForApartment(int id)
        {
            // Check if apartment exists
            var apartmentExists = await _context.Apartments.AnyAsync(a => a.ApartmentID == id);
            if (!apartmentExists)
            {
                return NotFound("Apartment not found");
            }

            var amenities = await _context.ApartmentAmenities
                .Where(aa => aa.ApartmentID == id)
                .Include(aa => aa.Amenity)
                .Select(aa => new AmenityDto
                {
                    AmenityID = aa.Amenity.AmenityID,
                    Name = aa.Amenity.Name,
                    IconBase64 = aa.Amenity.Icon != null ? Convert.ToBase64String(aa.Amenity.Icon) : null,
                    Category = aa.Amenity.Category,
                    UsageCount = aa.Amenity.ApartmentAmenities.Count
                })
                .OrderBy(a => a.Category)
                .ThenBy(a => a.Name)
                .ToListAsync();

            return amenities;
        }
    }
}