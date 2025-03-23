using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using ChabbyNb_API.Models.DTOs;

namespace ChabbyNb_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class BookingsController : ControllerBase
    {
        private readonly ChabbyNbDbContext _context;

        public BookingsController(ChabbyNbDbContext context)
        {
            _context = context;
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

            // Calculate total price
            decimal totalPrice = CalculateTotalPrice(
                apartment.PricePerNight,
                bookingDto.CheckInDate,
                bookingDto.CheckOutDate,
                bookingDto.PetCount,
                apartment.PetFee);

            // Create booking
            var booking = new Booking
            {
                UserID = userId,
                ApartmentID = bookingDto.ApartmentID,
                CheckInDate = bookingDto.CheckInDate,
                CheckOutDate = bookingDto.CheckOutDate,
                GuestCount = bookingDto.GuestCount,
                PetCount = bookingDto.PetCount,
                TotalPrice = totalPrice,
                BookingStatus = "Pending",
                PaymentStatus = "Pending",
                SpecialRequests = bookingDto.SpecialRequests,
                ReservationNumber = GenerateReservationNumber(),
                CreatedDate = DateTime.Now
            };

            _context.Bookings.Add(booking);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetBooking), new { id = booking.BookingID }, booking);
        }

        // PATCH: api/Bookings/5/Cancel
        [HttpPatch("{id}/Cancel")]
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

        // Helper method to calculate total price
        private decimal CalculateTotalPrice(decimal pricePerNight, DateTime checkIn, DateTime checkOut, int petCount, decimal? petFee)
        {
            int nights = (int)(checkOut - checkIn).TotalDays;
            decimal basePrice = pricePerNight * nights;
            decimal petCost = petCount > 0 && petFee.HasValue ? petFee.Value * petCount : 0;

            return basePrice + petCost;
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