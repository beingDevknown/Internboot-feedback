using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineAssessment.Web.Models
{
    /// <summary>
    /// This class is used only for in-memory operations to represent questions loaded from CategoryQuestions JSON.
    /// It is not stored in the database.
    /// </summary>
    [NotMapped]
    public class InMemoryQuestion
    {
        public int Id { get; set; }

        public string Text { get; set; }

        public string Title { get; set; }

        public QuestionType Type { get; set; }

        public int TestId { get; set; }

        public List<InMemoryAnswerOption> AnswerOptions { get; set; } = new List<InMemoryAnswerOption>();
    }

    /// <summary>
    /// This class is used only for in-memory operations to represent answer options loaded from CategoryQuestions JSON.
    /// It is not stored in the database.
    /// </summary>
    [NotMapped]
    public class InMemoryAnswerOption
    {
        public int Id { get; set; }

        public string Text { get; set; }

        public bool IsCorrect { get; set; }
    }
}
