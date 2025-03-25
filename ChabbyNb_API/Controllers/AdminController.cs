using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using System.Security.Claims;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using ChabbyNb_API.Models.DTOs;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp;

namespace ChabbyNb_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "RequireAdminRole")]
    public class AdminController : ControllerBase
    {
        private readonly ChabbyNbDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public AdminController(ChabbyNbDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        // GET: api/Admin/Dashboard
        [HttpGet("Dashboard")]
        public async Task<IActionResult> Dashboard()
        {
            // Get statistics for the dashboard
            var totalApartments = await _context.Apartments.CountAsync(a => a.IsActive);
            var totalBookings = await _context.Bookings.CountAsync();
            var totalUsers = await _context.Users.CountAsync(u => !u.IsAdmin);
            var totalRevenue = await _context.Bookings
                .Where(b => b.BookingStatus == "Completed" || b.BookingStatus == "Confirmed")
                .SumAsync(b => b.TotalPrice);

            // Get recent bookings for the dashboard
            var recentBookings = await _context.Bookings
                .Include(b => b.User)
                .Include(b => b.Apartment)
                .OrderByDescending(b => b.CreatedDate)
                .Take(5)
                .ToListAsync();

            return Ok(new
            {
                totalApartments,
                totalBookings,
                totalUsers,
                totalRevenue,
                recentBookings
            });
        }

        // GET: api/Admin/Apartments
        [HttpGet("Apartments")]
        public async Task<IActionResult> Apartments()
        {
            var apartments = await _context.Apartments
                .Include(a => a.ApartmentImages)
                .OrderByDescending(a => a.CreatedDate)
                .ToListAsync();

            return Ok(apartments);
        }

        // GET: api/Admin/Bookings
        [HttpGet("Bookings")]
        public async Task<IActionResult> Bookings()
        {
            var bookings = await _context.Bookings
                .Include(b => b.User)
                .Include(b => b.Apartment)
                .OrderByDescending(b => b.CreatedDate)
                .ToListAsync();

            return Ok(bookings);
        }

        // GET: api/Admin/Users
        [HttpGet("Users")]
        public async Task<IActionResult> Users()
        {
            var users = await _context.Users
                .Where(u => !u.IsAdmin)
                .OrderByDescending(u => u.CreatedDate)
                .ToListAsync();

            return Ok(users);
        }

        // GET: api/Admin/Amenities
        [HttpGet("Amenities")]
        public async Task<ActionResult<IEnumerable<AmenityDto>>> GetAmenities()
        {
            var amenities = await _context.Amenities
                .OrderBy(a => a.Category)
                .ThenBy(a => a.Name)
                .Select(a => new AmenityDto
                {
                    AmenityID = a.AmenityID,
                    Name = a.Name,
                    IconBase64 = a.Icon != null ? Convert.ToBase64String(a.Icon) : null,
                    IconContentType = a.IconContentType,
                    Category = a.Category,
                    UsageCount = a.ApartmentAmenities.Count
                })
                .ToListAsync();

            return Ok(amenities);
        }

        // GET: api/Admin/Amenities/{id}
        [HttpGet("Amenities/{id}")]
        public async Task<ActionResult<AmenityDto>> GetAmenity(int id)
        {
            var amenity = await _context.Amenities
                .Where(a => a.AmenityID == id)
                .Select(a => new AmenityDto
                {
                    AmenityID = a.AmenityID,
                    Name = a.Name,
                    IconBase64 = a.Icon != null ? Convert.ToBase64String(a.Icon) : null,
                    Category = a.Category,
                    UsageCount = a.ApartmentAmenities.Count
                })
                .FirstOrDefaultAsync();

            if (amenity == null)
            {
                return NotFound();
            }

            return amenity;
        }

        [HttpPost("Amenities")]
        public async Task<ActionResult<AmenityDto>> AddAmenity([FromForm] AmenityCreateDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            byte[] iconData = null;
            string contentType = null;

            if (dto.IconFile != null && dto.IconFile.Length > 0)
            {
                // Validate file extension
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                var extension = Path.GetExtension(dto.IconFile.FileName).ToLowerInvariant();

                if (!allowedExtensions.Contains(extension))
                {
                    return BadRequest(new { error = "Invalid file type. Only image files (jpg, jpeg, png, gif, webp) are allowed." });
                }

                // Validate file size (max 1MB)
                if (dto.IconFile.Length > 1048576) // 1MB
                {
                    return BadRequest(new { error = "File size exceeds the maximum allowed (1MB)." });
                }

                // Get the content type
                contentType = dto.IconFile.ContentType;

                // Read the file into a byte array
                using (var ms = new MemoryStream())
                {
                    await dto.IconFile.CopyToAsync(ms);

                    // Optional: Resize the image to save space
                    // (Would require ImageSharp or another library)

                    iconData = ms.ToArray();
                }
            }
            else
            {
                return BadRequest(new { error = "Icon image is required" });
            }

            var amenity = new Amenity
            {
                Name = dto.Name,
                Icon = iconData,
                IconContentType = contentType,
                Category = dto.Category
            };

            _context.Amenities.Add(amenity);
            await _context.SaveChangesAsync();

            // Convert the binary data to a Base64 string for the response
            string base64Icon = iconData != null ? Convert.ToBase64String(iconData) : null;

            var resultDto = new AmenityDto
            {
                AmenityID = amenity.AmenityID,
                Name = amenity.Name,
                IconBase64 = base64Icon,
                IconContentType = contentType,
                Category = amenity.Category,
                UsageCount = 0
            };

            return CreatedAtAction(nameof(GetAmenity), new { id = amenity.AmenityID }, resultDto);
        }

        // PUT: api/Admin/Amenities/{id}
        [HttpPut("Amenities/{id}")]
        public async Task<ActionResult<AmenityDto>> EditAmenity(int id, [FromForm] AmenityUpdateDto dto)
        {
            if (id != dto.AmenityID)
            {
                return BadRequest("ID mismatch");
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var amenity = await _context.Amenities.FindAsync(id);

            if (amenity == null)
            {
                return NotFound();
            }

            // Process the icon file if a new one was uploaded
            if (dto.IconFile != null && dto.IconFile.Length > 0)
            {
                // Validate file extension
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                var extension = Path.GetExtension(dto.IconFile.FileName).ToLowerInvariant();

                if (!allowedExtensions.Contains(extension))
                {
                    return BadRequest(new { error = "Invalid file type. Only image files (jpg, jpeg, png, gif, webp) are allowed." });
                }

                // Validate file size (max 1MB)
                if (dto.IconFile.Length > 1048576) // 1MB
                {
                    return BadRequest(new { error = "File size exceeds the maximum allowed (1MB)." });
                }

                // Get the content type
                amenity.IconContentType = dto.IconFile.ContentType;

                // Process and save the image as binary data
                amenity.Icon = await ProcessImageAsync(dto.IconFile);
            }

            // Update other amenity properties
            amenity.Name = dto.Name;
            amenity.Category = dto.Category;

            try
            {
                _context.Entry(amenity).State = EntityState.Modified;
                await _context.SaveChangesAsync();

                // Log the action
                await LogAdminAction("Updated amenity: " + amenity.Name);

                // Get usage count for the response
                int usageCount = await _context.ApartmentAmenities
                    .CountAsync(aa => aa.AmenityID == amenity.AmenityID);

                var resultDto = new AmenityDto
                {
                    AmenityID = amenity.AmenityID,
                    Name = amenity.Name,
                    IconBase64 = amenity.Icon != null ? Convert.ToBase64String(amenity.Icon) : null,
                    IconContentType = amenity.IconContentType,
                    Category = amenity.Category,
                    UsageCount = usageCount
                };

                return Ok(resultDto);
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!AmenityExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }
        }

        private async Task<byte[]> ProcessImageAsync(IFormFile imageFile)
        {
            // Create a memory stream to hold the resized image data
            using (var outputStream = new MemoryStream())
            {
                // Load the image using ImageSharp
                using (var inputStream = imageFile.OpenReadStream())
                using (var image = SixLabors.ImageSharp.Image.Load(inputStream))
                {
                    // Define the size we want for our amenity icons (48x48 is a good size for icons)
                    var size = new SixLabors.ImageSharp.Size(48, 48);

                    // Resize the image while preserving aspect ratio
                    image.Mutate(x => x.Resize(new SixLabors.ImageSharp.Processing.ResizeOptions
                    {
                        Size = size,
                        Mode = SixLabors.ImageSharp.Processing.ResizeMode.Max
                    }));

                    // Save the optimized image to our output stream as PNG
                    await image.SaveAsPngAsync(outputStream);
                }

                // Return the binary data
                return outputStream.ToArray();
            }
        }

        // DELETE: api/Admin/Amenities/{id}
        [HttpDelete("Amenities/{id}")]
        public async Task<IActionResult> DeleteAmenity(int id)
        {
            var amenity = await _context.Amenities.FindAsync(id);
            if (amenity == null)
            {
                return NotFound();
            }

            // Check if this amenity is in use
            bool isInUse = await _context.ApartmentAmenities.AnyAsync(aa => aa.AmenityID == id);

            if (isInUse)
            {
                return BadRequest(new { error = "This amenity is in use by one or more apartments and cannot be deleted." });
            }

            // Delete the amenity from the database
            _context.Amenities.Remove(amenity);
            await _context.SaveChangesAsync();

            // Log the action
            await LogAdminAction("Deleted amenity: " + amenity.Name);

            return NoContent();
        }

        private bool AmenityExists(int id)
        {
            return _context.Amenities.Any(e => e.AmenityID == id);
        }

        // POST: api/Admin/SendMessage
        [HttpPost("SendMessage/{userId}")]
        public async Task<IActionResult> SendMessage(int userId, [FromBody] AdminMessageDto message)
        {
            if (string.IsNullOrEmpty(message.Message))
            {
                return BadRequest("Message cannot be empty.");
            }

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return NotFound("User not found");
            }

            // In a real app, send the message through email or notification system
            // For now, just log it
            await LogAdminAction($"Sent message to {user.Email} with subject: {message.Subject}");

            return Ok(new { success = true, message = "Message sent successfully" });
        }

        // Helper to log admin actions
        private async Task LogAdminAction(string action)
        {
            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var adminEmail = User.Identity.Name;

            // Create an AdminLog model and add it to your context
            var log = new AdminLog
            {
                AdminID = int.Parse(adminId),
                AdminEmail = adminEmail,
                Action = action,
                Timestamp = DateTime.Now,
                IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            };

            _context.AdminLogs.Add(log);
            await _context.SaveChangesAsync();
        }

    }
}