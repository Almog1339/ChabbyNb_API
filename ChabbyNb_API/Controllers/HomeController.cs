using System.Diagnostics;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using ChabbyNb_API.Data;

namespace ChabbyNb_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HomeController : ControllerBase
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ChabbyNbDbContext _context;

        public HomeController(ILogger<HomeController> logger, ChabbyNbDbContext context)
        {
            _logger = logger;
            _context = context;
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
        public IActionResult Contact([FromBody] ContactViewModel model)
        {
            if (ModelState.IsValid)
            {
                // In a real application, you would send the message
                return Ok(new { success = true, message = "Message sent successfully" });
            }

            return BadRequest(ModelState);
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