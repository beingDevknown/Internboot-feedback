using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineAssessment.Web.Models
{
    [Table("specialusers")]
    public class SpecialUser
    {
        [Key]
        [Required]
        [StringLength(50)]
        public string UsersSapId { get; set; } = string.Empty; // Primary key using UsersSapId to match users table

        [Required]
        [EmailAddress]
        [StringLength(255)]
        public string Email { get; set; } // Email serves as both identifier and login credential

        [Required]
        [StringLength(100)]
        public string Username { get; set; } // Display name for the special user

        [Required]
        [StringLength(200)]
        public string FullName { get; set; } // Full name of the special user

        [Required]
        public string PasswordHash { get; set; } // Hashed password

        [Required]
        public string OrganizationSapId { get; set; } // Foreign key to Organization

        [Required]
        public DateTime CreatedAt { get; set; } = Utilities.TimeZoneHelper.GetCurrentIstTime();

        public DateTime? LastLoginAt { get; set; }

        public bool IsActive { get; set; } = true;

        public string? MobileNumber { get; set; } // Optional mobile number

        // Profile fields for test filtering
        public EducationLevel? Education { get; set; } // Education level for test filtering
        public EmploymentStatus? Employment { get; set; } // Employment status for test filtering
        public string? Category { get; set; } // Category for test filtering (e.g., Engineering, Medical, etc.)

        // Metadata fields
        public string? Description { get; set; } // Purpose or description of this special user

        public string? CreatedBy { get; set; } // Who created this special user account

        // Navigation property
        [ForeignKey("OrganizationSapId")]
        public virtual Organization? Organization { get; set; }
    }
}
