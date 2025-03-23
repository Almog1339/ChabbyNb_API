using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using ChabbyNb_API.Models;

namespace ChabbyNb_API.Models.DTOs
{
    // DTO for member dashboard
    public class DashboardDto
    {
        public User User { get; set; }
        public ICollection<Booking> UpcomingBookings { get; set; }
        public ICollection<Booking> RecentBookings { get; set; }
    }

    // DTO for reviews
    public class ReviewDto
    {
        public int ReviewID { get; set; }
        public int BookingID { get; set; }
        public int ApartmentID { get; set; }
        public string ApartmentTitle { get; set; }

        [Range(1, 5, ErrorMessage = "Please select a rating between 1 and 5")]
        public int Rating { get; set; }

        [Required(ErrorMessage = "Please provide a comment about your stay")]
        public string Comment { get; set; }

        public DateTime CheckInDate { get; set; }
        public DateTime CheckOutDate { get; set; }
    }

    // DTO for pet profiles
    public class PetDto
    {
        public int PetID { get; set; }

        [Required(ErrorMessage = "Pet name is required")]
        [StringLength(50, ErrorMessage = "Pet name cannot be longer than 50 characters")]
        public string Name { get; set; }

        [Required(ErrorMessage = "Pet type is required")]
        [StringLength(30, ErrorMessage = "Pet type cannot be longer than 30 characters")]
        public string Type { get; set; } // Dog, Cat, etc.

        [StringLength(50, ErrorMessage = "Breed cannot be longer than 50 characters")]
        public string Breed { get; set; }

        [Range(0, 30, ErrorMessage = "Please enter a valid age between 0 and 30")]
        public int? Age { get; set; }

        [Range(0.1, 100, ErrorMessage = "Please enter a valid weight between 0.1 and 100 kg")]
        public decimal? Weight { get; set; }

        [StringLength(500, ErrorMessage = "Special needs cannot be longer than 500 characters")]
        public string SpecialNeeds { get; set; }

        [StringLength(1000, ErrorMessage = "Additional information cannot be longer than 1000 characters")]
        public string AdditionalInfo { get; set; }

        public IFormFile PetImage { get; set; }

        public string ImageUrl { get; set; }
    }

    // DTO for booking information
    public class BookingDto
    {
        public int BookingID { get; set; }

        [Required]
        public int ApartmentID { get; set; }

        public string ApartmentTitle { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime CheckInDate { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime CheckOutDate { get; set; }

        [Required]
        [Range(1, 20, ErrorMessage = "Please enter a valid number of guests between 1 and 20")]
        public int GuestCount { get; set; }

        [Range(0, 5, ErrorMessage = "Please enter a valid number of pets between 0 and 5")]
        public int PetCount { get; set; }

        public decimal TotalPrice { get; set; }

        public string BookingStatus { get; set; }

        public string PaymentStatus { get; set; }

        [StringLength(500, ErrorMessage = "Special requests cannot be longer than 500 characters")]
        public string SpecialRequests { get; set; }

        // Additional information for presentation
        public string ImageUrl { get; set; }
        public int MaxOccupancy { get; set; }
        public bool PetFriendly { get; set; }
        public decimal PricePerNight { get; set; }
        public string Address { get; set; }
        public string Neighborhood { get; set; }

        // Calculated property
        public int NightsCount => (CheckOutDate - CheckInDate).Days;
    }
}