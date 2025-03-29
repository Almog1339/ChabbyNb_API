using Azure;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mail;
using System.Net;
using ChabbyNb_API.Models.DTOs;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "RequireAdminRole")]
public class ContactMessagesController : ControllerBase
{
    private readonly ChabbyNbDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ContactMessagesController> _logger;

    public ContactMessagesController(
        ChabbyNbDbContext context,
        IConfiguration configuration,
        ILogger<ContactMessagesController> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    // GET: api/ContactMessages
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ContactMessageDto>>> GetContactMessages(
        [FromQuery] string status = null,
        [FromQuery] bool? isRead = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var query = _context.ContactMessages
            .Include(c => c.User)
            .AsQueryable();

        // Apply filters
        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(c => c.Status == status);
        }

        if (isRead.HasValue)
        {
            query = query.Where(c => c.IsRead == isRead.Value);
        }

        // Get total count for pagination
        var totalCount = await query.CountAsync();

        // Get messages with pagination
        var messages = await query
            .OrderByDescending(c => c.CreatedDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new ContactMessageDto
            {
                ContactMessageID = c.ContactMessageID,
                Name = c.Name,
                Email = c.Email,
                Subject = c.Subject,
                Message = c.Message,
                UserID = c.UserID,
                UserName = c.User != null ? $"{c.User.FirstName} {c.User.LastName}".Trim() : null,
                IsRegisteredUser = c.UserID.HasValue,
                CreatedDate = c.CreatedDate,
                IsRead = c.IsRead,
                ReadDate = c.ReadDate,
                Status = c.Status,
                AdminNotes = c.AdminNotes
            })
            .ToListAsync();

        // Set pagination headers
        Response.Headers.Add("X-Total-Count", totalCount.ToString());
        Response.Headers.Add("X-Total-Pages", Math.Ceiling((double)totalCount / pageSize).ToString());

        return messages;
    }

    // GET: api/ContactMessages/5
    [HttpGet("{id}")]
    public async Task<ActionResult<ContactMessageDto>> GetContactMessage(int id)
    {
        var message = await _context.ContactMessages
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.ContactMessageID == id);

        if (message == null)
        {
            return NotFound();
        }

        // Mark as read if not already read
        if (!message.IsRead)
        {
            message.IsRead = true;
            message.ReadDate = DateTime.Now;
            message.Status = message.Status == "New" ? "Read" : message.Status;
            await _context.SaveChangesAsync();
        }

        return new ContactMessageDto
        {
            ContactMessageID = message.ContactMessageID,
            Name = message.Name,
            Email = message.Email,
            Subject = message.Subject,
            Message = message.Message,
            UserID = message.UserID,
            UserName = message.User != null ? $"{message.User.FirstName} {message.User.LastName}".Trim() : null,
            IsRegisteredUser = message.UserID.HasValue,
            CreatedDate = message.CreatedDate,
            IsRead = message.IsRead,
            ReadDate = message.ReadDate,
            Status = message.Status,
            AdminNotes = message.AdminNotes
        };
    }

    // PUT: api/ContactMessages/5/Status
    [HttpPut("{id}/Status")]
    public async Task<IActionResult> UpdateMessageStatus(int id, [FromBody] UpdateMessageStatusDto dto)
    {
        var message = await _context.ContactMessages.FindAsync(id);

        if (message == null)
        {
            return NotFound();
        }

        message.Status = dto.Status;

        if (dto.Status == "Read" && !message.IsRead)
        {
            message.IsRead = true;
            message.ReadDate = DateTime.Now;
        }

        await _context.SaveChangesAsync();

        return NoContent();
    }

    // PUT: api/ContactMessages/5/Notes
    [HttpPut("{id}/Notes")]
    public async Task<IActionResult> UpdateMessageNotes(int id, [FromBody] UpdateMessageNotesDto dto)
    {
        var message = await _context.ContactMessages.FindAsync(id);

        if (message == null)
        {
            return NotFound();
        }

        message.AdminNotes = dto.Notes;
        await _context.SaveChangesAsync();

        return NoContent();
    }

    // POST: api/ContactMessages/5/Reply
    [HttpPost("{id}/Reply")]
    public async Task<IActionResult> ReplyToMessage(int id, [FromBody] ReplyMessageDto dto)
    {
        var message = await _context.ContactMessages
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.ContactMessageID == id);

        if (message == null)
        {
            return NotFound();
        }

        try
        {
            // Send reply email
            await SendReplyEmail(message, dto.ReplyText);

            // Update message status
            message.Status = "Responded";
            message.IsRead = true;
            message.ReadDate ??= DateTime.Now;
            message.AdminNotes = (message.AdminNotes ?? "") + $"\n\n[{DateTime.Now}] Reply sent:\n{dto.ReplyText}";

            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Reply sent successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send reply email");
            return StatusCode(500, new { error = "Failed to send reply email", details = ex.Message });
        }
    }

    private async Task SendReplyEmail(ContactMessage message, string replyText)
    {
        // Get SMTP settings from configuration
        var smtpSettings = _configuration.GetSection("SmtpSettings");

        // Check if we should send real emails
        if (!_configuration.GetValue<bool>("SendRealEmails", false))
        {
            // For development, just log the email
            Console.WriteLine($"Reply email would be sent to: {message.Email}");
            Console.WriteLine($"Subject: Re: {message.Subject}");
            Console.WriteLine($"Reply: {replyText}");
            return;
        }

        // Prepare email message
        string subject = $"Re: {message.Subject}";
        string body = $@"
            <html>
            <head>
                <style>
                    body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
                    .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
                    .header {{ background-color: #ff5a5f; padding: 20px; color: white; text-align: center; }}
                    .content {{ padding: 20px; }}
                    .message-history {{ background-color: #f8f8f8; padding: 15px; margin: 20px 0; border-radius: 5px; }}
                    .footer {{ text-align: center; margin-top: 20px; font-size: 12px; color: #666; }}
                </style>
            </head>
            <body>
                <div class='container'>
                    <div class='header'>
                        <h1>Response from ChabbyNb</h1>
                    </div>
                    <div class='content'>
                        <p>Dear {message.Name},</p>
                        
                        <p>{replyText}</p>
                        
                        <div class='message-history'>
                            <p><strong>Your original message:</strong></p>
                            <p><strong>Subject:</strong> {message.Subject}</p>
                            <p><strong>Sent:</strong> {message.CreatedDate}</p>
                            <p>{message.Message}</p>
                        </div>
                        
                        <p>If you have any further questions, please don't hesitate to contact us.</p>
                        
                        <p>Best regards,<br>The ChabbyNb Team</p>
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
                emailMessage.From = new MailAddress(smtpSettings["FromEmail"], "ChabbyNb Support");
                emailMessage.Subject = subject;
                emailMessage.Body = body;
                emailMessage.IsBodyHtml = true;
                emailMessage.To.Add(new MailAddress(message.Email, message.Name));

                await client.SendMailAsync(emailMessage);
            }
        }
    }
}