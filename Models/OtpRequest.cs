using System.ComponentModel.DataAnnotations;

namespace OnlineAssessment.Web.Models
{
    public class OtpRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; }
    }

    public class OtpVerificationRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; }

        [Required]
        [StringLength(6, MinimumLength = 6)]
        public string OtpCode { get; set; }
    }
}
