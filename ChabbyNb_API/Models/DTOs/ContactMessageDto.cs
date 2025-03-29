using System.ComponentModel.DataAnnotations;

namespace ChabbyNb_API.Models.DTOs
{
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
}