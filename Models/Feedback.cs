using System;
using System.ComponentModel.DataAnnotations;

namespace OnlineAssessment.Web.Models
{
    public class Feedback
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [Display(Name = "Intern Name")]
        public string InternName { get; set; }

        [Required]
        [EmailAddress]
        [Display(Name = "Email ID")]
        public string Email { get; set; }

        [Required]
        public string Domain { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime Date { get; set; }

        [Required]
        [Display(Name = "Training Session Rating")]
        [Range(1, 5)]
        public int TrainingRating { get; set; }

        [Required]
        [Display(Name = "Training Relevance")]
        [Range(1, 5)]
        public int TrainingRelevance { get; set; }

        [Required]
        [Display(Name = "Mentor Rating")]
        [Range(1, 5)]
        public int MentorRating { get; set; }

        [Required]
        [Display(Name = "Liked Most About Training")]
        public string LikedMost { get; set; }

        [Required]
        [Display(Name = "Improvements for Upcoming Sessions")]
        public string ImprovementSuggestions { get; set; }

        [Display(Name = "Suggestions for Mentor")]
        public string MentorSuggestions { get; set; }

        // Optional: User-related foreign key if applicable
        public string Username { get; set; }
        public User User { get; set; }
    }
}
