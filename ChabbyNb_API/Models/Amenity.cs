using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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

        // Store the binary data of the image
        public byte[] Icon { get; set; }

        // Store the content type (e.g., "image/png")
        [StringLength(50)]
        public string IconContentType { get; set; }

        [StringLength(50)]
        public string Category { get; set; }

        public virtual ICollection<ApartmentAmenity> ApartmentAmenities { get; set; }
    }
}