using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ChabbyNb_API.Models.DTOs
{
    public class FinancialOverviewDto
    {
        public decimal TotalRevenue { get; set; }
        public decimal PendingRevenue { get; set; }
        public decimal RefundedAmount { get; set; }
        public int TotalTransactions { get; set; }
        public int PendingTransactions { get; set; }
        public int CompletedTransactions { get; set; }
        public int FailedTransactions { get; set; }
        public int RefundedTransactions { get; set; }
        public List<MonthlyRevenueDto> MonthlyRevenue { get; set; }
    }

    public class MonthlyRevenueDto
    {
        public string Month { get; set; }
        public decimal Revenue { get; set; }
        public decimal RefundedAmount { get; set; }
        public int Transactions { get; set; }
    }

    public class PaymentListItemDto
    {
        public int PaymentID { get; set; }
        public int BookingID { get; set; }
        public string ReservationNumber { get; set; }
        public string GuestName { get; set; }
        public string ApartmentTitle { get; set; }
        public DateTime CheckInDate { get; set; }
        public DateTime CheckOutDate { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public string Status { get; set; }
        public string PaymentMethod { get; set; }
        public DateTime CreatedDate { get; set; }
        public bool IsRefundable { get; set; }
    }

    public class PaymentDetailsDto : PaymentListItemDto
    {
        public string PaymentIntentID { get; set; }
        public string LastFour { get; set; }
        public string CardBrand { get; set; }
        public DateTime? CompletedDate { get; set; }
        public List<RefundDto> Refunds { get; set; }
        public decimal RefundableAmount { get; set; }
    }

    public class RefundDto
    {
        public int RefundID { get; set; }
        public string RefundIntentID { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; }
        public string Reason { get; set; }
        public string AdminName { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? CompletedDate { get; set; }
    }

    public class ProcessRefundDto
    {
        public int PaymentID { get; set; }

        [Required]
        [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
        public decimal Amount { get; set; }

        [Required]
        public string Reason { get; set; }
    }

    public class ManualChargeDto
    {
        [Required]
        public int BookingID { get; set; }

        [Required]
        [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
        public decimal Amount { get; set; }

        [Required]
        public string Description { get; set; }

        [Required]
        public string PaymentMethodID { get; set; }
    }
}