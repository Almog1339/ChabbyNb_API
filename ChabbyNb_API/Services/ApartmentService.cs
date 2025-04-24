using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using ChabbyNb_API.Models.DTOs;
using ChabbyNb.Models;
using System.Linq;
using Microsoft.AspNetCore.Http;

namespace ChabbyNb_API.Services
{
    public interface IApartmentService
    {
        Task<IEnumerable<Apartment>> GetAllApartmentsAsync();
        Task<IEnumerable<Apartment>> GetFeaturedApartmentsAsync();
        Task<Apartment> GetApartmentByIdAsync(int id);
        Task<IEnumerable<Apartment>> SearchApartmentsAsync(string query, int? minPrice, int? maxPrice, int? bedrooms, bool petFriendly);
        Task<Apartment> CreateApartmentAsync(ApartmentCreateDto dto);
        Task<bool> UpdateApartmentAsync(int id, ApartmentUpdateDto dto);
        Task<bool> UpdateApartmentStatusAsync(int id, bool isActive);
        Task<bool> DeleteApartmentImageAsync(int apartmentId, int imageId);
        Task<List<ApartmentImageDto>> GetApartmentImagesAsync(int apartmentId);
        Task<ApartmentImageDto> GetPrimaryImageAsync(int apartmentId);
    }

    public class ApartmentService : IApartmentService
    {
        private readonly ChabbyNbDbContext _context;
        private readonly IMapper _mapper;
        private readonly IFileStorageService _fileStorage;

        public ApartmentService(ChabbyNbDbContext context, IMapper mapper, IFileStorageService fileStorage)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _fileStorage = fileStorage ?? throw new ArgumentNullException(nameof(fileStorage));
        }

        public async Task<IEnumerable<Apartment>> GetAllApartmentsAsync()
        {
            return await _context.Apartments
                .Where(a => a.IsActive)
                .Include(a => a.ApartmentImages)
                .ToListAsync();
        }

        public async Task<IEnumerable<Apartment>> GetFeaturedApartmentsAsync()
        {
            return await _context.Apartments
                .Where(a => a.IsActive)
                .Include(a => a.Reviews)
                .Include(a => a.ApartmentImages)
                .OrderByDescending(a => a.Reviews.Any() ? a.Reviews.Average(r => r.Rating) : 0)
                .Take(3)
                .ToListAsync();
        }

        public async Task<Apartment> GetApartmentByIdAsync(int id)
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
                return null;
            }

            return apartment;
        }

        public async Task<IEnumerable<Apartment>> SearchApartmentsAsync(
            string query = "",
            int? minPrice = null,
            int? maxPrice = null,
            int? bedrooms = null,
            bool petFriendly = true)
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

        // Add other apartment-related methods...

        // Example of create apartment method
        public async Task<Apartment> CreateApartmentAsync(ApartmentCreateDto dto)
        {
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

            // Process images
            await ProcessApartmentImagesAsync(apartment, dto);

            return apartment;
        }

        // Helper methods for image processing
        private async Task ProcessApartmentImagesAsync(Apartment apartment, ApartmentCreateDto dto)
        {
            int sortOrder = 0;

            // Handle primary image upload
            if (dto.PrimaryImage != null && dto.PrimaryImage.Length > 0)
            {
                string imageUrl = await _fileStorage.SaveFileAsync(dto.PrimaryImage, "images/apartments");

                // Add image to database
                var apartmentImage = new ApartmentImage
                {
                    ApartmentID = apartment.ApartmentID,
                    ImageUrl = imageUrl,
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

                    string imageUrl = await _fileStorage.SaveFileAsync(image, "images/apartments");

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
                        ImageUrl = imageUrl,
                        IsPrimary = false,
                        SortOrder = sortOrder++,
                        Caption = caption
                    };
                    _context.ApartmentImages.Add(apartmentImage);
                }
            }

            // Save all images to database
            await _context.SaveChangesAsync();
        }

        // Other methods for apartment management...

        public async Task<bool> UpdateApartmentAsync(int id, ApartmentUpdateDto dto)
        {
            if (id != dto.ApartmentID)
            {
                return false;
            }

            // Find apartment
            var apartment = await _context.Apartments.FindAsync(id);
            if (apartment == null)
            {
                return false;
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

                // Process new images
                // Similar to the process in CreateApartmentAsync but for updates

                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await ApartmentExistsAsync(id))
                {
                    return false;
                }
                else
                {
                    throw;
                }
            }
        }

        public async Task<bool> UpdateApartmentStatusAsync(int id, bool isActive)
        {
            var apartment = await _context.Apartments.FindAsync(id);
            if (apartment == null)
            {
                return false;
            }

            apartment.IsActive = isActive;

            try
            {
                await _context.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await ApartmentExistsAsync(id))
                {
                    return false;
                }
                else
                {
                    throw;
                }
            }
        }

        public async Task<bool> DeleteApartmentImageAsync(int apartmentId, int imageId)
        {
            var image = await _context.ApartmentImages
                .FirstOrDefaultAsync(i => i.ImageID == imageId && i.ApartmentID == apartmentId);

            if (image == null)
            {
                return false;
            }

            // Delete the file from storage
            try
            {
                string fileName = image.ImageUrl.TrimStart('/').Split('/').Last();
                await _fileStorage.DeleteFileAsync(image.ImageUrl);
            }
            catch (Exception)
            {
                // Log error but continue
            }

            _context.ApartmentImages.Remove(image);
            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<List<ApartmentImageDto>> GetApartmentImagesAsync(int apartmentId)
        {
            // Validate apartment exists
            var apartment = await _context.Apartments.FindAsync(apartmentId);
            if (apartment == null)
            {
                return null;
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

        public async Task<ApartmentImageDto> GetPrimaryImageAsync(int apartmentId)
        {
            // Validate apartment exists
            var apartment = await _context.Apartments.FindAsync(apartmentId);
            if (apartment == null || !apartment.IsActive)
            {
                return null;
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
                return null;
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

        private async Task<bool> ApartmentExistsAsync(int id)
        {
            return await _context.Apartments.AnyAsync(e => e.ApartmentID == id);
        }
    }
}