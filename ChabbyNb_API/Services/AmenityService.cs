using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using ChabbyNb_API.Models.DTOs;
using System.Linq;
using Microsoft.AspNetCore.Http;

namespace ChabbyNb_API.Services
{
    public interface IAmenityService
    {
        Task<IEnumerable<AmenityDto>> GetAllAmenitiesAsync();
        Task<AmenityDto> GetAmenityByIdAsync(int id);
        Task<IEnumerable<string>> GetCategoriesAsync();
        Task<IEnumerable<AmenityDto>> GetAmenitiesByCategoryAsync(string category);
        Task<IEnumerable<AmenityDto>> GetPopularAmenitiesAsync(int count);
        Task<IEnumerable<AmenityDto>> SearchAmenitiesAsync(string query);
        Task<IEnumerable<AmenityDto>> GetAmenitiesForApartmentAsync(int apartmentId);
        Task<AmenityDto> CreateAmenityAsync(AmenityCreateDto dto);
        Task<AmenityDto> UpdateAmenityAsync(int id, AmenityUpdateDto dto);
        Task<bool> DeleteAmenityAsync(int id);
    }

    public class AmenityService : IAmenityService
    {
        private readonly ChabbyNbDbContext _context;
        private readonly IMapper _mapper;
        private readonly IFileStorageService _fileStorage;

        public AmenityService(ChabbyNbDbContext context, IMapper mapper, IFileStorageService fileStorage)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _fileStorage = fileStorage ?? throw new ArgumentNullException(nameof(fileStorage));
        }

        public async Task<IEnumerable<AmenityDto>> GetAllAmenitiesAsync()
        {
            var amenities = await _context.Amenities
                .OrderBy(a => a.Category)
                .ThenBy(a => a.Name)
                .Select(a => new AmenityDto
                {
                    AmenityID = a.AmenityID,
                    Name = a.Name,
                    IconBase64 = a.Icon != null ? Convert.ToBase64String(a.Icon) : null,
                    IconContentType = a.IconContentType,
                    Category = a.Category,
                    UsageCount = a.ApartmentAmenities.Count
                })
                .ToListAsync();

            return amenities;
        }

        public async Task<AmenityDto> GetAmenityByIdAsync(int id)
        {
            var amenity = await _context.Amenities
                .Where(a => a.AmenityID == id)
                .Select(a => new AmenityDto
                {
                    AmenityID = a.AmenityID,
                    Name = a.Name,
                    IconBase64 = a.Icon != null ? Convert.ToBase64String(a.Icon) : null,
                    IconContentType = a.IconContentType,
                    Category = a.Category,
                    UsageCount = a.ApartmentAmenities.Count
                })
                .FirstOrDefaultAsync();

            return amenity;
        }

        public async Task<IEnumerable<string>> GetCategoriesAsync()
        {
            var categories = await _context.Amenities
                .Select(a => a.Category)
                .Distinct()
                .Where(c => !string.IsNullOrEmpty(c))
                .OrderBy(c => c)
                .ToListAsync();

            return categories;
        }

        public async Task<IEnumerable<AmenityDto>> GetAmenitiesByCategoryAsync(string category)
        {
            var amenities = await _context.Amenities
                .Where(a => a.Category == category)
                .OrderBy(a => a.Name)
                .Select(a => new AmenityDto
                {
                    AmenityID = a.AmenityID,
                    Name = a.Name,
                    IconBase64 = a.Icon != null ? Convert.ToBase64String(a.Icon) : null,
                    IconContentType = a.IconContentType,
                    Category = a.Category,
                    UsageCount = a.ApartmentAmenities.Count
                })
                .ToListAsync();

            return amenities;
        }

