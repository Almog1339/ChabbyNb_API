using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using ChabbyNb_API.Models.DTOs;
using System.Linq;
using ChabbyNb_API.Services.Iterfaces;
using Stripe;

namespace ChabbyNb_API.Services
{
    public interface IBookingService
    {
        Task<IEnumerable<Booking>> GetUserBookingsAsync(int userId);
        Task<Booking> GetBookingByIdAsync(int id, int userId);
        Task<IEnumerable<Booking>> GetUpcomingBookingsAsync(int userId);
        Task<IEnumerable<Booking>> GetPastBookingsAsync(int userId);
        Task<BookingResponseDto> CreateBookingAsync(BookingCreateDto dto, int userId);
        Task<BookingPriceResult> CalculatePriceAsync(int apartmentId, DateTime checkInDate, DateTime checkOutDate, int guestCount, int petCount, string promotionCode);
        Task<BookingResponseDto> ConfirmPaymentAsync(int bookingId, PaymentConfirmationDto dto, int userId);
        Task<bool> CancelBookingAsync(int bookingId, int userId);
        Task<bool> IsApartmentAvailableAsync(int apartmentId, DateTime checkInDate, DateTime checkOutDate);
    }

    public class BookingService : IBookingService
    {
        private readonly ChabbyNbDbContext _context;
        private readonly IPricingService _pricingService;
        private readonly IPaymentService _paymentService;
        private readonly IEmailService _emailService;
        private readonly ILogger<BookingService> _logger;
        private readonly IConfiguration _configuration;

