using System;

namespace ChabbyNb_API.Models.DTOs
{
    public class AdminMessageDto
    {
        public string Subject { get; set; }
        public string Message { get; set; }
    }

    public class AdminDashboardDto
    {
        public int TotalApartments { get; set; }
        public int TotalBookings { get; set; }
        public int TotalUsers { get; set; }
        public decimal TotalRevenue { get; set; }
    }
}