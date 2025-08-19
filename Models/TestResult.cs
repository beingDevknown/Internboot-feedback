using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineAssessment.Web.Models
{
    [Table("testresult")]
    public class TestResult
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int TestId { get; set; }

        [Required]
        public string Username { get; set; }

        [Required]
        public int TotalQuestions { get; set; }

        [Required]
        public int CorrectAnswers { get; set; }

        [Required]
        public double Score { get; set; }

        // These properties are required by the database schema but we only use MCQ now
        [Required]
        public double McqScore { get; set; }

        [Required]
        public double CodingScore { get; set; }

        [Required]
        public DateTime SubmittedAt { get; set; } = Utilities.TimeZoneHelper.GetCurrentIstTime();

        // Test time information
        public DateTime? StartTime { get; set; }

        public DateTime? EndTime { get; set; }

        // SAP ID for unique identification - now required and used as foreign key
        [Required]
        public string UserSapId { get; set; }

        // Attempt number for tracking multiple attempts
        [Required]
        public int AttemptNumber { get; set; } = 1;

        // Flag to indicate if the test was automatically submitted
        // This property is not mapped to the database to avoid migration issues
        [NotMapped]
        public bool AutoSubmitted { get; set; } = false;

        [ForeignKey("TestId")]
        public Test Test { get; set; }

        // Navigation properties for User and SpecialUser relationships
        // These are configured without foreign key constraints in AppDbContext
        public User? User { get; set; }
        public SpecialUser? SpecialUser { get; set; }

        // Helper method to determine score rating for special users
        public string GetScoreRating()
        {
            if (TotalQuestions == 0) return "Below Average";

            double percentage = (CorrectAnswers * 100.0) / TotalQuestions;

            if (percentage >= 80) return "Best Performer";
            if (percentage >= 70) return "Good Performer";
            if (percentage >= 60) return "Average Performer";
            return "Below Average";
        }

        // Helper method to check if user is eligible for certificate (60% or above)
        public bool IsEligibleForCertificate()
        {
            if (TotalQuestions == 0) return false;
            double percentage = (CorrectAnswers * 100.0) / TotalQuestions;
            return percentage >= 60;
        }

        // Helper method to get score percentage
        public double GetScorePercentage()
        {
            if (TotalQuestions == 0) return 0;
            return (CorrectAnswers * 100.0) / TotalQuestions;
        }
    }
}