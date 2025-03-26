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

            // Ensure directory exists for images
            string uploadsFolder = GetUploadsDirectory();

            int sortOrder = 0;

            // Handle primary image upload
            if (dto.PrimaryImage != null && dto.PrimaryImage.Length > 0)
            {
                // Save the file
                string fileName = await SaveApartmentImageAsync(dto.PrimaryImage, uploadsFolder);

                // Add image to database
                var apartmentImage = new ApartmentImage
                {
                    ApartmentID = apartment.ApartmentID,
                    ImageUrl = "/images/apartments/" + fileName,
                    IsPrimary = true,
                    SortOrder = sortOrder++,
                    Caption = dto.PrimaryImageCaption
                };
                _context.ApartmentImages.Add(apartmentImage);
            }

            // Handle additional images
            if (dto.AdditionalImages != null && dto.AdditionalImages.Count > 0)
            {
                for (int i = 0; i < dto.AdditionalImages.Count; i++)
                {
                    var image = dto.AdditionalImages[i];

                    // Skip invalid files
                    if (image == null || image.Length == 0)
                        continue;

                    // Save the file
                    string fileName = await SaveApartmentImageAsync(image, uploadsFolder);

                    // Get caption if available
                    string caption = null;
                    if (dto.AdditionalImageCaptions != null && i < dto.AdditionalImageCaptions.Count)
                    {
                        caption = dto.AdditionalImageCaptions[i];
                    }

                    // Add image to database
                    var apartmentImage = new ApartmentImage
                    {
                        ApartmentID = apartment.ApartmentID,
                        ImageUrl = "/images/apartments/" + fileName,
                        IsPrimary = false, // Not primary
                        SortOrder = sortOrder++,
                        Caption = caption
                    };
                    _context.ApartmentImages.Add(apartmentImage);
                }
            }

            // Save all images to database
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetApartment), new { id = apartment.ApartmentID }, apartment);
        }

        // Helper method to save apartment images
        private async Task<string> SaveApartmentImageAsync(IFormFile imageFile, string uploadsFolder)
        {
            // Validate input
            if (imageFile == null || imageFile.Length == 0)
            {
                throw new ArgumentException("Invalid file");
            }

            // Validate file extension
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
            var extension = Path.GetExtension(imageFile.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(extension))
            {
                throw new InvalidOperationException("Invalid file type. Only image files (jpg, jpeg, png, gif, webp) are allowed.");
            }

            // Validate file size (max 10MB)
            if (imageFile.Length > 10485760) // 10MB
            {
                throw new InvalidOperationException("File size exceeds the maximum allowed (10MB).");
            }

            // Generate unique filename
            string fileName = Guid.NewGuid().ToString() + extension;
            string filePath = Path.Combine(uploadsFolder, fileName);

            // Ensure directory exists
            Directory.CreateDirectory(uploadsFolder);

            // Save the file
            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await imageFile.CopyToAsync(fileStream);
            }

            return fileName;
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

                // Ensure directory exists for images
                string uploadsFolder = GetUploadsDirectory();

                // Get the current highest sort order value
                int sortOrder = await _context.ApartmentImages
                    .Where(ai => ai.ApartmentID == apartment.ApartmentID)
                    .Select(ai => ai.SortOrder)
                    .DefaultIfEmpty(-1)
                    .MaxAsync() + 1;

                // Handle single new image upload (for backward compatibility)
                if (dto.NewImage != null && dto.NewImage.Length > 0)
                {
                    // Save the file
                    string fileName = await SaveApartmentImageAsync(dto.NewImage, uploadsFolder);

                    // Add image to database
                    var apartmentImage = new ApartmentImage
                    {
                        ApartmentID = apartment.ApartmentID,
                        ImageUrl = "/images/apartments/" + fileName,
                        IsPrimary = dto.SetAsPrimary,
                        Caption = dto.ImageCaption,
                        SortOrder = sortOrder++
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
                }

                // Handle additional images
                if (dto.AdditionalImages != null && dto.AdditionalImages.Count > 0)
                {
                    for (int i = 0; i < dto.AdditionalImages.Count; i++)
                    {
                        var image = dto.AdditionalImages[i];

                        // Skip invalid files
                        if (image == null || image.Length == 0)
                            continue;

                        // Save the file
                        string fileName = await SaveApartmentImageAsync(image, uploadsFolder);

                        // Get caption if available
                        string caption = null;
                        if (dto.AdditionalImageCaptions != null && i < dto.AdditionalImageCaptions.Count)
                        {
                            caption = dto.AdditionalImageCaptions[i];
                        }

                        // Add image to database
                        var apartmentImage = new ApartmentImage
                        {
                            ApartmentID = apartment.ApartmentID,
                            ImageUrl = "/images/apartments/" + fileName,
                            IsPrimary = false, // Additional images are never primary by default
                            SortOrder = sortOrder++,
                            Caption = caption
                        };
                        _context.ApartmentImages.Add(apartmentImage);
                    }
                }

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
                string baseDir = GetUploadsDirectory();
                string fileName = image.ImageUrl.TrimStart('/').Split('/').Last();
                string imagePath = Path.Combine(baseDir, fileName);

                if (System.IO.File.Exists(imagePath))
                {
                    System.IO.File.Delete(imagePath);
                }
            }
            catch (Exception ex)
            {
                // Log error but continue
                Console.WriteLine($"Error deleting file: {ex.Message}");
            }

            _context.ApartmentImages.Remove(image);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // PATCH: api/Apartments/{apartmentId}/Images/Reorder
        [HttpPatch("{apartmentId}/Images/Reorder")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<IActionResult> ReorderApartmentImages(int apartmentId, [FromBody] List<ImageReorderDto> imageOrders)
        {
            // Validate apartment exists
            var apartment = await _context.Apartments.FindAsync(apartmentId);
            if (apartment == null)
            {
                return NotFound("Apartment not found");
            }

            // Get all images for this apartment
            var images = await _context.ApartmentImages
                .Where(i => i.ApartmentID == apartmentId)
                .ToListAsync();

            // Validate that all image IDs in the request exist
            foreach (var order in imageOrders)
            {
                if (!images.Any(i => i.ImageID == order.ImageID))
                {
                    return BadRequest($"Image ID {order.ImageID} not found for this apartment");
                }
            }

            // Update sort orders
            foreach (var order in imageOrders)
            {
                var image = images.First(i => i.ImageID == order.ImageID);
                image.SortOrder = order.SortOrder;
                _context.Entry(image).State = EntityState.Modified;
            }

            await _context.SaveChangesAsync();
            return Ok();
        }

        // DTO for image reordering
        public class ImageReorderDto
        {
            public int ImageID { get; set; }
            public int SortOrder { get; set; }
        }

        // PATCH: api/Apartments/{apartmentId}/Images/{imageId}/SetPrimary
        [HttpPatch("{apartmentId}/Images/{imageId}/SetPrimary")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<IActionResult> SetPrimaryImage(int apartmentId, int imageId)
        {
            // Validate apartment exists
            var apartment = await _context.Apartments.FindAsync(apartmentId);
            if (apartment == null)
            {
                return NotFound("Apartment not found");
            }

            // Validate image exists
            var newPrimaryImage = await _context.ApartmentImages
                .FirstOrDefaultAsync(i => i.ImageID == imageId && i.ApartmentID == apartmentId);

            if (newPrimaryImage == null)
            {
                return NotFound("Image not found");
            }

            // Get current primary images
            var currentPrimaryImages = await _context.ApartmentImages
                .Where(i => i.ApartmentID == apartmentId && i.IsPrimary)
                .ToListAsync();

            // Update all current primary images to non-primary
            foreach (var image in currentPrimaryImages)
            {
                image.IsPrimary = false;
                _context.Entry(image).State = EntityState.Modified;
            }

            // Set new primary image
            newPrimaryImage.IsPrimary = true;
            _context.Entry(newPrimaryImage).State = EntityState.Modified;

            await _context.SaveChangesAsync();
            return Ok();
        }

        // GET: api/Apartments/{apartmentId}/Images
        [HttpGet("{apartmentId}/Images")]
        public async Task<ActionResult<IEnumerable<ApartmentImageDto>>> GetApartmentImages(int apartmentId)
        {
            // Validate apartment exists
            var apartment = await _context.Apartments.FindAsync(apartmentId);
            if (apartment == null)
            {
                return NotFound("Apartment not found");
            }

            var images = await _context.ApartmentImages
                .Where(i => i.ApartmentID == apartmentId)
                .OrderBy(i => i.SortOrder)
                .Select(i => new ApartmentImageDto
                {
                    ImageID = i.ImageID,
                    ApartmentID = i.ApartmentID,
                    ImageUrl = i.ImageUrl,
                    IsPrimary = i.IsPrimary,
                    Caption = i.Caption,
                    SortOrder = i.SortOrder
                })
                .ToListAsync();

            return images;
        }

        // DELETE: api/Apartments/{apartmentId}/Images/BatchDelete
        [HttpDelete("{apartmentId}/Images/BatchDelete")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<IActionResult> BatchDeleteApartmentImages(int apartmentId, [FromBody] List<int> imageIds)
        {
            // Validate apartment exists
            var apartment = await _context.Apartments.FindAsync(apartmentId);
            if (apartment == null)
            {
                return NotFound("Apartment not found");
            }

            if (imageIds == null || imageIds.Count == 0)
            {
                return BadRequest("No image IDs provided");
            }

            // Get all requested images that belong to this apartment
            var images = await _context.ApartmentImages
                .Where(i => i.ApartmentID == apartmentId && imageIds.Contains(i.ImageID))
                .ToListAsync();

            if (images.Count == 0)
            {
                return NotFound("None of the specified images were found");
            }

            // Track results
            var results = new
            {
                Requested = imageIds.Count,
                Deleted = 0,
                Failed = 0,
                Errors = new List<string>()
            };

            // Process each image
            foreach (var image in images)
            {
                // Delete the file from disk if it exists
                try
                {
                    string baseDir = GetUploadsDirectory();
                    string fileName = image.ImageUrl.TrimStart('/').Split('/').Last();
                    string imagePath = Path.Combine(baseDir, fileName);

                    if (System.IO.File.Exists(imagePath))
                    {
                        System.IO.File.Delete(imagePath);
                    }
                }
                catch (Exception ex)
                {
                    // Log error but continue
                    Console.WriteLine($"Error deleting file: {ex.Message}");
                }
            }

            // Save changes to database
            await _context.SaveChangesAsync();

            // If we're deleting the primary image, we need to set a new one
            if (images.Any(i => i.IsPrimary))
            {
                // Try to find another image to set as primary
                var newPrimary = await _context.ApartmentImages
                    .FirstOrDefaultAsync(i => i.ApartmentID == apartmentId);

                if (newPrimary != null)
                {
                    newPrimary.IsPrimary = true;
                    _context.Entry(newPrimary).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                }
            }

            return Ok(results);
        }

        // PATCH: api/Apartments/{apartmentId}/Images/BatchUpdateCaptions
        [HttpPatch("{apartmentId}/Images/BatchUpdateCaptions")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<IActionResult> BatchUpdateImageCaptions(int apartmentId, [FromBody] List<ImageCaptionUpdateDto> updates)
        {
            // Validate apartment exists
            var apartment = await _context.Apartments.FindAsync(apartmentId);
            if (apartment == null)
            {
                return NotFound("Apartment not found");
            }

            if (updates == null || updates.Count == 0)
            {
                return BadRequest("No caption updates provided");
            }

            // Get all image IDs to update
            var imageIds = updates.Select(u => u.ImageID).ToList();

            // Get all existing images that belong to this apartment
            var images = await _context.ApartmentImages
                .Where(i => i.ApartmentID == apartmentId && imageIds.Contains(i.ImageID))
                .ToListAsync();

            if (images.Count == 0)
            {
                return NotFound("None of the specified images were found");
            }

            // Track results
            var results = new
            {
                Requested = updates.Count,
                Updated = 0,
                NotFound = imageIds.Except(images.Select(i => i.ImageID)).ToList()
            };

            // Update each image caption
            foreach (var update in updates)
            {
                var image = images.FirstOrDefault(i => i.ImageID == update.ImageID);
                if (image != null)
                {
                    image.Caption = update.Caption;
                    _context.Entry(image).State = EntityState.Modified;
                    results.GetType().GetProperty("Updated").SetValue(results, ((int)results.GetType().GetProperty("Updated").GetValue(results)) + 1);
                }
            }

            // Save changes to database
            await _context.SaveChangesAsync();

            return Ok(results);
        }

        // GET: api/Apartments/{apartmentId}/PrimaryImage
        [HttpGet("{apartmentId}/PrimaryImage")]
        public async Task<ActionResult<ApartmentImageDto>> GetPrimaryImage(int apartmentId)
        {
            // Validate apartment exists
            var apartment = await _context.Apartments.FindAsync(apartmentId);
            if (apartment == null || !apartment.IsActive)
            {
                return NotFound("Apartment not found");
            }

            // Find the primary image
            var primaryImage = await _context.ApartmentImages
                .Where(i => i.ApartmentID == apartmentId && i.IsPrimary)
                .FirstOrDefaultAsync();

            // If no primary image is set, try to get the first image
            if (primaryImage == null)
            {
                primaryImage = await _context.ApartmentImages
                    .Where(i => i.ApartmentID == apartmentId)
                    .OrderBy(i => i.SortOrder)
                    .FirstOrDefaultAsync();
            }

            if (primaryImage == null)
            {
                return NotFound("No images found for this apartment");
            }

            var imageDto = new ApartmentImageDto
            {
                ImageID = primaryImage.ImageID,
                ApartmentID = primaryImage.ApartmentID,
                ImageUrl = primaryImage.ImageUrl,
                IsPrimary = primaryImage.IsPrimary,
                Caption = primaryImage.Caption,
                SortOrder = primaryImage.SortOrder
            };

            return imageDto;
        }

        // POST: api/Apartments/{apartmentId}/Images/AddMultiple
        [HttpPost("{apartmentId}/Images/AddMultiple")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<ActionResult<IEnumerable<ApartmentImageDto>>> AddMultipleImages(int apartmentId, [FromForm] ApartmentImagesUploadDto uploadDto)
        {
            // Validate apartment exists
            var apartment = await _context.Apartments.FindAsync(apartmentId);
            if (apartment == null)
            {
                return NotFound("Apartment not found");
            }

            // Validate the upload data
            if (uploadDto.Images == null || uploadDto.Images.Count == 0)
            {
                return BadRequest("No images provided");
            }

            // Ensure directory exists for images
            string uploadsFolder = GetUploadsDirectory();

            // Get the current highest sort order value
            int sortOrder = 0;
            var maxSortOrder = await _context.ApartmentImages
                .Where(ai => ai.ApartmentID == apartmentId)
                .MaxAsync(ai => (int?)ai.SortOrder);

            // If there are existing images, start with the next sort order value
            if (maxSortOrder.HasValue)
            {
                sortOrder = maxSortOrder.Value + 1;
            }

            // List to track added images and results
            var addedImages = new List<ApartmentImageDto>();
            var failedUploads = new List<string>();

            // Process each image
            for (int i = 0; i < uploadDto.Images.Count; i++)
            {
                var image = uploadDto.Images[i];

                // Skip invalid files
                if (image == null || image.Length == 0)
                {
                    failedUploads.Add($"Image at index {i} is invalid or empty");
                    continue;
                }

                try
                {
                    // Save the file
                    string fileName = await SaveApartmentImageAsync(image, uploadsFolder);

                    // Get caption if available
                    string caption = null;
                    if (uploadDto.Captions != null && i < uploadDto.Captions.Count)
                    {
                        caption = uploadDto.Captions[i];
                    }

                    // Determine if this should be the primary image
                    bool isPrimary = false;
                    if (uploadDto.SetPrimaryImageIndex.HasValue && uploadDto.SetPrimaryImageIndex.Value == i)
                    {
                        isPrimary = true;

                        // If setting a new primary, update existing primary images
                        if (isPrimary)
                        {
                            var currentPrimaryImages = await _context.ApartmentImages
                                .Where(ai => ai.ApartmentID == apartmentId && ai.IsPrimary)
                                .ToListAsync();

                            foreach (var primaryImage in currentPrimaryImages)
                            {
                                primaryImage.IsPrimary = false;
                                _context.Entry(primaryImage).State = EntityState.Modified;
                            }
                        }
                    }

                    // Add image to database
                    var apartmentImage = new ApartmentImage
                    {
                        ApartmentID = apartmentId,
                        ImageUrl = "/images/apartments/" + fileName,
                        IsPrimary = isPrimary,
                        SortOrder = sortOrder++,
                        Caption = caption
                    };

                    _context.ApartmentImages.Add(apartmentImage);
                    await _context.SaveChangesAsync();

                    // Add to result list
                    addedImages.Add(new ApartmentImageDto
                    {
                        ImageID = apartmentImage.ImageID,
                        ApartmentID = apartmentImage.ApartmentID,
                        ImageUrl = apartmentImage.ImageUrl,
                        IsPrimary = apartmentImage.IsPrimary,
                        Caption = apartmentImage.Caption,
                        SortOrder = apartmentImage.SortOrder
                    });
                }
                catch (Exception ex)
                {
                    // Log the error but continue with other images
                    failedUploads.Add($"Failed to upload image at index {i}: {ex.Message}");
                }
            }

            // Return results
            return Ok(new
            {
                Success = addedImages.Count > 0,
                Uploaded = addedImages.Count,
                Failed = failedUploads.Count,
                FailedUploads = failedUploads,
                Images = addedImages
            });
        }

        private string GetUploadsDirectory()
        {
            // Try to use WebRootPath first
            if (!string.IsNullOrEmpty(_webHostEnvironment.WebRootPath))
            {
                var path = Path.Combine(_webHostEnvironment.WebRootPath, "images", "apartments");
                Directory.CreateDirectory(path);
                return path;
            }

            // Fallback to a directory within the application folder
            var fallbackPath = Path.Combine(Directory.GetCurrentDirectory(), "Storage", "images", "apartments");
            Directory.CreateDirectory(fallbackPath);
            return fallbackPath;
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