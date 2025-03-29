using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ChabbyNb_API.Models
{
    public class Payment
    {
        [Key]
        public int PaymentID { get; set; }

        public int BookingID { get; set; }

        [Required]
        [StringLength(100)]
        public string PaymentIntentID { get; set; }

        [Required]
        [Column(TypeName = "decimal(10, 2)")]
        public decimal Amount { get; set; }

        [Required]
        [StringLength(20)]
        public string Currency { get; set; } // e.g., "USD", "EUR"

        [Required]
        [StringLength(20)]
        public string Status { get; set; } // "succeeded", "pending", "failed", "refunded", etc.

        [StringLength(255)]
        public string PaymentMethod { get; set; } // "card", "bank_transfer", etc.

        [StringLength(30)]
        public string LastFour { get; set; } // Last 4 digits of card

        [StringLength(50)]
        public string CardBrand { get; set; } // "visa", "mastercard", etc.

        public DateTime CreatedDate { get; set; }

        public DateTime? CompletedDate { get; set; }

        [ForeignKey("BookingID")]
        public virtual Booking Booking { get; set; }
    }

    public class Refund
    {
        [Key]
        public int RefundID { get; set; }

        public int PaymentID { get; set; }

        [Required]
        [StringLength(100)]
        public string RefundIntentID { get; set; }

        [Required]
        [Column(TypeName = "decimal(10, 2)")]
        public decimal Amount { get; set; }

        [Required]
        [StringLength(20)]
        public string Status { get; set; } // "succeeded", "pending", "failed"

        [StringLength(255)]
        public string Reason { get; set; }

        public int AdminID { get; set; } // The admin who processed the refund

        public DateTime CreatedDate { get; set; }

        public DateTime? CompletedDate { get; set; }

        [ForeignKey("PaymentID")]
        public virtual Payment Payment { get; set; }

        [ForeignKey("AdminID")]
        public virtual User Admin { get; set; }
    }
}