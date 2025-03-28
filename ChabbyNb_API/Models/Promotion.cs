using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ChabbyNb_API.Models
{
    public class Promotion
    {
        [Key]
        public int PromotionID { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; }

        [Required]
        [StringLength(20)]
        public string Code { get; set; }

        [StringLength(500)]
        public string Description { get; set; }

        [Required]
        public string DiscountType { get; set; } // "Percentage" or "FixedAmount"

        [Required]
        [Column(TypeName = "decimal(10, 2)")]
        public decimal DiscountValue { get; set; } // Percentage or fixed amount

        public decimal? MinimumStayNights { get; set; }

        public decimal? MinimumBookingAmount { get; set; }

        [Column(TypeName = "decimal(10, 2)")]
        public decimal? MaximumDiscountAmount { get; set; } // For percentage discounts

        [DataType(DataType.Date)]
        public DateTime? StartDate { get; set; }

        [DataType(DataType.Date)]
        public DateTime? EndDate { get; set; }

        public int? UsageLimit { get; set; } // Maximum number of times the code can be used

        public int UsageCount { get; set; } = 0; // Number of times the code has been used

        public bool IsActive { get; set; } = true;

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        // Nullable ApartmentID for apartment-specific promotions
        public int? ApartmentID { get; set; }

        [ForeignKey("ApartmentID")]
        public virtual ChabbyNb.Models.Apartment Apartment { get; set; }

        // Navigation property for bookings that used this promotion
        public virtual ICollection<Booking> Bookings { get; set; } = new HashSet<Booking>();
    }
}