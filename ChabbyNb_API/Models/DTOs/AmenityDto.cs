using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ChabbyNb_API.Models.DTOs
{
    public class AmenityDto
    {
        public int AmenityID { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; }

        // Base64-encoded image for display in frontend
        public string IconBase64 { get; set; }

        // Content type for the image
        public string IconContentType { get; set; }

        [StringLength(50)]
        public string Category { get; set; }

        // Count of apartments using this amenity
        public int UsageCount { get; set; }
    }

    public class AmenityCreateDto
    {
        [Required]
        [StringLength(100)]
        public string Name { get; set; }

        // The uploaded icon image file
        [Required(ErrorMessage = "Please upload an icon image")]
        public IFormFile IconFile { get; set; }

        [StringLength(50)]
        public string Category { get; set; }
    }

    public class AmenityUpdateDto
    {
        public int AmenityID { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; }

        // The uploaded icon image file (optional for updates)
        public IFormFile IconFile { get; set; }

        [StringLength(50)]
        public string Category { get; set; }
    }
}