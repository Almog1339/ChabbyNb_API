using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using ChabbyNb_API.Services;

public class BookingExpirationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BookingExpirationService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);

    public BookingExpirationService(
        IServiceProvider serviceProvider,
        ILogger<BookingExpirationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Booking Expiration Service running");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckForExpiredBookings();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while checking for expired bookings");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    private async Task CheckForExpiredBookings()
    {
        using (var scope = _serviceProvider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ChabbyNbDbContext>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

            var cutoffTime = DateTime.Now.AddMinutes(-10);

            var pendingBookings = await dbContext.Bookings
                .Include(b => b.User)
                .Include(b => b.Apartment)
                .Where(b =>
                    b.BookingStatus == "Pending" &&
                    b.PaymentStatus == "Pending" &&
                    b.CreatedDate < cutoffTime)
                .ToListAsync();

            foreach (var booking in pendingBookings)
            {
                booking.BookingStatus = "Canceled";
                booking.PaymentStatus = "Expired";

                // Send notification email
                try
                {
                    await SendBookingExpiredEmail(booking, emailService);
                    _logger.LogInformation($"Sent expiration email for booking {booking.BookingID}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error sending expiration email for booking {booking.BookingID}");
                }
            }

            if (pendingBookings.Any())
            {
                await dbContext.SaveChangesAsync();
                _logger.LogInformation($"Expired {pendingBookings.Count} pending bookings");
            }
        }
    }

    private async Task SendBookingExpiredEmail(Booking booking, IEmailService emailService)
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

        await emailService.SendEmailAsync(
            booking.User.Email,
            "Your ChabbyNb Booking Reservation Expired",
            "BookingExpired",
            model
        );
    }
}