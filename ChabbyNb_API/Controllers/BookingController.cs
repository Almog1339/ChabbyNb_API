using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using ChabbyNb_API.Data;  // The namespace containing your DbContext
using ChabbyNb_API.Models;  // The namespace containing your model classes
using ChabbyNb_API.Models.DTOs;  // The namespace containing your DTOs
using ChabbyNb_API.Services;  // The namespace containing your services


namespace ChabbyNb_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class BookingsController : ControllerBase
    {
        private readonly ChabbyNbDbContext _context;
        private readonly PriceCalculationService _priceService;

        public BookingsController(ChabbyNbDbContext context)
        {
            _context = context;
            _priceService = new PriceCalculationService(context);
        }

        // GET: api/Bookings
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Booking>>> GetUserBookings()
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var bookings = await _context.Bookings
                .Include(b => b.Apartment)
                    .ThenInclude(a => a.ApartmentImages)
                .Where(b => b.UserID == userId)
                .OrderByDescending(b => b.CreatedDate)
                .ToListAsync();

            return bookings;
        }

        // GET: api/Bookings/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Booking>> GetBooking(int id)
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var booking = await _context.Bookings
                .Include(b => b.Apartment)
                    .ThenInclude(a => a.ApartmentImages)
                .Include(b => b.Reviews)
                .Include(b => b.Promotion)
                .FirstOrDefaultAsync(b => b.BookingID == id && b.UserID == userId);

            if (booking == null)
            {
                return NotFound();
            }

            return booking;
        }

        // GET: api/Bookings/Upcoming
        [HttpGet("Upcoming")]
        public async Task<ActionResult<IEnumerable<Booking>>> GetUpcomingBookings()
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var bookings = await _context.Bookings
                .Include(b => b.Apartment)
                    .ThenInclude(a => a.ApartmentImages)
                .Where(b =>
                    b.UserID == userId &&
                    b.CheckInDate >= DateTime.Today &&
                    (b.BookingStatus == "Confirmed" || b.BookingStatus == "Pending"))
                .OrderBy(b => b.CheckInDate)
                .ToListAsync();

            return bookings;
        }

        // GET: api/Bookings/Past
        [HttpGet("Past")]
        public async Task<ActionResult<IEnumerable<Booking>>> GetPastBookings()
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var bookings = await _context.Bookings
                .Include(b => b.Apartment)
                    .ThenInclude(a => a.ApartmentImages)
                .Where(b =>
                    b.UserID == userId &&
                    b.CheckOutDate < DateTime.Today)
                .OrderByDescending(b => b.CheckOutDate)
                .ToListAsync();

            return bookings;
        }

        // POST: api/Bookings
        [HttpPost]
        public async Task<ActionResult<Booking>> CreateBooking([FromBody] BookingCreateDto bookingDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            // Check if apartment exists
            var apartment = await _context.Apartments
                .FirstOrDefaultAsync(a => a.ApartmentID == bookingDto.ApartmentID && a.IsActive);

            if (apartment == null)
            {
                return NotFound(new { error = "Apartment not found or not available" });
            }

            // Check if dates are valid
            if (bookingDto.CheckInDate < DateTime.Today)
            {
                return BadRequest(new { error = "Check-in date cannot be in the past" });
            }

            if (bookingDto.CheckOutDate <= bookingDto.CheckInDate)
            {
                return BadRequest(new { error = "Check-out date must be after check-in date" });
            }

            // Check if apartment is available for these dates
            bool isAvailable = await IsApartmentAvailable(
                bookingDto.ApartmentID,
                bookingDto.CheckInDate,
                bookingDto.CheckOutDate);

            if (!isAvailable)
            {
                return BadRequest(new { error = "Apartment is not available for the selected dates" });
            }

            // Check if guest count is valid
            if (bookingDto.GuestCount > apartment.MaxOccupancy)
            {
                return BadRequest(new { error = $"Maximum occupancy for this apartment is {apartment.MaxOccupancy} guests" });
            }

            // Check pet policy
            if (bookingDto.PetCount > 0 && !apartment.PetFriendly)
            {
                return BadRequest(new { error = "This apartment does not allow pets" });
            }

            // Calculate total price using our pricing service
            try
            {
                var priceResult = await _priceService.CalculateBookingPriceAsync(
                    bookingDto.ApartmentID,
                    bookingDto.CheckInDate,
                    bookingDto.CheckOutDate,
                    bookingDto.PetCount,
                    bookingDto.PromotionCode);

                // Create booking
                var booking = new Booking
                {
                    UserID = userId,
                    ApartmentID = bookingDto.ApartmentID,
                    CheckInDate = bookingDto.CheckInDate,
                    CheckOutDate = bookingDto.CheckOutDate,
                    GuestCount = bookingDto.GuestCount,
                    PetCount = bookingDto.PetCount,
                    BasePrice = priceResult.BasePrice,
                    DiscountAmount = priceResult.DiscountAmount,
                    TotalPrice = priceResult.TotalPrice,
                    PromotionID = priceResult.PromotionId,
                    PromotionCode = priceResult.PromotionCode ?? String.Empty,
                    BookingStatus = "Pending",
                    PaymentStatus = "Pending",
                    SpecialRequests = bookingDto.SpecialRequests,
                    ReservationNumber = GenerateReservationNumber(),
                    CreatedDate = DateTime.Now,
                    
                };

                _context.Bookings.Add(booking);
                await _context.SaveChangesAsync();

                // Increment usage count for the promotion
                if (priceResult.PromotionId.HasValue)
                {
                    var promotion = await _context.Promotions.FindAsync(priceResult.PromotionId.Value);
                    if (promotion != null)
                    {
                        promotion.UsageCount++;
                        await _context.SaveChangesAsync();
                    }
                }

                return CreatedAtAction(nameof(GetBooking), new { id = booking.BookingID }, booking);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = $"Error calculating booking price: {ex.Message}" });
            }
        }

        // GET: api/Bookings/CalculatePrice
        [HttpGet("CalculatePrice")]
        public async Task<ActionResult<BookingPriceResult>> CalculatePrice(
            [FromQuery] int apartmentId,
            [FromQuery] DateTime checkInDate,
            [FromQuery] DateTime checkOutDate,
            [FromQuery] int petCount = 0,
            [FromQuery] string promotionCode = null)
        {
            try
            {
                // Validate apartment exists
                var apartment = await _context.Apartments.FindAsync(apartmentId);
                if (apartment == null || !apartment.IsActive)
                {
                    return NotFound("Apartment not found or not available");
                }

                // Validate dates
                if (checkInDate < DateTime.Today)
                {
                    return BadRequest("Check-in date cannot be in the past");
                }

                if (checkOutDate <= checkInDate)
                {
                    return BadRequest("Check-out date must be after check-in date");
                }

                // Check if apartment is available for these dates
                bool isAvailable = await IsApartmentAvailable(apartmentId, checkInDate, checkOutDate);
                if (!isAvailable)
                {
                    return BadRequest("Apartment is not available for the selected dates");
                }

                // Check pet policy
                if (petCount > 0 && !apartment.PetFriendly)
                {
                    return BadRequest("This apartment does not allow pets");
                }

                // Calculate price
                var priceResult = await _priceService.CalculateBookingPriceAsync(
                    apartmentId, checkInDate, checkOutDate, petCount, promotionCode);

                return Ok(priceResult);
            }
            catch (Exception ex)
            {
                return BadRequest($"Error calculating price: {ex.Message}");
            }
        }

        // PATCH: api/Bookings/5/Cancel
        [HttpPost("{id}/Cancel")]
        public async Task<IActionResult> CancelBooking(int id)
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var booking = await _context.Bookings
                .FirstOrDefaultAsync(b =>
                    b.BookingID == id &&
                    b.UserID == userId &&
                    b.BookingStatus != "Canceled" &&
                    b.BookingStatus != "Completed" &&
                    b.CheckInDate > DateTime.Today);

            if (booking == null)
            {
                return NotFound();
            }

            // Update booking status
            booking.BookingStatus = "Canceled";

            // For demo purposes, assume full refund if cancellation is at least 3 days before check-in
            TimeSpan daysUntilCheckIn = booking.CheckInDate - DateTime.Today;
            if (daysUntilCheckIn.TotalDays >= 3)
            {
                booking.PaymentStatus = "Refunded";
            }
            else
            {
                booking.PaymentStatus = "Partially Refunded";
            }

            await _context.SaveChangesAsync();

            return Ok(booking);
        }

        // Helper method to check if apartment is available for specific dates
        private async Task<bool> IsApartmentAvailable(int apartmentId, DateTime checkIn, DateTime checkOut)
        {
            var overlappingBookings = await _context.Bookings
                .Where(b =>
                    b.ApartmentID == apartmentId &&
                    b.BookingStatus != "Canceled" &&
                    (
                        // Check-in date falls within an existing booking
                        (checkIn >= b.CheckInDate && checkIn < b.CheckOutDate) ||
                        // Check-out date falls within an existing booking
                        (checkOut > b.CheckInDate && checkOut <= b.CheckOutDate) ||
                        // New booking completely covers an existing booking
                        (checkIn <= b.CheckInDate && checkOut >= b.CheckOutDate)
                    ))
                .AnyAsync();

            return !overlappingBookings;
        }

        // Helper method to generate a unique reservation number
        private string GenerateReservationNumber()
        {
            // Format: CB-YYYYMMDD-XXXX (CB = ChabbyNb, YYYYMMDD = current date, XXXX = 4 random digits)
            string dateString = DateTime.Now.ToString("yyyyMMdd");
            string randomDigits = new Random().Next(1000, 9999).ToString();

            return $"CB-{dateString}-{randomDigits}";
        }
    }
}