using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ChabbyNb.Models;

namespace ChabbyNb_API.Models
{
    public class ApartmentAmenity
    {
        public int ApartmentAmenityID { get; set; }

        public int ApartmentID { get; set; }

        public int AmenityID { get; set; }

        [ForeignKey("ApartmentID")]
        public virtual Apartment Apartment { get; set; }

        [ForeignKey("AmenityID")]
        public virtual Amenity Amenity { get; set; }
    }
}