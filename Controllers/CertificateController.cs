using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineAssessment.Web.Models;
using OnlineAssessment.Web.Services;
using System.Security.Claims;

namespace OnlineAssessment.Web.Controllers
{
    public class CertificateController : Controller
    {
        private readonly ICertificateService _certificateService;
        private readonly IRazorpayService _razorpayService;
        private readonly ILogger<CertificateController> _logger;
        private readonly IWebHostEnvironment _environment;
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;

        public CertificateController(
            ICertificateService certificateService,
            IRazorpayService razorpayService,
            ILogger<CertificateController> logger,
            IWebHostEnvironment environment,
            AppDbContext context,
            IConfiguration configuration)
        {
            _certificateService = certificateService;
            _razorpayService = razorpayService;
            _logger = logger;
            _environment = environment;
            _context = context;
            _configuration = configuration;
        }

        // GET: Certificate/Purchase/{testResultId}
        [HttpGet]
        [Route("Certificate/Purchase/{testResultId}")]
        [AllowAnonymous] // Allow anonymous access to handle session expiry
        public async Task<IActionResult> Purchase(int testResultId)
        {
            try
            {
                var userSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                // If user is not authenticated, try to get user from test result
                if (string.IsNullOrEmpty(userSapId))
                {
                    // Check if there's a valid test result and get the user from it
                    var testResult = await _context.TestResults
                        .FirstOrDefaultAsync(tr => tr.Id == testResultId);

                    if (testResult == null)
                    {
                        TempData["ErrorMessage"] = "Test result not found.";
                        return RedirectToAction("Login", "Auth");
                    }

                    userSapId = testResult.UserSapId;

                    // Verify this is a special user
                    var isSpecialUser = await _context.SpecialUsers.AnyAsync(su => su.UsersSapId == userSapId);
                    if (!isSpecialUser)
                    {
                        TempData["ErrorMessage"] = "Certificate purchase is only available for special users. Please login.";
                        return RedirectToAction("Login", "Auth");
                    }

                    _logger.LogInformation("Certificate purchase accessed without session for special user {UserSapId} and test result {TestResultId}",
                        userSapId, testResultId);
                }
                else
                {
                    // Verify authenticated user owns this test result
                    var testResult = await _context.TestResults
                        .FirstOrDefaultAsync(tr => tr.Id == testResultId && tr.UserSapId == userSapId);

                    if (testResult == null)
                    {
                        TempData["ErrorMessage"] = "Test result not found or access denied.";
                        return RedirectToAction("Index", "Test");
                    }
                }

                // Check if certificate already purchased
                var existingPurchase = await _certificateService.GetCertificatePurchaseAsync(testResultId);
                if (existingPurchase != null && existingPurchase.Status == "Completed")
                {
                    TempData["ErrorMessage"] = "Certificate has already been purchased for this test result.";
                    return RedirectToAction("Result", "Test", new { id = testResultId });
                }

                // Clean up any stale initiated/pending purchases for this test result
                var stalePurchases = await _context.CertificatePurchases
                    .Where(cp => cp.TestResultId == testResultId &&
                                (cp.Status == "Initiated" || cp.Status == "Pending") &&
                                cp.CreatedAt < DateTime.UtcNow.AddHours(-1)) // Older than 1 hour
                    .ToListAsync();

                if (stalePurchases.Any())
                {
                    _context.CertificatePurchases.RemoveRange(stalePurchases);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Cleaned up {Count} stale purchase records for test result {TestResultId}",
                        stalePurchases.Count, testResultId);
                }

                // Refresh the existing purchase check after cleanup
                existingPurchase = await _certificateService.GetCertificatePurchaseAsync(testResultId);

                // Prepare payment details
                var amount = _configuration.GetValue<decimal>("Certificate:Price", 1000.00M); // Certificate price from configuration
                var productInfo = "Test Certificate";
                var phone = "9999999999"; // Default phone number

                // Generate transaction ID (keep under 40 characters for Razorpay receipt requirement)
                var txnId = $"CERT{testResultId}_{DateTime.Now:yyyyMMddHHmmss}";

                // Get user details for payment
                var firstName = User.FindFirst(ClaimTypes.Name)?.Value ?? "User";
                var email = User.FindFirst(ClaimTypes.Email)?.Value ?? "";

                // Create or update pending purchase record in database (similar to booking system)
                CertificatePurchase pendingPurchase;
                if (existingPurchase != null && existingPurchase.Status != "Completed")
                {
                    // Update existing purchase record
                    existingPurchase.Status = "Pending";
                    existingPurchase.TransactionId = txnId;
                    existingPurchase.CreatedAt = Utilities.TimeZoneHelper.GetCurrentIstTime();
                    pendingPurchase = existingPurchase;
                    _logger.LogInformation("Updated existing pending certificate purchase for test result {TestResultId}", testResultId);
                }
                else
                {
                    // Create a new pending purchase record
                    pendingPurchase = new CertificatePurchase
                    {
                        TestResultId = testResultId,
                        UserSapId = userSapId,
                        Amount = amount,
                        Currency = "INR",
                        Status = "Pending",
                        CreatedAt = Utilities.TimeZoneHelper.GetCurrentIstTime(),
                        TransactionId = txnId
                    };

                    _context.CertificatePurchases.Add(pendingPurchase);
                    _logger.LogInformation("Created new pending certificate purchase for test result {TestResultId}", testResultId);
                }

                await _context.SaveChangesAsync();

                // Create Razorpay order
                var orderRequest = _razorpayService.PrepareOrderRequest(txnId, amount.ToString("F2"), productInfo, firstName, email, phone, testResultId.ToString());
                var (orderSuccess, orderId, errorMessage) = await _razorpayService.CreateOrderAsync(orderRequest);

                if (!orderSuccess)
                {
                    TempData["ErrorMessage"] = $"Failed to create payment order: {errorMessage}";
                    return RedirectToAction("Result", "Test", new { id = testResultId });
                }

                // Prepare checkout options for certificate purchase (no callback URL, handled by JavaScript)
                var checkoutOptions = _razorpayService.PrepareCheckoutOptions(orderId, amount.ToString("F2"), productInfo, firstName, email, phone, testResultId.ToString());

                // Remove callback URL and redirect for certificate purchases since we handle it in JavaScript
                checkoutOptions.Remove("callback_url");
                checkoutOptions["redirect"] = false;

                _logger.LogInformation("Certificate purchase checkout options prepared: {CheckoutOptions}",
                    System.Text.Json.JsonSerializer.Serialize(checkoutOptions));

                // Pass data to view (no TempData needed, everything is in database)
                ViewBag.TestResultId = testResultId;
                ViewBag.Amount = amount;
                ViewBag.OrderId = orderId;
                ViewBag.TransactionId = txnId;
                ViewBag.CheckoutOptions = checkoutOptions;
                ViewBag.PurchaseId = pendingPurchase.Id; // Pass purchase ID for reference

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initiating certificate purchase for test result {TestResultId}", testResultId);
                TempData["ErrorMessage"] = "An error occurred while initiating the certificate purchase.";
                return RedirectToAction("Result", "Test", new { id = testResultId });
            }
        }

