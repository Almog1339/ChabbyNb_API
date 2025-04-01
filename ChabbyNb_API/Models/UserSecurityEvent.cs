using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ChabbyNb_API.Models
{
    public class UserSecurityEvent
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        public string EventType { get; set; } // Login, Logout, FailedLogin, TokenRefresh, etc.

        [Required]
        public DateTime EventTime { get; set; }

        [Required]
        public string IpAddress { get; set; }

        public string TokenId { get; set; } // Optional, for token-related events

        public string AdditionalInfo { get; set; } // Any additional information about the event

        public string UserAgent { get; set; } // Browser/device information

        [ForeignKey("UserId")]
        public virtual User User { get; set; }
    }
}