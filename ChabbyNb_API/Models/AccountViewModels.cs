using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using System.Linq;

namespace ChabbyNb_API.Models
{

    // ViewModel for adding a pet profile in the member zone
    public class PetViewModel
    {
        public int PetID { get; set; }

        [Required(ErrorMessage = "Pet name is required")]
        [Display(Name = "Pet Name")]
        [StringLength(50, ErrorMessage = "Pet name cannot be longer than 50 characters")]
        public string Name { get; set; }

        [Required(ErrorMessage = "Pet type is required")]
        [Display(Name = "Pet Type")]
        public string Type { get; set; } // Dog, Cat, etc.

        [Display(Name = "Breed")]
        public string Breed { get; set; }

        [Display(Name = "Age")]
        [Range(0, 30, ErrorMessage = "Please enter a valid age between 0 and 30")]
        public int? Age { get; set; }

        [Display(Name = "Weight (kg)")]
        [Range(0.1, 100, ErrorMessage = "Please enter a valid weight between 0.1 and 100 kg")]
        public decimal? Weight { get; set; }

        [Display(Name = "Special Needs")]
        public string SpecialNeeds { get; set; }

        [Display(Name = "Additional Information")]
        public string AdditionalInfo { get; set; }

        public IFormFile PetImage { get; set; }

        public string ImageUrl { get; set; }

        public int UserID { get; set; }
    }

    // ViewModel for booking information
    public class BookingViewModel
    {
        public int BookingID { get; set; }

        [Required]
        [Display(Name = "Apartment")]
        public int ApartmentID { get; set; }

        [Display(Name = "Apartment")]
        public string ApartmentTitle { get; set; }

        [Required]
        [Display(Name = "Check-in Date")]
        [DataType(DataType.Date)]
        public DateTime CheckInDate { get; set; }

        [Required]
        [Display(Name = "Check-out Date")]
        [DataType(DataType.Date)]
        public DateTime CheckOutDate { get; set; }

        [Required]
        [Display(Name = "Number of Guests")]
        [Range(1, 20, ErrorMessage = "Please enter a valid number of guests between 1 and 20")]
        public int GuestCount { get; set; }

        [Display(Name = "Number of Pets")]
        [Range(0, 5, ErrorMessage = "Please enter a valid number of pets between 0 and 5")]
        public int PetCount { get; set; }

        [Display(Name = "Total Price")]
        [DataType(DataType.Currency)]
        public decimal TotalPrice { get; set; }

        [Display(Name = "Booking Status")]
        public string BookingStatus { get; set; }

        [Display(Name = "Payment Status")]
        public string PaymentStatus { get; set; }

        [Display(Name = "Special Requests")]
        public string SpecialRequests { get; set; }

        [Display(Name = "Booking Date")]
        [DataType(DataType.DateTime)]
        public DateTime CreatedDate { get; set; }

        // Navigation properties
        public string ImageUrl { get; set; }
        public int MaxOccupancy { get; set; }
        public bool PetFriendly { get; set; }
        public decimal PricePerNight { get; set; }
        public string Address { get; set; }
        public string Neighborhood { get; set; }

        // Calculated properties
        public int NightsCount
        {
            get
            {
                return (CheckOutDate - CheckInDate).Days;
            }
        }
    }
}