        public async Task<IEnumerable<AmenityDto>> GetPopularAmenitiesAsync(int count)
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
                    IconContentType = a.IconContentType,
                    Category = a.Category,
                    UsageCount = a.ApartmentAmenities.Count
                })
                .ToListAsync();

            return amenities;
        }

        public async Task<IEnumerable<AmenityDto>> SearchAmenitiesAsync(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return Enumerable.Empty<AmenityDto>();
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
                    IconContentType = a.IconContentType,
                    Category = a.Category,
                    UsageCount = a.ApartmentAmenities.Count
                })
                .ToListAsync();

            return amenities;
        }

        public async Task<IEnumerable<AmenityDto>> GetAmenitiesForApartmentAsync(int apartmentId)
        {
            // Check if apartment exists
            var apartmentExists = await _context.Apartments.AnyAsync(a => a.ApartmentID == apartmentId);
            if (!apartmentExists)
            {
                return null;
            }

            var amenities = await _context.ApartmentAmenities
                .Where(aa => aa.ApartmentID == apartmentId)
                .Include(aa => aa.Amenity)
                .Select(aa => new AmenityDto
                {
                    AmenityID = aa.Amenity.AmenityID,
                    Name = aa.Amenity.Name,
                    IconBase64 = aa.Amenity.Icon != null ? Convert.ToBase64String(aa.Amenity.Icon) : null,
                    IconContentType = aa.Amenity.IconContentType,
                    Category = aa.Amenity.Category,
                    UsageCount = aa.Amenity.ApartmentAmenities.Count
                })
                .OrderBy(a => a.Category)
                .ThenBy(a => a.Name)
                .ToListAsync();

            return amenities;
        }

        public async Task<AmenityDto> CreateAmenityAsync(AmenityCreateDto dto)
        {
            byte[] iconData = null;
            string contentType = null;

            if (dto.IconFile != null && dto.IconFile.Length > 0)
            {
                // Validate file extension
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                var extension = Path.GetExtension(dto.IconFile.FileName).ToLowerInvariant();

                if (!allowedExtensions.Contains(extension))
                {
                    throw new ArgumentException("Invalid file type. Only image files (jpg, jpeg, png, gif, webp) are allowed.");
                }

                // Validate file size (max 1MB)
                if (dto.IconFile.Length > 1048576) // 1MB
                {
                    throw new ArgumentException("File size exceeds the maximum allowed (1MB).");
                }

                // Get the content type
                contentType = dto.IconFile.ContentType;

                // Process the image and get optimized data
                iconData = await _fileStorage.OptimizeImageAsync(dto.IconFile, 48, 48);
            }
            else
            {
                throw new ArgumentException("Icon image is required");
            }

            var amenity = new Amenity
            {
                Name = dto.Name,
                Icon = iconData,
                IconContentType = contentType,
                Category = dto.Category
            };

            _context.Amenities.Add(amenity);
            await _context.SaveChangesAsync();

            // Convert the binary data to a Base64 string for the response
            string base64Icon = iconData != null ? Convert.ToBase64String(iconData) : null;

            var resultDto = new AmenityDto
            {
                AmenityID = amenity.AmenityID,
                Name = amenity.Name,
                IconBase64 = base64Icon,
                IconContentType = contentType,
                Category = amenity.Category,
                UsageCount = 0
            };

            return resultDto;
        }

        public async Task<AmenityDto> UpdateAmenityAsync(int id, AmenityUpdateDto dto)
        {
            if (id != dto.AmenityID)
            {
                throw new ArgumentException("ID mismatch");
            }

            var amenity = await _context.Amenities.FindAsync(id);

            if (amenity == null)
            {
                return null;
            }

            // Process the icon file if a new one was uploaded
            if (dto.IconFile != null && dto.IconFile.Length > 0)
            {
                // Validate file extension
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                var extension = Path.GetExtension(dto.IconFile.FileName).ToLowerInvariant();

                if (!allowedExtensions.Contains(extension))
                {
                    throw new ArgumentException("Invalid file type. Only image files (jpg, jpeg, png, gif, webp) are allowed.");
                }

                // Validate file size (max 1MB)
                if (dto.IconFile.Length > 1048576) // 1MB
                {
                    throw new ArgumentException("File size exceeds the maximum allowed (1MB).");
                }

                // Get the content type
                amenity.IconContentType = dto.IconFile.ContentType;

                // Process and save the image as binary data
                amenity.Icon = await _fileStorage.OptimizeImageAsync(dto.IconFile, 48, 48);
            }

            // Update other amenity properties
            amenity.Name = dto.Name;
            amenity.Category = dto.Category;

            try
            {
                _context.Entry(amenity).State = EntityState.Modified;
                await _context.SaveChangesAsync();

                // Get usage count for the response
                int usageCount = await _context.ApartmentAmenities
                    .CountAsync(aa => aa.AmenityID == amenity.AmenityID);

                var resultDto = new AmenityDto
                {
                    AmenityID = amenity.AmenityID,
                    Name = amenity.Name,
                    IconBase64 = amenity.Icon != null ? Convert.ToBase64String(amenity.Icon) : null,
                    IconContentType = amenity.IconContentType,
                    Category = amenity.Category,
                    UsageCount = usageCount
                };

                return resultDto;
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await AmenityExistsAsync(id))
                {
                    return null;
                }
                else
                {
                    throw;
                }
            }
        }

        public async Task<bool> DeleteAmenityAsync(int id)
        {
            var amenity = await _context.Amenities.FindAsync(id);
            if (amenity == null)
            {
                return false;
            }

            // Check if this amenity is in use
            bool isInUse = await _context.ApartmentAmenities.AnyAsync(aa => aa.AmenityID == id);

            if (isInUse)
            {
                throw new InvalidOperationException("This amenity is in use by one or more apartments and cannot be deleted.");
            }

            // Delete the amenity from the database
            _context.Amenities.Remove(amenity);
            await _context.SaveChangesAsync();

            return true;
        }

        private async Task<bool> AmenityExistsAsync(int id)
        {
            return await _context.Amenities.AnyAsync(e => e.AmenityID == id);
        }
    }
}