using Microsoft.AspNetCore.Mvc;
using OnlineAssessment.Web.Models;
using System.Net.Mail;
using System.Net;

public class FeedbackController : Controller
{
    [HttpGet]
    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Create(Feedback model)
    {
        Console.WriteLine("POST /Feedback/Create triggered");


        // Prepare email body with all feedback details
        string body = $@"
New Feedback Submitted:

Intern Name: {model.InternName}
Email ID: {model.Email}
Domain: {model.Domain}
Date: {model.Date:yyyy-MM-dd}

Training Session Rating: {model.TrainingRating}/5
Training Relevance to Learning Needs: {model.TrainingRelevance}/5
Mentor Rating: {model.MentorRating}/5

What was liked most:
{model.LikedMost}

Suggestions for improvement:
{model.ImprovementSuggestions}

Suggestions for mentor:
{model.MentorSuggestions ?? "None"}

----------------------------------------
Submitted on {DateTime.Now}
        ";

        // Send email
        try
        {
            SendEmail("ys4727052@gmail.com", "New Intern Feedback Submission", body);
            TempData["SuccessMessage"] = "Thank you for your feedback!";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Email sending failed: {ex.Message}");
            TempData["ErrorMessage"] = "There was an error sending your feedback. Please try again later.";
            return View(model);
        }

        return RedirectToAction("Create");
    }

    private void SendEmail(string toEmail, string subject, string body)
    {
        Console.WriteLine($"Sending email to: {toEmail}");

        var fromEmail = "info.internboot@gmail.com";
        var appPassword = "dbbo wtnp znhn kild";  // ⚠️ Use App Password securely in production

        var smtpClient = new SmtpClient("smtp.gmail.com")
        {
            Port = 587,
            Credentials = new NetworkCredential(fromEmail, appPassword),
            EnableSsl = true,
        };

        var mailMessage = new MailMessage
        {
            From = new MailAddress(fromEmail),
            Subject = subject,
            Body = body,
            IsBodyHtml = false,
        };

        mailMessage.To.Add(toEmail);

        try
        {
            smtpClient.Send(mailMessage);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in sending email: {ex.Message}");
            throw;
        }
    }
}
