using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Stripe;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using ChabbyNb_API.Models.DTOs;
using ChabbyNb_API.Services.Iterfaces;

namespace ChabbyNb_API.Services
{
    /// <summary>
    /// Implementation of payment service using Stripe as the payment processor.
    /// This service handles all payment-related operations including creating payments,
    /// processing refunds, and handling Stripe webhook events.
    /// </summary>
    public class StripePaymentService : IPaymentService
    {
        private readonly ChabbyNbDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<StripePaymentService> _logger;
        private readonly string _apiKey;
        private readonly string _webhookSecret;
        private readonly string _defaultCurrency;

        /// <summary>
        /// Initializes a new instance of the StripePaymentService class.
        /// </summary>
        /// <param name="context">Database context</param>
        /// <param name="configuration">Application configuration</param>
        /// <param name="logger">Logger for this service</param>
        public StripePaymentService(
            ChabbyNbDbContext context,
            IConfiguration configuration,
            ILogger<StripePaymentService> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Get configuration values
            _apiKey = _configuration["Stripe:SecretKey"];
            _webhookSecret = _configuration["Stripe:WebhookSecret"];
            _defaultCurrency = _configuration["Stripe:DefaultCurrency"] ?? "usd";

            if (string.IsNullOrEmpty(_apiKey))
            {
                throw new InvalidOperationException("Stripe:SecretKey configuration is missing or empty.");
            }

            // Configure Stripe API globally
            StripeConfiguration.ApiKey = _apiKey;

            // Set up stripe logging
            StripeConfiguration.AppInfo = new AppInfo
            {
                Name = "ChabbyNb",
                Version = "1.0"
            };
        }

