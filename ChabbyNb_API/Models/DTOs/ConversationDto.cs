using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ChabbyNb_API.Models.DTOs
{
    #region Chat Conversation DTOs

    public class ConversationDto
    {
        public int ConversationID { get; set; }
        public int UserID { get; set; }
        public string UserName { get; set; }
        public string UserEmail { get; set; }
        public string Title { get; set; }
        public bool IsArchived { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime LastMessageDate { get; set; }
        public bool HasUnreadMessages { get; set; }
        public string LastMessagePreview { get; set; }
    }

    public class ConversationWithMessagesDto : ConversationDto
    {
        public List<MessageDto> Messages { get; set; }
    }

    public class CreateConversationDto
    {
        [Required]
        [StringLength(100)]
        public string Title { get; set; }

        public string InitialMessage { get; set; }
    }

    public class ArchiveConversationDto
    {
        [Required]
        public bool IsArchived { get; set; }
    }

    public class UnreadCountDto
    {
        public int UnreadCount { get; set; }
    }

    #endregion

    #region Message DTOs

    public class MessageDto
    {
        public int MessageID { get; set; }
        public int ConversationID { get; set; }
        public int? SenderID { get; set; }
        public string SenderName { get; set; }
        public bool IsFromAdmin { get; set; }
        public string Content { get; set; }
        public string ContentType { get; set; }
        public string MediaUrl { get; set; }
        public bool IsRead { get; set; }
        public DateTime SentDate { get; set; }
        public DateTime? ReadDate { get; set; }
        public bool IsFromTemplate { get; set; }
        public int? TemplateID { get; set; }
        public string TemplateName { get; set; }
    }

    public class SendMessageDto
    {
        [Required(ErrorMessage = "Message content is required")]
        public string Content { get; set; }

        public IFormFile MediaFile { get; set; }

        public int? TemplateID { get; set; }
    }

    #endregion

    #region Message Template DTOs

    public class MessageTemplateDto
    {
        public int TemplateID { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public string Category { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? LastUsedDate { get; set; }
        public int UseCount { get; set; }
    }

    public class CreateTemplateDto
    {
        [Required]
        [StringLength(100)]
        public string Title { get; set; }

        [Required]
        public string Content { get; set; }

        public string Category { get; set; }

        public int SortOrder { get; set; }
    }

    public class UpdateTemplateDto
    {
        public int TemplateID { get; set; }

        [Required]
        [StringLength(100)]
        public string Title { get; set; }

        [Required]
        public string Content { get; set; }

        public string Category { get; set; }

        public int SortOrder { get; set; }

        public bool IsActive { get; set; }
    }

    #endregion

    #region Contact Message DTOs

    public class ContactMessageDto
    {
        public int ContactMessageID { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Subject { get; set; }
        public string Message { get; set; }
        public int? UserID { get; set; }
        public string UserName { get; set; }
        public bool IsRegisteredUser { get; set; }
        public DateTime CreatedDate { get; set; }
        public bool IsRead { get; set; }
        public DateTime? ReadDate { get; set; }
        public string Status { get; set; }
        public string AdminNotes { get; set; }
    }

    public class UpdateMessageStatusDto
    {
        [Required]
        [RegularExpression("^(New|Read|Responded|Archived)$",
            ErrorMessage = "Status must be one of: New, Read, Responded, Archived")]
        public string Status { get; set; }
    }

    public class UpdateMessageNotesDto
    {
        public string Notes { get; set; }
    }

    public class ReplyMessageDto
    {
        [Required]
        public string ReplyText { get; set; }
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

    #endregion

    #region Email Template DTOs

    public class EmailTemplateInfo
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public DateTime LastModified { get; set; }
    }

    public class EmailTemplateContent
    {
        public string Name { get; set; }
        public string Content { get; set; }
        public DateTime LastModified { get; set; }
    }

    public class EmailTemplateUpdateDto
    {
        public string Name { get; set; }
        public string Content { get; set; }
    }

    public class EmailTemplateCreateDto
    {
        public string Name { get; set; }
        public string Content { get; set; }
    }

    #endregion
}