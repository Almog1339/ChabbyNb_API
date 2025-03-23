using System;
using System.ComponentModel.DataAnnotations;

namespace ChabbyNb_API.Models
{
    public class AdminLog
    {
        [Key]
        public int LogID { get; set; }

        public int AdminID { get; set; }

        public string AdminEmail { get; set; }

        public string Action { get; set; }

        public DateTime Timestamp { get; set; }

        public string IPAddress { get; set; }
    }
}