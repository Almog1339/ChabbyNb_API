using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ChabbyNb_API.Models
{
    public class Amenity
    {
        public Amenity()
        {
            ApartmentAmenities = new HashSet<ApartmentAmenity>();
        }

        public int AmenityID { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; }

        [StringLength(50)]
        public string Icon { get; set; }

        [StringLength(50)]
        public string Category { get; set; }

        public virtual ICollection<ApartmentAmenity> ApartmentAmenities { get; set; }
    }
}