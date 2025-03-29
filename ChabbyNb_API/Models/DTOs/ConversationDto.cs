using System.ComponentModel.DataAnnotations;

namespace ChabbyNb_API.Models.DTOs
{
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

    public class CreateConversationDto
    {
        [Required]
        [StringLength(100)]
        public string Title { get; set; }

        public string InitialMessage { get; set; }
    }

    public class SendMessageDto
    {
        [Required(ErrorMessage = "Message content is required")]
        public string Content { get; set; }

        public IFormFile MediaFile { get; set; }

        public int? TemplateID { get; set; }
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
}