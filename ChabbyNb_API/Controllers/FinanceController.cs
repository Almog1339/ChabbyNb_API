using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using ChabbyNb_API.Models.DTOs;
using ChabbyNb_API.Services.Iterfaces;

namespace ChabbyNb_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "RequireAdminRole")]
    public class FinanceController : ControllerBase
    {
        private readonly ChabbyNbDbContext _context;
        private readonly IPaymentService _paymentService;

        public FinanceController(
            ChabbyNbDbContext context,
            IPaymentService paymentService)
        {
            _context = context;
            _paymentService = paymentService;
        }

        // GET: api/Finance/Dashboard
        [HttpGet("Dashboard")]
        public async Task<ActionResult<FinancialOverviewDto>> GetDashboard([FromQuery] int? year)
        {
            int currentYear = year ?? DateTime.Now.Year;

            // Get all payments
            var payments = await _context.Payments
                .Include(p => p.Booking)
                .ToListAsync();

            // Calculate financial overview
            var overview = new FinancialOverviewDto
            {
                TotalRevenue = payments.Where(p => p.Status == "succeeded").Sum(p => p.Amount),
                PendingRevenue = payments.Where(p => p.Status == "pending").Sum(p => p.Amount),
                RefundedAmount = _context.Refunds.Where(r => r.Status == "succeeded").Sum(r => r.Amount),
                TotalTransactions = payments.Count,
                PendingTransactions = payments.Count(p => p.Status == "pending"),
                CompletedTransactions = payments.Count(p => p.Status == "succeeded"),
                FailedTransactions = payments.Count(p => p.Status == "failed"),
                RefundedTransactions = _context.Refunds.Count(r => r.Status == "succeeded"),
                MonthlyRevenue = new List<MonthlyRevenueDto>()
            };

            // Calculate monthly revenue for the selected year
            var monthlyData = payments
                .Where(p => p.CreatedDate.Year == currentYear)
                .GroupBy(p => new { Month = p.CreatedDate.Month })
                .Select(g => new
                {
                    Month = g.Key.Month,
                    Revenue = g.Where(p => p.Status == "succeeded").Sum(p => p.Amount),
                    Transactions = g.Count()
                })
                .OrderBy(x => x.Month)
                .ToList();

            // Calculate monthly refunds
            var monthlyRefunds = await _context.Refunds
                .Where(r => r.CreatedDate.Year == currentYear)
                .GroupBy(r => new { Month = r.CreatedDate.Month })
                .Select(g => new
                {
                    Month = g.Key.Month,
                    RefundedAmount = g.Where(r => r.Status == "succeeded").Sum(r => r.Amount)
                })
                .OrderBy(x => x.Month)
                .ToListAsync();

            // Create monthly revenue DTOs
            for (int month = 1; month <= 12; month++)
            {
                var monthData = monthlyData.FirstOrDefault(m => m.Month == month);
                var monthRefunds = monthlyRefunds.FirstOrDefault(m => m.Month == month);

                overview.MonthlyRevenue.Add(new MonthlyRevenueDto
                {
                    Month = new DateTime(currentYear, month, 1).ToString("MMM"),
                    Revenue = monthData?.Revenue ?? 0,
                    RefundedAmount = monthRefunds?.RefundedAmount ?? 0,
                    Transactions = monthData?.Transactions ?? 0
                });
            }

            return overview;
        }

        // GET: api/Finance/Payments
        [HttpGet("Payments")]
        public async Task<ActionResult<IEnumerable<PaymentListItemDto>>> GetPayments(
            [FromQuery] string status = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var query = _context.Payments
                .Include(p => p.Booking)
                    .ThenInclude(b => b.User)
                .Include(p => p.Booking)
                    .ThenInclude(b => b.Apartment)
                .AsQueryable();

            // Apply filters
            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(p => p.Status == status);
            }

            if (fromDate.HasValue)
            {
                query = query.Where(p => p.CreatedDate >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(p => p.CreatedDate <= toDate.Value.AddDays(1));
            }

            // Calculate total count for pagination
            var totalCount = await query.CountAsync();

            // Apply pagination
            var payments = await query
                .OrderByDescending(p => p.CreatedDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new PaymentListItemDto
                {
                    PaymentID = p.PaymentID,
                    BookingID = p.BookingID,
                    ReservationNumber = p.Booking.ReservationNumber,
                    GuestName = $"{p.Booking.User.FirstName} {p.Booking.User.LastName}",
                    ApartmentTitle = p.Booking.Apartment.Title,
                    CheckInDate = p.Booking.CheckInDate,
                    CheckOutDate = p.Booking.CheckOutDate,
                    Amount = p.Amount,
                    Currency = p.Currency,
                    Status = p.Status,
                    PaymentMethod = p.PaymentMethod,
                    CreatedDate = p.CreatedDate,
                    IsRefundable = p.Status == "succeeded" && p.Booking.CheckInDate > DateTime.Now
                })
                .ToListAsync();

            // Set pagination headers
            Response.Headers.Add("X-Total-Count", totalCount.ToString());
            Response.Headers.Add("X-Total-Pages", Math.Ceiling((double)totalCount / pageSize).ToString());

            return payments;
        }

        // GET: api/Finance/Payments/{id}
        [HttpGet("Payments/{id}")]
        public async Task<ActionResult<PaymentDetailsDto>> GetPaymentDetails(int id)
        {
            var payment = await _context.Payments
                .Include(p => p.Booking)
                    .ThenInclude(b => b.User)
                .Include(p => p.Booking)
                    .ThenInclude(b => b.Apartment)
                .FirstOrDefaultAsync(p => p.PaymentID == id);

            if (payment == null)
            {
                return NotFound();
            }

            // Get refunds for this payment
            var refunds = await _context.Refunds
                .Include(r => r.Admin)
                .Where(r => r.PaymentID == id)
                .Select(r => new RefundDto
                {
                    RefundID = r.RefundID,
                    RefundIntentID = r.RefundIntentID,
                    Amount = r.Amount,
                    Status = r.Status,
                    Reason = r.Reason,
                    AdminName = $"{r.Admin.FirstName} {r.Admin.LastName}",
                    CreatedDate = r.CreatedDate,
                    CompletedDate = r.CompletedDate
                })
                .ToListAsync();

            // Calculate refundable amount
            decimal refundedAmount = refunds
                .Where(r => r.Status == "succeeded" || r.Status == "pending")
                .Sum(r => r.Amount);

            decimal refundableAmount = payment.Status == "succeeded" ?
                payment.Amount - refundedAmount : 0;

            var details = new PaymentDetailsDto
            {
                PaymentID = payment.PaymentID,
                BookingID = payment.BookingID,
                ReservationNumber = payment.Booking.ReservationNumber,
                GuestName = $"{payment.Booking.User.FirstName} {payment.Booking.User.LastName}",
                ApartmentTitle = payment.Booking.Apartment.Title,
                CheckInDate = payment.Booking.CheckInDate,
                CheckOutDate = payment.Booking.CheckOutDate,
                Amount = payment.Amount,
                Currency = payment.Currency,
                Status = payment.Status,
                PaymentMethod = payment.PaymentMethod,
                CreatedDate = payment.CreatedDate,
                PaymentIntentID = payment.PaymentIntentID,
                LastFour = payment.LastFour,
                CardBrand = payment.CardBrand,
                CompletedDate = payment.CompletedDate,
                Refunds = refunds,
                RefundableAmount = refundableAmount,
                IsRefundable = payment.Status == "succeeded" &&
                              payment.Booking.CheckInDate > DateTime.Now &&
                              refundableAmount > 0
            };

            return details;
        }

        // POST: api/Finance/Refunds
        [HttpPost("Refunds")]
        public async Task<ActionResult<RefundDto>> ProcessRefund([FromBody] ProcessRefundDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var payment = await _context.Payments
                .Include(p => p.Booking)
                .FirstOrDefaultAsync(p => p.PaymentID == model.PaymentID);

            if (payment == null)
            {
                return NotFound();
            }

            // Verify payment is refundable
            if (payment.Status != "succeeded")
            {
                return BadRequest(new { error = "Only successful payments can be refunded." });
            }

            // Calculate total refunded amount
            decimal refundedAmount = await _context.Refunds
                .Where(r => r.PaymentID == model.PaymentID && (r.Status == "succeeded" || r.Status == "pending"))
                .SumAsync(r => r.Amount);

            decimal refundableAmount = payment.Amount - refundedAmount;

            if (model.Amount > refundableAmount)
            {
                return BadRequest(new { error = $"Maximum refundable amount is {refundableAmount} {payment.Currency}." });
            }

            try
            {
                // Get admin ID
                int adminId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

                // Process the refund through payment service
                var refund = await _paymentService.ProcessRefund(model.PaymentID, model.Amount, model.Reason, adminId);

                // Update booking status if full refund
                if (refundedAmount + model.Amount >= payment.Amount)
                {
                    payment.Booking.BookingStatus = "Canceled";
                    payment.Booking.PaymentStatus = "Refunded";
                }
                else if (refundedAmount + model.Amount > 0)
                {
                    payment.Booking.PaymentStatus = "Partially Refunded";
                }

                await _context.SaveChangesAsync();

                // Create response DTO
                var admin = await _context.Users.FindAsync(adminId);
                var refundDto = new RefundDto
                {
                    RefundID = refund.RefundID,
                    RefundIntentID = refund.RefundIntentID,
                    Amount = refund.Amount,
                    Status = refund.Status,
                    Reason = refund.Reason,
                    AdminName = $"{admin.FirstName} {admin.LastName}",
                    CreatedDate = refund.CreatedDate,
                    CompletedDate = refund.CompletedDate
                };

                return Ok(refundDto);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Failed to process refund: {ex.Message}" });
            }
        }

        // POST: api/Finance/ManualCharge
        [HttpPost("ManualCharge")]
        public async Task<ActionResult<PaymentListItemDto>> ProcessManualCharge([FromBody] ManualChargeDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var booking = await _context.Bookings
                .Include(b => b.User)
                .Include(b => b.Apartment)
                .FirstOrDefaultAsync(b => b.BookingID == model.BookingID);

            if (booking == null)
            {
                return NotFound();
            }

            try
            {
                // Get admin ID
                int adminId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

                // Process the charge
                var payment = await _paymentService.ProcessManualCharge(model, adminId);

                // Create response DTO
                var paymentDto = new PaymentListItemDto
                {
                    PaymentID = payment.PaymentID,
                    BookingID = payment.BookingID,
                    ReservationNumber = booking.ReservationNumber,
                    GuestName = $"{booking.User.FirstName} {booking.User.LastName}",
                    ApartmentTitle = booking.Apartment.Title,
                    CheckInDate = booking.CheckInDate,
                    CheckOutDate = booking.CheckOutDate,
                    Amount = payment.Amount,
                    Currency = payment.Currency,
                    Status = payment.Status,
                    PaymentMethod = payment.PaymentMethod,
                    CreatedDate = payment.CreatedDate,
                    IsRefundable = payment.Status == "succeeded" && booking.CheckInDate > DateTime.Now
                };

                return Ok(paymentDto);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Failed to process charge: {ex.Message}" });
            }
        }

        // GET: api/Finance/Refunds
        [HttpGet("Refunds")]
        public async Task<ActionResult<IEnumerable<RefundDto>>> GetRefunds(
            [FromQuery] string status = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var query = _context.Refunds
                .Include(r => r.Payment)
                    .ThenInclude(p => p.Booking)
                .Include(r => r.Admin)
                .AsQueryable();

            // Apply filters
            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(r => r.Status == status);
            }

            if (fromDate.HasValue)
            {
                query = query.Where(r => r.CreatedDate >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(r => r.CreatedDate <= toDate.Value.AddDays(1));
            }

            // Calculate total count for pagination
            var totalCount = await query.CountAsync();

            // Apply pagination
            var refunds = await query
                .OrderByDescending(r => r.CreatedDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(r => new RefundDto
                {
                    RefundID = r.RefundID,
                    RefundIntentID = r.RefundIntentID,
                    Amount = r.Amount,
                    Status = r.Status,
                    Reason = r.Reason,
                    AdminName = $"{r.Admin.FirstName} {r.Admin.LastName}",
                    CreatedDate = r.CreatedDate,
                    CompletedDate = r.CompletedDate
                })
                .ToListAsync();

            // Set pagination headers
            Response.Headers.Add("X-Total-Count", totalCount.ToString());
            Response.Headers.Add("X-Total-Pages", Math.Ceiling((double)totalCount / pageSize).ToString());

            return refunds;
        }
    }
}