        /// <summary>
        /// Creates a payment intent for a booking in Stripe.
        /// </summary>
        /// <param name="booking">The booking to create a payment for</param>
        /// <returns>Client secret for the payment intent</returns>
        public async Task<string> CreatePaymentIntent(Booking booking)
        {
            if (booking == null)
            {
                throw new ArgumentNullException(nameof(booking));
            }

            _logger.LogInformation($"Creating payment intent for booking {booking.BookingID} with amount {booking.TotalPrice}");

            try
            {
                // Ensure booking has related entities loaded
                if (booking.User == null)
                {
                    await _context.Entry(booking).Reference(b => b.User).LoadAsync();
                }

                if (booking.Apartment == null)
                {
                    await _context.Entry(booking).Reference(b => b.Apartment).LoadAsync();
                }

                // Convert amount to smallest unit (cents, etc.)
                long amountInSmallestUnit = ConvertToSmallestUnit(booking.TotalPrice);

                // Create metadata for better tracking
                var metadata = new Dictionary<string, string>
                {
                    { "booking_id", booking.BookingID.ToString() },
                    { "reservation_number", booking.ReservationNumber },
                    { "apartment_id", booking.ApartmentID.ToString() },
                    { "user_id", booking.UserID.ToString() },
                    { "check_in_date", booking.CheckInDate.ToString("yyyy-MM-dd") },
                    { "check_out_date", booking.CheckOutDate.ToString("yyyy-MM-dd") }
                };

                // Create a payment intent with Stripe
                var options = new PaymentIntentCreateOptions
                {
                    Amount = amountInSmallestUnit,
                    Currency = _defaultCurrency,
                    Metadata = metadata,
                    Description = $"Booking #{booking.ReservationNumber} - {booking.Apartment.Title}",
                    ReceiptEmail = booking.User.Email,
                    CaptureMethod = "automatic", // This is good
                    StatementDescriptor = "ChabbyNb Apartment",
                    StatementDescriptorSuffix = "Booking",
                    SetupFutureUsage = "off_session"  // This might need to be changed based on your needs
                };

                // Create the payment intent
                var service = new PaymentIntentService();
                var intent = await service.CreateAsync(options);
                _logger.LogInformation($"Created payment intent {intent.Id} for booking {booking.BookingID}");

                // Create a payment record in our database
                var payment = new Payment
                {
                    BookingID = booking.BookingID,
                    PaymentIntentID = intent.Id,
                    Amount = booking.TotalPrice,
                    Currency = _defaultCurrency,
                    Status = intent.Status,
                    PaymentMethod = string.IsNullOrEmpty(intent.PaymentMethodId) ? "Pending" : intent.PaymentMethodId,
                    LastFour = null, // This can be null
                    CardBrand = null, // This can be null
                    CreatedDate = DateTime.UtcNow
                };

                _context.Payments.Add(payment);

                // Update booking payment status
                _context.Entry(booking).State = EntityState.Modified;

                await _context.SaveChangesAsync();

                return intent.ClientSecret;
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, $"Stripe error creating payment intent for booking {booking.BookingID}: {ex.Message}");
                throw new ApplicationException("Error processing payment with Stripe", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating payment intent for booking {booking.BookingID}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Confirms a payment based on a payment intent ID.
        /// </summary>
        /// <param name="paymentIntentId">The Stripe payment intent ID</param>
        /// <returns>The updated payment record</returns>
        public async Task<Payment> ConfirmPayment(string paymentIntentId)
        {
            if (string.IsNullOrEmpty(paymentIntentId))
            {
                throw new ArgumentException("Payment intent ID cannot be null or empty", nameof(paymentIntentId));
            }

            _logger.LogInformation($"Confirming payment for intent {paymentIntentId}");

            try
            {
                // קבל את ה-Payment Intent מ-Stripe
                var service = new PaymentIntentService();

                var intent = await service.GetAsync(paymentIntentId);

                // מצא את רשומת התשלום במסד הנתונים
                var payment = await _context.Payments
                    .Include(p => p.Booking)
                    .FirstOrDefaultAsync(p => p.PaymentIntentID == paymentIntentId);

                if (payment == null)
                {
                    throw new InvalidOperationException($"Payment with intent ID {paymentIntentId} not found in database");
                }

                // עדכן את פרטי התשלום
                payment.Status = intent.Status;
                payment.PaymentMethod = intent.PaymentMethodId;

                if (intent.Status == "succeeded")
                {
                    payment.CompletedDate = DateTime.UtcNow;

                    // עדכן את סטטוס ההזמנה
                    payment.Booking.PaymentStatus = "Paid";
                    payment.Booking.BookingStatus = "Confirmed";

                    // קבל פרטי אמצעי תשלום אם זמינים
                    if (!string.IsNullOrEmpty(intent.PaymentMethodId))
                    {
                        var paymentMethodService = new PaymentMethodService();
                        var paymentMethod = await paymentMethodService.GetAsync(intent.PaymentMethodId);

                        if (paymentMethod.Card != null)
                        {
                            payment.LastFour = paymentMethod.Card.Last4;
                            payment.CardBrand = paymentMethod.Card.Brand;
                        }
                    }
                    _logger.LogInformation($"Payment {payment.PaymentID} confirmed successfully for booking {payment.BookingID}");
                }
                else if (intent.Status == "canceled")
                {
                    payment.Booking.PaymentStatus = "Canceled";
                    _logger.LogInformation($"Payment {payment.PaymentID} was canceled for booking {payment.BookingID}");
                }
                else
                {
                    _logger.LogInformation($"Payment {payment.PaymentID} status updated to '{intent.Status}' for booking {payment.BookingID}");
                }

                await _context.SaveChangesAsync();

                return payment;
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, $"Stripe error confirming payment intent {paymentIntentId}: {ex.Message}");
                throw new ApplicationException("Error confirming payment with Stripe", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error confirming payment intent {paymentIntentId}: {ex.Message}");
                throw;
            }
        }


        /// <summary>
        /// Processes a refund for a payment.
        /// </summary>
        /// <param name="paymentId">ID of the payment to refund</param>
        /// <param name="amount">Amount to refund</param>
        /// <param name="reason">Reason for the refund</param>
        /// <param name="adminId">ID of the admin processing the refund</param>
        /// <returns>The created refund record</returns>
        public async Task<Refund> ProcessRefund(int paymentId, decimal amount, string reason, int adminId)
        {
            _logger.LogInformation($"Processing refund of {amount} for payment {paymentId} by admin {adminId}");

            try
            {
                // Get the payment from database
                var payment = await _context.Payments
                    .Include(p => p.Booking)
                    .FirstOrDefaultAsync(p => p.PaymentID == paymentId);

                if (payment == null)
                    throw new InvalidOperationException($"Payment with ID {paymentId} not found");
                

                // Verify payment is refundable
                if (payment.Status != "succeeded")
                    throw new InvalidOperationException($"Cannot refund payment with status '{payment.Status}'. Only succeeded payments can be refunded.");
                

                // Calculate already refunded amount
                decimal alreadyRefunded = await _context.Refunds
                    .Where(r => r.PaymentID == paymentId && (r.Status == "succeeded" || r.Status == "pending"))
                    .SumAsync(r => r.Amount);

                decimal refundableAmount = payment.Amount - alreadyRefunded;

                if (amount > refundableAmount)
                    throw new InvalidOperationException($"Requested refund amount {amount} exceeds available refundable amount {refundableAmount}");
                

                // Convert amount to smallest unit (cents, etc.)
                long amountInSmallestUnit = ConvertToSmallestUnit(amount);

                // Create refund in Stripe
                var refundOptions = new RefundCreateOptions
                {
                    PaymentIntent = payment.PaymentIntentID,
                    Amount = amountInSmallestUnit,
                    Reason = ConvertToStripeRefundReason(reason),
                    Metadata = new Dictionary<string, string>
                    {
                        { "payment_id", paymentId.ToString() },
                        { "booking_id", payment.BookingID.ToString() },
                        { "reservation_number", payment.Booking.ReservationNumber },
                        { "admin_id", adminId.ToString() },
                        { "reason", reason },
                        { "timestamp", DateTime.UtcNow.ToString("o") }
                    }
                };

                var refundService = new RefundService();
                var stripeRefund = await refundService.CreateAsync(refundOptions);

                _logger.LogInformation($"Created Stripe refund {stripeRefund.Id} for payment {paymentId}");

                // Create refund record in database
                var refund = new Refund
                {
                    PaymentID = paymentId,
                    RefundIntentID = stripeRefund.Id,
                    Amount = amount,
                    Status = stripeRefund.Status,
                    Reason = reason,
                    AdminID = adminId,
                    CreatedDate = DateTime.UtcNow
                };

                _context.Refunds.Add(refund);

                // Update booking status if necessary
                decimal totalRefunded = alreadyRefunded + amount;
                if (Math.Round(totalRefunded, 2) >= Math.Round(payment.Amount, 2))
                {
                    // Full refund
                    payment.Booking.BookingStatus = "Canceled";
                    payment.Booking.PaymentStatus = "Refunded";
                    _logger.LogInformation($"Booking {payment.BookingID} status updated to Canceled/Refunded due to full refund");
                }
                else if (totalRefunded > 0)
                {
                    // Partial refund
                    payment.Booking.PaymentStatus = "Partially Refunded";
                    _logger.LogInformation($"Booking {payment.BookingID} status updated to Partially Refunded");
                }

                await _context.SaveChangesAsync();

                return refund;
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, $"Stripe error processing refund for payment {paymentId}: {ex.Message}");
                throw new ApplicationException("Error processing refund with Stripe", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing refund for payment {paymentId}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Processes a manual charge for additional services.
        /// </summary>
        /// <param name="chargeDetails">Details of the charge</param>
        /// <param name="adminId">ID of the admin processing the charge</param>
        /// <returns>The created payment record</returns>
        public async Task<Payment> ProcessManualCharge(ManualChargeDto chargeDetails, int adminId)
        {
            _logger.LogInformation($"Processing manual charge of {chargeDetails.Amount} for booking {chargeDetails.BookingID} by admin {adminId}");

            try
            {
                // Get the booking from database
                var booking = await _context.Bookings
                    .Include(b => b.User)
                    .Include(b => b.Apartment)
                    .FirstOrDefaultAsync(b => b.BookingID == chargeDetails.BookingID);

                if (booking == null)
                {
                    throw new InvalidOperationException($"Booking with ID {chargeDetails.BookingID} not found");
                }

                // Convert amount to smallest unit (cents, etc.)
                long amountInSmallestUnit = ConvertToSmallestUnit(chargeDetails.Amount);

                // Create metadata for better tracking
                var metadata = new Dictionary<string, string>
                {
                    { "booking_id", booking.BookingID.ToString() },
                    { "reservation_number", booking.ReservationNumber },
                    { "apartment_id", booking.ApartmentID.ToString() },
                    { "user_id", booking.UserID.ToString() },
                    { "admin_id", adminId.ToString() },
                    { "description", chargeDetails.Description },
                    { "is_manual_charge", "true" },
                    { "timestamp", DateTime.UtcNow.ToString("o") }
                };

                // Create a payment record in our database first
                var payment = new Payment
                {
                    BookingID = booking.BookingID,
                    Amount = chargeDetails.Amount,
                    Currency = _defaultCurrency,
                    Status = "pending", // Initial status is pending
                    PaymentMethod = "card", // Default value
                    CreatedDate = DateTime.UtcNow
                };

                _context.Payments.Add(payment);
                await _context.SaveChangesAsync();

                try
                {
                    // Get or create the Stripe customer
                    string customerId = await GetOrCreateStripeCustomerId(booking.User);

                    // Create payment intent in Stripe
                    var options = new PaymentIntentCreateOptions
                    {
                        Amount = amountInSmallestUnit,
                        Currency = _defaultCurrency,
                        Customer = customerId,
                        PaymentMethod = chargeDetails.PaymentMethodID,
                        Confirm = true,  // Confirm the payment immediately
                        OffSession = true,  // This is an off-session payment (admin initiated)
                        Metadata = metadata,
                        Description = $"Additional charge: {chargeDetails.Description} - Booking #{booking.ReservationNumber}",
                        ReceiptEmail = booking.User.Email,
                        StatementDescriptor = "ChabbyNb Charge",
                        StatementDescriptorSuffix = "Extras"
                    };

                    var service = new PaymentIntentService();
                    var intent = await service.CreateAsync(options);

                    _logger.LogInformation($"Created Stripe payment intent {intent.Id} for manual charge");

                    // Update payment record with Stripe details
                    payment.PaymentIntentID = intent.Id;
                    payment.Status = intent.Status;
                    payment.PaymentMethod = intent.PaymentMethodId ?? "card";

                    // If payment succeeded, update with additional details
                    if (intent.Status == "succeeded")
                    {
                        payment.CompletedDate = DateTime.UtcNow;

                        // Get payment method details if available
                        if (!string.IsNullOrEmpty(intent.PaymentMethodId))
                        {
                            var paymentMethodService = new PaymentMethodService();
                            var paymentMethod = await paymentMethodService.GetAsync(intent.PaymentMethodId);

                            if (paymentMethod.Card != null)
                            {
                                payment.LastFour = paymentMethod.Card.Last4;
                                payment.CardBrand = paymentMethod.Card.Brand;
                            }
                        }
                    }

                    await _context.SaveChangesAsync();
                    return payment;
                }
                catch (Exception ex)
                {
                    // If Stripe payment fails, update payment status
                    payment.Status = "failed";
                    await _context.SaveChangesAsync();

                    _logger.LogError(ex, $"Stripe error processing manual charge: {ex.Message}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing manual charge: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Gets a payment by its ID.
        /// </summary>
        /// <param name="paymentId">The payment ID</param>
        /// <returns>The payment record with related entities</returns>
        public async Task<Payment> GetPaymentById(int paymentId)
        {
            _logger.LogInformation($"Getting payment details for ID {paymentId}");

            return await _context.Payments
                .Include(p => p.Booking)
                .FirstOrDefaultAsync(p => p.PaymentID == paymentId);
        }

        /// <summary>
        /// Handles webhook events from Stripe.
        /// </summary>
        /// <param name="json">The raw JSON payload from Stripe</param>
        /// <param name="signature">The Stripe-Signature header value</param>
        /// <returns>True if the event was handled successfully</returns>
        public async Task<bool> HandleWebhookEvent(string json, string signature)
        {
            if (string.IsNullOrEmpty(json))
            {
                throw new ArgumentException("JSON payload cannot be null or empty", nameof(json));
            }

            if (string.IsNullOrEmpty(signature))
            {
                throw new ArgumentException("Stripe signature cannot be null or empty", nameof(signature));
            }

            if (string.IsNullOrEmpty(_webhookSecret))
            {
                _logger.LogError("Webhook secret is not configured");
                throw new InvalidOperationException("Stripe webhook secret is not configured");
            }

            try
            {
                // Verify and construct the event
                var stripeEvent = EventUtility.ConstructEvent(
                    json,
                    signature,
                    _webhookSecret
                );

                _logger.LogInformation($"Received Stripe webhook event of type {stripeEvent.Type}");

                // Handle specific event types
                switch (stripeEvent.Type)
                {
                    case "payment_intent.succeeded":
                        var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
                        await HandlePaymentIntentSucceeded(paymentIntent);
                        break;

                    case "payment_intent.payment_failed":
                        var failedPaymentIntent = stripeEvent.Data.Object as PaymentIntent;
                        await HandlePaymentIntentFailed(failedPaymentIntent);
                        break;

                    case "payment_intent.canceled":
                        var canceledPaymentIntent = stripeEvent.Data.Object as PaymentIntent;
                        await HandlePaymentIntentCanceled(canceledPaymentIntent);
                        break;

                    case "refund.created":
                    case "refund.updated":
                        var refund = stripeEvent.Data.Object as Stripe.Refund;
                        await HandleRefundUpdated(refund);
                        break;

                    case "charge.refunded":
                        var charge = stripeEvent.Data.Object as Charge;
                        await HandleChargeRefunded(charge);
                        break;

                    case "customer.created":
                    case "customer.updated":
                        var customer = stripeEvent.Data.Object as Customer;
                        await HandleCustomerUpdated(customer);
                        break;

                    default:
                        _logger.LogInformation($"Unhandled Stripe event type {stripeEvent.Type}");
                        break;
                }

                return true;
            }
            catch (StripeException e)
            {
                _logger.LogError(e, $"Stripe webhook error: {e.Message}");
                return false;
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Webhook error: {e.Message}");
                return false;
            }
        }

        // ----- Private Helper Methods -----

        /// <summary>
        /// Gets or creates a Stripe customer ID for a user.
        /// </summary>
        /// <param name="user">The user to create a customer for</param>
        /// <returns>The Stripe customer ID</returns>
        private async Task<string> GetOrCreateStripeCustomerId(User user)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            try
            {
                // Check if we already have a Stripe customer ID for this user
                // In a real implementation, you'd want to add a StripeCustomerId field to your User model
                // For this implementation, we'll search for the customer in Stripe

                var customerService = new CustomerService();
                var customers = await customerService.ListAsync(new CustomerListOptions
                {
                    Email = user.Email,
                    Limit = 1
                });

                if (customers.Data.Any())
                {
                    _logger.LogInformation($"Found existing Stripe customer for user {user.UserID}: {customers.Data.First().Id}");
                    return customers.Data.First().Id;
                }

                // No existing customer found, create a new one
                var customerOptions = new CustomerCreateOptions
                {
                    Email = user.Email,
                    Name = string.IsNullOrEmpty(user.FirstName) && string.IsNullOrEmpty(user.LastName)
                        ? user.Username
                        : $"{user.FirstName} {user.LastName}".Trim(),
                    Phone = user.PhoneNumber,
                    Metadata = new Dictionary<string, string>
                    {
                        { "user_id", user.UserID.ToString() },
                        { "app_username", user.Username },
                        { "created_at", DateTime.UtcNow.ToString("o") }
                    }
                };

                var customer = await customerService.CreateAsync(customerOptions);
                _logger.LogInformation($"Created new Stripe customer for user {user.UserID}: {customer.Id}");

                // In a real implementation, you'd want to save this customer ID to your user record
                // user.StripeCustomerId = customer.Id;
                // await _context.SaveChangesAsync();

                return customer.Id;
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, $"Stripe error creating/retrieving customer for user {user.UserID}: {ex.Message}");
                throw new ApplicationException("Error creating Stripe customer", ex);
            }
        }

        private long ConvertToSmallestUnit(decimal amount)
        {
            // Stripe requires amounts in the smallest currency unit (cents for USD)
            return Convert.ToInt64(amount * 100);
        }

        private string ConvertToStripeRefundReason(string reason)
        {
            // Stripe only accepts these specific refund reasons
            return reason?.ToLower() switch
            {
                "requested_by_customer" => "requested_by_customer",
                "fraudulent" => "fraudulent",
                "duplicate" => "duplicate",
                _ => "requested_by_customer"  // Default
            };
        }

        private async Task HandlePaymentIntentSucceeded(PaymentIntent paymentIntent)
        {
            if (paymentIntent == null)
            {
                _logger.LogWarning("Received null payment intent in HandlePaymentIntentSucceeded");
                return;
            }

            _logger.LogInformation($"Processing payment intent succeeded event for {paymentIntent.Id}");

            var payment = await _context.Payments
                .Include(p => p.Booking)
                .FirstOrDefaultAsync(p => p.PaymentIntentID == paymentIntent.Id);

            if (payment == null)
            {
                _logger.LogWarning($"Payment not found for payment intent {paymentIntent.Id}");
                return;
            }

            payment.Status = paymentIntent.Status;
            payment.CompletedDate = DateTime.UtcNow;

            // Update the booking status
            payment.Booking.PaymentStatus = "Paid";
            payment.Booking.BookingStatus = "Confirmed";

            // Get payment method details
            if (!string.IsNullOrEmpty(paymentIntent.PaymentMethodId))
            {
                var paymentMethodService = new PaymentMethodService();
                var paymentMethod = await paymentMethodService.GetAsync(paymentIntent.PaymentMethodId);

                payment.PaymentMethod = paymentIntent.PaymentMethodId;

                if (paymentMethod.Card != null)
                {
                    payment.LastFour = paymentMethod.Card.Last4;
                    payment.CardBrand = paymentMethod.Card.Brand;
                }
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation($"Updated payment {payment.PaymentID} status to {payment.Status} for booking {payment.BookingID}");
        }

        private async Task HandlePaymentIntentFailed(PaymentIntent paymentIntent)
        {
            if (paymentIntent == null)
            {
                _logger.LogWarning("Received null payment intent in HandlePaymentIntentFailed");
                return;
            }

            _logger.LogInformation($"Processing payment intent failed event for {paymentIntent.Id}");

            var payment = await _context.Payments
                .Include(p => p.Booking)
                .FirstOrDefaultAsync(p => p.PaymentIntentID == paymentIntent.Id);

            if (payment == null)
            {
                _logger.LogWarning($"Payment not found for payment intent {paymentIntent.Id}");
                return;
            }

            payment.Status = paymentIntent.Status;

            // Update the booking status
            payment.Booking.PaymentStatus = "Failed";

            await _context.SaveChangesAsync();
            _logger.LogInformation($"Updated payment {payment.PaymentID} status to Failed for booking {payment.BookingID}");
        }

        private async Task HandlePaymentIntentCanceled(PaymentIntent paymentIntent)
        {
            if (paymentIntent == null)
            {
                _logger.LogWarning("Received null payment intent in HandlePaymentIntentCanceled");
                return;
            }

            _logger.LogInformation($"Processing payment intent canceled event for {paymentIntent.Id}");

            var payment = await _context.Payments
                .Include(p => p.Booking)
                .FirstOrDefaultAsync(p => p.PaymentIntentID == paymentIntent.Id);

            if (payment == null)
            {
                _logger.LogWarning($"Payment not found for payment intent {paymentIntent.Id}");
                return;
            }

            payment.Status = paymentIntent.Status;

            // Update the booking status
            payment.Booking.PaymentStatus = "Canceled";

            await _context.SaveChangesAsync();
            _logger.LogInformation($"Updated payment {payment.PaymentID} status to Canceled for booking {payment.BookingID}");
        }

        private async Task HandleRefundUpdated(Stripe.Refund stripeRefund)
        {
            if (stripeRefund == null)
            {
                _logger.LogWarning("Received null refund in HandleRefundUpdated");
                return;
            }

            _logger.LogInformation($"Processing refund update event for refund {stripeRefund.Id} with status {stripeRefund.Status}");

            var refund = await _context.Refunds
                .Include(r => r.Payment)
                    .ThenInclude(p => p.Booking)
                .FirstOrDefaultAsync(r => r.RefundIntentID == stripeRefund.Id);

            if (refund == null)
            {
                _logger.LogWarning($"Refund not found in database for Stripe refund {stripeRefund.Id}");
                return;
            }

            refund.Status = stripeRefund.Status;

            if (stripeRefund.Status == "succeeded")
            {
                refund.CompletedDate = DateTime.UtcNow;

                // Calculate total refunded amount for this payment
                var totalRefunded = await _context.Refunds
                    .Where(r => r.PaymentID == refund.PaymentID && r.Status == "succeeded")
                    .SumAsync(r => r.Amount) + refund.Amount;

                // Update booking status if fully refunded
                if (Math.Round(totalRefunded, 2) >= Math.Round(refund.Payment.Amount, 2))
                {
                    refund.Payment.Booking.BookingStatus = "Canceled";
                    refund.Payment.Booking.PaymentStatus = "Refunded";
                    _logger.LogInformation($"Booking {refund.Payment.BookingID} status updated to Canceled/Refunded");
                }
                else
                {
                    refund.Payment.Booking.PaymentStatus = "Partially Refunded";
                    _logger.LogInformation($"Booking {refund.Payment.BookingID} status updated to Partially Refunded");
                }
            }
            else if (stripeRefund.Status == "failed")
            {
                _logger.LogWarning($"Refund {refund.RefundID} failed for payment {refund.PaymentID}");
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation($"Updated refund {refund.RefundID} status to {refund.Status}");
        }

        private async Task HandleChargeRefunded(Charge charge)
        {
            if (charge == null)
            {
                _logger.LogWarning("Received null charge in HandleChargeRefunded");
                return;
            }

            _logger.LogInformation($"Processing charge refunded event for charge {charge.Id}");

            // Find the associated payment using payment intent ID
            var payment = await _context.Payments
                .Include(p => p.Booking)
                .FirstOrDefaultAsync(p => p.PaymentIntentID == charge.PaymentIntentId);

            if (payment == null)
            {
                _logger.LogWarning($"No payment found for charge {charge.Id} with payment intent {charge.PaymentIntentId}");
                return;
            }

            // Check if the charge was fully refunded
            bool isFullyRefunded = charge.AmountRefunded == charge.Amount;

            if (isFullyRefunded)
            {
                payment.Booking.BookingStatus = "Canceled";
                payment.Booking.PaymentStatus = "Refunded";
                _logger.LogInformation($"Booking {payment.BookingID} status updated to Canceled/Refunded due to full charge refund");
            }
            else if (charge.AmountRefunded > 0)
            {
                payment.Booking.PaymentStatus = "Partially Refunded";
                _logger.LogInformation($"Booking {payment.BookingID} status updated to Partially Refunded due to partial charge refund");
            }

            await _context.SaveChangesAsync();
        }


        private async Task HandleCustomerUpdated(Customer customer)
        {
            if (customer == null)
            {
                _logger.LogWarning("Received null customer in HandleCustomerUpdated");
                return;
            }

            _logger.LogInformation($"Processing customer updated event for customer {customer.Id}");

            // In a real implementation, you'd update the StripeCustomerId field in your User model
            // For this implementation, we'll just log the event
            if (customer.Metadata.TryGetValue("user_id", out string userIdStr) && int.TryParse(userIdStr, out int userId))
            {
                var user = await _context.Users.FindAsync(userId);
                if (user != null)
                {
                    // Example: user.StripeCustomerId = customer.Id;
                    // await _context.SaveChangesAsync();
                    _logger.LogInformation($"Customer {customer.Id} updated for user {userId}");
                }
                else
                {
                    _logger.LogWarning($"User {userId} not found for Stripe customer {customer.Id}");
                }
            }
            else
            {
                _logger.LogWarning($"No user_id found in metadata for Stripe customer {customer.Id}");
            }
        }

    }

}