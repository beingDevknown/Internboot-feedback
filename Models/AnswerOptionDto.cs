using System.ComponentModel.DataAnnotations;

namespace OnlineAssessment.Web.Models
{
    public class AnswerOptionDto
    {
        public required string Text { get; set; }
        public required bool IsCorrect { get; set; }
    }
}
