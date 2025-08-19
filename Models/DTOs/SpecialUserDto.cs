using System.ComponentModel.DataAnnotations;

namespace OnlineAssessment.Web.Models.DTOs
{
    public class CreateSpecialUserRequest
    {
        [Required]
        [EmailAddress]
        [StringLength(255)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = 3)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [StringLength(200, MinimumLength = 2)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = 6)]
        public string Password { get; set; } = string.Empty;

        [Phone]
        public string? MobileNumber { get; set; }

        public EducationLevel? Education { get; set; }

        public EmploymentStatus? Employment { get; set; }

        [StringLength(100)]
        public string? Category { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }
    }

    public class SpecialUserResponse
    {
        public string SapId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? MobileNumber { get; set; }
        public EducationLevel? Education { get; set; }
        public EmploymentStatus? Employment { get; set; }
        public string? Category { get; set; }
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public bool IsActive { get; set; }
        public string? CreatedBy { get; set; }
        public string OrganizationName { get; set; } = string.Empty;
    }

    public class UpdateSpecialUserRequest
    {
        [Required]
        [StringLength(100, MinimumLength = 3)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [StringLength(200, MinimumLength = 2)]
        public string FullName { get; set; } = string.Empty;

        [Phone]
        public string? MobileNumber { get; set; }

        public EducationLevel? Education { get; set; }

        public EmploymentStatus? Employment { get; set; }

        [StringLength(100)]
        public string? Category { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class ChangeSpecialUserPasswordRequest
    {
        [Required]
        [StringLength(100, MinimumLength = 6)]
        public string NewPassword { get; set; } = string.Empty;
    }


}
