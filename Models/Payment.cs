using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineAssessment.Web.Models
{
    public class Payment
    {
        public int Id { get; set; }

        [Required]
        public string UserSapId { get; set; }

        [Required]
        public decimal Amount { get; set; }

        [Required]
        public string Currency { get; set; } = "INR";

        [Required]
        public string Status { get; set; } = "Pending";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? PaidAt { get; set; }

        // Transaction ID from payment gateway (Razorpay)
        [Required]
        public string TransactionId { get; set; }

        // Navigation property
        [ForeignKey("UserSapId")]
        public User User { get; set; }
    }
}
