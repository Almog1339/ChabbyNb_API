using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace ChabbyNb_API.Models
{
    public class ChatConversation
    {
        public int ConversationID { get; set; }

        public int UserID { get; set; }

        [Required]
        [StringLength(100)]
        public string Title { get; set; }

        public bool IsArchived { get; set; }

        public DateTime CreatedDate { get; set; }

        public DateTime LastMessageDate { get; set; }

        // For quick access to see if there are unread messages by the user or admin
        public bool HasUnreadUserMessages { get; set; }

        public bool HasUnreadAdminMessages { get; set; }

        [ForeignKey("UserID")]
        public virtual User User { get; set; }

        public virtual ICollection<ChatMessage> Messages { get; set; } = new HashSet<ChatMessage>();
    }

    public class ChatMessage
    {
        public int MessageID { get; set; }

        public int ConversationID { get; set; }

        public int? SenderID { get; set; } // Null means admin

        [Required]
        public string Content { get; set; }

        public string ContentType { get; set; } = "text"; // text, image, video

        public string MediaUrl { get; set; } // URL for images or videos

        public bool IsRead { get; set; }

        public DateTime SentDate { get; set; }

        public DateTime? ReadDate { get; set; }

        public bool IsFromTemplate { get; set; }

        public int? TemplateID { get; set; }

        [ForeignKey("ConversationID")]
        public virtual ChatConversation Conversation { get; set; }

        [ForeignKey("SenderID")]
        public virtual User Sender { get; set; }

        [ForeignKey("TemplateID")]
        public virtual MessageTemplate Template { get; set; }
    }

    public class MessageTemplate
    {
        public int TemplateID { get; set; }

        [Required]
        [StringLength(100)]
        public string Title { get; set; }

        [Required]
        public string Content { get; set; }

        public string Category { get; set; }

        public int SortOrder { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedDate { get; set; }

        public DateTime? LastUsedDate { get; set; }

        // Statistics about how often the template is used
        public int UseCount { get; set; }
    }
}