        // POST: Certificate/ProcessPayment
        [HttpPost]
        [Route("Certificate/ProcessPayment")]
        [AllowAnonymous] // Allow anonymous access for payment callback
        public async Task<IActionResult> ProcessPayment(
            int testResultId,
            string razorpayPaymentId,
            string razorpayOrderId,
            string razorpaySignature)
        {
            try
            {
                // Find the pending purchase record for this test result (similar to booking system)
                var pendingPurchase = await _context.CertificatePurchases
                    .Where(cp => cp.TestResultId == testResultId && cp.Status == "Pending")
                    .OrderByDescending(cp => cp.CreatedAt)
                    .FirstOrDefaultAsync();

                if (pendingPurchase == null)
                {
                    _logger.LogWarning("Certificate payment processing failed: No pending purchase found. TestResultId: {TestResultId}", testResultId);
                    return Json(new { success = false, message = "Payment session expired. Please try again." });
                }

                var userSapId = pendingPurchase.UserSapId;

                // Verify payment with Razorpay
                var isPaymentValid = _razorpayService.VerifyPaymentSignature(razorpayOrderId, razorpayPaymentId, razorpaySignature);
                if (!isPaymentValid)
                {
                    _logger.LogWarning("Invalid payment signature for certificate purchase. TestResultId: {TestResultId}, PaymentId: {PaymentId}",
                        testResultId, razorpayPaymentId);

                    // Update purchase status to Failed (similar to booking system)
                    pendingPurchase.Status = "Failed";
                    pendingPurchase.TransactionId = razorpayPaymentId; // Store the payment ID even for failed payments
                    await _context.SaveChangesAsync();

                    return Json(new { success = false, message = "Payment verification failed" });
                }

                // Update the purchase record with payment details (similar to booking system)
                pendingPurchase.TransactionId = razorpayPaymentId;
                pendingPurchase.Status = "Completed";
                pendingPurchase.PaidAt = Utilities.TimeZoneHelper.GetCurrentIstTime();

                await _context.SaveChangesAsync();

                // Generate the certificate
                var (certSuccess, certMessage, certificateUrl) = await _certificateService.GenerateCertificateAsync(testResultId, userSapId);
                if (!certSuccess)
                {
                    _logger.LogError("Failed to generate certificate after successful payment. TestResultId: {TestResultId}, PaymentId: {PaymentId}",
                        testResultId, razorpayPaymentId);
                    return Json(new { success = false, message = "Payment successful but certificate generation failed. Please contact support." });
                }

                _logger.LogInformation("Certificate purchased and generated successfully for test result {TestResultId} by user {UserSapId}",
                    testResultId, userSapId);

                // Return success response with redirect to certificate download page
                return Json(new
                {
                    success = true,
                    message = "Certificate purchased successfully! Redirecting to download...",
                    certificateUrl = certificateUrl,
                    redirectUrl = $"/Certificate/DownloadPage/{testResultId}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing certificate payment for test result {TestResultId}", testResultId);
                return Json(new { success = false, message = "An error occurred while processing the payment" });
            }
        }

        // GET: Certificate/Download/{testResultId}
        [HttpGet]
        [Route("Certificate/Download/{testResultId}")]
        [AllowAnonymous] // Allow anonymous access for certificate download
        public async Task<IActionResult> Download(int testResultId)
        {
            try
            {
                var userSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                // If user is not authenticated, try to get user from certificate purchase
                if (string.IsNullOrEmpty(userSapId))
                {
                    var existingPurchase = await _certificateService.GetCertificatePurchaseAsync(testResultId);
                    if (existingPurchase == null || existingPurchase.Status != "Completed")
                    {
                        TempData["ErrorMessage"] = "Certificate not found or not yet generated. Please login.";
                        return RedirectToAction("Login", "Auth");
                    }

                    userSapId = existingPurchase.UserSapId;

                    // Verify this is a special user
                    var isSpecialUser = await _context.SpecialUsers.AnyAsync(su => su.UsersSapId == userSapId);
                    if (!isSpecialUser)
                    {
                        TempData["ErrorMessage"] = "Certificate download is only available for special users. Please login.";
                        return RedirectToAction("Login", "Auth");
                    }

                    _logger.LogInformation("Certificate download accessed without session for special user {UserSapId} and test result {TestResultId}",
                        userSapId, testResultId);
                }

                // Check if certificate has been purchased and generated
                var purchase = await _certificateService.GetCertificatePurchaseAsync(testResultId);
                if (purchase == null || purchase.Status != "Completed" || string.IsNullOrEmpty(purchase.CertificateUrl))
                {
                    TempData["ErrorMessage"] = "Certificate not found or not yet generated.";
                    return RedirectToAction("Result", "Test", new { id = testResultId });
                }

                // Verify that this user owns the certificate
                if (purchase.UserSapId != userSapId)
                {
                    return Forbid();
                }

                // Get the physical file path
                var certificateFileName = Path.GetFileName(purchase.CertificateUrl);
                var certificatePath = Path.Combine(_environment.WebRootPath, "certificates", certificateFileName);

                if (!System.IO.File.Exists(certificatePath))
                {
                    TempData["ErrorMessage"] = "Certificate file not found.";
                    return RedirectToAction("Result", "Test", new { id = testResultId });
                }

                // Return the file for download
                var fileBytes = await System.IO.File.ReadAllBytesAsync(certificatePath);

                // Determine content type and filename based on file extension
                var extension = Path.GetExtension(certificateFileName).ToLower();
                string contentType;
                string downloadFileName;

                if (extension == ".txt")
                {
                    contentType = "text/plain";
                    downloadFileName = $"Certificate_{testResultId}_{DateTime.Now:yyyyMMdd}.txt";
                }
                else
                {
                    contentType = "image/jpeg";
                    downloadFileName = $"Certificate_{testResultId}_{DateTime.Now:yyyyMMdd}.jpg";
                }

                return File(fileBytes, contentType, downloadFileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading certificate for test result {TestResultId}", testResultId);
                TempData["ErrorMessage"] = "An error occurred while downloading the certificate.";
                return RedirectToAction("Result", "Test", new { id = testResultId });
            }
        }



        // API endpoint to check certificate status
        [HttpGet]
        [Route("Certificate/api/status/{testResultId}")]
        [AllowAnonymous] // Allow anonymous access for certificate status check
        public async Task<IActionResult> GetCertificateStatus(int testResultId)
        {
            try
            {
                var userSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

                _logger.LogInformation("Certificate status check: TestResultId={TestResultId}, UserSapId={UserSapId}, UserRole={UserRole}",
                    testResultId, userSapId, userRole);

                // If user is not authenticated, try to get user from test result
                if (string.IsNullOrEmpty(userSapId))
                {
                    var testResult = await _context.TestResults
                        .FirstOrDefaultAsync(tr => tr.Id == testResultId);

                    if (testResult == null)
                    {
                        _logger.LogWarning("Certificate status check failed: Test result not found");
                        return Json(new { success = false, message = "Test result not found" });
                    }

                    userSapId = testResult.UserSapId;

                    // Verify this is a special user
                    var isSpecialUser = await _context.SpecialUsers.AnyAsync(su => su.UsersSapId == userSapId);
                    if (!isSpecialUser)
                    {
                        _logger.LogWarning("Certificate status check failed: Not a special user");
                        return Json(new { success = false, message = "Certificate access is only available for special users" });
                    }

                    _logger.LogInformation("Certificate status check accessed without session for special user {UserSapId}", userSapId);
                }

                var purchase = await _certificateService.GetCertificatePurchaseAsync(testResultId);
                if (purchase == null)
                {
                    _logger.LogInformation("Certificate status: No purchase found for test result {TestResultId}", testResultId);
                    return Json(new
                    {
                        success = true,
                        hasPurchased = false,
                        status = "Not Purchased",
                        certificateUrl = (string?)null,
                        debugInfo = new { userSapId, userRole, testResultId }
                    });
                }

                // Verify that this user owns the certificate
                if (purchase.UserSapId != userSapId)
                {
                    _logger.LogWarning("Certificate access denied: Purchase belongs to {PurchaseUserSapId} but request from {UserSapId}",
                        purchase.UserSapId, userSapId);
                    return Forbid();
                }

                _logger.LogInformation("Certificate status: Found purchase for test result {TestResultId}, Status={Status}, CertificateUrl={CertificateUrl}",
                    testResultId, purchase.Status, purchase.CertificateUrl);

                return Json(new
                {
                    success = true,
                    hasPurchased = purchase.Status == "Completed",
                    status = purchase.Status,
                    certificateUrl = purchase.CertificateUrl,
                    purchaseDate = purchase.PaidAt?.ToString("yyyy-MM-dd HH:mm:ss"),
                    debugInfo = new { userSapId, userRole, testResultId, purchaseId = purchase.Id }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting certificate status for test result {TestResultId}", testResultId);
                return Json(new { success = false, message = "An error occurred while checking certificate status", error = ex.Message });
            }
        }

        // GET: Certificate/DownloadPage/{testResultId}
        [HttpGet]
        [Route("Certificate/DownloadPage/{testResultId}")]
        public async Task<IActionResult> DownloadPage(int testResultId)
        {
            try
            {
                var userSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userSapId))
                {
                    TempData["ErrorMessage"] = "Please login to access your certificate.";
                    return RedirectToAction("Login", "Auth");
                }

                // Get the test result with related data
                var testResult = await _context.TestResults
                    .Include(tr => tr.Test)
                    .Include(tr => tr.User)
                    .Include(tr => tr.SpecialUser)
                    .FirstOrDefaultAsync(tr => tr.Id == testResultId && tr.UserSapId == userSapId);

                if (testResult == null)
                {
                    TempData["ErrorMessage"] = "Test result not found.";
                    return RedirectToAction("MyBookings", "Test");
                }

                // Check if certificate purchase exists and is completed
                var purchase = await _context.CertificatePurchases
                    .FirstOrDefaultAsync(cp => cp.TestResultId == testResultId && cp.Status == "Completed");

                if (purchase == null)
                {
                    TempData["ErrorMessage"] = "Certificate not purchased or payment not completed.";
                    return RedirectToAction("Result", "Test", new { id = testResultId });
                }

                // Prepare view data
                ViewBag.TestResultId = testResultId;
                ViewBag.CertificateUrl = purchase.CertificateUrl;
                ViewBag.TestTitle = testResult.Test?.Title ?? "Assessment";

                // Get user name
                string userName = testResult.Username;
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

                ViewBag.UserName = userName;
                ViewBag.Score = $"{testResult.CorrectAnswers}/{testResult.TotalQuestions} ({testResult.GetScorePercentage():F1}%)";
                ViewBag.Rating = testResult.GetScoreRating().ToUpper();

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading certificate download page for test result {TestResultId}", testResultId);
                TempData["ErrorMessage"] = "An error occurred while loading the certificate download page.";
                return RedirectToAction("MyBookings", "Test");
            }
        }
    }
}
