using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace ChabbyNb_API.Models.DTOs
{
    public class ApartmentCreateDto
    {
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

        // Primary image (marked as primary automatically)
        public IFormFile PrimaryImage { get; set; }
        public string PrimaryImageCaption { get; set; }

        // Additional images
        public List<IFormFile> AdditionalImages { get; set; }

        // Captions for additional images (index should match the AdditionalImages list)
        public List<string> AdditionalImageCaptions { get; set; }

        public int[] AmenityIds { get; set; }
    }

    public class ApartmentUpdateDto
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

        // New image to add
        public IFormFile NewImage { get; set; }
        public string ImageCaption { get; set; }
        public bool SetAsPrimary { get; set; }

        // Additional images
        public List<IFormFile> AdditionalImages { get; set; }

        // Captions for additional images (index should match the AdditionalImages list)
        public List<string> AdditionalImageCaptions { get; set; }

        public int[] AmenityIds { get; set; }
    }

    public class ApartmentMapDto
    {
        public int ApartmentID { get; set; }
        public string Title { get; set; }
        public decimal PricePerNight { get; set; }
        public decimal Latitude { get; set; }
        public decimal Longitude { get; set; }
        public string PrimaryImageUrl { get; set; }
    }

    // DTO for apartment images
    public class ApartmentImageDto
    {
        public int ImageID { get; set; }
        public int ApartmentID { get; set; }
        public string ImageUrl { get; set; }
        public bool IsPrimary { get; set; }
        public string Caption { get; set; }
        public int SortOrder { get; set; }
    }

    // DTO for image reordering
    public class ImageReorderDto
    {
        public int ImageID { get; set; }
        public int SortOrder { get; set; }
    }

    // DTO for batch updating image captions
    public class ImageCaptionUpdateDto
    {
        public int ImageID { get; set; }
        public string Caption { get; set; }
    }

    // DTO for uploading multiple images
    public class ApartmentImagesUploadDto
    {
        // List of images to upload
        public List<IFormFile> Images { get; set; }

        // Optional captions for each image (should match the Images list in order)
        public List<string> Captions { get; set; }

        // Optional: index of the image to set as primary (if any)
        public int? SetPrimaryImageIndex { get; set; }
    }
}