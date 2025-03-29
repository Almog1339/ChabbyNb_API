using System.Diagnostics;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Net.Mail;
using System.Net;
using ChabbyNb_API.Services;

namespace ChabbyNb_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HomeController : ControllerBase
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ChabbyNbDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IEmailService _emailService;

        public HomeController(ILogger<HomeController> logger,ChabbyNbDbContext context,IConfiguration configuration,IEmailService emailService)
        {
            _logger = logger;
            _context = context;
            _configuration = configuration;
            _emailService = emailService;
        }

        // GET: api/Home
        [HttpGet]
        public IActionResult Index()
        {
            return Ok(new { message = "Welcome to ChabbyNb API" });
        }

        // GET: api/Home/About
        [HttpGet("About")]
        public IActionResult About()
        {
            return Ok(new { message = "About ChabbyNb" });
        }

        // GET: api/Home/Contact
        [HttpGet("Contact")]
        public IActionResult Contact()
        {
            return Ok(new { message = "Contact ChabbyNb" });
        }

        // POST: api/Home/Contact
        [HttpPost("Contact")]
        public async Task<IActionResult> Contact([FromBody] ContactViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Create a new contact message
            var contactMessage = new ContactMessage
            {
                Name = model.Name,
                Email = model.Email,
                Subject = model.Subject,
                Message = model.Message,
                CreatedDate = DateTime.Now,
                IsRead = false,
                Status = "New"
            };

            // Check if this is a registered user
            int? userId = null;

            if (User.Identity.IsAuthenticated)
            {
                // If authenticated, get the user ID from claims
                userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                contactMessage.UserID = userId;

                // Get the user to fill in any missing details
                var user = await _context.Users.FindAsync(userId);
                if (user != null)
                {
                    // If name is not provided but user has a name, use it
                    if (string.IsNullOrWhiteSpace(contactMessage.Name) &&
                        (!string.IsNullOrWhiteSpace(user.FirstName) || !string.IsNullOrWhiteSpace(user.LastName)))
                    {
                        contactMessage.Name = $"{user.FirstName} {user.LastName}".Trim();
                    }
                }
            }
            else
            {
                // If not authenticated, try to find user by email
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == model.Email);
                if (user != null)
                {
                    contactMessage.UserID = user.UserID;
                }
            }

            // Add to database
            _context.ContactMessages.Add(contactMessage);
            await _context.SaveChangesAsync();

            // Send notification email to admin
            try
            {
                await SendContactNotificationEmail(contactMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send contact notification email");
                // Continue processing - don't fail the request just because email failed
            }

            return Ok(new { success = true, message = "Message sent successfully. We'll get back to you shortly." });
        }

        // GET: api/Home/Privacy
        [HttpGet("Privacy")]
        public IActionResult Privacy()
        {
            return Ok(new { message = "Privacy Policy" });
        }

        // GET: api/Home/Terms
        [HttpGet("Terms")]
        public IActionResult Terms()
        {
            return Ok(new { message = "Terms of Service" });
        }

        // GET: api/Home/Search
        [HttpGet("Search")]
        public IActionResult Search([FromQuery] string query)
        {
            return Ok(new { message = $"Search results for: {query}" });
        }

        [HttpGet("Error")]
        public IActionResult Error()
        {
            return StatusCode(500, new { message = "An error occurred", requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        private async Task SendContactNotificationEmail(ContactMessage message)
        {
            // Get admin email from configuration
            string adminEmail = _configuration["AdminEmail"];
            if (string.IsNullOrEmpty(adminEmail))
            {
                throw new InvalidOperationException("Admin email is not configured");
            }

            var model = new
            {
                SenderName = message.Name,
                SenderEmail = message.Email,
                Subject = message.Subject,
                MessageContent = message.Message,
                HasUserAccount = message.UserID.HasValue ? "Yes" : "No",
                Timestamp = message.CreatedDate.ToString()
            };

            await _emailService.SendEmailAsync(
                adminEmail,
                $"New Contact Message: {message.Subject}",
                "ContactNotification",
                model
            );
        }
    }

    public class ContactViewModel
    {
        [Required(ErrorMessage = "Name is required")]
        [StringLength(100, ErrorMessage = "Name cannot be longer than 100 characters")]
        public string Name { get; set; }

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email address")]
        public string Email { get; set; }

        [Required(ErrorMessage = "Subject is required")]
        [StringLength(100, ErrorMessage = "Subject cannot be longer than 100 characters")]
        public string Subject { get; set; }

        [Required(ErrorMessage = "Message is required")]
        public string Message { get; set; }
    }

    public class ErrorViewModel
    {
        public string RequestId { get; set; }

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }
}