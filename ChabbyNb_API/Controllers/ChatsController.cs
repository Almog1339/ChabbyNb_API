using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mail;
using System.Net;
using System.Security.Claims;
using ChabbyNb_API.Models.DTOs;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChatsController : ControllerBase
{
    private readonly ChabbyNbDbContext _context;
    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ChatsController> _logger;

    public ChatsController(
        ChabbyNbDbContext context,
        IWebHostEnvironment webHostEnvironment,
        IConfiguration configuration,
        ILogger<ChatsController> logger)
    {
        _context = context;
        _webHostEnvironment = webHostEnvironment;
        _configuration = configuration;
        _logger = logger;
    }

    // GET: api/Chats - Get all user conversations
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ConversationDto>>> GetConversations()
    {
        int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
        bool isAdmin = User.HasClaim(c => c.Type == "IsAdmin" && c.Value == "True");

        var query = _context.ChatConversations
            .Include(c => c.User)
            .AsQueryable();

        if (!isAdmin)
        {
            // Regular users can only see their own conversations
            query = query.Where(c => c.UserID == userId);
        }

        var conversations = await query
            .OrderByDescending(c => c.LastMessageDate)
            .Select(c => new ConversationDto
            {
                ConversationID = c.ConversationID,
                UserID = c.UserID,
                UserName = $"{c.User.FirstName} {c.User.LastName}".Trim(),
                UserEmail = c.User.Email,
                Title = c.Title,
                IsArchived = c.IsArchived,
                CreatedDate = c.CreatedDate,
                LastMessageDate = c.LastMessageDate,
                HasUnreadMessages = isAdmin ? c.HasUnreadUserMessages : c.HasUnreadAdminMessages,
                LastMessagePreview = c.Messages
                    .OrderByDescending(m => m.SentDate)
                    .Select(m => m.Content)
                    .FirstOrDefault() ?? ""
            })
            .ToListAsync();

        return conversations;
    }

    // GET: api/Chats/{id} - Get a specific conversation with messages
    [HttpGet("{id}")]
    public async Task<ActionResult<ConversationWithMessagesDto>> GetConversation(int id)
    {
        int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
        bool isAdmin = User.HasClaim(c => c.Type == "IsAdmin" && c.Value == "True");

        var conversation = await _context.ChatConversations
            .Include(c => c.User)
            .Include(c => c.Messages)
                .ThenInclude(m => m.Sender)
            .Include(c => c.Messages)
                .ThenInclude(m => m.Template)
            .FirstOrDefaultAsync(c => c.ConversationID == id);

        if (conversation == null)
        {
            return NotFound();
        }

        // Ensure user can access this conversation
        if (!isAdmin && conversation.UserID != userId)
        {
            return Forbid();
        }

        // Mark messages as read (if current user is recipient)
        var unreadMessages = conversation.Messages
            .Where(m => !m.IsRead &&
                ((isAdmin && m.SenderID.HasValue) || // Admin is viewing user messages
                (!isAdmin && !m.SenderID.HasValue))) // User is viewing admin messages
            .ToList();

        foreach (var message in unreadMessages)
        {
            message.IsRead = true;
            message.ReadDate = DateTime.Now;
        }

        // Update conversation unread flags
        if (isAdmin && conversation.HasUnreadUserMessages && !conversation.Messages.Any(m => !m.IsRead && m.SenderID.HasValue))
        {
            conversation.HasUnreadUserMessages = false;
        }
        else if (!isAdmin && conversation.HasUnreadAdminMessages && !conversation.Messages.Any(m => !m.IsRead && !m.SenderID.HasValue))
        {
            conversation.HasUnreadAdminMessages = false;
        }

        // Save changes
        if (unreadMessages.Any())
        {
            await _context.SaveChangesAsync();
        }

        // Create DTO
        var conversationDto = new ConversationWithMessagesDto
        {
            ConversationID = conversation.ConversationID,
            UserID = conversation.UserID,
            UserName = $"{conversation.User.FirstName} {conversation.User.LastName}".Trim(),
            UserEmail = conversation.User.Email,
            Title = conversation.Title,
            IsArchived = conversation.IsArchived,
            CreatedDate = conversation.CreatedDate,
            LastMessageDate = conversation.LastMessageDate,
            HasUnreadMessages = isAdmin ? conversation.HasUnreadUserMessages : conversation.HasUnreadAdminMessages,
            Messages = conversation.Messages
                .OrderBy(m => m.SentDate)
                .Select(m => new MessageDto
                {
                    MessageID = m.MessageID,
                    ConversationID = m.ConversationID,
                    SenderID = m.SenderID,
                    SenderName = m.SenderID.HasValue
                        ? $"{m.Sender.FirstName} {m.Sender.LastName}".Trim()
                        : "Admin",
                    IsFromAdmin = !m.SenderID.HasValue,
                    Content = m.Content,
                    ContentType = m.ContentType,
                    MediaUrl = m.MediaUrl,
                    IsRead = m.IsRead,
                    SentDate = m.SentDate,
                    ReadDate = m.ReadDate,
                    IsFromTemplate = m.IsFromTemplate,
                    TemplateID = m.TemplateID,
                    TemplateName = m.Template?.Title
                })
                .ToList()
        };

        return conversationDto;
    }

    // POST: api/Chats - Create a new conversation
    [HttpPost]
    public async Task<ActionResult<ConversationDto>> CreateConversation([FromBody] CreateConversationDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

        // Create new conversation
        var conversation = new ChatConversation
        {
            UserID = userId,
            Title = dto.Title,
            IsArchived = false,
            CreatedDate = DateTime.Now,
            LastMessageDate = DateTime.Now,
            HasUnreadUserMessages = false,
            HasUnreadAdminMessages = false
        };

        _context.ChatConversations.Add(conversation);
        await _context.SaveChangesAsync();

        // If initial message is provided, create it
        if (!string.IsNullOrEmpty(dto.InitialMessage))
        {
            var message = new ChatMessage
            {
                ConversationID = conversation.ConversationID,
                SenderID = userId,
                Content = dto.InitialMessage,
                ContentType = "text",
                IsRead = false,
                SentDate = DateTime.Now,
                IsFromTemplate = false
            };

            conversation.Messages.Add(message);
            conversation.HasUnreadUserMessages = true;

            _context.ChatMessages.Add(message);
            await _context.SaveChangesAsync();

            // Send notification email to admin
            try
            {
                await SendChatNotificationEmail(conversation, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send chat notification email");
                // Continue processing - don't fail the request just because email failed
            }
        }

        // Get user for response
        var user = await _context.Users.FindAsync(userId);

        // Create response DTO
        var responseDto = new ConversationDto
        {
            ConversationID = conversation.ConversationID,
            UserID = conversation.UserID,
            UserName = $"{user.FirstName} {user.LastName}".Trim(),
            UserEmail = user.Email,
            Title = conversation.Title,
            IsArchived = conversation.IsArchived,
            CreatedDate = conversation.CreatedDate,
            LastMessageDate = conversation.LastMessageDate,
            HasUnreadMessages = false,
            LastMessagePreview = dto.InitialMessage ?? ""
        };

        return CreatedAtAction(nameof(GetConversation), new { id = conversation.ConversationID }, responseDto);
    }

    // POST: api/Chats/{id}/Messages - Send a new message in a conversation
    [HttpPost("{id}/Messages")]
    public async Task<ActionResult<MessageDto>> SendMessage(int id, [FromForm] SendMessageDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
        bool isAdmin = User.HasClaim(c => c.Type == "IsAdmin" && c.Value == "True");

        var conversation = await _context.ChatConversations
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.ConversationID == id);

        if (conversation == null)
        {
            return NotFound();
        }

        // Regular users can only access their own conversations
        if (!isAdmin && conversation.UserID != userId)
        {
            return Forbid();
        }

        // Handle file upload if present
        string mediaUrl = null;
        string contentType = "text";

        if (dto.MediaFile != null && dto.MediaFile.Length > 0)
        {
            // Validate file (type, size, etc.)
            if (!IsValidMediaFile(dto.MediaFile))
            {
                return BadRequest(new { error = "Invalid file. Only images (jpg, png, gif) and videos (mp4) under 10MB are allowed." });
            }

            // Save the file
            mediaUrl = await SaveMediaFile(dto.MediaFile);
            contentType = GetContentType(dto.MediaFile);
        }

        // Create message
        var message = new ChatMessage
        {
            ConversationID = conversation.ConversationID,
            SenderID = isAdmin ? null : userId, // null sender means admin
            Content = dto.Content,
            ContentType = contentType,
            MediaUrl = mediaUrl,
            IsRead = false,
            SentDate = DateTime.Now,
            IsFromTemplate = dto.TemplateID.HasValue,
            TemplateID = dto.TemplateID
        };

        _context.ChatMessages.Add(message);

        // Update conversation
        conversation.LastMessageDate = message.SentDate;

        if (isAdmin)
        {
            conversation.HasUnreadAdminMessages = true;

            // Update template usage statistics if using a template
            if (dto.TemplateID.HasValue)
            {
                var template = await _context.MessageTemplates.FindAsync(dto.TemplateID.Value);
                if (template != null)
                {
                    template.UseCount++;
                    template.LastUsedDate = DateTime.Now;
                }
            }
        }
        else
        {
            conversation.HasUnreadUserMessages = true;
        }

        await _context.SaveChangesAsync();

        // Send notification email
        try
        {
            await SendChatNotificationEmail(conversation, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send chat notification email");
            // Continue processing - don't fail the request
        }

        // Create response DTO
        var responseDto = new MessageDto
        {
            MessageID = message.MessageID,
            ConversationID = message.ConversationID,
            SenderID = message.SenderID,
            SenderName = message.SenderID.HasValue
                ? $"{conversation.User.FirstName} {conversation.User.LastName}".Trim()
                : "Admin",
            IsFromAdmin = !message.SenderID.HasValue,
            Content = message.Content,
            ContentType = message.ContentType,
            MediaUrl = message.MediaUrl,
            IsRead = message.IsRead,
            SentDate = message.SentDate,
            ReadDate = message.ReadDate,
            IsFromTemplate = message.IsFromTemplate,
            TemplateID = message.TemplateID
        };

        return Ok(responseDto);
    }

    // PUT: api/Chats/{id}/Archive - Archive or unarchive a conversation
    [HttpPut("{id}/Archive")]
    public async Task<IActionResult> ArchiveConversation(int id, [FromBody] ArchiveConversationDto dto)
    {
        int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
        bool isAdmin = User.HasClaim(c => c.Type == "IsAdmin" && c.Value == "True");

        var conversation = await _context.ChatConversations.FindAsync(id);

        if (conversation == null)
        {
            return NotFound();
        }

        // Regular users can only access their own conversations
        if (!isAdmin && conversation.UserID != userId)
        {
            return Forbid();
        }

        // Update archive status
        conversation.IsArchived = dto.IsArchived;
        await _context.SaveChangesAsync();

        return NoContent();
    }

    // GET: api/Chats/Unread - Get count of unread messages
    [HttpGet("Unread")]
    public async Task<ActionResult<UnreadCountDto>> GetUnreadCount()
    {
        int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
        bool isAdmin = User.HasClaim(c => c.Type == "IsAdmin" && c.Value == "True");

        var query = _context.ChatConversations.AsQueryable();

        if (!isAdmin)
        {
            // Regular users can only see their own conversations
            query = query.Where(c => c.UserID == userId);
        }

        int unreadCount = await query
            .Where(c => isAdmin ? c.HasUnreadUserMessages : c.HasUnreadAdminMessages)
            .CountAsync();

        return new UnreadCountDto
        {
            UnreadCount = unreadCount
        };
    }

    // GET: api/Chats/Templates - Get message templates (admin only)
    [HttpGet("Templates")]
    [Authorize(Policy = "RequireAdminRole")]
    public async Task<ActionResult<IEnumerable<MessageTemplateDto>>> GetTemplates()
    {
        var templates = await _context.MessageTemplates
            .OrderBy(t => t.Category)
            .ThenBy(t => t.SortOrder)
            .Select(t => new MessageTemplateDto
            {
                TemplateID = t.TemplateID,
                Title = t.Title,
                Content = t.Content,
                Category = t.Category,
                SortOrder = t.SortOrder,
                IsActive = t.IsActive,
                CreatedDate = t.CreatedDate,
                LastUsedDate = t.LastUsedDate,
                UseCount = t.UseCount
            })
            .ToListAsync();

        return templates;
    }

    // POST: api/Chats/Templates - Create a message template (admin only)
    [HttpPost("Templates")]
    [Authorize(Policy = "RequireAdminRole")]
    public async Task<ActionResult<MessageTemplateDto>> CreateTemplate([FromBody] CreateTemplateDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var template = new MessageTemplate
        {
            Title = dto.Title,
            Content = dto.Content,
            Category = dto.Category,
            SortOrder = dto.SortOrder,
            IsActive = true,
            CreatedDate = DateTime.Now,
            UseCount = 0
        };

        _context.MessageTemplates.Add(template);
        await _context.SaveChangesAsync();

        // Create response DTO
        var responseDto = new MessageTemplateDto
        {
            TemplateID = template.TemplateID,
            Title = template.Title,
            Content = template.Content,
            Category = template.Category,
            SortOrder = template.SortOrder,
            IsActive = template.IsActive,
            CreatedDate = template.CreatedDate,
            UseCount = template.UseCount
        };

        return CreatedAtAction(nameof(GetTemplates), null, responseDto);
    }

    // PUT: api/Chats/Templates/{id} - Update a message template (admin only)
    [HttpPut("Templates/{id}")]
    [Authorize(Policy = "RequireAdminRole")]
    public async Task<IActionResult> UpdateTemplate(int id, [FromBody] UpdateTemplateDto dto)
    {
        if (id != dto.TemplateID)
        {
            return BadRequest("ID mismatch");
        }

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var template = await _context.MessageTemplates.FindAsync(id);

        if (template == null)
        {
            return NotFound();
        }

        // Update properties
        template.Title = dto.Title;
        template.Content = dto.Content;
        template.Category = dto.Category;
        template.SortOrder = dto.SortOrder;
        template.IsActive = dto.IsActive;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    // DELETE: api/Chats/Templates/{id} - Delete a message template (admin only)
    [HttpDelete("Templates/{id}")]
    [Authorize(Policy = "RequireAdminRole")]
    public async Task<IActionResult> DeleteTemplate(int id)
    {
        var template = await _context.MessageTemplates.FindAsync(id);

        if (template == null)
        {
            return NotFound();
        }

        // Check if template is used in any messages
        bool isUsed = await _context.ChatMessages.AnyAsync(m => m.TemplateID == id);

        if (isUsed)
        {
            // Just mark as inactive instead of deleting
            template.IsActive = false;
        }
        else
        {
            _context.MessageTemplates.Remove(template);
        }

        await _context.SaveChangesAsync();

        return NoContent();
    }

    #region Helper Methods

    private bool IsValidMediaFile(IFormFile file)
    {
        // Check file size (10MB max)
        if (file.Length > 10 * 1024 * 1024)
        {
            return false;
        }

        // Check file extension
        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".mp4" };
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (!allowedExtensions.Contains(extension))
        {
            return false;
        }

        // Further validation could be added here

        return true;
    }

    private async Task<string> SaveMediaFile(IFormFile file)
    {
        string contentType = GetContentType(file);
        string uploadsSubfolder = contentType == "image" ? "images" : "videos";

        // Ensure uploads directory exists
        string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "chat", uploadsSubfolder);
        Directory.CreateDirectory(uploadsFolder);

        // Generate a unique filename
        string uniqueFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        string filePath = Path.Combine(uploadsFolder, uniqueFileName);

        // Save the file
        using (var fileStream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(fileStream);
        }

        // Return the relative URL
        return $"/uploads/chat/{uploadsSubfolder}/{uniqueFileName}";
    }

    private string GetContentType(IFormFile file)
    {
        string extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (extension == ".mp4")
        {
            return "video";
        }

        return "image";
    }

    private async Task SendChatNotificationEmail(ChatConversation conversation, ChatMessage message)
    {
        // Get recipient information
        string recipientEmail;
        string recipientName;

        if (message.SenderID.HasValue)
        {
            // User sent a message, notify admin
            recipientEmail = _configuration["AdminEmail"];
            recipientName = "Admin";

            if (string.IsNullOrEmpty(recipientEmail))
            {
                throw new InvalidOperationException("Admin email is not configured");
            }
        }
        else
        {
            // Admin sent a message, notify user
            recipientEmail = conversation.User.Email;
            recipientName = conversation.User.FirstName ?? conversation.User.Username;
        }

        // Get sender information
        string senderName = message.SenderID.HasValue
            ? $"{conversation.User.FirstName} {conversation.User.LastName}".Trim()
            : "Admin";

        // Get SMTP settings from configuration
        var smtpSettings = _configuration.GetSection("SmtpSettings");

        // Check if we should send real emails
        if (!_configuration.GetValue<bool>("SendRealEmails", false))
        {
            // For development, just log the email
            Console.WriteLine($"Chat notification email would be sent to: {recipientEmail}");
            Console.WriteLine($"Subject: New message from {senderName}");
            Console.WriteLine($"Message preview: {(message.Content.Length > 50 ? message.Content.Substring(0, 50) + "..." : message.Content)}");
            return;
        }

        // Prepare email content based on message type
        string messagePreview;

        if (message.ContentType == "text")
        {
            messagePreview = message.Content.Length > 200
                ? message.Content.Substring(0, 200) + "..."
                : message.Content;
        }
        else
        {
            messagePreview = $"[{message.ContentType.ToUpper()}] {(message.Content.Length > 100 ? message.Content.Substring(0, 100) + "..." : message.Content)}";
        }

        // Prepare email message
        string subject = $"New message from {senderName}";
        string body = $@"
            <html>
            <head>
                <style>
                    body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
                    .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
                    .header {{ background-color: #ff5a5f; padding: 20px; color: white; text-align: center; }}
                    .content {{ padding: 20px; }}
                    .message-preview {{ background-color: #f8f8f8; padding: 15px; margin: 20px 0; border-radius: 5px; }}
                    .button {{ display: inline-block; background-color: #ff5a5f; color: white; padding: 10px 20px; 
                            text-decoration: none; border-radius: 5px; margin-top: 20px; }}
                    .footer {{ text-align: center; margin-top: 20px; font-size: 12px; color: #666; }}
                </style>
            </head>
            <body>
                <div class='container'>
                    <div class='header'>
                        <h1>New Message</h1>
                    </div>
                    <div class='content'>
                        <p>Dear {recipientName},</p>
                        <p>You have received a new message from {senderName} in conversation: <strong>{conversation.Title}</strong></p>
                        
                        <div class='message-preview'>
                            <p>{messagePreview}</p>
                            {(message.ContentType != "text" ? "<p><em>This message contains media that can be viewed in the app.</em></p>" : "")}
                        </div>
                        
                        <p>Please log in to your account to view the full message and reply.</p>
                        
                        <p style='text-align: center;'>
                            <a href='{_configuration["WebsiteUrl"]}/messages/{conversation.ConversationID}' class='button'>View Message</a>
                        </p>
                        
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
                emailMessage.From = new MailAddress(smtpSettings["FromEmail"], "ChabbyNb Messaging");
                emailMessage.Subject = subject;
                emailMessage.Body = body;
                emailMessage.IsBodyHtml = true;
                emailMessage.To.Add(new MailAddress(recipientEmail, recipientName));

                await client.SendMailAsync(emailMessage);
            }
        }
    }

    #endregion
}