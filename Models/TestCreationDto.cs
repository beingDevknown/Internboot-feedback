using System.ComponentModel.DataAnnotations;

namespace OnlineAssessment.Web.Models
{
    public class TestCreationDto
    {
        [Required]
        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        [Required]
        [Range(1, 1440)]
        public int DurationMinutes { get; set; }

        [Required]
        public TestType Type { get; set; }

        [Required]
        [Range(1, int.MaxValue)]
        public int MaxAttempts { get; set; } = 1;

        // Domain/Category for the test
        public string? Domain { get; set; }

        // Category ID to pull questions from (for dynamic question loading)
        public int? CategoryQuestionsId { get; set; }

        // Number of questions to pull from the category
        public int QuestionCount { get; set; } = 20;

        // Flag to indicate if we should use questions from CategoryQuestions
        public bool UseCategory { get; set; } = false;

        // Scheduling properties removed - will be handled by booking system
        public bool IsScheduleRestricted { get; set; } = false;

        // Price for taking the test
        [Range(0, 100000)]
        public decimal Price { get; set; } = 1.00M; // Default price is 1.00

        // Passing score percentage (0-100)
        [Range(0, 100)]
        public int PassingScore { get; set; } = 60; // Default passing score is 60%

        public List<QuestionDto> Questions { get; set; } = new();

        // Note: UseCategory and QuestionCount are already defined above
    }
}