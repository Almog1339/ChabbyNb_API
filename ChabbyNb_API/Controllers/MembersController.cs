using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.IO;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using ChabbyNb_API.Models.DTOs;

namespace ChabbyNb_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class MembersController : BaseApiController
    {
        private readonly IWebHostEnvironment _webHostEnvironment;

        public MembersController(
            ChabbyNbDbContext context,
            IWebHostEnvironment webHostEnvironment,
            ILogger<MembersController> logger)
            : base(context, (ILogger<AccountController>)logger)
        {
            _webHostEnvironment = webHostEnvironment;
        }

        // GET: api/Members/Dashboard
        [HttpGet("Dashboard")]
        public async Task<IActionResult> Dashboard()
        {
            // Get current user ID using the base controller method
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
            {
                return Unauthorized(CreateResponse(false, "User not authenticated"));
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserID == userId.Value);
            if (user == null)
            {
                return NotFound(CreateResponse(false, "User not found"));
            }

            // Get user's upcoming bookings
            var upcomingBookings = await _context.Bookings
                .Include(b => b.Apartment)
                    .ThenInclude(a => a.ApartmentImages)
                .Where(b =>
                    b.UserID == user.UserID &&
                    b.CheckInDate >= DateTime.Today &&
                    (b.BookingStatus == "Confirmed" || b.BookingStatus == "Pending"))
                .OrderBy(b => b.CheckInDate)
                .Take(3)
                .ToListAsync();

            // Get user's recent bookings
            var recentBookings = await _context.Bookings
                .Include(b => b.Apartment)
                    .ThenInclude(a => a.ApartmentImages)
                .Where(b =>
                    b.UserID == user.UserID &&
                    b.CheckOutDate < DateTime.Today &&
                    b.BookingStatus == "Completed")
                .OrderByDescending(b => b.CheckOutDate)
                .Take(3)
                .ToListAsync();

            // Create dashboard view model
            var dashboardData = new DashboardDto
            {
                User = user,
                UpcomingBookings = upcomingBookings,
                RecentBookings = recentBookings
            };

            return Ok(dashboardData);
        }

        // GET: api/Members/Bookings
        [HttpGet("Bookings")]
        public async Task<IActionResult> Bookings()
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
            {
                return Unauthorized(CreateResponse(false, "User not authenticated"));
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserID == userId.Value);
            if (user == null)
            {
                return NotFound(CreateResponse(false, "User not found"));
            }

            var bookings = await _context.Bookings
                .Include(b => b.Apartment)
                    .ThenInclude(a => a.ApartmentImages)
                .Where(b => b.UserID == user.UserID)
                .OrderByDescending(b => b.CreatedDate)
                .ToListAsync();

            return Ok(bookings);
        }

        // GET: api/Members/Bookings/{id}
        [HttpGet("Bookings/{id}")]
        public async Task<IActionResult> BookingDetails(int id)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
            {
                return Unauthorized(CreateResponse(false, "User not authenticated"));
            }

            var booking = await _context.Bookings
                .Include(b => b.Apartment)
                    .ThenInclude(a => a.ApartmentImages)
                .Include(b => b.Reviews)
                .FirstOrDefaultAsync(b => b.BookingID == id && b.UserID == userId.Value);

            if (booking == null)
            {
                return NotFound(CreateResponse(false, "Booking not found"));
            }

            return Ok(booking);
        }

        // GET: api/Members/Bookings/{id}/CancelInfo
        [HttpGet("Bookings/{id}/CancelInfo")]
        public async Task<IActionResult> CancelBookingInfo(int id)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
            {
                return Unauthorized(CreateResponse(false, "User not authenticated"));
            }

            var booking = await _context.Bookings
                .Include(b => b.Apartment)
                .FirstOrDefaultAsync(b =>
                    b.BookingID == id &&
                    b.UserID == userId.Value &&
                    b.BookingStatus != "Canceled" &&
                    b.BookingStatus != "Completed" &&
                    b.CheckInDate > DateTime.Today);

            if (booking == null)
            {
                return NotFound(CreateResponse(false, "Booking not found or cannot be canceled"));
            }

            return Ok(booking);
        }

        // POST: api/Members/Bookings/{id}/Cancel
        [HttpPost("Bookings/{id}/Cancel")]
        public async Task<IActionResult> CancelBooking(int id)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
            {
                return Unauthorized(CreateResponse(false, "User not authenticated"));
            }

            var booking = await _context.Bookings
                .FirstOrDefaultAsync(b =>
                    b.BookingID == id &&
                    b.UserID == userId.Value &&
                    b.BookingStatus != "Canceled" &&
                    b.BookingStatus != "Completed" &&
                    b.CheckInDate > DateTime.Today);

            if (booking == null)
            {
                return NotFound(CreateResponse(false, "Booking not found or cannot be canceled"));
            }

            // Update booking status
            booking.BookingStatus = "Canceled";

            // For demo purposes, assume full refund
            booking.PaymentStatus = "Refunded";

            await _context.SaveChangesAsync();

            return Ok(CreateResponse(true, "Booking canceled successfully", booking));
        }

        // GET: api/Members/Reviews
        [HttpGet("Reviews")]
        public async Task<IActionResult> Reviews()
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
            {
                return Unauthorized(CreateResponse(false, "User not authenticated"));
            }

            var reviews = await _context.Reviews
                .Include(r => r.Apartment)
                .Include(r => r.Booking)
                .Where(r => r.UserID == userId.Value)
                .OrderByDescending(r => r.CreatedDate)
                .ToListAsync();

            return Ok(reviews);
        }

        // GET: api/Members/Bookings/{id}/AddReview
        [HttpGet("Bookings/{id}/AddReview")]
        public async Task<IActionResult> AddReviewInfo(int id)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
            {
                return Unauthorized(CreateResponse(false, "User not authenticated"));
            }

            var booking = await _context.Bookings
                .Include(b => b.Apartment)
                .FirstOrDefaultAsync(b =>
                    b.BookingID == id &&
                    b.UserID == userId.Value &&
                    b.BookingStatus == "Completed" &&
                    b.CheckOutDate < DateTime.Today);

            if (booking == null)
            {
                return NotFound(CreateResponse(false, "Booking not found or not eligible for review"));
            }

            // Check if review already exists
            var existingReview = await _context.Reviews
                .FirstOrDefaultAsync(r => r.BookingID == booking.BookingID);

            if (existingReview != null)
            {
                return BadRequest(CreateResponse(false, "Review already exists for this booking"));
            }

            var reviewInfo = new ReviewDto
            {
                BookingID = booking.BookingID,
                ApartmentID = booking.ApartmentID,
                ApartmentTitle = booking.Apartment.Title,
                CheckInDate = booking.CheckInDate,
                CheckOutDate = booking.CheckOutDate
            };

            return Ok(reviewInfo);
        }

        // POST: api/Members/Reviews
        [HttpPost("Reviews")]
        public async Task<IActionResult> AddReview([FromBody] ReviewDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = GetCurrentUserId();
            if (!userId.HasValue)
            {
                return Unauthorized(CreateResponse(false, "User not authenticated"));
            }

            // Check if booking belongs to user
            var booking = await _context.Bookings
                .FirstOrDefaultAsync(b =>
                    b.BookingID == model.BookingID &&
                    b.UserID == userId.Value);

            if (booking == null)
            {
                return NotFound(CreateResponse(false, "Booking not found"));
            }

            // Check if review already exists
            var existingReview = await _context.Reviews
                .FirstOrDefaultAsync(r => r.BookingID == model.BookingID);

            if (existingReview != null)
            {
                return BadRequest(CreateResponse(false, "Review already exists for this booking"));
            }

            // Create new review
            var review = new Review
            {
                BookingID = model.BookingID,
                UserID = userId.Value,
                ApartmentID = model.ApartmentID,
                Rating = model.Rating,
                Comment = model.Comment,
                CreatedDate = DateTime.Now
            };

            _context.Reviews.Add(review);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(Reviews), new { id = review.ReviewID }, review);
        }

        // GET: api/Members/Reviews/{id}
        [HttpGet("Reviews/{id}")]
        public async Task<IActionResult> GetReview(int id)
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
            {
                return Unauthorized(CreateResponse(false, "User not authenticated"));
            }

            var review = await _context.Reviews
                .Include(r => r.Booking)
                .Include(r => r.Apartment)
                .FirstOrDefaultAsync(r => r.ReviewID == id && r.UserID == userId.Value);

            if (review == null)
            {
                return NotFound(CreateResponse(false, "Review not found"));
            }

            var reviewDto = new ReviewDto
            {
                ReviewID = review.ReviewID,
                BookingID = review.BookingID,
                ApartmentID = review.ApartmentID,
                ApartmentTitle = review.Apartment.Title,
                Rating = review.Rating,
                Comment = review.Comment,
                CheckInDate = review.Booking.CheckInDate,
                CheckOutDate = review.Booking.CheckOutDate
            };

            return Ok(reviewDto);
        }

        // PUT: api/Members/Reviews/{id}
        [HttpPut("Reviews/{id}")]
        public async Task<IActionResult> EditReview(int id, [FromBody] ReviewDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = GetCurrentUserId();
            if (!userId.HasValue)
            {
                return Unauthorized(CreateResponse(false, "User not authenticated"));
            }

            var review = await _context.Reviews
                .FirstOrDefaultAsync(r => r.ReviewID == id && r.UserID == userId.Value);

            if (review == null)
            {
                return NotFound(CreateResponse(false, "Review not found"));
            }

            // Update review
            review.Rating = model.Rating;
            review.Comment = model.Comment;

            await _context.SaveChangesAsync();

            return Ok(review);
        }

        // POST: api/Members/Pets
        [HttpPost("Pets")]
        public async Task<IActionResult> AddPet([FromForm] PetDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var userId = GetCurrentUserId();
            if (!userId.HasValue)
            {
                return Unauthorized(CreateResponse(false, "User not authenticated"));
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserID == userId.Value);
            if (user == null)
            {
                return NotFound(CreateResponse(false, "User not found"));
            }

            try
            {
                // Handle pet image upload
                string imageUrl = null;
                if (model.PetImage != null && model.PetImage.Length > 0)
                {
                    // Save the file
                    string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "images", "pets");
                    string uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(model.PetImage.FileName);
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    // Ensure directory exists
                    Directory.CreateDirectory(uploadsFolder);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await model.PetImage.CopyToAsync(fileStream);
                    }

                    imageUrl = "/images/pets/" + uniqueFileName;
                }

                // In a real application, we would store this pet information
                // For now, just return success message
                return Ok(CreateResponse(true, "Pet profile added successfully!", new
                {
                    name = model.Name,
                    type = model.Type,
                    breed = model.Breed,
                    imageUrl = imageUrl
                }));
            }
            catch (Exception ex)
            {
                return HandleException(ex, "Error adding pet profile");
            }
        }
    }
}