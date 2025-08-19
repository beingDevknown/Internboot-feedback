using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineAssessment.Web.Models
{
    [Table("certificatepurchases")]
    public class CertificatePurchase
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int TestResultId { get; set; } // Foreign key to TestResult

        [Required]
        public string UserSapId { get; set; } // User who purchased the certificate

        [Required]
        public decimal Amount { get; set; } = 1000.00M; // Certificate cost (₹1000) - default value, actual value set from configuration

        [Required]
        public string Currency { get; set; } = "INR";

        [Required]
        public string Status { get; set; } = "Pending"; // Pending, Completed, Failed

        [Required]
        public DateTime CreatedAt { get; set; } = Utilities.TimeZoneHelper.GetCurrentIstTime();

        public DateTime? PaidAt { get; set; }

        public string? TransactionId { get; set; } // Payment gateway transaction ID

        public string? CertificateUrl { get; set; } // URL to the generated certificate

        public DateTime? CertificateGeneratedAt { get; set; }

        // Navigation properties
        [ForeignKey("TestResultId")]
        public virtual TestResult? TestResult { get; set; }

        // Note: UserSapId can reference either Users or SpecialUsers table
        // We don't use a foreign key constraint to allow flexibility
        // The relationship is handled manually in the service layer
    }
}
