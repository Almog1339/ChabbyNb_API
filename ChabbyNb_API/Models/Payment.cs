using ChabbyNb_API.Models;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

public class Payment
{
    [Key]
    public int PaymentID { get; set; }

    public int BookingID { get; set; }

    [StringLength(50)]
    public string? PaymentIntentID { get; set; } // Nullable nvarchar(50)

    [Required]
    [Column(TypeName = "decimal(10, 2)")]
    public decimal Amount { get; set; }

    [Required]
    [StringLength(20)]
    public string Currency { get; set; } // nvarchar(20), not nullable

    [StringLength(50)]
    public string? Status { get; set; } // Nullable nvarchar(50)

    [StringLength(255)]
    public string? PaymentMethod { get; set; } // Nullable nvarchar(255)

    [StringLength(50)]
    public string? LastFour { get; set; } // Nullable nvarchar(50)

    [StringLength(50)]
    public string? CardBrand { get; set; } // Nullable nvarchar(50)

    [Required]
    public DateTime CreatedDate { get; set; } // datetime2(7), not nullable

    public DateTime? CompletedDate { get; set; } // Nullable datetime2(7)

    [ForeignKey("BookingID")]
    public virtual Booking Booking { get; set; }

    // Navigation property for refunds
    public virtual ICollection<Refund> Refunds { get; set; } = new HashSet<Refund>();
}

public class Refund
{
    [Key]
    public int RefundID { get; set; }

    [Required]
    public int PaymentID { get; set; }

    [Required]
    [StringLength(100)]
    public string RefundIntentID { get; set; } // nvarchar(100), not nullable

    [Required]
    [Column(TypeName = "decimal(10, 2)")]
    public decimal Amount { get; set; }

    [Required]
    [StringLength(20)]
    public string Status { get; set; } // nvarchar(20), not nullable

    [Required]
    [StringLength(255)]
    public string Reason { get; set; } // nvarchar(255), not nullable

    [Required]
    public int AdminID { get; set; } // int, not nullable

    [Required]
    public DateTime CreatedDate { get; set; } // datetime2(7), not nullable

    public DateTime? CompletedDate { get; set; } // Nullable datetime2(7)

    [ForeignKey("PaymentID")]
    public virtual Payment Payment { get; set; }

    [ForeignKey("AdminID")]
    public virtual User Admin { get; set; }
}