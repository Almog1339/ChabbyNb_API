using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ChabbyNb_API.Models
{
    public class RefreshToken
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Token { get; set; }

        [Required]
        public string JwtId { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        public DateTime IssuedAt { get; set; }

        [Required]
        public DateTime ExpiresAt { get; set; }

        [Required]
        public bool IsRevoked { get; set; }

        public DateTime? RevokedAt { get; set; }

        public string RevokedByIp { get; set; }

        public string ReplacedByToken { get; set; }

        public string ReasonRevoked { get; set; }

        [Required]
        public string CreatedByIp { get; set; }

        [ForeignKey("UserId")]
        public virtual User User { get; set; }
    }
}