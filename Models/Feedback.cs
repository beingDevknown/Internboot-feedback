using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineAssessment.Web.Models
{using Microsoft.AspNetCore.Mvc.ModelBinding;

    public class Feedback
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Subject { get; set; }

        [Required]
        public string Message { get; set; }

        [Range(1, 5)]
        public int Rating { get; set; }

          [Required]
    public string Username { get; set; }  // This will be the foreign key to User

    public User User { get; set; }  // Navigation property to User

}

}
