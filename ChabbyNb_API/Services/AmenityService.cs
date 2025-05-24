using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using ChabbyNb_API.Models.DTOs;
using ChabbyNb_API.Services.Core;

namespace ChabbyNb_API.Services
{
    public interface IAmenityService :
        IEntityService<Amenity, AmenityDto, AmenityCreateDto, AmenityUpdateDto>,
        ISearchableService<AmenityDto>,
        ICategorizedService<AmenityDto>
    {
        Task<IEnumerable<AmenityDto>> GetPopularAmenitiesAsync(int count = 10);
        Task<IEnumerable<AmenityDto>> GetAmenitiesForApartmentAsync(int apartmentId);
    }

    public class AmenityService : BaseEntityService<Amenity, AmenityDto, AmenityCreateDto, AmenityUpdateDto>,
        IAmenityService
    {
        private readonly IFileStorageService _fileStorage;

        public AmenityService(
            ChabbyNbDbContext context,
            IMapper mapper,
            ILogger<AmenityService> logger,
            IFileStorageService fileStorage)
            : base(context, mapper, logger)
        {
            _fileStorage = fileStorage ?? throw new ArgumentNullException(nameof(fileStorage));
        }

        #region Base Service Implementation

        protected override async Task<Amenity> GetEntityByIdAsync(int id)
        {
            return await _dbSet.FindAsync(id);
        }

        protected override IQueryable<Amenity> GetBaseQuery()
        {
            return _dbSet.OrderBy(a => a.Category).ThenBy(a => a.Name);
        }

        protected override async Task<AmenityDto> MapToDto(Amenity entity)
        {
            return new AmenityDto
            {
                AmenityID = entity.AmenityID,
                Name = entity.Name,
                IconBase64 = entity.Icon != null ? Convert.ToBase64String(entity.Icon) : null,
                IconContentType = entity.IconContentType,
                Category = entity.Category,
                UsageCount = entity.ApartmentAmenities?.Count ?? 0
            };
        }

        protected override async Task<IEnumerable<AmenityDto>> MapToDtos(IEnumerable<Amenity> entities)
        {
            return entities.Select(a => new AmenityDto
            {
                AmenityID = a.AmenityID,
                Name = a.Name,
                IconBase64 = a.Icon != null ? Convert.ToBase64String(a.Icon) : null,
                IconContentType = a.IconContentType,
                Category = a.Category,
                UsageCount = a.ApartmentAmenities?.Count ?? 0
            });
        }

        protected override async Task<Amenity> MapFromCreateDto(AmenityCreateDto createDto)
        {
            byte[] iconData = null;
            string contentType = null;

            if (createDto.IconFile != null && createDto.IconFile.Length > 0)
            {
                contentType = createDto.IconFile.ContentType;
                iconData = await _fileStorage.OptimizeImageAsync(createDto.IconFile, 48, 48);
            }

            return new Amenity
            {
                Name = createDto.Name,
                Icon = iconData,
                IconContentType = contentType,
                Category = createDto.Category
            };
        }

        protected override async Task MapFromUpdateDto(AmenityUpdateDto updateDto, Amenity entity)
        {
            entity.Name = updateDto.Name;
            entity.Category = updateDto.Category;

            if (updateDto.IconFile != null && updateDto.IconFile.Length > 0)
            {
                entity.IconContentType = updateDto.IconFile.ContentType;
                entity.Icon = await _fileStorage.OptimizeImageAsync(updateDto.IconFile, 48, 48);
            }
        }

        protected override async Task ValidateCreateDto(AmenityCreateDto createDto)
        {
            if (createDto.IconFile == null || createDto.IconFile.Length == 0)
                throw new ArgumentException("Icon image is required");

            ValidateIconFile(createDto.IconFile);
        }

        protected override async Task ValidateUpdateDto(AmenityUpdateDto updateDto, Amenity entity)
        {
            if (updateDto.IconFile != null && updateDto.IconFile.Length > 0)
                ValidateIconFile(updateDto.IconFile);
        }

        protected override async Task BeforeDelete(Amenity entity)
        {
            // Check if amenity is in use
            var isInUse = await _context.ApartmentAmenities.AnyAsync(aa => aa.AmenityID == entity.AmenityID);
            if (isInUse)
                throw new InvalidOperationException("This amenity is in use by one or more apartments and cannot be deleted.");
        }

        #endregion

        #region IAmenityService Implementation

        public async Task<IEnumerable<AmenityDto>> SearchAsync(string query)
        {
            if (string.IsNullOrEmpty(query))
                return await GetAllAsync();

            var amenities = await _dbSet
                .Where(a => a.Name.Contains(query) || a.Category.Contains(query))
                .OrderBy(a => a.Category)
                .ThenBy(a => a.Name)
                .ToListAsync();

            return await MapToDtos(amenities);
        }

        public async Task<IEnumerable<string>> GetCategoriesAsync()
        {
            return await _dbSet
                .Select(a => a.Category)
                .Distinct()
                .Where(c => !string.IsNullOrEmpty(c))
                .OrderBy(c => c)
                .ToListAsync();
        }

        public async Task<IEnumerable<AmenityDto>> GetByCategoryAsync(string category)
        {
            var amenities = await _dbSet
                .Where(a => a.Category == category)
                .OrderBy(a => a.Name)
                .ToListAsync();

            return await MapToDtos(amenities);
        }

        public async Task<IEnumerable<AmenityDto>> GetPopularAmenitiesAsync(int count = 10)
        {
            if (count > 50) count = 50; // Limit maximum

            var amenities = await _dbSet
                .Include(a => a.ApartmentAmenities)
                .OrderByDescending(a => a.ApartmentAmenities.Count)
                .Take(count)
                .ToListAsync();

            return await MapToDtos(amenities);
        }

        public async Task<IEnumerable<AmenityDto>> GetAmenitiesForApartmentAsync(int apartmentId)
        {
            var apartmentExists = await _context.Apartments.AnyAsync(a => a.ApartmentID == apartmentId);
            if (!apartmentExists)
                return null;

            var amenities = await _context.ApartmentAmenities
                .Where(aa => aa.ApartmentID == apartmentId)
                .Include(aa => aa.Amenity)
                .Select(aa => aa.Amenity)
                .OrderBy(a => a.Category)
                .ThenBy(a => a.Name)
                .ToListAsync();

            return await MapToDtos(amenities);
        }

        #endregion

        #region Private Helper Methods

        private void ValidateIconFile(Microsoft.AspNetCore.Http.IFormFile iconFile)
        {
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
            var extension = Path.GetExtension(iconFile.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(extension))
                throw new ArgumentException("Invalid file type. Only image files (jpg, jpeg, png, gif, webp) are allowed.");

            if (iconFile.Length > 1048576) // 1MB
                throw new ArgumentException("File size exceeds the maximum allowed (1MB).");
        }

        #endregion
    }
}