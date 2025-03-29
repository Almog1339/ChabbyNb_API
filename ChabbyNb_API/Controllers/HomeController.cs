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

namespace ChabbyNb_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HomeController : ControllerBase
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ChabbyNbDbContext _context;
        private readonly IConfiguration _configuration;

        public HomeController(ILogger<HomeController> logger, ChabbyNbDbContext context, IConfiguration configuration)
        {
            _logger = logger;
            _context = context;
            _configuration = configuration;
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

            // Get SMTP settings from configuration
            var smtpSettings = _configuration.GetSection("SmtpSettings");

            // Check if we should send real emails
            if (!_configuration.GetValue<bool>("SendRealEmails", false))
            {
                // For development, just log the email
                Console.WriteLine($"Contact notification email would be sent to: {adminEmail}");
                Console.WriteLine($"Subject: New Contact Message: {message.Subject}");
                Console.WriteLine($"From: {message.Name} ({message.Email})");
                Console.WriteLine($"Message: {message.Message}");
                return;
            }

            // Prepare email message
            string subject = $"New Contact Message: {message.Subject}";
            string body = $@"
        <html>
        <head>
            <style>
                body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
                .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
                .header {{ background-color: #ff5a5f; padding: 20px; color: white; text-align: center; }}
                .content {{ padding: 20px; }}
                .message-details {{ background-color: #f8f8f8; padding: 15px; margin: 20px 0; border-radius: 5px; }}
                .footer {{ text-align: center; margin-top: 20px; font-size: 12px; color: #666; }}
            </style>
        </head>
        <body>
            <div class='container'>
                <div class='header'>
                    <h1>New Contact Message</h1>
                </div>
                <div class='content'>
                    <p>You have received a new contact message from your ChabbyNb website.</p>
                    
                    <div class='message-details'>
                        <p><strong>From:</strong> {message.Name} ({message.Email})</p>
                        <p><strong>Subject:</strong> {message.Subject}</p>
                        <p><strong>User Account:</strong> {(message.UserID.HasValue ? "Registered User" : "Guest")}</p>
                        <p><strong>Time:</strong> {message.CreatedDate}</p>
                        <p><strong>Message:</strong></p>
                        <p>{message.Message}</p>
                    </div>
                    
                    <p>To respond to this message, please log in to your admin panel.</p>
                </div>
                <div class='footer'>
                    <p>© 2025 ChabbyNb. All rights reserved.</p>
                    <p>25 Adrianou St, Athens, Greece</p>
                </div>
            </div>
        </body>
        </html>";

            // Configure and send email
            using (var client = new SmtpClient())
            {
                // Set up the SMTP client
                client.Host = smtpSettings["Host"];
                client.Port = int.Parse(smtpSettings["Port"] ?? "587");
                client.EnableSsl = bool.Parse(smtpSettings["EnableSsl"] ?? "true");
                client.DeliveryMethod = SmtpDeliveryMethod.Network;
                client.UseDefaultCredentials = false;

                // Make sure credentials are correctly set
                string username = smtpSettings["Username"];
                string password = smtpSettings["Password"];

                if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                {
                    throw new InvalidOperationException("SMTP username or password is not configured.");
                }

                client.Credentials = new NetworkCredential(username, password);

                // Create the email message
                using (var emailMessage = new MailMessage())
                {
                    emailMessage.From = new MailAddress(smtpSettings["FromEmail"], "ChabbyNb Contact Form");
                    emailMessage.Subject = subject;
                    emailMessage.Body = body;
                    emailMessage.IsBodyHtml = true;
                    emailMessage.To.Add(new MailAddress(adminEmail));

                    // Add reply-to header so admin can reply directly to the sender
                    emailMessage.ReplyToList.Add(new MailAddress(message.Email, message.Name));

                    await client.SendMailAsync(emailMessage);
                }
            }
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