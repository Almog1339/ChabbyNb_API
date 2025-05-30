﻿using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mail;
using System.Net;
using System.Security.Claims;
using ChabbyNb_API.Models.DTOs;
using Microsoft.EntityFrameworkCore;
using ChabbyNb_API.Services;
using ChabbyNb_API.Services.Iterfaces;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChatsController : ControllerBase
{
    private readonly ChabbyNbDbContext _context;
    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ChatsController> _logger;
    private readonly IFileStorageService _fileService;
    private readonly IEmailService _emailService;

    public ChatsController(ChabbyNbDbContext context,IWebHostEnvironment webHostEnvironment,IConfiguration configuration,ILogger<ChatsController> logger, IFileStorageService fileService,IEmailService emailService)
    {
        _context = context;
        _webHostEnvironment = webHostEnvironment;
        _configuration = configuration;
        _logger = logger;
        _fileService = fileService;
        _emailService = emailService;
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
            if (!_fileService.ValidateFile(dto.MediaFile))
            {
                return BadRequest(new { error = "Invalid file. Only images (jpg, png, gif) and videos (mp4) under 10MB are allowed." });
            }

            // Save the file using the file service
            mediaUrl = await _fileService.SaveFileAsync(dto.MediaFile, "uploads/chat/" + _fileService.GetContentType(dto.MediaFile) + "s");
            contentType = _fileService.GetContentType(dto.MediaFile);
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

    // Update the method to use the email service
    private async Task SendChatNotificationEmail(ChatConversation conversation, ChatMessage message)
    {
        // Determine recipient information
        string recipientEmail;
        string recipientName;

        // Get sender information
        string senderName = message.SenderID.HasValue
            ? $"{conversation.User.FirstName} {conversation.User.LastName}".Trim()
            : "Admin";

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

        // Prepare message preview based on content type
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

        // Create model for the email template
        var model = new
        {
            RecipientName = recipientName,
            SenderName = senderName,
            ConversationTitle = conversation.Title,
            MessagePreview = messagePreview,
            HasMedia = message.ContentType != "text",
            ConversationID = conversation.ConversationID.ToString(),
            WebsiteUrl = _configuration["WebsiteUrl"]
        };

        // Send the email using the email service
        await _emailService.SendEmailAsync(
            recipientEmail,
            $"New message from {senderName}",
            "ChatNotification",
            model
        );
    }

    #endregion
}