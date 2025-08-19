using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace OnlineAssessment.Web.Models
{
    public class CategoryQuestionsUploadDto
    {
        [Required]
        public string Category { get; set; }
        [Required]
        public List<QuestionDto> Questions { get; set; }
    }
}
