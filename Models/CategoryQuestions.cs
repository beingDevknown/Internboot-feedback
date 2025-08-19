using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace OnlineAssessment.Web.Models
{
    public class CategoryQuestions
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Category { get; set; }

        [Required]
        public string QuestionsJson { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string CreatedBySapId { get; set; } // User SAP ID who created these questions

        [NotMapped]
        public List<QuestionDto> Questions
        {
            get
            {
                if (string.IsNullOrEmpty(QuestionsJson))
                    return new List<QuestionDto>();

                try
                {
                    var options = new JsonSerializerOptions
                    {
                        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve,
                        MaxDepth = 64,
                        PropertyNameCaseInsensitive = true, // Add case insensitivity for better compatibility
                        AllowTrailingCommas = true, // More lenient JSON parsing
                        ReadCommentHandling = JsonCommentHandling.Skip // Skip comments in JSON
                    };

                    var result = JsonSerializer.Deserialize<List<QuestionDto>>(QuestionsJson, options);
                    if (result == null)
                    {
                        Console.Error.WriteLine("JSON deserialized to null");
                        return new List<QuestionDto>();
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    // Log the error or handle it appropriately
                    Console.Error.WriteLine($"Error deserializing questions: {ex.Message}");
                    Console.Error.WriteLine($"JSON preview: {(QuestionsJson?.Length > 100 ? QuestionsJson.Substring(0, 100) + "..." : QuestionsJson)}");
                    return new List<QuestionDto>();
                }
            }
            set
            {
                var options = new JsonSerializerOptions
                {
                    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve,
                    MaxDepth = 64,
                    PropertyNameCaseInsensitive = true, // Add case insensitivity for better compatibility
                    WriteIndented = true // Make JSON more readable for debugging
                };

                QuestionsJson = JsonSerializer.Serialize(value, options);
            }
        }

        // Note: CreatedBySapId can reference either Users or Organizations table
        // No navigation property to avoid foreign key constraints

        // Static list of allowed categories
        public static readonly List<string> AllowedCategories = new List<string>
        {
            "BFSI Internship",
            "Digital Marketing  Internships",
            "IT Internships",
            "Relationship Executive Internships",
            "Business Development Internships",
            "Sales Internships",
            "Portfolio Internships",
            "Web Development Internships",
            "Software Development Internships",
            "Pharma Intern",
            "Medical Coding Intern",
            "AI",
            "DataScience",
            "Cybersecurity",
            "Human Resource",
            "Financial Analyst",
            "Data Analyst"
        };
    }
}
