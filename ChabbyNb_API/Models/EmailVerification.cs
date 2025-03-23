using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ChabbyNb_API.Models
{
    public class EmailVerification
    {
        [Key]
        public int VerificationID { get; set; }
        
        [Required]
        public int UserID { get; set; }
        
        [Required]
        [StringLength(100)]
        public string Email { get; set; }
        
        [Required]
        [StringLength(128)]
        public string VerificationToken { get; set; }
        
        [Required]
        public DateTime ExpiryDate { get; set; }
        
        public DateTime? VerifiedDate { get; set; }
        
        [Required]
        public bool IsVerified { get; set; }
        
        [Required]
        public DateTime CreatedDate { get; set; }
        
        [ForeignKey("UserID")]
        public virtual User User { get; set; }
    }
}
