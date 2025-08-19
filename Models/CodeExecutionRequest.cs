using System.ComponentModel.DataAnnotations;

namespace OnlineAssessment.Web.Models
{
    public class CodeExecutionRequest
    {
        [Required]
        public string Code { get; set; }

        [Required]
        public string Language { get; set; }

        [Required]
        public int QuestionId { get; set; }
    }
} 