using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ChabbyNb_API.Models
{
    // ViewModel for member dashboard
    public class DashboardViewModel
    {
        public User User { get; set; }
        public ICollection<Booking> UpcomingBookings { get; set; }
        public ICollection<Booking> RecentBookings { get; set; }
    }

    // ViewModel for reviews
    public class ReviewViewModel
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
}