using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ChabbyNb.Models;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using ChabbyNb_API.Models.DTOs;

namespace ChabbyNb_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ApartmentsController : ControllerBase
    {
        private readonly ChabbyNbDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public ApartmentsController(ChabbyNbDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        // GET: api/Apartments
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Apartment>>> GetApartments()
        {
            return await _context.Apartments
                .Where(a => a.IsActive)
                .Include(a => a.ApartmentImages)
                .ToListAsync();
        }

        // GET: api/Apartments/Featured
        [HttpGet("Featured")]
        public async Task<ActionResult<IEnumerable<Apartment>>> GetFeaturedApartments()
        {
            var featuredApartments = await _context.Apartments
                .Where(a => a.IsActive)
                .Include(a => a.Reviews)
                .Include(a => a.ApartmentImages)
                .OrderByDescending(a => a.Reviews.Any() ? a.Reviews.Average(r => r.Rating) : 0)
                .Take(3)
                .ToListAsync();

            return featuredApartments;
        }

        // GET: api/Apartments/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Apartment>> GetApartment(int id)
        {
            var apartment = await _context.Apartments
                .Include(a => a.ApartmentImages)
                .Include(a => a.ApartmentAmenities)
                    .ThenInclude(aa => aa.Amenity)
                .Include(a => a.Reviews)
                    .ThenInclude(r => r.User)
                .FirstOrDefaultAsync(a => a.ApartmentID == id);

            if (apartment == null || !apartment.IsActive)
            {
                return NotFound();
            }

            return apartment;
        }

        // GET: api/Apartments/Search
        [HttpGet("Search")]
        public async Task<ActionResult<IEnumerable<Apartment>>> SearchApartments(
            [FromQuery] string query = "",
            [FromQuery] int? minPrice = null,
            [FromQuery] int? maxPrice = null,
            [FromQuery] int? bedrooms = null,
            [FromQuery] bool petFriendly = true)
        {
            var apartments = _context.Apartments.Where(a => a.IsActive);

            // Apply search query to title, description, and address
            if (!string.IsNullOrEmpty(query))
            {
                apartments = apartments.Where(a =>
                    a.Title.Contains(query) ||
                    a.Description.Contains(query) ||
                    a.Address.Contains(query) ||
                    a.Neighborhood.Contains(query));
            }

            // Apply price filter
            if (minPrice.HasValue)
            {
                apartments = apartments.Where(a => a.PricePerNight >= minPrice.Value);
            }

            if (maxPrice.HasValue)
            {
                apartments = apartments.Where(a => a.PricePerNight <= maxPrice.Value);
            }

            // Apply bedrooms filter
            if (bedrooms.HasValue)
            {
                apartments = apartments.Where(a => a.Bedrooms >= bedrooms.Value);
            }

            // Apply pet-friendly filter
            if (petFriendly)
            {
                apartments = apartments.Where(a => a.PetFriendly);
            }

            return await apartments
                .Include(a => a.ApartmentImages)
                .ToListAsync();
        }

        // POST: api/Apartments
        [HttpPost]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<ActionResult<Apartment>> CreateApartment([FromForm] ApartmentCreateDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Create and configure apartment
            var apartment = new Apartment
            {
                Title = dto.Title,
                Description = dto.Description,
                Address = dto.Address,
                Neighborhood = dto.Neighborhood,
                PricePerNight = dto.PricePerNight,
                Bedrooms = dto.Bedrooms,
                Bathrooms = dto.Bathrooms,
                MaxOccupancy = dto.MaxOccupancy,
                SquareMeters = dto.SquareMeters,
                PetFriendly = dto.PetFriendly,
                PetFee = dto.PetFee,
                Latitude = dto.Latitude,
                Longitude = dto.Longitude,
                CreatedDate = DateTime.Now,
                IsActive = true
            };

            // Add apartment to database
            _context.Apartments.Add(apartment);
            await _context.SaveChangesAsync();

            // Add selected amenities
            if (dto.AmenityIds != null && dto.AmenityIds.Length > 0)
            {
                foreach (var amenityId in dto.AmenityIds)
                {
                    var apartmentAmenity = new ApartmentAmenity
                    {
                        ApartmentID = apartment.ApartmentID,
                        AmenityID = amenityId
                    };
                    _context.ApartmentAmenities.Add(apartmentAmenity);
                }
                await _context.SaveChangesAsync();
            }

            // Handle primary image upload
            if (dto.PrimaryImage != null && dto.PrimaryImage.Length > 0)
            {
                // Save the file
                string fileName = Guid.NewGuid().ToString() + Path.GetExtension(dto.PrimaryImage.FileName);
                string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "Content", "Images", "Apartments");

                // Ensure directory exists
                Directory.CreateDirectory(uploadsFolder);

                string filePath = Path.Combine(uploadsFolder, fileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await dto.PrimaryImage.CopyToAsync(fileStream);
                }

                // Add image to database
                var apartmentImage = new ApartmentImage
                {
                    ApartmentID = apartment.ApartmentID,
                    ImageUrl = "/Content/Images/Apartments/" + fileName,
                    IsPrimary = true,
                    SortOrder = 0,
                    Caption = dto.ImageCaption
                };
                _context.ApartmentImages.Add(apartmentImage);
                await _context.SaveChangesAsync();
            }

            return CreatedAtAction(nameof(GetApartment), new { id = apartment.ApartmentID }, apartment);
        }

        // PUT: api/Apartments/5
        [HttpPut("{id}")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<IActionResult> UpdateApartment(int id, [FromForm] ApartmentUpdateDto dto)
        {
            if (id != dto.ApartmentID)
            {
                return BadRequest("ID mismatch");
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Find apartment
            var apartment = await _context.Apartments.FindAsync(id);
            if (apartment == null)
            {
                return NotFound();
            }

            // Update apartment properties
            apartment.Title = dto.Title;
            apartment.Description = dto.Description;
            apartment.Address = dto.Address;
            apartment.Neighborhood = dto.Neighborhood;
            apartment.PricePerNight = dto.PricePerNight;
            apartment.Bedrooms = dto.Bedrooms;
            apartment.Bathrooms = dto.Bathrooms;
            apartment.MaxOccupancy = dto.MaxOccupancy;
            apartment.SquareMeters = dto.SquareMeters;
            apartment.PetFriendly = dto.PetFriendly;
            apartment.PetFee = dto.PetFee;
            apartment.Latitude = dto.Latitude;
            apartment.Longitude = dto.Longitude;
            apartment.IsActive = dto.IsActive;

            try
            {
                _context.Entry(apartment).State = EntityState.Modified;
                await _context.SaveChangesAsync();

                // Update amenities
                // First remove all existing amenities
                var existingAmenities = _context.ApartmentAmenities.Where(aa => aa.ApartmentID == apartment.ApartmentID);
                _context.ApartmentAmenities.RemoveRange(existingAmenities);
                await _context.SaveChangesAsync();

                // Then add selected amenities
                if (dto.AmenityIds != null && dto.AmenityIds.Length > 0)
                {
                    foreach (var amenityId in dto.AmenityIds)
                    {
                        var apartmentAmenity = new ApartmentAmenity
                        {
                            ApartmentID = apartment.ApartmentID,
                            AmenityID = amenityId
                        };
                        _context.ApartmentAmenities.Add(apartmentAmenity);
                    }
                    await _context.SaveChangesAsync();
                }

                // Handle new image upload
                if (dto.NewImage != null && dto.NewImage.Length > 0)
                {
                    // Save the file
                    string fileName = Guid.NewGuid().ToString() + Path.GetExtension(dto.NewImage.FileName);
                    string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "Content", "Images", "Apartments");

                    // Ensure directory exists
                    Directory.CreateDirectory(uploadsFolder);

                    string filePath = Path.Combine(uploadsFolder, fileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await dto.NewImage.CopyToAsync(fileStream);
                    }

                    // Add image to database
                    var apartmentImage = new ApartmentImage
                    {
                        ApartmentID = apartment.ApartmentID,
                        ImageUrl = "/Content/Images/Apartments/" + fileName,
                        IsPrimary = dto.SetAsPrimary,
                        Caption = dto.ImageCaption,
                        SortOrder = await _context.ApartmentImages.Where(ai => ai.ApartmentID == apartment.ApartmentID).CountAsync()
                    };
                    _context.ApartmentImages.Add(apartmentImage);

                    // If this is primary, reset other images
                    if (dto.SetAsPrimary)
                    {
                        var existingImages = await _context.ApartmentImages
                            .Where(ai => ai.ApartmentID == apartment.ApartmentID && ai.IsPrimary)
                            .ToListAsync();
                        
                        foreach (var image in existingImages)
                        {
                            image.IsPrimary = false;
                            _context.Entry(image).State = EntityState.Modified;
                        }
                    }

                    await _context.SaveChangesAsync();
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ApartmentExists(id))
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

        // PATCH: api/Apartments/5/Status
        [HttpPatch("{id}/Status")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<IActionResult> UpdateApartmentStatus(int id, [FromBody] bool isActive)
        {
            var apartment = await _context.Apartments.FindAsync(id);
            if (apartment == null)
            {
                return NotFound();
            }

            apartment.IsActive = isActive;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ApartmentExists(id))
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

        // DELETE: api/Apartments/5/Image/6
        [HttpDelete("{apartmentId}/Image/{imageId}")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<IActionResult> DeleteApartmentImage(int apartmentId, int imageId)
        {
            var image = await _context.ApartmentImages
                .FirstOrDefaultAsync(i => i.ImageID == imageId && i.ApartmentID == apartmentId);
            
            if (image == null)
            {
                return NotFound();
            }

            // Delete the file from disk if it exists
            try
            {
                string imagePath = Path.Combine(_webHostEnvironment.WebRootPath, image.ImageUrl.TrimStart('/'));
                if (System.IO.File.Exists(imagePath))
                {
                    System.IO.File.Delete(imagePath);
                }
            }
            catch (Exception)
            {
                // Log error but continue
            }

            _context.ApartmentImages.Remove(image);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // GET: api/Apartments/Map
        [HttpGet("Map")]
        public async Task<ActionResult<IEnumerable<ApartmentMapDto>>> GetApartmentsForMap()
        {
            var apartments = await _context.Apartments
                .Where(a => a.IsActive && a.Latitude.HasValue && a.Longitude.HasValue)
                .Include(a => a.ApartmentImages)
                .Select(a => new ApartmentMapDto
                {
                    ApartmentID = a.ApartmentID,
                    Title = a.Title,
                    PricePerNight = a.PricePerNight,
                    Latitude = a.Latitude.Value,
                    Longitude = a.Longitude.Value,
                    PrimaryImageUrl = a.ApartmentImages
                        .Where(ai => ai.IsPrimary)
                        .Select(ai => ai.ImageUrl)
                        .FirstOrDefault() ?? 
                        a.ApartmentImages
                        .Select(ai => ai.ImageUrl)
                        .FirstOrDefault()
                })
                .ToListAsync();

            return apartments;
        }

        private bool ApartmentExists(int id)
        {
            return _context.Apartments.Any(e => e.ApartmentID == id);
        }
    }

}