using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ChabbyNb_API.Models
{
    public class Tempwd
    {
        [Key]
        public int TempwdID { get; set; }

        public int UserID { get; set; }

        public DateTime ExperationTime { get; set; }

        [StringLength(50)]
        public string Token { get; set; }

        public bool IsUsed { get; set; }

        [ForeignKey("UserID")]
        public virtual User User { get; set; }
    }
}