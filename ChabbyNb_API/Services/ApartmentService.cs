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
using ChabbyNb.Models;

namespace ChabbyNb_API.Services
{
    public interface IApartmentService :
        IEntityService<Apartment, ApartmentDto, ApartmentCreateDto, ApartmentUpdateDto>,
        ISearchableService<ApartmentDto>
    {
        Task<IEnumerable<ApartmentDto>> GetFeaturedApartmentsAsync(int count = 3);
        Task<IEnumerable<ApartmentDto>> SearchApartmentsAsync(
            string query = "", int? minPrice = null, int? maxPrice = null,
            int? bedrooms = null, bool? petFriendly = null);
        Task<ServiceResult> UpdateApartmentStatusAsync(int id, bool isActive);
        Task<ServiceResult> DeleteApartmentImageAsync(int apartmentId, int imageId);
        Task<IEnumerable<ApartmentImageDto>> GetApartmentImagesAsync(int apartmentId);
        Task<ApartmentImageDto> GetPrimaryImageAsync(int apartmentId);
        Task<IEnumerable<ApartmentMapDto>> GetApartmentsForMapAsync();
    }

    public class ApartmentService : BaseEntityService<Apartment, ApartmentDto, ApartmentCreateDto, ApartmentUpdateDto>,
        IApartmentService
    {
        private readonly IFileStorageService _fileStorage;

        public ApartmentService(
            ChabbyNbDbContext context,
            IMapper mapper,
            ILogger<ApartmentService> logger,
            IFileStorageService fileStorage)
            : base(context, mapper, logger)
        {
            _fileStorage = fileStorage ?? throw new ArgumentNullException(nameof(fileStorage));
        }

        #region Base Service Implementation

        protected override async Task<Apartment> GetEntityByIdAsync(int id)
        {
            return await _dbSet
                .Include(a => a.ApartmentImages)
                .Include(a => a.ApartmentAmenities)
                    .ThenInclude(aa => aa.Amenity)
                .Include(a => a.Reviews)
                    .ThenInclude(r => r.User)
                .FirstOrDefaultAsync(a => a.ApartmentID == id);
        }

        protected override IQueryable<Apartment> GetBaseQuery()
        {
            return _dbSet
                .Where(a => a.IsActive)
                .Include(a => a.ApartmentImages)
                .Include(a => a.ApartmentAmenities)
                    .ThenInclude(aa => aa.Amenity);
        }

        protected override async Task<ApartmentDto> MapToDto(Apartment entity)
        {
            return new ApartmentDto
            {
                ApartmentID = entity.ApartmentID,
                Title = entity.Title,
                Description = entity.Description,
                Address = entity.Address,
                Neighborhood = entity.Neighborhood,
                PricePerNight = entity.PricePerNight,
                Bedrooms = entity.Bedrooms,
                Bathrooms = entity.Bathrooms,
                MaxOccupancy = entity.MaxOccupancy,
                SquareMeters = entity.SquareMeters,
                PetFriendly = entity.PetFriendly,
                PetFee = entity.PetFee,
                Latitude = entity.Latitude,
                Longitude = entity.Longitude,
                IsActive = entity.IsActive,
                PrimaryImageUrl = entity.GetPrimaryImageUrl(),
                AverageRating = entity.GetAverageRating(),
                ReviewCount = entity.GetReviewCount(),
                Images = entity.ApartmentImages.Select(i => new ApartmentImageDto
                {
                    ImageID = i.ImageID,
                    ApartmentID = i.ApartmentID,
                    ImageUrl = i.ImageUrl,
                    IsPrimary = i.IsPrimary,
                    Caption = i.Caption,
                    SortOrder = i.SortOrder
                }).ToList(),
                Amenities = entity.ApartmentAmenities.Select(aa => new AmenityDto
                {
                    AmenityID = aa.Amenity.AmenityID,
                    Name = aa.Amenity.Name,
                    IconBase64 = aa.Amenity.Icon != null ? Convert.ToBase64String(aa.Amenity.Icon) : null,
                    Category = aa.Amenity.Category
                }).ToList()
            };
        }

        protected override async Task<IEnumerable<ApartmentDto>> MapToDtos(IEnumerable<Apartment> entities)
        {
            var tasks = entities.Select(MapToDto);
            return await Task.WhenAll(tasks);
        }

        protected override async Task<Apartment> MapFromCreateDto(ApartmentCreateDto createDto)
        {
            var apartment = new Apartment
            {
                Title = createDto.Title,
                Description = createDto.Description,
                Address = createDto.Address,
                Neighborhood = createDto.Neighborhood,
                PricePerNight = createDto.PricePerNight,
                Bedrooms = createDto.Bedrooms,
                Bathrooms = createDto.Bathrooms,
                MaxOccupancy = createDto.MaxOccupancy,
                SquareMeters = createDto.SquareMeters,
                PetFriendly = createDto.PetFriendly,
                PetFee = createDto.PetFee,
                Latitude = createDto.Latitude,
                Longitude = createDto.Longitude,
                CreatedDate = DateTime.Now,
                IsActive = true
            };

            return apartment;
        }

        protected override async Task MapFromUpdateDto(ApartmentUpdateDto updateDto, Apartment entity)
        {
            entity.Title = updateDto.Title;
            entity.Description = updateDto.Description;
            entity.Address = updateDto.Address;
            entity.Neighborhood = updateDto.Neighborhood;
            entity.PricePerNight = updateDto.PricePerNight;
            entity.Bedrooms = updateDto.Bedrooms;
            entity.Bathrooms = updateDto.Bathrooms;
            entity.MaxOccupancy = updateDto.MaxOccupancy;
            entity.SquareMeters = updateDto.SquareMeters;
            entity.PetFriendly = updateDto.PetFriendly;
            entity.PetFee = updateDto.PetFee;
            entity.Latitude = updateDto.Latitude;
            entity.Longitude = updateDto.Longitude;
            entity.IsActive = updateDto.IsActive;
        }

        protected override async Task AfterCreate(Apartment entity, ApartmentCreateDto createDto)
        {
            // Add amenities
            if (createDto.AmenityIds != null && createDto.AmenityIds.Length > 0)
            {
                await AddAmenitiesToApartment(entity.ApartmentID, createDto.AmenityIds);
            }

            // Process images
            await ProcessApartmentImages(entity, createDto);
        }

        protected override async Task AfterUpdate(Apartment entity, ApartmentUpdateDto updateDto)
        {
            // Update amenities
            await UpdateApartmentAmenities(entity.ApartmentID, updateDto.AmenityIds);

            // Process new images if any
            await ProcessApartmentImages(entity, updateDto);
        }

        #endregion

        #region IApartmentService Implementation

        public async Task<IEnumerable<ApartmentDto>> GetFeaturedApartmentsAsync(int count = 3)
        {
            var apartments = await _dbSet
                .Where(a => a.IsActive)
                .Include(a => a.Reviews)
                .Include(a => a.ApartmentImages)
                .Include(a => a.ApartmentAmenities)
                    .ThenInclude(aa => aa.Amenity)
                .OrderByDescending(a => a.Reviews.Any() ? a.Reviews.Average(r => r.Rating) : 0)
                .Take(count)
                .ToListAsync();

            return await MapToDtos(apartments);
        }

        public async Task<IEnumerable<ApartmentDto>> SearchAsync(string query)
        {
            return await SearchApartmentsAsync(query);
        }

        public async Task<IEnumerable<ApartmentDto>> SearchApartmentsAsync(
            string query = "", int? minPrice = null, int? maxPrice = null,
            int? bedrooms = null, bool? petFriendly = null)
        {
            var apartments = _dbSet.Where(a => a.IsActive);

            if (!string.IsNullOrEmpty(query))
            {
                apartments = apartments.Where(a =>
                    a.Title.Contains(query) ||
                    a.Description.Contains(query) ||
                    a.Address.Contains(query) ||
                    a.Neighborhood.Contains(query));
            }

            if (minPrice.HasValue)
                apartments = apartments.Where(a => a.PricePerNight >= minPrice.Value);

            if (maxPrice.HasValue)
                apartments = apartments.Where(a => a.PricePerNight <= maxPrice.Value);

            if (bedrooms.HasValue)
                apartments = apartments.Where(a => a.Bedrooms >= bedrooms.Value);

            if (petFriendly.HasValue)
                apartments = apartments.Where(a => a.PetFriendly == petFriendly.Value);

            var results = await apartments
                .Include(a => a.ApartmentImages)
                .Include(a => a.ApartmentAmenities)
                    .ThenInclude(aa => aa.Amenity)
                .ToListAsync();

            return await MapToDtos(results);
        }

        public async Task<ServiceResult> UpdateApartmentStatusAsync(int id, bool isActive)
        {
            var apartment = await GetEntityByIdAsync(id);
            if (apartment == null)
                return ServiceResult.ErrorResult("Apartment not found");

            apartment.IsActive = isActive;
            await _context.SaveChangesAsync();

            return ServiceResult.SuccessResult($"Apartment status updated to {(isActive ? "active" : "inactive")}");
        }

        public async Task<ServiceResult> DeleteApartmentImageAsync(int apartmentId, int imageId)
        {
            var image = await _context.ApartmentImages
                .FirstOrDefaultAsync(i => i.ImageID == imageId && i.ApartmentID == apartmentId);

            if (image == null)
                return ServiceResult.ErrorResult("Image not found");

            try
            {
                // Delete from storage
                await _fileStorage.DeleteFileAsync(image.ImageUrl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Failed to delete image file: {image.ImageUrl}");
            }

            _context.ApartmentImages.Remove(image);
            await _context.SaveChangesAsync();

            return ServiceResult.SuccessResult("Image deleted successfully");
        }

        public async Task<IEnumerable<ApartmentImageDto>> GetApartmentImagesAsync(int apartmentId)
        {
            var apartment = await GetEntityByIdAsync(apartmentId);
            if (apartment == null)
                return null;

            return apartment.ApartmentImages
                .OrderBy(i => i.SortOrder)
                .Select(i => new ApartmentImageDto
                {
                    ImageID = i.ImageID,
                    ApartmentID = i.ApartmentID,
                    ImageUrl = i.ImageUrl,
                    IsPrimary = i.IsPrimary,
                    Caption = i.Caption,
                    SortOrder = i.SortOrder
                });
        }

        public async Task<ApartmentImageDto> GetPrimaryImageAsync(int apartmentId)
        {
            var apartment = await GetEntityByIdAsync(apartmentId);
            if (apartment == null)
                return null;

            var primaryImage = apartment.ApartmentImages.FirstOrDefault(i => i.IsPrimary) ??
                              apartment.ApartmentImages.OrderBy(i => i.SortOrder).FirstOrDefault();

            if (primaryImage == null)
                return null;

            return new ApartmentImageDto
            {
                ImageID = primaryImage.ImageID,
                ApartmentID = primaryImage.ApartmentID,
                ImageUrl = primaryImage.ImageUrl,
                IsPrimary = primaryImage.IsPrimary,
                Caption = primaryImage.Caption,
                SortOrder = primaryImage.SortOrder
            };
        }

        public async Task<IEnumerable<ApartmentMapDto>> GetApartmentsForMapAsync()
        {
            return await _dbSet
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
                        .Where(i => i.IsPrimary)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault() ??
                        a.ApartmentImages
                        .OrderBy(i => i.SortOrder)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault()
                })
                .ToListAsync();
        }