        public BookingService(
            ChabbyNbDbContext context,
            IPricingService pricingService,
            IPaymentService paymentService,
            IEmailService emailService,
            ILogger<BookingService> logger,
            IConfiguration configuration,
            IHttpContextAccessor httpContextAccessor) // Add IHttpContextAccessor as a dependency
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _pricingService = pricingService ?? throw new ArgumentNullException(nameof(pricingService));
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor)); // Initialize IHttpContextAccessor
        }

        public async Task<IEnumerable<Booking>> GetUserBookingsAsync(int userId)
        {
            return await _context.Bookings
                .Include(b => b.Apartment)
                    .ThenInclude(a => a.ApartmentImages)
                .Where(b => b.UserID == userId)
                .OrderByDescending(b => b.CreatedDate)
                .ToListAsync();
        }

        public async Task<Booking> GetBookingByIdAsync(int id, int userId)
        {
            return await _context.Bookings
                .Include(b => b.Apartment)
                    .ThenInclude(a => a.ApartmentImages)
                .Include(b => b.Reviews)
                .Include(b => b.Promotion)
                .FirstOrDefaultAsync(b => b.BookingID == id && b.UserID == userId);
        }

        public async Task<IEnumerable<Booking>> GetUpcomingBookingsAsync(int userId)
        {
            return await _context.Bookings
                .Include(b => b.Apartment)
                    .ThenInclude(a => a.ApartmentImages)
                .Where(b =>
                    b.UserID == userId &&
                    b.CheckInDate >= DateTime.Today &&
                    b.BookingStatus != "Canceled")
                .OrderBy(b => b.CheckInDate)
                .ToListAsync();
        }

        public async Task<IEnumerable<Booking>> GetPastBookingsAsync(int userId)
        {
            return await _context.Bookings
                .Include(b => b.Apartment)
                    .ThenInclude(a => a.ApartmentImages)
                .Where(b =>
                    b.UserID == userId &&
                    b.CheckOutDate < DateTime.Today)
                .OrderByDescending(b => b.CheckOutDate)
                .ToListAsync();
        }

        public async Task<BookingResponseDto> CreateBookingAsync(BookingCreateDto dto, int userId)
        {
            // Check if apartment exists
            var apartment = await _context.Apartments
                .Include(a => a.ApartmentImages)
                .FirstOrDefaultAsync(a => a.ApartmentID == dto.ApartmentID && a.IsActive);

            if (apartment == null)
            {
                throw new InvalidOperationException("Apartment not found or not available");
            }

            // Validate booking details
            if (dto.CheckInDate < DateTime.Today)
            {
                throw new ArgumentException("Check-in date cannot be in the past");
            }

            if (dto.CheckOutDate <= dto.CheckInDate)
            {
                throw new ArgumentException("Check-out date must be after check-in date");
            }

            // Check if apartment is available for these dates
            bool isAvailable = await IsApartmentAvailableAsync(dto.ApartmentID, dto.CheckInDate, dto.CheckOutDate);
            if (!isAvailable)
            {
                throw new InvalidOperationException("Apartment is not available for the selected dates");
            }

            // Check if guest count is valid
            if (dto.GuestCount > apartment.MaxOccupancy)
            {
                throw new ArgumentException($"Maximum occupancy for this apartment is {apartment.MaxOccupancy} guests");
            }

            // Check pet policy
            if (dto.PetCount > 0 && !apartment.PetFriendly)
            {
                throw new ArgumentException("This apartment does not allow pets");
            }

            // Calculate total price
            var priceResult = await _pricingService.CalculateBookingPriceAsync(
                dto.ApartmentID,
                dto.CheckInDate,
                dto.CheckOutDate,
                dto.GuestCount,
                dto.PetCount,
                dto.PromotionCode);

            // Create booking record
            var booking = new Booking
            {
                UserID = userId,
                ApartmentID = dto.ApartmentID,
                CheckInDate = dto.CheckInDate,
                CheckOutDate = dto.CheckOutDate,
                GuestCount = dto.GuestCount,
                PetCount = dto.PetCount,
                BasePrice = priceResult.BasePrice,
                DiscountAmount = priceResult.DiscountAmount,
                TotalPrice = priceResult.TotalPrice,
                PromotionID = priceResult.PromotionId,
                PromotionCode = priceResult.PromotionCode ?? string.Empty,
                BookingStatus = "Pending", // Initial status is Pending until payment is confirmed
                PaymentStatus = "Pending",
                SpecialRequests = dto.SpecialRequests,
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
                throw new InvalidOperationException("User not found");
            }

            booking.User = user;
            booking.Apartment = apartment;

            // Create a payment intent
            string clientSecret = await _paymentService.CreatePaymentIntent(booking);

            // Find the payment record that was created
            var payment = await _context.Payments
                .FirstOrDefaultAsync(p => p.BookingID == booking.BookingID);

            if (payment == null)
            {
                throw new InvalidOperationException("Failed to create payment record");
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
                PaymentIntentClientSecret = clientSecret,
                PaymentIntentId = payment.PaymentIntentID
            };

            return response;
        }

        public async Task<BookingPriceResult> CalculatePriceAsync(
            int apartmentId,
            DateTime checkInDate,
            DateTime checkOutDate,
            int guestCount = 1,
            int petCount = 0,
            string promotionCode = null)
        {
            // Validate apartment exists
            var apartment = await _context.Apartments.FindAsync(apartmentId);
            if (apartment == null || !apartment.IsActive)
            {
                throw new ArgumentException("Apartment not found or not available");
            }

            // Validate dates
            if (checkInDate < DateTime.Today)
            {
                throw new ArgumentException("Check-in date cannot be in the past");
            }

            if (checkOutDate <= checkInDate)
            {
                throw new ArgumentException("Check-out date must be after check-in date");
            }

            // Check if apartment is available for these dates
            bool isAvailable = await IsApartmentAvailableAsync(apartmentId, checkInDate, checkOutDate);
            if (!isAvailable)
            {
                throw new InvalidOperationException("Apartment is not available for the selected dates");
            }

            // Check pet policy
            if (petCount > 0 && !apartment.PetFriendly)
            {
                throw new ArgumentException("This apartment does not allow pets");
            }

            // Calculate price
            return await _pricingService.CalculateBookingPriceAsync(
                apartmentId, checkInDate, checkOutDate, guestCount, petCount, promotionCode);
        }

        public async Task<BookingResponseDto> ConfirmPaymentAsync(int bookingId, PaymentConfirmationDto dto, int userId)
        {
            // Get booking with necessary includes
            var booking = await _context.Bookings
                .Include(b => b.Apartment)
                    .ThenInclude(a => a.ApartmentImages)
                .Include(b => b.User)
                .FirstOrDefaultAsync(b => b.BookingID == bookingId && b.UserID == userId);

            if (booking == null)
            {
                throw new ArgumentException($"Booking with ID {bookingId} not found or doesn't belong to the current user");
            }

            if (booking.BookingStatus != "Pending" || booking.PaymentStatus != "Pending")
            {
                throw new InvalidOperationException($"Booking #{booking.BookingID} is not in a pending state (Status: {booking.BookingStatus}, Payment: {booking.PaymentStatus})");
            }

            // Get existing payment record or create new one if needed
            var payment = await _context.Payments
                .FirstOrDefaultAsync(p => p.BookingID == booking.BookingID);

            if (payment == null)
            {
                _logger.LogInformation($"No payment record found for booking {bookingId}, creating new payment intent");
                string clientSecret = await _paymentService.CreatePaymentIntent(booking);

                payment = await _context.Payments
                    .FirstOrDefaultAsync(p => p.BookingID == booking.BookingID);

                if (payment == null)
                {
                    throw new InvalidOperationException("Failed to create payment record");
                }
            }

            _logger.LogInformation($"Processing payment confirmation for booking {bookingId} with payment method {dto.PaymentMethodId}");

            try
            {
                // Configure Stripe API
                StripeConfiguration.ApiKey = _configuration["Stripe:SecretKey"];

                // Process payment with Stripe
                var result = await ProcessStripePayment(payment, dto, booking.User);

                // Update booking status based on payment result
                if (result.Status == "succeeded")
                {
                    booking.BookingStatus = "Confirmed";
                    booking.PaymentStatus = "Paid";

                    await _context.SaveChangesAsync();

                    // Send confirmation email
                    await SendBookingConfirmationEmailAsync(booking);
                    _logger.LogInformation($"Payment succeeded for booking {bookingId}, confirmation email sent");
                }
                else if (result.Status == "canceled" || result.Status == "failed")
                {
                    booking.PaymentStatus = result.Status == "canceled" ? "Canceled" : "Failed";
                    await _context.SaveChangesAsync();
                    _logger.LogWarning($"Payment {result.Status} for booking {bookingId}");
                }

                // Build response DTO
                return await BuildBookingResponseDto(booking, payment, result.ClientSecret);
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, $"Stripe error processing payment for booking {bookingId}: {ex.StripeError?.Message}");
                throw new ApplicationException($"Payment processor error: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error confirming payment for booking {bookingId}");
                throw;
            }
        }

        public async Task<bool> CancelBookingAsync(int bookingId, int userId)
        {
            var booking = await _context.Bookings
                .Include(b => b.User)
                .Include(b => b.Apartment)
                .FirstOrDefaultAsync(b =>
                    b.BookingID == bookingId &&
                    b.UserID == userId &&
                    b.BookingStatus != "Canceled" &&
                    b.BookingStatus != "Completed");

            if (booking == null)
            {
                throw new InvalidOperationException("Booking not found or cannot be canceled");
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
                    await SendBookingExpiredEmailAsync(booking);
                    _logger.LogInformation($"Sent booking expired email for booking {booking.BookingID}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error sending booking expired email for booking {booking.BookingID}");
                }

                return true;
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
                            int adminId = Convert.ToUInt16(_configuration["SysAdm"].ToString());
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
                await SendBookingCancellationEmailAsync(booking, isEligibleForAutoRefund);
                _logger.LogInformation($"Sent booking cancellation email for booking {booking.BookingID}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending cancellation email for booking {booking.BookingID}");
            }

            return true;
        }

        public async Task<bool> IsApartmentAvailableAsync(int apartmentId, DateTime checkIn, DateTime checkOut)
        {
            // First check if the apartment exists and is active
            var apartmentExists = await _context.Apartments
                .AnyAsync(a => a.ApartmentID == apartmentId && a.IsActive);

            if (!apartmentExists)
            {
                return false;
            }

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

        private async Task SendBookingExpiredEmailAsync(Booking booking)
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

        private async Task SendBookingCancellationEmailAsync(Booking booking, bool isEligibleForRefund)
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

        private string GenerateReservationNumber()
        {
            // Format: CB-YYYYMMDD-XXXX (CB = ChabbyNb, YYYYMMDD = current date, XXXX = 4 random digits)
            string dateString = DateTime.Now.ToString("yyyyMMdd");
            string randomDigits = new Random().Next(1000, 9999).ToString();

            return $"CB-{dateString}-{randomDigits}";
        }
        // Helper methods for ConfirmPaymentAsync
        private async Task<(string Status, string ClientSecret)> ProcessStripePayment(Payment payment, PaymentConfirmationDto dto, User user)
        {
            // Initialize Stripe services
            var paymentIntentService = new PaymentIntentService();
            var paymentMethodService = new PaymentMethodService();
            var customerService = new CustomerService();

            // Get current payment intent
            var paymentIntent = await paymentIntentService.GetAsync(payment.PaymentIntentID);

            // Only attach payment method if provided
            if (!string.IsNullOrEmpty(dto.PaymentMethodId))
            {
                // Get the payment method details
                var paymentMethod = await paymentMethodService.GetAsync(dto.PaymentMethodId);

                // Prepare options for updating payment intent
                var updateOptions = new PaymentIntentUpdateOptions
                {
                    PaymentMethod = dto.PaymentMethodId
                };

                // Add customer to payment intent
                string customerId = await GetOrCreateCustomer(customerService, paymentMethod, user);
                if (!string.IsNullOrEmpty(customerId))
                {
                    updateOptions.Customer = customerId;
                }

                // Update the payment intent with payment method and customer
                paymentIntent = await paymentIntentService.UpdateAsync(payment.PaymentIntentID, updateOptions);
            }

            // Handle payment confirmation if necessary
            if (paymentIntent.Status == "requires_confirmation")
            {
                string returnUrl = BuildReturnUrl(payment.BookingID);
                paymentIntent = await paymentIntentService.ConfirmAsync(
                    payment.PaymentIntentID,
                    new PaymentIntentConfirmOptions { ReturnUrl = returnUrl }
                );
            }

            return (paymentIntent.Status, paymentIntent.ClientSecret);
        }

        private async Task<string> GetOrCreateCustomer(CustomerService customerService, PaymentMethod paymentMethod, User user)
        {
            // If payment method already has customer, use it
            if (!string.IsNullOrEmpty(paymentMethod.CustomerId))
            {
                return paymentMethod.CustomerId;
            }

            // Try to find existing customer
            var customers = await customerService.ListAsync(new CustomerListOptions
            {
                Email = user.Email,
                Limit = 1
            });

            if (customers.Data.Count > 0)
            {
                return customers.Data[0].Id;
            }

            // Create new customer
            var customerOptions = new CustomerCreateOptions
            {
                Email = user.Email,
                Name = string.IsNullOrEmpty(user.FirstName) && string.IsNullOrEmpty(user.LastName)
                    ? user.Username
                    : $"{user.FirstName} {user.LastName}".Trim(),
                Phone = user.PhoneNumber
            };

            var customer = await customerService.CreateAsync(customerOptions);
            return customer.Id;
        }

        private readonly IHttpContextAccessor _httpContextAccessor;



        private string BuildReturnUrl(int bookingId)
        {
            string baseUrl = string.IsNullOrEmpty(_configuration["ApplicationUrl"])
                ? $"{_httpContextAccessor.HttpContext.Request.Scheme}://{_httpContextAccessor.HttpContext.Request.Host}" // Use IHttpContextAccessor to access HttpContext
                : _configuration["ApplicationUrl"];

            return $"{baseUrl}/bookings/confirmation/{bookingId}";
        }

        private async Task<BookingResponseDto> BuildBookingResponseDto(Booking booking, Payment payment, string clientSecret)
        {
            // Get primary image URL
            string primaryImageUrl = booking.Apartment.ApartmentImages
                .Where(ai => ai.IsPrimary)
                .Select(ai => ai.ImageUrl)
                .FirstOrDefault() ??
                booking.Apartment.ApartmentImages
                .Select(ai => ai.ImageUrl)
                .FirstOrDefault();

            return new BookingResponseDto
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
                PromotionCode = booking.PromotionCode ?? string.Empty,
                BookingStatus = booking.BookingStatus,
                PaymentStatus = booking.PaymentStatus,
                ReservationNumber = booking.ReservationNumber,
                CreatedDate = booking.CreatedDate,
                Address = booking.Apartment.Address,
                Neighborhood = booking.Apartment.Neighborhood,
                PricePerNight = booking.Apartment.PricePerNight,
                HasReview = booking.Reviews.Any(),
                PaymentIntentClientSecret = clientSecret,
                PaymentIntentId = payment.PaymentIntentID
            };
        }

        private async Task SendBookingConfirmationEmailAsync(Booking booking)
        {
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