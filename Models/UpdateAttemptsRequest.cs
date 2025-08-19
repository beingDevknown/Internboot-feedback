using System.ComponentModel.DataAnnotations;

namespace OnlineAssessment.Web.Models
{
    public class UpdateAttemptsRequest
    {
        [Required]
        public int TestId { get; set; }
        
        [Required]
        public string Username { get; set; }
        
        [Required]
        public int Change { get; set; } // Positive to increase, negative to decrease
    }
}
