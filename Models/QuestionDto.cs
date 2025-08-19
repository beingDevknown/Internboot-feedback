using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace OnlineAssessment.Web.Models
{
    public class QuestionDto
    {
        [Required]
        public string Text { get; set; } = string.Empty;

        [Required]
        public string Title { get; set; } = string.Empty;

        [Required]
        public QuestionType Type { get; set; } = QuestionType.MultipleChoice;

        public int TestId { get; set; }
        public List<AnswerOptionDto>? AnswerOptions { get; set; }
    }
}
