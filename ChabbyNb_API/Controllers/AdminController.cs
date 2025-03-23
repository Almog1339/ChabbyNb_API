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
        public async Task<IActionResult> Amenities()
        {
            var amenities = await _context.Amenities
                .OrderBy(a => a.Category)
                .ThenBy(a => a.Name)
                .ToListAsync();

            return Ok(amenities);
        }

        // POST: api/Admin/Amenities
        [HttpPost("Amenities")]
        public async Task<IActionResult> AddAmenity([FromBody] Amenity amenity)
        {
            if (ModelState.IsValid)
            {
                _context.Amenities.Add(amenity);
                await _context.SaveChangesAsync();

                // Log the action
                await LogAdminAction("Added a new amenity: " + amenity.Name);

                return CreatedAtAction(nameof(Amenities), new { id = amenity.AmenityID }, amenity);
            }
            return BadRequest(ModelState);
        }

        // GET: api/Admin/Amenities/{id}
        [HttpGet("Amenities/{id}")]
        public async Task<IActionResult> GetAmenity(int id)
        {
            var amenity = await _context.Amenities.FindAsync(id);
            if (amenity == null)
            {
                return NotFound();
            }
            return Ok(amenity);
        }

        // PUT: api/Admin/Amenities/{id}
        [HttpPut("Amenities/{id}")]
        public async Task<IActionResult> EditAmenity(int id, [FromBody] Amenity amenity)
        {
            if (id != amenity.AmenityID)
            {
                return BadRequest("ID mismatch");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(amenity);
                    await _context.SaveChangesAsync();

                    // Log the action
                    await LogAdminAction("Updated amenity: " + amenity.Name);

                    return Ok(amenity);
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!AmenityExists(amenity.AmenityID))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
            }
            return BadRequest(ModelState);
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

            _context.Amenities.Remove(amenity);
            await _context.SaveChangesAsync();

            // Log the action
            await LogAdminAction("Deleted amenity: " + amenity.Name);

            return NoContent();
        }

        // PATCH: api/Admin/VerifyApartment/{id}
        [HttpPatch("VerifyApartment/{id}")]
        public async Task<IActionResult> VerifyApartment(int id)
        {
            var apartment = await _context.Apartments.FindAsync(id);
            if (apartment == null)
            {
                return NotFound();
            }

            apartment.IsActive = true;
            await _context.SaveChangesAsync();

            // Log the action
            await LogAdminAction("Verified apartment: " + apartment.Title);

            return Ok(apartment);
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

        private bool AmenityExists(int id)
        {
            return _context.Amenities.Any(e => e.AmenityID == id);
        }
    }
}