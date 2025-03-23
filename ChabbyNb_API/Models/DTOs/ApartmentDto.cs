using System;
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
        public IFormFile PrimaryImage { get; set; }
        public string ImageCaption { get; set; }
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
        public IFormFile NewImage { get; set; }
        public string ImageCaption { get; set; }
        public bool SetAsPrimary { get; set; }
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
}