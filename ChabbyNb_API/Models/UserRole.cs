using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ChabbyNb_API.Models
{
    public class UserRole
    {
        [Key]
        public int UserRoleID { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        public int Role { get; set; } // Maps to UserRole enum value

        [Required]
        public DateTime AssignedDate { get; set; }

        [ForeignKey("UserId")]
        public virtual User User { get; set; }
    }
}