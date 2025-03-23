using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ChabbyNb_API.Models
{
    public class User
    {
        public User()
        {
            Bookings = new HashSet<Booking>();
            Reviews = new HashSet<Review>();
        }

        public int UserID { get; set; }

        [Required]
        [StringLength(50)]
        public string Username { get; set; }

        [Required]
        [StringLength(100)]
        [EmailAddress]
        public string Email { get; set; }

        [Required]
        [StringLength(128)]
        public string PasswordHash { get; set; }

        [StringLength(50)]
        public string FirstName { get; set; }

        [StringLength(50)]
        public string LastName { get; set; }

        [StringLength(20)]
        public string PhoneNumber { get; set; }

        public bool IsAdmin { get; set; }

        public DateTime CreatedDate { get; set; }

        public bool IsEmailVerified { get; set; }

        public virtual ICollection<Booking> Bookings { get; set; }
        public virtual ICollection<Review> Reviews { get; set; }
    }
}