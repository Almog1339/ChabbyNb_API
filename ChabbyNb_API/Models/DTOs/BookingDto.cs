using System;
using System.ComponentModel.DataAnnotations;

namespace ChabbyNb_API.Models.DTOs
{
    public class BookingCreateDto
    {
        [Required]
        public int ApartmentID { get; set; }

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

        [StringLength(500, ErrorMessage = "Special requests cannot be longer than 500 characters")]
        public string SpecialRequests { get; set; }
    }

    public class BookingResponseDto
    {
        public int BookingID { get; set; }
        public int ApartmentID { get; set; }
        public string ApartmentTitle { get; set; }
        public string PrimaryImageUrl { get; set; }
        public DateTime CheckInDate { get; set; }
        public DateTime CheckOutDate { get; set; }
        public int GuestCount { get; set; }
        public int PetCount { get; set; }
        public decimal TotalPrice { get; set; }
        public string BookingStatus { get; set; }
        public string PaymentStatus { get; set; }
        public string ReservationNumber { get; set; }
        public DateTime CreatedDate { get; set; }
        public string Address { get; set; }
        public string Neighborhood { get; set; }
        public decimal PricePerNight { get; set; }
        public int NightsCount => (CheckOutDate - CheckInDate).Days;
        public bool HasReview { get; set; }
    }

    public class BookingDetailsDto
    {
        public int BookingID { get; set; }
        public int ApartmentID { get; set; }
        public string ApartmentTitle { get; set; }
        public string PrimaryImageUrl { get; set; }
        public DateTime CheckInDate { get; set; }
        public DateTime CheckOutDate { get; set; }
        public int GuestCount { get; set; }
        public int PetCount { get; set; }
        public decimal TotalPrice { get; set; }
        public string BookingStatus { get; set; }
        public string PaymentStatus { get; set; }
        public string ReservationNumber { get; set; }
        public DateTime CreatedDate { get; set; }
        public string SpecialRequests { get; set; }
        public string Address { get; set; }
        public string Neighborhood { get; set; }
        public decimal PricePerNight { get; set; }
        public int Bedrooms { get; set; }
        public int Bathrooms { get; set; }
        public int MaxOccupancy { get; set; }
        public bool PetFriendly { get; set; }
        public int NightsCount => (CheckOutDate - CheckInDate).Days;
        public bool HasReview { get; set; }
        public ReviewSummaryDto Review { get; set; }
    }

    public class BookingCancellationDto
    {
        public int BookingID { get; set; }
        public DateTime CheckInDate { get; set; }
        public DateTime CheckOutDate { get; set; }
        public decimal TotalPrice { get; set; }
        public decimal RefundAmount { get; set; }
        public string CancellationPolicy { get; set; }
    }

    public class ReviewSummaryDto
    {
        public int ReviewID { get; set; }
        public int Rating { get; set; }
        public string Comment { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class BookingStatisticsDto
    {
        public int TotalBookings { get; set; }
        public int CompletedBookings { get; set; }
        public int UpcomingBookings { get; set; }
        public int CancelledBookings { get; set; }
        public decimal TotalSpent { get; set; }
        public int NightsStayed { get; set; }
        public double AverageRating { get; set; }
    }
}