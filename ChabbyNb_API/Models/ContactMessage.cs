using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace ChabbyNb_API.Models
{
    public class ContactMessage
    {
        public int ContactMessageID { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; }

        [Required]
        [EmailAddress]
        [StringLength(100)]
        public string Email { get; set; }

        [Required]
        [StringLength(100)]
        public string Subject { get; set; }

        [Required]
        public string Message { get; set; }

        // Optional foreign key to link to registered users
        public int? UserID { get; set; }

        [ForeignKey("UserID")]
        public virtual User User { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public bool IsRead { get; set; } = false;

        public DateTime? ReadDate { get; set; }

        public string Status { get; set; } = "New"; // New, Read, Responded, Archived

        public string AdminNotes { get; set; }
    }
}