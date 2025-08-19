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

        // Check if model state is valid
        if (string.IsNullOrWhiteSpace(model.Subject) ||
            string.IsNullOrWhiteSpace(model.Message) ||
            model.Rating < 1 || model.Rating > 5)
        {
            ModelState.AddModelError("", "All fields are required and rating must be between 1 and 5.");
            return View(model);
        }

        // Prepare email body
        string body = $"Subject: {model.Subject}\n\nMessage:\n{model.Message}\n\nRating: {model.Rating}";

        // Send the email
        try
        {
            SendEmail("ys4727052@gmail.com", "New Feedback Received", body); // 👈 updated
            TempData["SuccessMessage"] = "Thank you for your feedback!";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Email sending failed: {ex.Message}");
            TempData["ErrorMessage"] = "There was an error sending your feedback.";
            return View(model);
        }

        return RedirectToAction("Create");
    }

    private void SendEmail(string toEmail, string subject, string body)
    {
        Console.WriteLine($"Sending email to: {toEmail}");

        var fromEmail = "info.internboot@gmail.com";
        var appPassword = "dbbo wtnp znhn kild";  // ⚠️ Replace with your generated App Password

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
