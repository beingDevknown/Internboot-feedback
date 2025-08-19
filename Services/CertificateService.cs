using Microsoft.EntityFrameworkCore;
using OnlineAssessment.Web.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;
using SixLabors.Fonts;

namespace OnlineAssessment.Web.Services
{
    public interface ICertificateService
    {
        Task<(bool Success, string Message, string? CertificateUrl)> GenerateCertificateAsync(int testResultId, string userSapId);
        Task<bool> HasPurchasedCertificateAsync(int testResultId);
        Task<CertificatePurchase?> GetCertificatePurchaseAsync(int testResultId);
        Task<(bool Success, string Message)> InitiateCertificatePurchaseAsync(int testResultId, string userSapId);
        Task<(bool Success, string Message)> CompleteCertificatePurchaseAsync(int testResultId, string transactionId);
    }

    public class CertificateService(AppDbContext context, ILogger<CertificateService> logger, IWebHostEnvironment environment, IConfiguration configuration) : ICertificateService
    {
        private readonly AppDbContext _context = context;
        private readonly ILogger<CertificateService> _logger = logger;
        private readonly IWebHostEnvironment _environment = environment;
        private readonly IConfiguration _configuration = configuration;

        public async Task<(bool Success, string Message, string? CertificateUrl)> GenerateCertificateAsync(int testResultId, string userSapId)
        {
            try
            {
                // Get the test result with related data - include Test, User, and SpecialUser
                var testResult = await _context.TestResults
                    .Include(tr => tr.Test)
                    .Include(tr => tr.User)
                    .Include(tr => tr.SpecialUser)
                    .FirstOrDefaultAsync(tr => tr.Id == testResultId && tr.UserSapId == userSapId);

                if (testResult == null)
                {
                    _logger.LogError("Test result not found for testResultId: {TestResultId}, userSapId: {UserSapId}", testResultId, userSapId);
                    return (false, "Test result not found", null);
                }

                // Check if user is eligible for certificate (60% or above)
                if (!testResult.IsEligibleForCertificate())
                {
                    var percentage = testResult.GetScorePercentage();
                    _logger.LogInformation("Certificate generation denied for test result {TestResultId} - score {Percentage}% is below minimum 60%", testResultId, percentage);
                    return (false, $"Certificate not available. Minimum 60% score required. Your score: {percentage:F1}%", null);
                }

                // Log which type of user was found
                if (testResult.User != null)
                {
                    _logger.LogInformation("Found test result {TestResultId} for regular user {UserSapId}", testResultId, userSapId);
                }
                else if (testResult.SpecialUser != null)
                {
                    _logger.LogInformation("Found test result {TestResultId} for special user {UserSapId}", testResultId, userSapId);
                }
                else
                {
                    _logger.LogWarning("Test result {TestResultId} found but no corresponding user or special user for userSapId: {UserSapId}", testResultId, userSapId);
                }

                // Check if certificate purchase exists and is completed
                var purchase = await _context.CertificatePurchases
                    .FirstOrDefaultAsync(cp => cp.TestResultId == testResultId && cp.Status == "Completed");

                if (purchase == null)
                {
                    _logger.LogError("Certificate purchase not found or not completed for test result {TestResultId}", testResultId);
                    return (false, "Certificate purchase not found or not completed", null);
                }

                _logger.LogInformation("Found completed certificate purchase {PurchaseId} for test result {TestResultId}", purchase.Id, testResultId);

                // Check if certificate already exists
                if (!string.IsNullOrEmpty(purchase.CertificateUrl))
                {
                    _logger.LogInformation("Certificate already exists for test result {TestResultId}: {CertificateUrl}", testResultId, purchase.CertificateUrl);
                    return (true, "Certificate already generated", purchase.CertificateUrl);
                }

                // Generate certificate
                _logger.LogInformation("Starting certificate generation for test result {TestResultId}", testResultId);
                var certificateUrl = await GenerateCertificateFileAsync(testResult);
                if (string.IsNullOrEmpty(certificateUrl))
                {
                    _logger.LogError("Failed to generate certificate file for test result {TestResultId}", testResultId);
                    return (false, "Failed to generate certificate file", null);
                }

                _logger.LogInformation("Certificate file generated successfully for test result {TestResultId}: {CertificateUrl}", testResultId, certificateUrl);

                // Update purchase record with certificate URL
                purchase.CertificateUrl = certificateUrl;
                purchase.CertificateGeneratedAt = Utilities.TimeZoneHelper.GetCurrentIstTime();
                await _context.SaveChangesAsync();

                _logger.LogInformation("Certificate generated successfully for test result {TestResultId}", testResultId);
                return (true, "Certificate generated successfully", certificateUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating certificate for test result {TestResultId}", testResultId);
                return (false, "An error occurred while generating the certificate", null);
            }
        }

        public async Task<bool> HasPurchasedCertificateAsync(int testResultId)
        {
            try
            {
                return await _context.CertificatePurchases
                    .AnyAsync(cp => cp.TestResultId == testResultId && cp.Status == "Completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking certificate purchase for test result {TestResultId}", testResultId);
                return false;
            }
        }

        public async Task<CertificatePurchase?> GetCertificatePurchaseAsync(int testResultId)
        {
            try
            {
                return await _context.CertificatePurchases
                    .FirstOrDefaultAsync(cp => cp.TestResultId == testResultId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting certificate purchase for test result {TestResultId}", testResultId);
                return null;
            }
        }

        public async Task<(bool Success, string Message)> InitiateCertificatePurchaseAsync(int testResultId, string userSapId)
        {
            try
            {
                // Check if test result exists
                var testResult = await _context.TestResults
                    .FirstOrDefaultAsync(tr => tr.Id == testResultId && tr.UserSapId == userSapId);

                if (testResult == null)
                {
                    return (false, "Test result not found");
                }

                // Check if user is eligible for certificate (60% or above)
                if (!testResult.IsEligibleForCertificate())
                {
                    var percentage = testResult.GetScorePercentage();
                    return (false, $"Certificate not available. Minimum 60% score required. Your score: {percentage:F1}%");
                }

                // Check if purchase already exists
                var existingPurchase = await _context.CertificatePurchases
                    .FirstOrDefaultAsync(cp => cp.TestResultId == testResultId);

                if (existingPurchase != null)
                {
                    if (existingPurchase.Status == "Completed")
                    {
                        return (false, "Certificate already purchased");
                    }
                    else if (existingPurchase.Status == "Pending")
                    {
                        return (false, "Certificate purchase already in progress");
                    }
                }

                // Check if user exists in either Users or SpecialUsers table
                var user = await _context.Users.FindAsync(userSapId);
                var specialUser = await _context.SpecialUsers.FindAsync(userSapId);

                if (user == null && specialUser == null)
                {
                    _logger.LogError("InitiateCertificatePurchaseAsync: User SAP ID {UserSapId} not found in Users or SpecialUsers table", userSapId);
                    return (false, "User not found in Users or SpecialUsers table");
                }

                // Use the userSapId directly for the purchase record - don't create duplicate users
                string effectiveUserSapId = userSapId;
                string effectiveUsername = "";

                if (user != null)
                {
                    effectiveUsername = user.Username;
                    _logger.LogInformation("InitiateCertificatePurchaseAsync: Found user in Users table with SapId: {UserSapId}", userSapId);
                }
                else if (specialUser != null)
                {
                    effectiveUsername = specialUser.Username;
                    _logger.LogInformation("InitiateCertificatePurchaseAsync: Found user in SpecialUsers table with SapId: {UserSapId}", userSapId);
                }

                // CRITICAL DEBUG: Log user details before creating purchase
                _logger.LogInformation("InitiateCertificatePurchaseAsync: Using SapId: {EffectiveUserSapId}, Username: {EffectiveUsername}", effectiveUserSapId, effectiveUsername);

                // Create new purchase record using the original userSapId
                var purchase = new CertificatePurchase
                {
                    TestResultId = testResultId,
                    UserSapId = effectiveUserSapId, // Use the original userSapId
                    Amount = _configuration.GetValue<decimal>("Certificate:Price", 1000.00M),
                    Currency = "INR",
                    Status = "Pending",
                    CreatedAt = Utilities.TimeZoneHelper.GetCurrentIstTime()
                };

                _context.CertificatePurchases.Add(purchase);
                await _context.SaveChangesAsync();

                _logger.LogInformation("InitiateCertificatePurchaseAsync: Created new certificate purchase record with ID {PurchaseId} for test result {TestResultId} and user {EffectiveUserSapId}", purchase.Id, testResultId, effectiveUserSapId);

                return (true, "Certificate purchase initiated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initiating certificate purchase for test result {TestResultId} and user {UserSapId}", testResultId, userSapId);
                return (false, $"An error occurred while initiating the certificate purchase: {ex.Message}");
            }
        }

        public async Task<(bool Success, string Message)> CompleteCertificatePurchaseAsync(int testResultId, string transactionId)
        {
            try
            {
                var purchase = await _context.CertificatePurchases
                    .FirstOrDefaultAsync(cp => cp.TestResultId == testResultId);

                if (purchase == null)
                {
                    return (false, "Certificate purchase not found");
                }

                purchase.Status = "Completed";
                purchase.TransactionId = transactionId;
                purchase.PaidAt = Utilities.TimeZoneHelper.GetCurrentIstTime();

                await _context.SaveChangesAsync();

                _logger.LogInformation("Certificate purchase completed for test result {TestResultId} with transaction {TransactionId}",
                    testResultId, transactionId);
                return (true, "Certificate purchase completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error completing certificate purchase for test result {TestResultId}", testResultId);
                return (false, "An error occurred while completing the certificate purchase");
            }
        }

        private async Task<string?> GenerateCertificateFileAsync(TestResult testResult)
        {
            try
            {
                // Create certificates directory if it doesn't exist
                var certificatesDir = Path.Combine(_environment.WebRootPath, "certificates");
                if (!Directory.Exists(certificatesDir))
                {
                    Directory.CreateDirectory(certificatesDir);
                }

                // Generate unique filename
                var fileName = $"certificate_{testResult.Id}_{Guid.NewGuid()}.png";
                var filePath = Path.Combine(certificatesDir, fileName);

                // Generate certificate image using the template
                var success = await GenerateCertificateImageAsync(testResult, filePath);
                if (!success)
                {
                    return null;
                }

                // Check if fallback text certificate was created instead
                var fallbackPath = Path.ChangeExtension(filePath, ".txt");
                if (File.Exists(fallbackPath) && !File.Exists(filePath))
                {
                    // Fallback text certificate was created
                    var fallbackFileName = Path.GetFileName(fallbackPath);
                    return $"/certificates/{fallbackFileName}";
                }

                // Return relative URL for image certificate
                return $"/certificates/{fileName}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating certificate file for test result {TestResultId}", testResult.Id);
                return null;
            }
        }

        private async Task<bool> GenerateCertificateImageAsync(TestResult testResult, string outputPath)
        {
            try
            {
                // Path to the template image
                var templatePath = Path.Combine(_environment.WebRootPath, "images", "TestPortal.jpeg");
                if (!File.Exists(templatePath))
                {
                    _logger.LogError("Certificate template image not found at {TemplatePath}", templatePath);
                    return false;
                }

                _logger.LogInformation("Starting certificate generation for test result {TestResultId}, template: {TemplatePath}, output: {OutputPath}",
                    testResult.Id, templatePath, outputPath);

                // Ensure output directory exists
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                    _logger.LogInformation("Created output directory: {OutputDir}", outputDir);
                }

                // Prepare user data - check both Users and SpecialUsers tables
                var scorePercentage = testResult.GetScorePercentage();
                var rating = testResult.GetScoreRating();
                var testTitle = testResult.Test?.Title ?? "Assessment";
                var completionDate = testResult.SubmittedAt.ToString("MMMM dd, yyyy");

                // Get user name from either Users or SpecialUsers navigation properties
                string userName = testResult.Username; // Default fallback

                if (testResult.User != null)
                {
                    // User exists in Users table
                    userName = $"{testResult.User.FirstName} {testResult.User.LastName}".Trim();
                    if (string.IsNullOrEmpty(userName))
                    {
                        userName = testResult.User.Username;
                    }
                    _logger.LogInformation("Using regular user name for certificate: {UserName}", userName);
                }
                else if (testResult.SpecialUser != null)
                {
                    // User exists in SpecialUsers table - use FullName if available, fallback to Username
                    userName = !string.IsNullOrEmpty(testResult.SpecialUser.FullName)
                        ? testResult.SpecialUser.FullName
                        : testResult.SpecialUser.Username;
                    _logger.LogInformation("Using special user name for certificate: {UserName} (from {Source})",
                        userName, !string.IsNullOrEmpty(testResult.SpecialUser.FullName) ? "FullName" : "Username");
                }
                else
                {
                    _logger.LogWarning("No user or special user found for certificate generation, using fallback username: {UserName}", userName);
                }

                // Load the template image using ImageSharp
                using var image = await Image.LoadAsync(templatePath);

                // Create a font collection and load fonts
                var fontCollection = new FontCollection();
                Font nameFont, ratingFont;

                try
                {
                    // Load Great Vibes font for both name and rating
                    var greatVibesPath = Path.Combine(_environment.WebRootPath, "fonts", "GreatVibes-Regular.ttf");

                    if (File.Exists(greatVibesPath))
                    {
                        try
                        {
                            var greatVibesFamily = fontCollection.Add(greatVibesPath);
                            nameFont = greatVibesFamily.CreateFont(60, FontStyle.Regular); // Increased from 42 to 52
                            ratingFont = greatVibesFamily.CreateFont(60, FontStyle.Regular); // Increased from 36 to 46
                            _logger.LogInformation("Great Vibes font loaded and applied successfully from {FontPath}", greatVibesPath);
                        }
                        catch (Exception fontEx)
                        {
                            _logger.LogError(fontEx, "Failed to load Great Vibes font from {FontPath}, using fallback", greatVibesPath);
                            // Fallback to system font if Great Vibes loading fails
                            var fallbackFamily = SystemFonts.Get("Arial");
                            nameFont = fallbackFamily.CreateFont(60, FontStyle.Bold); // Increased from 36 to 46
                            ratingFont = fallbackFamily.CreateFont(60, FontStyle.Bold); // Increased from 32 to 40
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Great Vibes font not found at {FontPath}, using fallback", greatVibesPath);
                        // Fallback to system font if Great Vibes is not available
                        var fallbackFamily = SystemFonts.Get("Arial");
                        nameFont = fallbackFamily.CreateFont(46, FontStyle.Bold); // Increased from 36 to 46
                        ratingFont = fallbackFamily.CreateFont(40, FontStyle.Bold); // Increased from 32 to 40
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading fonts, falling back to system fonts");
                    // Fallback to system fonts
                    var fontFamily = SystemFonts.Get("Arial");
                    nameFont = fontFamily.CreateFont(46, FontStyle.Bold); // Increased from 36 to 46
                    ratingFont = fontFamily.CreateFont(40, FontStyle.Bold); // Increased from 32 to 40
                }

                // Define colors based on the reference image
                // Name color (Divyansh Sharma) - golden/brown color from reference
                var nameColor = Color.FromRgb(184, 134, 11); // Golden brown color
                // Rating color (Good Performer) - golden/brown color from reference
                var ratingColor = Color.FromRgb(184, 134, 11); // Same golden brown color

                // Get image dimensions for positioning
                var width = image.Width;
                var height = image.Height;

                // Draw only the essential text on the image - simplified design
                image.Mutate(ctx =>
                {
                    // Draw user name positioned a bit lower than before
                    // Moved from 40% to 45% from the top for better positioning
                    var nameBounds = TextMeasurer.MeasureBounds(userName, new TextOptions(nameFont));
                    ctx.DrawText(userName, nameFont, nameColor,
                        new PointF((width - nameBounds.Width) / 2, height * 0.45f));

                    // Draw performance rating positioned a bit lower than before
                    // Moved from 65% to 70% from the top for better positioning
                    var ratingBounds = TextMeasurer.MeasureBounds(rating, new TextOptions(ratingFont));
                    ctx.DrawText(rating, ratingFont, ratingColor,
                        new PointF((width - ratingBounds.Width) / 2, height * 0.70f));
                });

                // Save the final image
                await image.SaveAsPngAsync(outputPath, new PngEncoder());

                _logger.LogInformation("Certificate image generated successfully at {OutputPath}", outputPath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating certificate image for test result {TestResultId}. Error: {ErrorMessage}, StackTrace: {StackTrace}",
                    testResult.Id, ex.Message, ex.StackTrace);

                // Try to create a simple text-based certificate as fallback
                try
                {
                    _logger.LogInformation("Attempting to create fallback text certificate for test result {TestResultId}", testResult.Id);
                    await CreateFallbackCertificateAsync(testResult, outputPath);
                    return true;
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogError(fallbackEx, "Failed to create fallback certificate for test result {TestResultId}", testResult.Id);
                    return false;
                }
            }
        }

        private static string? GetSystemFontPath(string fontName)
        {
            try
            {
                // Try common font paths for different operating systems
                var possiblePaths = new[]
                {
                    // Windows
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), $"{fontName}.ttf"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", $"{fontName}.ttf"),
                    // macOS
                    $"/System/Library/Fonts/{fontName}.ttc",
                    $"/System/Library/Fonts/{fontName}.ttf",
                    $"/Library/Fonts/{fontName}.ttf",
                    // Linux
                    $"/usr/share/fonts/truetype/dejavu/{fontName}.ttf",
                    $"/usr/share/fonts/truetype/liberation/{fontName}.ttf"
                };

                return possiblePaths.FirstOrDefault(File.Exists);
            }
            catch
            {
                return null;
            }
        }

        private async Task CreateFallbackCertificateAsync(TestResult testResult, string outputPath)
        {
            // Create a simple text file as a fallback certificate with simplified format
            var rating = testResult.GetScoreRating();

            // Get user name from either Users or SpecialUsers navigation properties
            string userName = testResult.Username; // Default fallback

            if (testResult.User != null)
            {
                userName = $"{testResult.User.FirstName} {testResult.User.LastName}".Trim();
                if (string.IsNullOrEmpty(userName))
                {
                    userName = testResult.User.Username;
                }
            }
            else if (testResult.SpecialUser != null)
            {
                // Use FullName if available, fallback to Username
                userName = !string.IsNullOrEmpty(testResult.SpecialUser.FullName)
                    ? testResult.SpecialUser.FullName
                    : testResult.SpecialUser.Username;
            }

            // Simplified certificate content with only name and rating
            var certificateContent = $@"CERTIFICATE

{userName}

{rating}
";

            // Change extension to .txt for fallback
            var fallbackPath = Path.ChangeExtension(outputPath, ".txt");
            await File.WriteAllTextAsync(fallbackPath, certificateContent);

            _logger.LogInformation("Fallback text certificate created at {FallbackPath}", fallbackPath);
        }
    }
}
