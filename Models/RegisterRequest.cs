using System.ComponentModel.DataAnnotations;

namespace OnlineAssessment.Web.Models
{
    public class RegisterRequest
    {
        [Required(ErrorMessage = "Username is required")]
        public required string Username { get; set; }

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        public required string Email { get; set; }

        public string? Password { get; set; } // Password is now optional
        public string? Role { get; set; } // Role is now optional

        // User fields - common for all users
        [Required(ErrorMessage = "First name is required")]
        public required string FirstName { get; set; }

        [Required(ErrorMessage = "Last name is required")]
        public required string LastName { get; set; }

        [Required(ErrorMessage = "Mobile number is required")]
        public required string MobileNumber { get; set; }

        public string? ProfilePicture { get; set; } // Will be stored as PhotoUrl in User model

        [Required(ErrorMessage = "Key skills are required")]
        public required string KeySkills { get; set; }

        [Required(ErrorMessage = "Employment status is required")]
        public required string Employment { get; set; } // Will be converted to enum

        [Required(ErrorMessage = "Education level is required")]
        public required string Education { get; set; } // Will be converted to enum

        [Required(ErrorMessage = "Category is required")]
        public required string Category { get; set; }

        [Required(ErrorMessage = "Internship Duration is required")]
         public required string InternshipDuration { get; set; }

        // Optional organization token for multi-tenancy
        public string? OrganizationToken { get; set; }

        // Organization-specific fields - only used for creating Organization records
        // These fields will NOT be stored in the User table
        public string? OrganizationName { get; set; }
        public string? ContactPerson { get; set; }
        public string? Address { get; set; }
        public string? Website { get; set; }
        public string? Description { get; set; }
    }
}