        #endregion

        #region Private Helper Methods

        private async Task AddAmenitiesToApartment(int apartmentId, int[] amenityIds)
        {
            foreach (var amenityId in amenityIds)
            {
                var apartmentAmenity = new ApartmentAmenity
                {
                    ApartmentID = apartmentId,
                    AmenityID = amenityId
                };
                _context.ApartmentAmenities.Add(apartmentAmenity);
            }
            await _context.SaveChangesAsync();
        }

        private async Task UpdateApartmentAmenities(int apartmentId, int[] amenityIds)
        {
            // Remove existing amenities
            var existingAmenities = _context.ApartmentAmenities
                .Where(aa => aa.ApartmentID == apartmentId);
            _context.ApartmentAmenities.RemoveRange(existingAmenities);

            // Add new amenities
            if (amenityIds != null && amenityIds.Length > 0)
            {
                await AddAmenitiesToApartment(apartmentId, amenityIds);
            }
        }

        private async Task ProcessApartmentImages(Apartment apartment, ApartmentCreateDto createDto)
        {
            int sortOrder = 0;

            // Process primary image
            if (createDto.PrimaryImage != null && createDto.PrimaryImage.Length > 0)
            {
                string imageUrl = await _fileStorage.SaveFileAsync(createDto.PrimaryImage, "images/apartments");

                var primaryImage = new ApartmentImage
                {
                    ApartmentID = apartment.ApartmentID,
                    ImageUrl = imageUrl,
                    IsPrimary = true,
                    SortOrder = sortOrder++,
                    Caption = createDto.PrimaryImageCaption
                };
                _context.ApartmentImages.Add(primaryImage);
            }

            // Process additional images
            if (createDto.AdditionalImages != null && createDto.AdditionalImages.Count > 0)
            {
                for (int i = 0; i < createDto.AdditionalImages.Count; i++)
                {
                    var image = createDto.AdditionalImages[i];
                    if (image == null || image.Length == 0) continue;

                    string imageUrl = await _fileStorage.SaveFileAsync(image, "images/apartments");
                    string caption = null;

                    if (createDto.AdditionalImageCaptions != null &&
                        i < createDto.AdditionalImageCaptions.Count)
                    {
                        caption = createDto.AdditionalImageCaptions[i];
                    }

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

            await _context.SaveChangesAsync();
        }

        private async Task ProcessApartmentImages(Apartment apartment, ApartmentUpdateDto updateDto)
        {
            // Process new image if provided
            if (updateDto.NewImage != null && updateDto.NewImage.Length > 0)
            {
                string imageUrl = await _fileStorage.SaveFileAsync(updateDto.NewImage, "images/apartments");

                // If setting as primary, update existing primary images
                if (updateDto.SetAsPrimary)
                {
                    var existingPrimary = apartment.ApartmentImages.Where(i => i.IsPrimary);
                    foreach (var img in existingPrimary)
                    {
                        img.IsPrimary = false;
                    }
                }

                var maxSortOrder = apartment.ApartmentImages.Any() ?
                    apartment.ApartmentImages.Max(i => i.SortOrder) : 0;

                var newImage = new ApartmentImage
                {
                    ApartmentID = apartment.ApartmentID,
                    ImageUrl = imageUrl,
                    IsPrimary = updateDto.SetAsPrimary,
                    SortOrder = maxSortOrder + 1,
                    Caption = updateDto.ImageCaption
                };
                _context.ApartmentImages.Add(newImage);
                await _context.SaveChangesAsync();
            }

            // Process additional images similar to create
            if (updateDto.AdditionalImages != null && updateDto.AdditionalImages.Count > 0)
            {
                var maxSortOrder = apartment.ApartmentImages.Any() ?
                    apartment.ApartmentImages.Max(i => i.SortOrder) : 0;

                for (int i = 0; i < updateDto.AdditionalImages.Count; i++)
                {
                    var image = updateDto.AdditionalImages[i];
                    if (image == null || image.Length == 0) continue;

                    string imageUrl = await _fileStorage.SaveFileAsync(image, "images/apartments");
                    string caption = null;

                    if (updateDto.AdditionalImageCaptions != null &&
                        i < updateDto.AdditionalImageCaptions.Count)
                    {
                        caption = updateDto.AdditionalImageCaptions[i];
                    }

                    var apartmentImage = new ApartmentImage
                    {
                        ApartmentID = apartment.ApartmentID,
                        ImageUrl = imageUrl,
                        IsPrimary = false,
                        SortOrder = ++maxSortOrder,
                        Caption = caption
                    };
                    _context.ApartmentImages.Add(apartmentImage);
                }
                await _context.SaveChangesAsync();
            }
        }

        #endregion
    }

    // Add missing DTO for apartment list view
    public class ApartmentDto
    {
        public int ApartmentID { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Address { get; set; }
        public string Neighborhood { get; set; }
        public decimal PricePerNight { get; set; }
        public int Bedrooms { get; set; }
        public int Bathrooms { get; set; }
        public int MaxOccupancy { get; set; }
        public int? SquareMeters { get; set; }
        public bool PetFriendly { get; set; }
        public decimal? PetFee { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public bool IsActive { get; set; }
        public string PrimaryImageUrl { get; set; }
        public double AverageRating { get; set; }
        public int ReviewCount { get; set; }
        public List<ApartmentImageDto> Images { get; set; } = new List<ApartmentImageDto>();
        public List<AmenityDto> Amenities { get; set; } = new List<AmenityDto>();
    }
}

