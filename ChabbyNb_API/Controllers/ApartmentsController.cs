using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ChabbyNb.Models;
using ChabbyNb_API.Models.DTOs;
using ChabbyNb_API.Services;
using ChabbyNb_API.Services.Core;
using Microsoft.Extensions.Logging;

namespace ChabbyNb_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ApartmentsController : ControllerBase
    {
        private readonly IApartmentService _apartmentService;
        private readonly ILogger<ApartmentsController> _logger;

        public ApartmentsController(IApartmentService apartmentService, ILogger<ApartmentsController> logger)
        {
            _apartmentService = apartmentService ?? throw new ArgumentNullException(nameof(apartmentService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // GET: api/Apartments
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ApartmentDto>>> GetApartments()
        {
            try
            {
                var apartments = await _apartmentService.GetAllAsync();
                return Ok(apartments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting apartments");
                return StatusCode(500, new { error = "An error occurred while retrieving apartments" });
            }
        }

        // GET: api/Apartments/Featured
        [HttpGet("Featured")]
        public async Task<ActionResult<IEnumerable<ApartmentDto>>> GetFeaturedApartments([FromQuery] int count = 3)
        {
            try
            {
                var featuredApartments = await _apartmentService.GetFeaturedApartmentsAsync(count);
                return Ok(featuredApartments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting featured apartments");
                return StatusCode(500, new { error = "An error occurred while retrieving featured apartments" });
            }
        }

        // GET: api/Apartments/Map
        [HttpGet("Map")]
        public async Task<ActionResult<IEnumerable<ApartmentMapDto>>> GetApartmentsForMap()
        {
            try
            {
                var apartments = await _apartmentService.GetApartmentsForMapAsync();
                return Ok(apartments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting apartments for map");
                return StatusCode(500, new { error = "An error occurred while retrieving apartments for map" });
            }
        }

        // GET: api/Apartments/5
        [HttpGet("{id}")]
        public async Task<ActionResult<ApartmentDto>> GetApartment(int id)
        {
            try
            {
                var apartment = await _apartmentService.GetByIdAsync(id);
                if (apartment == null)
                {
                    return NotFound(new { error = "Apartment not found" });
                }
                return Ok(apartment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting apartment with ID {id}");
                return StatusCode(500, new { error = "An error occurred while retrieving the apartment" });
            }
        }

        // GET: api/Apartments/Search
        [HttpGet("Search")]
        public async Task<ActionResult<IEnumerable<ApartmentDto>>> SearchApartments(
            [FromQuery] string query = "",
            [FromQuery] int? minPrice = null,
            [FromQuery] int? maxPrice = null,
            [FromQuery] int? bedrooms = null,
            [FromQuery] bool? petFriendly = null)
        {
            try
            {
                var apartments = await _apartmentService.SearchApartmentsAsync(query, minPrice, maxPrice, bedrooms, petFriendly);
                return Ok(apartments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching apartments");
                return StatusCode(500, new { error = "An error occurred while searching apartments" });
            }
        }

        // GET: api/Apartments/{apartmentId}/Images
        [HttpGet("{apartmentId}/Images")]
        public async Task<ActionResult<IEnumerable<ApartmentImageDto>>> GetApartmentImages(int apartmentId)
        {
            try
            {
                var images = await _apartmentService.GetApartmentImagesAsync(apartmentId);
                if (images == null)
                {
                    return NotFound(new { error = "Apartment not found" });
                }

                return Ok(images);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting images for apartment {apartmentId}");
                return StatusCode(500, new { error = "An error occurred while retrieving apartment images" });
            }
        }

        // GET: api/Apartments/{apartmentId}/PrimaryImage
        [HttpGet("{apartmentId}/PrimaryImage")]
        public async Task<ActionResult<ApartmentImageDto>> GetPrimaryImage(int apartmentId)
        {
            try
            {
                var image = await _apartmentService.GetPrimaryImageAsync(apartmentId);
                if (image == null)
                {
                    return NotFound(new { error = "No primary image found for this apartment" });
                }

                return Ok(image);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting primary image for apartment {apartmentId}");
                return StatusCode(500, new { error = "An error occurred while retrieving the primary image" });
            }
        }

        // POST: api/Apartments
        [HttpPost]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<ActionResult<ApartmentDto>> CreateApartment([FromForm] ApartmentCreateDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var apartment = await _apartmentService.CreateAsync(dto);
                return CreatedAtAction(nameof(GetApartment), new { id = apartment.ApartmentID }, apartment);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid input for apartment creation");
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating apartment");
                return StatusCode(500, new { error = "An error occurred while creating the apartment" });
            }
        }

        // PUT: api/Apartments/5
        [HttpPut("{id}")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<IActionResult> UpdateApartment(int id, [FromForm] ApartmentUpdateDto dto)
        {
            try
            {
                if (id != dto.ApartmentID)
                {
                    return BadRequest(new { error = "ID mismatch" });
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var updatedApartment = await _apartmentService.UpdateAsync(id, dto);
                if (updatedApartment == null)
                {
                    return NotFound(new { error = "Apartment not found" });
                }

                return Ok(updatedApartment);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, $"Invalid input for apartment update ID {id}");
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating apartment with ID {id}");
                return StatusCode(500, new { error = "An error occurred while updating the apartment" });
            }
        }

        // PATCH: api/Apartments/5/Status
        [HttpPatch("{id}/Status")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<IActionResult> UpdateApartmentStatus(int id, [FromBody] UpdateApartmentStatusDto statusDto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var result = await _apartmentService.UpdateApartmentStatusAsync(id, statusDto.IsActive);
                if (!result.Success)
                {
                    return NotFound(new { error = result.Errors.FirstOrDefault() ?? "Apartment not found" });
                }

                return Ok(new { success = true, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating status for apartment with ID {id}");
                return StatusCode(500, new { error = "An error occurred while updating the apartment status" });
            }
        }

        // DELETE: api/Apartments/5/Image/6
        [HttpDelete("{apartmentId}/Image/{imageId}")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<IActionResult> DeleteApartmentImage(int apartmentId, int imageId)
        {
            try
            {
                var result = await _apartmentService.DeleteApartmentImageAsync(apartmentId, imageId);
                if (!result.Success)
                {
                    return NotFound(new { error = result.Errors.FirstOrDefault() ?? "Image not found" });
                }

                return Ok(new { success = true, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting image {imageId} for apartment {apartmentId}");
                return StatusCode(500, new { error = "An error occurred while deleting the apartment image" });
            }
        }
    }

    // Supporting DTOs
    public class UpdateApartmentStatusDto
    {
        public bool IsActive { get; set; }
    }
}