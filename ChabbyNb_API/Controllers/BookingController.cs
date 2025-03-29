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
using ChabbyNb_API.Services;
using Stripe;  // The namespace containing your services


namespace ChabbyNb_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class BookingsController : ControllerBase
    {
        private readonly ChabbyNbDbContext _context;
        private readonly PriceCalculationService _priceService;
        private readonly IPaymentService _paymentService;
        private readonly IConfiguration _configuration;
        private readonly IEmailService _emailService;
        private readonly ILogger<BookingsController> _logger;

        public BookingsController(ChabbyNbDbContext context, IPaymentService paymentService, IConfiguration configuration, IEmailService emailService, ILogger<BookingsController> logger)
        {
            _context = context;
            _priceService = new PriceCalculationService(context);
            _paymentService = paymentService;
            _configuration = configuration;
            _emailService = emailService;
            _logger = logger;
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
                    (b.BookingStatus == "Confirmed" || b.BookingStatus == "Pending") || b.BookingStatus == "Canceled")
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
        public async Task<ActionResult<BookingResponseDto>> CreateBooking([FromBody] BookingCreateDto bookingDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            // Check if apartment exists
            var apartment = await _context.Apartments
                .Include(a => a.ApartmentImages)
                .FirstOrDefaultAsync(a => a.ApartmentID == bookingDto.ApartmentID && a.IsActive);

            if (apartment == null)
            {
                return NotFound(new { error = "Apartment not found or not available" });
            }

            // Validate booking details
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

            try
            {
                // Calculate total price using our pricing service
                var priceResult = await _priceService.CalculateBookingPriceAsync(
                    bookingDto.ApartmentID,
                    bookingDto.CheckInDate,
                    bookingDto.CheckOutDate,
                    bookingDto.PetCount,
                    bookingDto.PromotionCode);

                // Create booking record
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
                    PromotionCode = priceResult.PromotionCode ?? string.Empty,
                    BookingStatus = "Pending", // Initial status is Pending until payment is confirmed
                    PaymentStatus = "Pending",
                    SpecialRequests = bookingDto.SpecialRequests,
                    ReservationNumber = GenerateReservationNumber(),
                    CreatedDate = DateTime.Now
                };

                _context.Bookings.Add(booking);
                await _context.SaveChangesAsync();

                // Increment usage count for the promotion if applicable
                if (priceResult.PromotionId.HasValue)
                {
                    var promotion = await _context.Promotions.FindAsync(priceResult.PromotionId.Value);
                    if (promotion != null)
                    {
                        promotion.UsageCount++;
                        await _context.SaveChangesAsync();
                    }
                }

                // Load user data for the payment intent
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return BadRequest(new { error = "User not found" });
                }

                booking.User = user;
                booking.Apartment = apartment;

                // Create a payment intent using the payment service
                string clientSecret = await _paymentService.CreatePaymentIntent(booking);

                // Find the payment record that was created
                var payment = await _context.Payments
                    .FirstOrDefaultAsync(p => p.BookingID == booking.BookingID);

                if (payment == null)
                {
                    return BadRequest(new { error = "Failed to create payment record" });
                }

                // Get primary image URL for the apartment
                string primaryImageUrl = apartment.ApartmentImages
                    .Where(ai => ai.IsPrimary)
                    .Select(ai => ai.ImageUrl)
                    .FirstOrDefault() ??
                    apartment.ApartmentImages
                    .Select(ai => ai.ImageUrl)
                    .FirstOrDefault();

                // Create response DTO
                var response = new BookingResponseDto
                {
                    BookingID = booking.BookingID,
                    ApartmentID = booking.ApartmentID,
                    ApartmentTitle = apartment.Title,
                    PrimaryImageUrl = primaryImageUrl,
                    CheckInDate = booking.CheckInDate,
                    CheckOutDate = booking.CheckOutDate,
                    GuestCount = booking.GuestCount,
                    PetCount = booking.PetCount,
                    BasePrice = booking.BasePrice,
                    DiscountAmount = booking.DiscountAmount ?? 0,
                    TotalPrice = booking.TotalPrice,
                    PromotionCode = booking.PromotionCode ?? string.Empty,
                    BookingStatus = booking.BookingStatus,
                    PaymentStatus = booking.PaymentStatus,
                    ReservationNumber = booking.ReservationNumber,
                    CreatedDate = booking.CreatedDate,
                    Address = apartment.Address,
                    Neighborhood = apartment.Neighborhood,
                    PricePerNight = apartment.PricePerNight,
                    HasReview = false,
                    PaymentIntentClientSecret = clientSecret, // Include client secret for frontend payment processing
                    PaymentIntentId = payment.PaymentIntentID // Also include the payment intent ID
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = $"Error creating booking: {ex.Message}" });
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

        [HttpPost("{id}/ConfirmPayment")]
        public async Task<ActionResult<BookingResponseDto>> ConfirmPayment(int id, [FromBody] PaymentConfirmationDto confirmationDto)
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var booking = await _context.Bookings
                .Include(b => b.Apartment)
                    .ThenInclude(a => a.ApartmentImages)
                .Include(b => b.User)
                .FirstOrDefaultAsync(b => b.BookingID == id && b.UserID == userId);

            if (booking == null)
            {
                return NotFound(new { error = "Booking not found" });
            }

            if (booking.BookingStatus != "Pending" || booking.PaymentStatus != "Pending")
            {
                return BadRequest(new { error = "Booking is not in a pending state" });
            }

            try
            {
                // Find the payment record in the database
                var payment = await _context.Payments
                    .FirstOrDefaultAsync(p => p.BookingID == booking.BookingID);

                // If no payment record exists, create a new payment intent
                if (payment == null)
                {
                    // Create a new payment intent
                    string clientSecret = await _paymentService.CreatePaymentIntent(booking);

                    // Get the newly created payment record
                    payment = await _context.Payments
                        .FirstOrDefaultAsync(p => p.BookingID == booking.BookingID);

                    if (payment == null)
                    {
                        return BadRequest(new { error = "Failed to create payment record" });
                    }
                }

                // Initialize Stripe services
                var stripePaymentIntentService = new Stripe.PaymentIntentService();
                var stripePaymentMethodService = new Stripe.PaymentMethodService();
                var stripeCustomerService = new Stripe.CustomerService();

                // Get the current payment intent from Stripe
                var paymentIntent = await stripePaymentIntentService.GetAsync(payment.PaymentIntentID);

                // Check if we need to attach a payment method
                if (paymentIntent.Status == "requires_payment_method" && !string.IsNullOrEmpty(confirmationDto.PaymentMethodId))
                {
                    // Get payment method details
                    var paymentMethod = await stripePaymentMethodService.GetAsync(confirmationDto.PaymentMethodId);

                    // Get or create a customer for this user
                    string customerId = null;

                    // If the payment method already belongs to a customer
                    if (!string.IsNullOrEmpty(paymentMethod.CustomerId))
                    {
                        customerId = paymentMethod.CustomerId;
                    }
                    else
                    {
                        // Try to find an existing customer by email
                        var customerListOptions = new CustomerListOptions
                        {
                            Email = booking.User.Email,
                            Limit = 1
                        };

                        var customers = await stripeCustomerService.ListAsync(customerListOptions);

                        if (customers.Data.Count > 0)
                        {
                            customerId = customers.Data[0].Id;
                        }
                        else
                        {
                            // Create a new customer
                            var customerOptions = new CustomerCreateOptions
                            {
                                Email = booking.User.Email,
                                Name = $"{booking.User.FirstName} {booking.User.LastName}".Trim(),
                                Phone = booking.User.PhoneNumber,
                                PaymentMethod = confirmationDto.PaymentMethodId,
                            };

                            var customer = await stripeCustomerService.CreateAsync(customerOptions);
                            customerId = customer.Id;
                        }
                    }

                    // Update the payment intent with customer and payment method
                    var updateOptions = new PaymentIntentUpdateOptions
                    {
                        Customer = customerId,
                        PaymentMethod = confirmationDto.PaymentMethodId
                    };

                    paymentIntent = await stripePaymentIntentService.UpdateAsync(payment.PaymentIntentID, updateOptions);
                }

                // Check if we need to confirm the payment
                if (paymentIntent.Status == "requires_confirmation" ||
                    (paymentIntent.Status == "requires_payment_method" && !string.IsNullOrEmpty(confirmationDto.PaymentMethodId)))
                {
                    // Define return URL from server configuration
                    string baseUrl = _configuration["ApplicationUrl"] ?? $"{Request.Scheme}://{Request.Host}";
                    string returnUrl = $"{baseUrl}/bookings/confirmation/{booking.BookingID}";

                    // Confirm the payment intent
                    var confirmOptions = new PaymentIntentConfirmOptions
                    {
                        ReturnUrl = returnUrl
                    };

                    paymentIntent = await stripePaymentIntentService.ConfirmAsync(payment.PaymentIntentID, confirmOptions);
                }

                // Check if additional action is required (3D Secure, etc.)
                if (paymentIntent.Status == "requires_action")
                {
                    // Return the client secret and next action URL to the client
                    return Ok(new
                    {
                        requiresAction = true,
                        paymentIntentId = paymentIntent.Id,
                        clientSecret = paymentIntent.ClientSecret,
                        nextActionUrl = paymentIntent.NextAction?.RedirectToUrl?.Url,
                        bookingId = booking.BookingID
                    });
                }

                // Update the payment record in our database
                payment = await _paymentService.ConfirmPayment(payment.PaymentIntentID);

                // Update booking status based on payment status
                if (payment.Status == "succeeded")
                {
                    booking.BookingStatus = "Confirmed";
                    booking.PaymentStatus = "Paid";
                    await _context.SaveChangesAsync();

                    // Send confirmation email
                    try
                    {
                        await SendBookingConfirmationEmail(booking);
                    }
                    catch (Exception ex)
                    {
                        // Log error but continue
                        _logger.LogError(ex, $"Error sending confirmation email for booking {booking.BookingID}");
                    }
                }
                else if (payment.Status == "canceled" || payment.Status == "failed")
                {
                    booking.PaymentStatus = payment.Status == "canceled" ? "Canceled" : "Failed";
                    await _context.SaveChangesAsync();
                }

                // Get primary image URL
                string primaryImageUrl = booking.Apartment.ApartmentImages
                    .Where(ai => ai.IsPrimary)
                    .Select(ai => ai.ImageUrl)
                    .FirstOrDefault() ??
                    booking.Apartment.ApartmentImages
                    .Select(ai => ai.ImageUrl)
                    .FirstOrDefault();

                // Create response DTO
                var response = new BookingResponseDto
                {
                    BookingID = booking.BookingID,
                    ApartmentID = booking.ApartmentID,
                    ApartmentTitle = booking.Apartment.Title,
                    PrimaryImageUrl = primaryImageUrl,
                    CheckInDate = booking.CheckInDate,
                    CheckOutDate = booking.CheckOutDate,
                    GuestCount = booking.GuestCount,
                    PetCount = booking.PetCount,
                    BasePrice = booking.BasePrice,
                    DiscountAmount = booking.DiscountAmount ?? 0,
                    TotalPrice = booking.TotalPrice,
                    PromotionCode = booking.PromotionCode,
                    BookingStatus = booking.BookingStatus,
                    PaymentStatus = booking.PaymentStatus,
                    ReservationNumber = booking.ReservationNumber,
                    CreatedDate = booking.CreatedDate,
                    Address = booking.Apartment.Address,
                    Neighborhood = booking.Apartment.Neighborhood,
                    PricePerNight = booking.Apartment.PricePerNight,
                    HasReview = false,
                    PaymentIntentClientSecret = paymentIntent.ClientSecret
                };

                return Ok(response);
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, $"Stripe error confirming payment for booking {id}: {ex.Message}");
                return BadRequest(new { error = $"Payment processing error: {ex.Message}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error confirming payment for booking {id}: {ex.Message}");
                return BadRequest(new { error = $"Error confirming payment: {ex.Message}" });
            }
        }

        // Add this helper method to send a booking confirmation email
        private async Task SendBookingConfirmationEmail(Booking booking)
        {
            // Get SMTP settings from configuration
            var model = new
            {
                GuestName = booking.User.FirstName ?? booking.User.Username,
                ReservationNumber = booking.ReservationNumber,
                ApartmentTitle = booking.Apartment.Title,
                Address = booking.Apartment.Address,
                Neighborhood = booking.Apartment.Neighborhood,
                CheckInDate = booking.CheckInDate.ToShortDateString(),
                CheckOutDate = booking.CheckOutDate.ToShortDateString(),
                GuestCount = booking.GuestCount.ToString(),
                TotalPrice = booking.TotalPrice.ToString("F2")
            };

            await _emailService.SendEmailAsync(
                booking.User.Email,
                "Your ChabbyNb Booking Confirmation",
                "BookingConfirmation",
                model
            );
        }
    }
}