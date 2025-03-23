using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ChabbyNb.Models;

namespace ChabbyNb_API.Models
{
    public class Booking
    {
        public Booking()
        {
            Reviews = new HashSet<Review>();
        }

        public int BookingID { get; set; }

        public int UserID { get; set; }

        public int ApartmentID { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime CheckInDate { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime CheckOutDate { get; set; }

        public int GuestCount { get; set; }

        public int PetCount { get; set; }

        [Column(TypeName = "decimal(10, 2)")]
        public decimal TotalPrice { get; set; }

        [Required]
        [StringLength(20)]
        public string BookingStatus { get; set; }

        [Required]
        [StringLength(20)]
        public string PaymentStatus { get; set; }

        public string SpecialRequests { get; set; }

        [Required]
        [StringLength(20)]
        public string ReservationNumber { get; set; }

        public DateTime CreatedDate { get; set; }

        [ForeignKey("UserID")]
        public virtual User User { get; set; }

        [ForeignKey("ApartmentID")]
        public virtual Apartment Apartment { get; set; }

        public virtual ICollection<Review> Reviews { get; set; }
    }
}