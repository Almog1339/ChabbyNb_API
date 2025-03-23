using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ChabbyNb.Models;

namespace ChabbyNb_API.Models
{
    public class Review
    {
        public int ReviewID { get; set; }

        public int BookingID { get; set; }

        public int UserID { get; set; }

        public int ApartmentID { get; set; }

        [Range(1, 5)]
        public int Rating { get; set; }

        public string Comment { get; set; }

        public DateTime CreatedDate { get; set; }

        [ForeignKey("BookingID")]
        public virtual Booking Booking { get; set; }

        [ForeignKey("UserID")]
        public virtual User User { get; set; }

        [ForeignKey("ApartmentID")]
        public virtual Apartment Apartment { get; set; }
    }
}