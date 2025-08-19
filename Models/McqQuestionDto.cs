using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace OnlineAssessment.Web.Models.DTOs
{
    public class McqQuestionDto
    {
        [Required]
        public string Text { get; set; }

        [Required]
        public int TestId { get; set; }

        [Required]
        public List<AnswerOptionDto> AnswerOptions { get; set; } = new List<AnswerOptionDto>();
    }
}
