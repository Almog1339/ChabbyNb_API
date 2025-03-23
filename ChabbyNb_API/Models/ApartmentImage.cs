using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ChabbyNb.Models;

namespace ChabbyNb_API.Models
{
    public class ApartmentImage
    {
        [Key]
        public int ImageID { get; set; }

        public int ApartmentID { get; set; }

        [Required]
        [StringLength(255)]
        public string ImageUrl { get; set; }

        public bool IsPrimary { get; set; }

        [StringLength(200)]
        public string Caption { get; set; }

        public int SortOrder { get; set; }

        [ForeignKey("ApartmentID")]
        public virtual Apartment Apartment { get; set; }
    }
}