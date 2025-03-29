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

        // POST: api/Bookings/5/Cancel
        [HttpPost("{id}/Cancel")]
        public async Task<IActionResult> CancelBooking(int id)
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var booking = await _context.Bookings
                .Include(b => b.User)
                .Include(b => b.Apartment)
                .FirstOrDefaultAsync(b =>
                    b.BookingID == id &&
                    b.UserID == userId &&
                    b.BookingStatus != "Canceled" &&
                    b.BookingStatus != "Completed");

            if (booking == null)
            {
                return NotFound(new { error = "Booking not found or cannot be canceled" });
            }

            // Check if the booking is still pending and created more than 10 minutes ago
            if (booking.BookingStatus == "Pending" && booking.PaymentStatus == "Pending" &&
                (DateTime.Now - booking.CreatedDate).TotalMinutes > 10)
            {
                booking.BookingStatus = "Canceled";
                booking.PaymentStatus = "Expired";

                await _context.SaveChangesAsync();

                // Send email notification that booking expired due to non-payment
                try
                {
                    await SendBookingExpiredEmail(booking);
                    _logger.LogInformation($"Sent booking expired email for booking {booking.BookingID}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error sending booking expired email for booking {booking.BookingID}");
                }

                return Ok(new
                {
                    success = true,
                    message = "Booking was canceled because payment was not completed within the time limit.",
                    booking
                });
            }

            // For confirmed bookings, check cancellation policy based on days until check-in
            TimeSpan daysUntilCheckIn = booking.CheckInDate - DateTime.Now;
            bool isEligibleForAutoRefund = daysUntilCheckIn.TotalDays >= 7;

            // Update booking status
            booking.BookingStatus = "Canceled";

            // Handle refund based on cancellation policy
            if (booking.PaymentStatus == "Paid" || booking.PaymentStatus == "Partially Paid")
            {
                // Find payment record
                var payment = await _context.Payments
                    .FirstOrDefaultAsync(p => p.BookingID == booking.BookingID && p.Status == "succeeded");

                if (payment != null)
                {
                    if (isEligibleForAutoRefund)
                    {
                        // Process automatic full refund
                        try
                        {
                            int adminId = 1; // Use a default admin ID for system-generated refunds
                            var refundReason = "Automatic refund - Cancellation more than 7 days before check-in";

                            await _paymentService.ProcessRefund(payment.PaymentID, payment.Amount, refundReason, adminId);

                            booking.PaymentStatus = "Refunded";
                            _logger.LogInformation($"Automatic full refund processed for booking {booking.BookingID}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error processing automatic refund for booking {booking.BookingID}");
                            booking.PaymentStatus = "Cancellation Pending"; // Mark for admin review
                        }
                    }
                    else
                    {
                        // No automatic refund, mark for admin review
                        booking.PaymentStatus = "Cancellation Pending";
                        _logger.LogInformation($"Booking {booking.BookingID} canceled, pending admin review for refund");
                    }
                }
            }

            await _context.SaveChangesAsync();

            // Send cancellation confirmation email
            try
            {
                await SendBookingCancellationEmail(booking, isEligibleForAutoRefund);
                _logger.LogInformation($"Sent booking cancellation email for booking {booking.BookingID}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending cancellation email for booking {booking.BookingID}");
            }

            return Ok(new
            {
                success = true,
                message = isEligibleForAutoRefund
                    ? "Your booking has been canceled with a full refund."
                    : "Your booking has been canceled. Refund eligibility will be reviewed by an administrator.",
                booking
            });
        }

        // Helper method to send booking expired email
        private async Task SendBookingExpiredEmail(Booking booking)
        {
            var model = new
            {
                GuestName = booking.User.FirstName ?? booking.User.Username,
                BookingID = booking.BookingID,
                ReservationNumber = booking.ReservationNumber,
                ApartmentTitle = booking.Apartment.Title,
                CheckInDate = booking.CheckInDate.ToShortDateString(),
                CheckOutDate = booking.CheckOutDate.ToShortDateString(),
                TotalPrice = booking.TotalPrice.ToString("C")
            };

            await _emailService.SendEmailAsync(
                booking.User.Email,
                "Your ChabbyNb Booking Reservation Expired",
                "BookingExpired",
                model
            );
        }

        // Helper method to send booking cancellation email
        private async Task SendBookingCancellationEmail(Booking booking, bool isEligibleForRefund)
        {
            var model = new
            {
                GuestName = booking.User.FirstName ?? booking.User.Username,
                ReservationNumber = booking.ReservationNumber,
                ApartmentTitle = booking.Apartment.Title,
                CheckInDate = booking.CheckInDate.ToShortDateString(),
                CheckOutDate = booking.CheckOutDate.ToShortDateString(),
                TotalPrice = booking.TotalPrice.ToString("C"),
                IsEligibleForRefund = isEligibleForRefund,
                RefundMessage = isEligibleForRefund
                    ? "A full refund has been processed and will be returned to your original payment method."
                    : "Your cancellation request will be reviewed by our team. If eligible for a refund, it will be processed within 5-7 business days."
            };

            await _emailService.SendEmailAsync(
                booking.User.Email,
                "Your ChabbyNb Booking Has Been Canceled",
                "BookingCancellation",
                model
            );
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

                    // Fetch the newly created payment record
                    payment = await _context.Payments
                        .FirstOrDefaultAsync(p => p.BookingID == booking.BookingID);

                    if (payment == null)
                    {
                        return BadRequest(new { error = "Failed to create payment record" });
                    }
                }

                // Set up Stripe API key
                StripeConfiguration.ApiKey = _configuration["Stripe:SecretKey"];

                // Initialize Stripe services
                var stripePaymentIntentService = new Stripe.PaymentIntentService();
                var stripePaymentMethodService = new Stripe.PaymentMethodService();
                var stripeCustomerService = new Stripe.CustomerService();

                // Get the current payment intent from Stripe
                var paymentIntent = await stripePaymentIntentService.GetAsync(payment.PaymentIntentID);

                _logger.LogInformation($"Payment intent {payment.PaymentIntentID} current status: {paymentIntent.Status}");

                // If a payment method ID was provided, attach it to the payment intent
                if (!string.IsNullOrEmpty(confirmationDto.PaymentMethodId))
                {
                    _logger.LogInformation($"Attaching payment method {confirmationDto.PaymentMethodId} to payment intent {payment.PaymentIntentID}");

                    // Get the payment method to check if it belongs to a customer
                    var paymentMethod = await stripePaymentMethodService.GetAsync(confirmationDto.PaymentMethodId);

                    // Set up the update options for the payment intent
                    var updateOptions = new PaymentIntentUpdateOptions
                    {
                        PaymentMethod = confirmationDto.PaymentMethodId
                    };

                    // If the payment method belongs to a customer, include that customer
                    if (!string.IsNullOrEmpty(paymentMethod.CustomerId))
                    {
                        _logger.LogInformation($"Payment method belongs to customer {paymentMethod.CustomerId}");
                        updateOptions.Customer = paymentMethod.CustomerId;
                    }
                    else
                    {
                        _logger.LogInformation("Payment method does not belong to a customer, finding or creating one");

                        // Try to find an existing customer for this user
                        var customerListOptions = new CustomerListOptions
                        {
                            Email = booking.User.Email,
                            Limit = 1
                        };

                        var customers = await stripeCustomerService.ListAsync(customerListOptions);

                        if (customers.Data.Count > 0)
                        {
                            _logger.LogInformation($"Found existing customer {customers.Data[0].Id} for user {booking.User.Email}");
                            updateOptions.Customer = customers.Data[0].Id;
                        }
                        else
                        {
                            _logger.LogInformation($"Creating new customer for user {booking.User.Email}");

                            // Create a new customer
                            var customerOptions = new CustomerCreateOptions
                            {
                                Email = booking.User.Email,
                                Name = string.IsNullOrEmpty(booking.User.FirstName) && string.IsNullOrEmpty(booking.User.LastName)
                                    ? booking.User.Username
                                    : $"{booking.User.FirstName} {booking.User.LastName}".Trim(),
                                Phone = booking.User.PhoneNumber
                            };

                            var customer = await stripeCustomerService.CreateAsync(customerOptions);
                            _logger.LogInformation($"Created new customer {customer.Id}");
                            updateOptions.Customer = customer.Id;
                        }
                    }

                    // Update the payment intent with the payment method and customer
                    _logger.LogInformation("Updating payment intent with payment method and customer");
                    paymentIntent = await stripePaymentIntentService.UpdateAsync(payment.PaymentIntentID, updateOptions);
                    _logger.LogInformation($"Updated payment intent, new status: {paymentIntent.Status}");
                }

                // Check if we need to confirm the payment intent
                if (paymentIntent.Status == "requires_confirmation" || paymentIntent.Status == "requires_payment_method")
                {
                    _logger.LogInformation($"Payment intent requires confirmation, status: {paymentIntent.Status}");

                    // Define return URL from server configuration
                    string baseUrl = _configuration["ApplicationUrl"];

                    // Make sure the URL is properly formatted and accessible
                    if (string.IsNullOrEmpty(baseUrl) || !Uri.IsWellFormedUriString(baseUrl, UriKind.Absolute))
                    {
                        baseUrl = $"{Request.Scheme}://{Request.Host}";
                        _logger.LogInformation($"Using request-based URL: {baseUrl}");
                    }
                    else
                    {
                        _logger.LogInformation($"Using configured URL: {baseUrl}");
                    }

                    string returnUrl = $"{baseUrl}/bookings/confirmation/{booking.BookingID}";
                    _logger.LogInformation($"Return URL: {returnUrl}");

                    // Confirm the payment intent
                    var confirmOptions = new PaymentIntentConfirmOptions
                    {
                        ReturnUrl = returnUrl
                    };

                    _logger.LogInformation("Confirming payment intent");
                    paymentIntent = await stripePaymentIntentService.ConfirmAsync(payment.PaymentIntentID, confirmOptions);
                    _logger.LogInformation($"Confirmed payment intent, new status: {paymentIntent.Status}");
                }

                // Check if additional action is required (3D Secure, etc.)
                if (paymentIntent.Status == "requires_action")
                {
                    _logger.LogInformation("Payment requires additional action");

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
                _logger.LogInformation($"Updating payment record for booking {booking.BookingID}");
                payment = await _paymentService.ConfirmPayment(payment.PaymentIntentID);
                _logger.LogInformation($"Payment status after confirmation: {payment.Status}");

                // Update booking status based on payment status
                if (payment.Status == "succeeded")
                {
                    _logger.LogInformation($"Payment succeeded for booking {booking.BookingID}");
                    booking.BookingStatus = "Confirmed";
                    booking.PaymentStatus = "Paid";
                    await _context.SaveChangesAsync();

                    // Send confirmation email
                    try
                    {
                        await SendBookingConfirmationEmail(booking);
                        _logger.LogInformation($"Sent booking confirmation email for booking {booking.BookingID}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error sending confirmation email for booking {booking.BookingID}");
                    }
                }
                else if (payment.Status == "canceled" || payment.Status == "failed")
                {
                    _logger.LogInformation($"Payment {payment.Status} for booking {booking.BookingID}");
                    booking.PaymentStatus = payment.Status == "canceled" ? "Canceled" : "Failed";
                    await _context.SaveChangesAsync();
                }
                else
                {
                    _logger.LogInformation($"Payment in state {payment.Status} for booking {booking.BookingID}");
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
                    PaymentIntentClientSecret = paymentIntent.ClientSecret,
                    PaymentIntentId = paymentIntent.Id
                };

                return Ok(response);
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, $"Stripe error confirming payment for booking {id}: {ex.Message}");
                _logger.LogError($"Stripe error details - Code: {ex.StripeError?.Code}, Type: {ex.StripeError?.Type}, Parameter: {ex.StripeError?.Param}");

                return BadRequest(new
                {
                    error = $"Payment processing error: {ex.Message}",
                    stripeErrorCode = ex.StripeError?.Code,
                    stripeErrorType = ex.StripeError?.Type,
                    stripeErrorParam = ex.StripeError?.Param
                });
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