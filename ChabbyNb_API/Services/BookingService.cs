using ChabbyNb.Models;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using ChabbyNb_API.Models.DTOs;
using ChabbyNb_API.Services.Core;
using ChabbyNb_API.Services.Iterfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Stripe;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChabbyNb_API.Services
{
    public interface IBookingService : IEntityService<Booking, BookingResponseDto, BookingCreateDto, BookingCreateDto>
    {
        Task<IEnumerable<BookingResponseDto>> GetUserBookingsAsync(int userId);
        Task<BookingResponseDto> GetBookingByIdAsync(int id, int userId);
        Task<IEnumerable<BookingResponseDto>> GetUpcomingBookingsAsync(int userId);
        Task<IEnumerable<BookingResponseDto>> GetPastBookingsAsync(int userId);
        Task<ServiceResult<BookingResponseDto>> CreateBookingWithPaymentAsync(BookingCreateDto dto, int userId);
        Task<ServiceResult<BookingResponseDto>> ConfirmPaymentAsync(int bookingId, PaymentConfirmationDto dto, int userId);
        Task<ServiceResult> CancelBookingAsync(int bookingId, int userId);
        Task<ServiceResult<BookingPriceResult>> CalculatePriceAsync(int apartmentId, DateTime checkInDate, DateTime checkOutDate, int guestCount, int petCount, string promotionCode);
        Task<bool> IsApartmentAvailableAsync(int apartmentId, DateTime checkInDate, DateTime checkOutDate, int? excludeBookingId = null);
    }

    public class BookingService : BaseEntityService<Booking, BookingResponseDto, BookingCreateDto, BookingCreateDto>,
        IBookingService
    {
        private readonly IPricingService _pricingService;
        private readonly IPaymentService _paymentService;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _configuration;
        private readonly Microsoft.AspNetCore.Http.IHttpContextAccessor _httpContextAccessor;

        public BookingService(
            ChabbyNbDbContext context,
            IMapper mapper,
            ILogger<BookingService> logger,
            IPricingService pricingService,
            IPaymentService paymentService,
            IEmailService emailService,
            IConfiguration configuration,
            Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor)
            : base(context, mapper, logger)
        {
            _pricingService = pricingService ?? throw new ArgumentNullException(nameof(pricingService));
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        }

        #region Base Service Implementation

        protected override async Task<Booking> GetEntityByIdAsync(int id)
        {
            return await _dbSet
                .Include(b => b.Apartment)
                    .ThenInclude(a => a.ApartmentImages)
                .Include(b => b.User)
                .Include(b => b.Reviews)
                .Include(b => b.Promotion)
                .FirstOrDefaultAsync(b => b.BookingID == id);
        }

        protected override IQueryable<Booking> GetBaseQuery()
        {
            return _dbSet
                .Include(b => b.Apartment)
                    .ThenInclude(a => a.ApartmentImages)
                .Include(b => b.User);
        }

        protected override async Task<BookingResponseDto> MapToDto(Booking entity)
        {
            return new BookingResponseDto
            {
                BookingID = entity.BookingID,
                ApartmentID = entity.ApartmentID,
                ApartmentTitle = entity.Apartment?.Title,
                PrimaryImageUrl = GetPrimaryImageUrl(entity.Apartment),
                CheckInDate = entity.CheckInDate,
                CheckOutDate = entity.CheckOutDate,
                GuestCount = entity.GuestCount,
                PetCount = entity.PetCount,
                BasePrice = entity.BasePrice,
                DiscountAmount = entity.DiscountAmount ?? 0,
                TotalPrice = entity.TotalPrice,
                PromotionCode = entity.PromotionCode ?? string.Empty,
                BookingStatus = entity.BookingStatus,
                PaymentStatus = entity.PaymentStatus,
                ReservationNumber = entity.ReservationNumber,
                CreatedDate = entity.CreatedDate,
                Address = entity.Apartment?.Address,
                Neighborhood = entity.Apartment?.Neighborhood,
                PricePerNight = entity.Apartment?.PricePerNight ?? 0,
                HasReview = entity.Reviews?.Any() ?? false
            };
        }

        protected override async Task<IEnumerable<BookingResponseDto>> MapToDtos(IEnumerable<Booking> entities)
        {
            var tasks = entities.Select(MapToDto);
            return await Task.WhenAll(tasks);
        }

        protected override async Task<Booking> MapFromCreateDto(BookingCreateDto createDto)
        {
            // This will be handled in CreateBookingWithPaymentAsync
            throw new NotImplementedException("Use CreateBookingWithPaymentAsync instead");
        }

        protected override async Task MapFromUpdateDto(BookingCreateDto updateDto, Booking entity)
        {
            // Bookings typically aren't updated after creation
            throw new NotImplementedException("Booking updates not supported");
        }

        #endregion

        #region IBookingService Implementation

        public async Task<IEnumerable<BookingResponseDto>> GetUserBookingsAsync(int userId)
        {
            var bookings = await _dbSet
                .Include(b => b.Apartment)
                    .ThenInclude(a => a.ApartmentImages)
                .Where(b => b.UserID == userId)
                .OrderByDescending(b => b.CreatedDate)
                .ToListAsync();

            return await MapToDtos(bookings);
        }

        public async Task<BookingResponseDto> GetBookingByIdAsync(int id, int userId)
        {
            var booking = await _dbSet
                .Include(b => b.Apartment)
                    .ThenInclude(a => a.ApartmentImages)
                .Include(b => b.Reviews)
                .Include(b => b.Promotion)
                .FirstOrDefaultAsync(b => b.BookingID == id && b.UserID == userId);

            return booking != null ? await MapToDto(booking) : null;
        }

        public async Task<IEnumerable<BookingResponseDto>> GetUpcomingBookingsAsync(int userId)
        {
            var bookings = await _dbSet
                .Include(b => b.Apartment)
                    .ThenInclude(a => a.ApartmentImages)
                .Where(b =>
                    b.UserID == userId &&
                    b.CheckInDate >= DateTime.Today &&
                    b.BookingStatus != "Canceled")
                .OrderBy(b => b.CheckInDate)
                .ToListAsync();

            return await MapToDtos(bookings);
        }

        public async Task<IEnumerable<BookingResponseDto>> GetPastBookingsAsync(int userId)
        {
            var bookings = await _dbSet
                .Include(b => b.Apartment)
                    .ThenInclude(a => a.ApartmentImages)
                .Where(b =>
                    b.UserID == userId &&
                    b.CheckOutDate < DateTime.Today)
                .OrderByDescending(b => b.CheckOutDate)
                .ToListAsync();

            return await MapToDtos(bookings);
        }

        public async Task<ServiceResult<BookingResponseDto>> CreateBookingWithPaymentAsync(BookingCreateDto dto, int userId)
        {
            try
            {
                // Validate apartment
                var apartment = await _context.Apartments
                    .Include(a => a.ApartmentImages)
                    .FirstOrDefaultAsync(a => a.ApartmentID == dto.ApartmentID && a.IsActive);

                if (apartment == null)
                    return ServiceResult<BookingResponseDto>.ErrorResult("Apartment not found or not available");

                // Validate booking details
                var validationResult = await ValidateBookingRequest(dto, apartment);
                if (!validationResult.Success)
                    return ServiceResult<BookingResponseDto>.ErrorResult(validationResult.Errors);

                // Calculate pricing
                var priceResult = await _pricingService.CalculateBookingPriceAsync(
                    dto.ApartmentID, dto.CheckInDate, dto.CheckOutDate,
                    dto.GuestCount, dto.PetCount, dto.PromotionCode);

                // Create booking
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
                    BookingStatus = "Pending",
                    PaymentStatus = "Pending",
                    SpecialRequests = dto.SpecialRequests,
                    ReservationNumber = GenerateReservationNumber(),
                    CreatedDate = DateTime.Now
                };

                _dbSet.Add(booking);
                await _context.SaveChangesAsync();

                // Update promotion usage
                if (priceResult.PromotionId.HasValue)
                {
                    await UpdatePromotionUsage(priceResult.PromotionId.Value);
                }

                // Load user and create payment intent
                var user = await _context.Users.FindAsync(userId);
                booking.User = user;
                booking.Apartment = apartment;

                string clientSecret = await _paymentService.CreatePaymentIntent(booking);

                var payment = await _context.Payments
                    .FirstOrDefaultAsync(p => p.BookingID == booking.BookingID);

                var response = await MapToDto(booking);
                response.PaymentIntentClientSecret = clientSecret;
                response.PaymentIntentId = payment?.PaymentIntentID;

                return ServiceResult<BookingResponseDto>.SuccessResult(response, "Booking created successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating booking");
                return ServiceResult<BookingResponseDto>.ErrorResult($"Error creating booking: {ex.Message}");
            }
        }

        public async Task<ServiceResult<BookingResponseDto>> ConfirmPaymentAsync(int bookingId, PaymentConfirmationDto dto, int userId)
        {
            try
            {
                var booking = await GetEntityByIdAsync(bookingId);
                if (booking == null || booking.UserID != userId)
                    return ServiceResult<BookingResponseDto>.ErrorResult("Booking not found");

                if (booking.BookingStatus != "Pending" || booking.PaymentStatus != "Pending")
                    return ServiceResult<BookingResponseDto>.ErrorResult("Booking is not in a pending state");

                var payment = await _context.Payments
                    .FirstOrDefaultAsync(p => p.BookingID == booking.BookingID);

                if (payment == null)
                {
                    string clientSecret = await _paymentService.CreatePaymentIntent(booking);
                    payment = await _context.Payments
                        .FirstOrDefaultAsync(p => p.BookingID == booking.BookingID);
                }

                var result = await ProcessStripePayment(payment, dto, booking.User);

                if (result.Status == "succeeded")
                {
                    booking.BookingStatus = "Confirmed";
                    booking.PaymentStatus = "Paid";
                    await _context.SaveChangesAsync();

                    await SendBookingConfirmationEmailAsync(booking);
                    _logger.LogInformation($"Payment succeeded for booking {bookingId}");
                }
                else if (result.Status == "canceled" || result.Status == "failed")
                {
                    booking.PaymentStatus = result.Status == "canceled" ? "Canceled" : "Failed";
                    await _context.SaveChangesAsync();
                }

                var response = await MapToDto(booking);
                response.PaymentIntentClientSecret = result.ClientSecret;
                response.PaymentIntentId = payment.PaymentIntentID;

                return ServiceResult<BookingResponseDto>.SuccessResult(response, "Payment processed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error confirming payment for booking {bookingId}");
                return ServiceResult<BookingResponseDto>.ErrorResult($"Error processing payment: {ex.Message}");
            }
        }

        public async Task<ServiceResult> CancelBookingAsync(int bookingId, int userId)
        {
            try
            {
                var booking = await _dbSet
                    .Include(b => b.User)
                    .Include(b => b.Apartment)
                    .FirstOrDefaultAsync(b =>
                        b.BookingID == bookingId &&
                        b.UserID == userId &&
                        b.BookingStatus != "Canceled" &&
                        b.BookingStatus != "Completed");

                if (booking == null)
                    return ServiceResult.ErrorResult("Booking not found or cannot be canceled");

                // Handle pending bookings
                if (booking.BookingStatus == "Pending" && booking.PaymentStatus == "Pending" &&
                    (DateTime.Now - booking.CreatedDate).TotalMinutes > 10)
                {
                    booking.BookingStatus = "Canceled";
                    booking.PaymentStatus = "Expired";
                    await _context.SaveChangesAsync();

                    await SendBookingExpiredEmailAsync(booking);
                    return ServiceResult.SuccessResult("Booking canceled due to payment timeout");
                }

                // Handle confirmed bookings
                var daysUntilCheckIn = (booking.CheckInDate - DateTime.Now).TotalDays;
                var isEligibleForAutoRefund = daysUntilCheckIn >= 7;

                booking.BookingStatus = "Canceled";

                if (booking.PaymentStatus == "Paid" || booking.PaymentStatus == "Partially Paid")
                {
                    await ProcessCancellationRefund(booking, isEligibleForAutoRefund);
                }

                await _context.SaveChangesAsync();
                await SendBookingCancellationEmailAsync(booking, isEligibleForAutoRefund);

                return ServiceResult.SuccessResult(isEligibleForAutoRefund
                    ? "Booking canceled with full refund"
                    : "Booking canceled, refund pending admin review");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error canceling booking {bookingId}");
                return ServiceResult.ErrorResult($"Error canceling booking: {ex.Message}");
            }
        }

        public async Task<ServiceResult<BookingPriceResult>> CalculatePriceAsync(
            int apartmentId, DateTime checkInDate, DateTime checkOutDate,
            int guestCount, int petCount, string promotionCode)
        {
            try
            {
                var apartment = await _context.Apartments.FindAsync(apartmentId);
                if (apartment == null || !apartment.IsActive)
                    return ServiceResult<BookingPriceResult>.ErrorResult("Apartment not found or not available");

                if (checkInDate < DateTime.Today)
                    return ServiceResult<BookingPriceResult>.ErrorResult("Check-in date cannot be in the past");

                if (checkOutDate <= checkInDate)
                    return ServiceResult<BookingPriceResult>.ErrorResult("Check-out date must be after check-in date");

                if (!await IsApartmentAvailableAsync(apartmentId, checkInDate, checkOutDate))
                    return ServiceResult<BookingPriceResult>.ErrorResult("Apartment is not available for the selected dates");

                if (petCount > 0 && !apartment.PetFriendly)
                    return ServiceResult<BookingPriceResult>.ErrorResult("This apartment does not allow pets");

                var priceResult = await _pricingService.CalculateBookingPriceAsync(
                    apartmentId, checkInDate, checkOutDate, guestCount, petCount, promotionCode);

                return ServiceResult<BookingPriceResult>.SuccessResult(priceResult, "Price calculated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating price");
                return ServiceResult<BookingPriceResult>.ErrorResult($"Error calculating price: {ex.Message}");
            }
        }

        public async Task<bool> IsApartmentAvailableAsync(int apartmentId, DateTime checkIn, DateTime checkOut, int? excludeBookingId = null)
        {
            var apartmentExists = await _context.Apartments
                .AnyAsync(a => a.ApartmentID == apartmentId && a.IsActive);

            if (!apartmentExists)
                return false;

            var query = _dbSet
                .Where(b =>
                    b.ApartmentID == apartmentId &&
                    b.BookingStatus != "Canceled" &&
                    (
                        (checkIn >= b.CheckInDate && checkIn < b.CheckOutDate) ||
                        (checkOut > b.CheckInDate && checkOut <= b.CheckOutDate) ||
                        (checkIn <= b.CheckInDate && checkOut >= b.CheckOutDate)
                    ));

            if (excludeBookingId.HasValue)
                query = query.Where(b => b.BookingID != excludeBookingId.Value);

            return !await query.AnyAsync();
        }

        #endregion

        #region Private Helper Methods

        private string GetPrimaryImageUrl(Apartment apartment)
        {
            if (apartment?.ApartmentImages == null || !apartment.ApartmentImages.Any())
                return null;

            return apartment.ApartmentImages
                .Where(ai => ai.IsPrimary)
                .Select(ai => ai.ImageUrl)
                .FirstOrDefault() ??
                apartment.ApartmentImages
                .Select(ai => ai.ImageUrl)
                .FirstOrDefault();
        }

        private async Task<ServiceResult> ValidateBookingRequest(BookingCreateDto dto, Apartment apartment)
        {
            var errors = new List<string>();

            if (dto.CheckInDate < DateTime.Today)
                errors.Add("Check-in date cannot be in the past");

            if (dto.CheckOutDate <= dto.CheckInDate)
                errors.Add("Check-out date must be after check-in date");

            if (dto.GuestCount > apartment.MaxOccupancy)
                errors.Add($"Maximum occupancy for this apartment is {apartment.MaxOccupancy} guests");

            if (dto.PetCount > 0 && !apartment.PetFriendly)
                errors.Add("This apartment does not allow pets");

            if (!await IsApartmentAvailableAsync(dto.ApartmentID, dto.CheckInDate, dto.CheckOutDate))
                errors.Add("Apartment is not available for the selected dates");

            return errors.Any() ? ServiceResult.ErrorResult(errors) : ServiceResult.SuccessResult();
        }

        private string GenerateReservationNumber()
        {
            string dateString = DateTime.Now.ToString("yyyyMMdd");
            string randomDigits = new Random().Next(1000, 9999).ToString();
            return $"CB-{dateString}-{randomDigits}";
        }

        private async Task UpdatePromotionUsage(int promotionId)
        {
            var promotion = await _context.Promotions.FindAsync(promotionId);
            if (promotion != null)
            {
                promotion.UsageCount++;
                await _context.SaveChangesAsync();
            }
        }

        private async Task<(string Status, string ClientSecret)> ProcessStripePayment(Payment payment, PaymentConfirmationDto dto, User user)
        {
            StripeConfiguration.ApiKey = _configuration["Stripe:SecretKey"];

            var paymentIntentService = new PaymentIntentService();
            var paymentMethodService = new PaymentMethodService();
            var customerService = new CustomerService();

            var paymentIntent = await paymentIntentService.GetAsync(payment.PaymentIntentID);

            if (!string.IsNullOrEmpty(dto.PaymentMethodId))
            {
                var paymentMethod = await paymentMethodService.GetAsync(dto.PaymentMethodId);
                var updateOptions = new PaymentIntentUpdateOptions
                {
                    PaymentMethod = dto.PaymentMethodId
                };

                string customerId = await GetOrCreateCustomer(customerService, paymentMethod, user);
                if (!string.IsNullOrEmpty(customerId))
                {
                    updateOptions.Customer = customerId;
                }

                paymentIntent = await paymentIntentService.UpdateAsync(payment.PaymentIntentID, updateOptions);
            }

            if (paymentIntent.Status == "requires_confirmation")
            {
                string returnUrl = BuildReturnUrl(payment.BookingID);
                paymentIntent = await paymentIntentService.ConfirmAsync(
                    payment.PaymentIntentID,
                    new PaymentIntentConfirmOptions { ReturnUrl = returnUrl });
            }

            return (paymentIntent.Status, paymentIntent.ClientSecret);
        }

        private async Task<string> GetOrCreateCustomer(CustomerService customerService, PaymentMethod paymentMethod, User user)
        {
            if (!string.IsNullOrEmpty(paymentMethod.CustomerId))
                return paymentMethod.CustomerId;

            var customers = await customerService.ListAsync(new CustomerListOptions
            {
                Email = user.Email,
                Limit = 1
            });

            if (customers.Data.Count > 0)
                return customers.Data[0].Id;

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

        private string BuildReturnUrl(int bookingId)
        {
            string baseUrl = string.IsNullOrEmpty(_configuration["ApplicationUrl"])
                ? $"{_httpContextAccessor.HttpContext.Request.Scheme}://{_httpContextAccessor.HttpContext.Request.Host}"
                : _configuration["ApplicationUrl"];

            return $"{baseUrl}/bookings/confirmation/{bookingId}";
        }

        private async Task ProcessCancellationRefund(Booking booking, bool isEligibleForAutoRefund)
        {
            var payment = await _context.Payments
                .FirstOrDefaultAsync(p => p.BookingID == booking.BookingID && p.Status == "succeeded");

            if (payment != null)
            {
                if (isEligibleForAutoRefund)
                {
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
                        booking.PaymentStatus = "Cancellation Pending";
                    }
                }
                else
                {
                    booking.PaymentStatus = "Cancellation Pending";
                    _logger.LogInformation($"Booking {booking.BookingID} canceled, pending admin review for refund");
                }
            }
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

        #endregion
    }
}