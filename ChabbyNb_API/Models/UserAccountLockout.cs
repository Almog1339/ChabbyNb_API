using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ChabbyNb_API.Models
{
    public class UserAccountLockout
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        public DateTime LockoutStart { get; set; }

        public DateTime? LockoutEnd { get; set; }

        [Required]
        public string Reason { get; set; } // E.g., "Too many failed login attempts"

        public string IpAddress { get; set; }

        public int FailedAttempts { get; set; }

        public bool IsActive { get; set; } // Whether the lockout is currently active

        public DateTime? UnlockedAt { get; set; }

        public string UnlockedByAdminId { get; set; } // If unlocked by an admin

        public string Notes { get; set; }

        [ForeignKey("UserId")]
        public virtual User User { get; set; }
    }
}