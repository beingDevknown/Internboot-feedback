using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineAssessment.Web.Models;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using System.Linq;
using OnlineAssessment.Web.Services;
using MySqlConnector;

namespace OnlineAssessment.Web.Controllers
{
    public class TestController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<TestController> _logger;
        private readonly IEmailService _emailService;
        private readonly ICertificateService _certificateService;

        public TestController(AppDbContext context, IWebHostEnvironment environment, ILogger<TestController> logger, IEmailService emailService, ICertificateService certificateService)
        {
            _context = context;
            _environment = environment;
            _logger = logger;
            _emailService = emailService;
            _certificateService = certificateService;
        }

        // View action for test list page
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Index(string message = null, string error = null, bool clear = false, int? testCreated = null, bool refresh = false, bool ajax = false, bool isReattempt = false, bool clearSession = false)
        {
            // Clear session data if requested
            if (clearSession)
            {
                // Clear all session data
                HttpContext.Session.Clear();
                _logger.LogInformation("Session data cleared by user request");

                // Clear all cookies
                foreach (var cookie in Request.Cookies.Keys)
                {
                    Response.Cookies.Delete(cookie);
                }
                _logger.LogInformation("Cookies cleared by user request");

                // Redirect to the same page without the clearSession parameter
                return RedirectToAction("Index", new { message = "Session data and cookies cleared successfully" });
            }
            try
            {
                // Check if the error is related to session data loss and the user has bookings
                if (error != null && error.Contains("session data was lost"))
                {
                    // Get the current user SAP ID
                    var currentUserSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    if (!string.IsNullOrEmpty(currentUserSapId))
                    {
                        // Check if the user has any bookings
                        var hasBookings = await _context.TestBookings
                            .AsNoTracking()
                            .AnyAsync(tb => tb.UserSapId == currentUserSapId);

                        if (hasBookings)
                        {
                            // User has bookings, so suppress the error
                            _logger.LogInformation("Suppressing session error message because user has bookings");
                            error = null;
                            message = "Your booking was created successfully. You can view it in 'My Bookings'.";
                        }
                    }
                }

                // We're removing the automatic redirect to MyBookings
                // This allows users to see the test list after booking
                // They can still access their bookings through the navigation menu

                // If clear parameter is true or we have TestCompleted flag, clear all test-related TempData
                if (clear || TempData.ContainsKey("TestCompleted"))
                {
                    // Clear all test-related TempData
                    if (TempData.ContainsKey("TestCompleted"))
                    {
                        TempData.Remove("TestCompleted");
                    }
                    if (TempData.ContainsKey("JustPaid"))
                    {
                        TempData.Remove("JustPaid");
                    }
                    if (TempData.ContainsKey("BookedTestId"))
                    {
                        TempData.Remove("BookedTestId");
                    }
                    if (TempData.ContainsKey("BookedDate"))
                    {
                        TempData.Remove("BookedDate");
                    }
                    if (TempData.ContainsKey("BookedStartTime"))
                    {
                        TempData.Remove("BookedStartTime");
                    }
                    if (TempData.ContainsKey("BookedEndTime"))
                    {
                        TempData.Remove("BookedEndTime");
                    }

                    // Add a success message if coming from test completion
                    if (string.IsNullOrEmpty(message) && clear)
                    {
                        message = "You can now browse available tests or check your test history.";
                    }
                }

                // Set message and error in ViewBag
                if (testCreated.HasValue)
                {
                    // Get the test title if available
                    string testTitle = "Test";
                    try
                    {
                        var createdTest = await _context.Tests.FindAsync(testCreated.Value);
                        if (createdTest != null)
                        {
                            testTitle = createdTest.Title;
                        }
                    }
                    catch {}

                    ViewBag.SuccessMessage = $"{testTitle} created successfully! You can now manage it from this page.";
                }
                else if (!string.IsNullOrEmpty(message))
                {
                    ViewBag.SuccessMessage = message;
                }

                if (!string.IsNullOrEmpty(error))
                {
                    ViewBag.ErrorMessage = error;
                }

                // If this is an AJAX request for refresh checking, return a simple response
                if (ajax && refresh)
                {
                    return Ok();
                }

                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
                var username = User.Identity?.Name ?? "Guest";
                var userEmail = User.FindFirst(ClaimTypes.Email)?.Value;

                // If SapId is missing but we have email, try to find the user by email
                if (string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(userEmail) && !string.IsNullOrEmpty(userRole))
                {
                    _logger.LogWarning($"TestController Index - SapId claim is missing. Attempting to find user by email: {userEmail}");

                    try
                    {
                        if (userRole == "Candidate")
                        {
                            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == userEmail.ToLower());
                            if (user != null)
                            {
                                userId = user.SapId;
                                _logger.LogInformation($"TestController Index - Found user by email. SapId: {userId}");
                            }
                        }
                        else if (userRole == "Organization")
                        {
                            var org = await _context.Organizations.FirstOrDefaultAsync(o => o.Email.ToLower() == userEmail.ToLower());
                            if (org != null)
                            {
                                userId = org.SapId;
                                _logger.LogInformation($"TestController Index - Found organization by email. SapId: {userId}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "TestController Index - Error finding user by email");
                    }
                }

                ViewBag.IsAdmin = false;
                ViewBag.Username = username;

                // Payment is now handled during test booking, not after registration
                // No need to check if the user has paid here

                if (userRole == "Organization" && userId != null)
                {
                    var organizationSapId = userId;

                    // Always allow organizations to create tests
                    ViewBag.HasActiveSubscription = true;
                    ViewBag.CanCreateMcq = true;
                    ViewBag.CanCreateCoding = true;

                    try
                    {
                        // PERFORMANCE OPTIMIZATION: Use a single query with projection to get only needed fields
                        // This avoids the two-step query process and reduces data transfer
                        // No need to filter by IsDeleted since we're now doing hard deletes
                        var tests = await _context.Tests
                            .AsNoTracking()
                            .Where(t => t.CreatedBySapId == organizationSapId)
                            .Select(t => new Test
                            {
                                Id = t.Id,
                                Title = t.Title,
                                Description = t.Description,
                                Domain = t.Domain,
                                CreatedAt = t.CreatedAt,
                                DurationMinutes = t.DurationMinutes,
                                PassingScore = t.PassingScore,
                                Price = t.Price,
                                HasUploadedFile = t.HasUploadedFile,
                                QuestionCount = t.QuestionCount,
                                CategoryQuestionsId = t.CategoryQuestionsId
                                // Exclude other properties to reduce data transfer
                            })
                            .ToListAsync();

                        // Add response caching headers
                        Response.Headers["Cache-Control"] = "private, max-age=60"; // Cache for 1 minute

                        _logger.LogInformation("Successfully loaded {Count} tests for organization {OrganizationSapId} using optimized query", tests.Count, organizationSapId);
                        return View(tests);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error retrieving tests for organization {OrganizationSapId}", organizationSapId);
                        return View(new List<Test>());
                    }
                }
                else if (userRole == "Candidate" && userId != null)
                {
                    // For candidates, get their domain from the user record
                    var candidateSapId = userId;
                    var candidate = await _context.Users.FindAsync(candidateSapId);

                    // Get the list of tests that the candidate has booked with confirmed or pending status
                    // We don't want to exclude tests with failed bookings
                    // This is only used for tracking purposes, not for filtering available tests
                    var bookedTestIds = await _context.TestBookings
                        .Where(tb => tb.UserSapId == candidateSapId &&
                               (tb.Status == "Confirmed" || tb.Status == "Pending") &&
                               tb.Status != "Failed") // Explicitly exclude failed bookings
                        .Select(tb => tb.TestId)
                        .ToListAsync();

                    // Check if the user has any failed bookings
                    var failedBookings = await _context.TestBookings
                        .Where(tb => tb.UserSapId == candidateSapId && tb.Status == "Failed")
                        .ToListAsync();

                    if (failedBookings.Any())
                    {
                        // If there are failed bookings, set a message to inform the user
                        if (string.IsNullOrEmpty(error))
                        {
                            error = "You have one or more failed bookings. Please book a new slot to take the test.";
                        }
                    }

                    ViewBag.BookedTests = bookedTestIds;

                    // Set the timezone to IST for date/time operations
                    ViewBag.TimeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

                    // Even if the candidate doesn't have a category, we should still show tests
                    if (candidate != null)
                    {
                        try
                        {
                            // Get upcoming bookings for this candidate (only confirmed or pending)
                            // Use a simpler query to avoid NULL casting issues
                            var now = DateTime.Now;

                            // First get the IDs of upcoming bookings
                            // Use a try-catch block to handle potential NULL values
                            try
                            {
                                // Modified query to avoid using NotMapped properties (EndTime)
                                var upcomingBookingIds = await _context.TestBookings
                                    .Where(tb => tb.UserSapId == candidateSapId &&
                                               (tb.BookingDate > now.Date || tb.BookingDate == now.Date) &&
                                               (tb.Status == "Confirmed" || tb.Status == "Pending"))
                                    .Select(tb => tb.Id)
                                    .ToListAsync();

                                _logger.LogInformation($"Found {upcomingBookingIds.Count} upcoming booking IDs for candidate {candidateSapId}");

                                // Then fetch the full bookings with includes
                                var upcomingBookings = new List<TestBooking>();
                                if (upcomingBookingIds.Any())
                                {
                                    upcomingBookings = await _context.TestBookings
                                        .Include(tb => tb.Test)
                                        .Where(tb => upcomingBookingIds.Contains(tb.Id))
                                        .OrderBy(tb => tb.BookingDate)
                                        .ThenBy(tb => tb.Id) // Changed from StartTime to Id since StartTime is NotMapped
                                        .ToListAsync();
                                }

                                ViewBag.UpcomingBookings = upcomingBookings;
                                _logger.LogInformation($"Successfully loaded {upcomingBookings.Count} upcoming bookings for candidate {candidateSapId}");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error loading upcoming bookings for candidate {CandidateSapId}", candidateSapId);
                                ViewBag.UpcomingBookings = new List<TestBooking>();
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error loading upcoming bookings for candidate {CandidateSapId}", candidateSapId);
                            ViewBag.UpcomingBookings = new List<TestBooking>();
                        }

                        // Get tests that match the candidate's domain and are not deleted
                        // AND are not already booked by the user
                        // Remove tests from available list if user already has an upcoming booking for the same test
                        var upcomingBookingsList = ViewBag.UpcomingBookings as List<TestBooking> ?? new List<TestBooking>();
                        var upcomingTestIds = upcomingBookingsList.Select(tb => tb.TestId).ToList();

                        _logger.LogInformation($"Found {upcomingTestIds.Count} upcoming test IDs for candidate {candidateSapId}");
                        // Handle NULL values in Domain field
                        var candidateCategory = candidate.Category;

                        // Log the candidate's category
                        _logger.LogInformation($"Candidate {candidateSapId} has category: {(candidateCategory ?? "NULL")}");

                        List<Test> tests = new List<Test>();

                        try
                        {
                            // Get tests based on user's organization
                            var userOrganizationSapId = candidate.OrganizationSapId;

                            List<int> allTestIds;
                            if (!string.IsNullOrEmpty(userOrganizationSapId))
                            {
                                // Filter tests by user's organization
                                allTestIds = await _context.Tests
                                    .AsNoTracking()
                                    .Where(t => t.CreatedBySapId == userOrganizationSapId)
                                    .Select(t => t.Id)
                                    .ToListAsync();

                                _logger.LogInformation($"Found {allTestIds.Count} tests for organization {userOrganizationSapId}");
                            }
                            else
                            {
                                // Fallback: show all tests if user has no organization (backward compatibility)
                                allTestIds = await _context.Tests
                                    .AsNoTracking()
                                    .Select(t => t.Id)
                                    .ToListAsync();

                                _logger.LogInformation($"User has no organization, showing all {allTestIds.Count} tests");
                            }

                            _logger.LogInformation($"Found {allTestIds.Count} non-deleted tests");

                            // Show all tests regardless of booking status
                            // Tests should be available until their time duration is complete
                            var availableTestIds = allTestIds.ToList();

                            _logger.LogInformation($"After filtering, {availableTestIds.Count} tests are available");

                            // Now fetch the full test objects
                            if (availableTestIds.Any())
                            {
                                if (string.IsNullOrEmpty(candidateCategory))
                                {
                                    // If candidate has no category, don't show any tests
                                    _logger.LogInformation($"Candidate {candidateSapId} has no category, not showing any tests");
                                    tests = new List<Test>();
                                    ViewBag.InfoMessage = "You need to set your category in your profile to see available tests. Please update your profile with your domain/category.";
                                }
                                else if (candidateCategory.Equals("BFSI Internship", StringComparison.OrdinalIgnoreCase) ||
                                         candidateCategory.Equals("Pharma Intern", StringComparison.OrdinalIgnoreCase) ||
                                         candidateCategory.Equals("Medical Coding Intern", StringComparison.OrdinalIgnoreCase) ||
                                         candidateCategory.Equals("AI", StringComparison.OrdinalIgnoreCase) ||
                                         candidateCategory.Equals("DataScience", StringComparison.OrdinalIgnoreCase) ||
                                         candidateCategory.Equals("Cybersecurity", StringComparison.OrdinalIgnoreCase) ||
                                         candidateCategory.Equals("Portfolio", StringComparison.OrdinalIgnoreCase))
                                {
                                    // For special categories, only show tests from that specific category
                                    _logger.LogInformation($"Candidate {candidateSapId} has {candidateCategory} category, showing only {candidateCategory} tests");

                                    // Log the available test domains for debugging
                                    var availableTests = await _context.Tests
                                        .AsNoTracking()
                                        .Where(t => availableTestIds.Contains(t.Id) && t.Domain != null)
                                        .ToListAsync();

                                    foreach (var test in availableTests)
                                    {
                                        _logger.LogInformation($"Available test: ID={test.Id}, Title={test.Title}, Domain={test.Domain}");
                                    }

                                    // Use pattern matching based on the candidate's category
                                    if (candidateCategory.Equals("BFSI Internship", StringComparison.OrdinalIgnoreCase))
                                    {
                                        tests = await _context.Tests
                                            .AsNoTracking()
                                            .Where(t => availableTestIds.Contains(t.Id) &&
                                                   t.Domain != null &&
                                                   EF.Functions.Like(t.Domain, "%BFSI%Internship%"))
                                            .ToListAsync();

                                        _logger.LogInformation($"Found {tests.Count} tests for BFSI Internship category using pattern matching");
                                    }
                                    else
                                    {
                                        // For other special categories, use exact matching
                                        tests = await _context.Tests
                                            .AsNoTracking()
                                            .Where(t => availableTestIds.Contains(t.Id) &&
                                                   t.Domain != null &&
                                                   t.Domain == candidateCategory)
                                            .ToListAsync();

                                        _logger.LogInformation($"Found {tests.Count} tests for {candidateCategory} category using exact matching");
                                    }
                                }
                                else
                                {
                                    // First try to get tests matching the candidate's category
                                    tests = await _context.Tests
                                        .AsNoTracking()
                                        .Where(t => availableTestIds.Contains(t.Id) && t.Domain == candidateCategory)
                                        .ToListAsync();

                                    _logger.LogInformation($"Found {tests.Count} tests matching category '{candidateCategory}'");

                                    // We no longer show tests with null domain as fallback
                                    // Only show tests that match the user's category
                                    if (tests.Count == 0)
                                    {
                                        _logger.LogInformation($"No tests found for category '{candidateCategory}'. Strictly filtering by domain.");
                                        ViewBag.InfoMessage = $"No tests are currently available for your category '{candidateCategory}'. Please check back later or contact support.";
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error fetching tests for candidate {CandidateSapId}", candidateSapId);
                            tests = new List<Test>();
                        }

                        // Log the number of tests found
                        _logger.LogInformation($"Found {tests.Count} tests for candidate {candidateSapId} with category {candidate.Category}");

                        // If no tests are found and we were filtering by category, check if we should show all tests
                        if (tests.Count == 0 && !string.IsNullOrEmpty(candidateCategory))
                        {
                            // For special categories, don't show other category tests
                            if (candidateCategory.Equals("BFSI Internship", StringComparison.OrdinalIgnoreCase) ||
                                candidateCategory.Equals("Pharma Intern", StringComparison.OrdinalIgnoreCase) ||
                                candidateCategory.Equals("Medical Coding Intern", StringComparison.OrdinalIgnoreCase) ||
                                candidateCategory.Equals("AI", StringComparison.OrdinalIgnoreCase) ||
                                candidateCategory.Equals("DataScience", StringComparison.OrdinalIgnoreCase) ||
                                candidateCategory.Equals("Cybersecurity", StringComparison.OrdinalIgnoreCase) ||
                                candidateCategory.Equals("Portfolio", StringComparison.OrdinalIgnoreCase))
                            {
                                _logger.LogInformation($"No tests found for {candidateCategory} category. Not showing other tests as per requirement.");

                                // Try one more time with a more flexible search
                                // Use case-insensitive comparison for domain matching
                                var allTests = await _context.Tests
                                    .AsNoTracking()
                                    .Where(t => !t.IsDeleted &&
                                           t.Domain != null &&
                                           EF.Functions.Like(t.Domain, candidateCategory)) // Case-insensitive domain filtering
                                    .ToListAsync();

                                _logger.LogInformation($"All available tests in the system: {allTests.Count}");
                                foreach (var test in allTests)
                                {
                                    _logger.LogInformation($"Test: ID={test.Id}, Title={test.Title}, Domain={test.Domain}");
                                }

                                List<Test> categoryTests = new List<Test>();

                                // Try to find tests with the specific category
                                if (candidateCategory.Equals("BFSI Internship", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Special case for BFSI Internship - use pattern matching
                                    categoryTests = allTests.Where(t =>
                                        t.Domain != null &&
                                        (t.Domain.IndexOf("BFSI", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                         t.Domain.IndexOf("Internship", StringComparison.OrdinalIgnoreCase) >= 0))
                                        .ToList();
                                }
                                else
                                {
                                    // For other categories, use exact matching
                                    categoryTests = allTests.Where(t =>
                                        t.Domain != null && t.Domain.Equals(candidateCategory, StringComparison.OrdinalIgnoreCase))
                                        .ToList();
                                }

                                if (categoryTests.Any())
                                {
                                    _logger.LogInformation($"Found {categoryTests.Count} {candidateCategory} tests using flexible search");

                                    // Filter out tests that are already booked
                                    var availableCategoryTests = categoryTests
                                        .Where(t => !bookedTestIds.Contains(t.Id) && !upcomingTestIds.Contains(t.Id))
                                        .ToList();

                                    if (availableCategoryTests.Any())
                                    {
                                        tests = availableCategoryTests;
                                        _logger.LogInformation($"Found {tests.Count} available {candidateCategory} tests after filtering booked tests");
                                    }
                                }

                                if (tests.Count == 0)
                                {
                                    ViewBag.InfoMessage = $"No tests are currently available for your category '{candidateCategory}'. Only tests specifically designed for {candidateCategory} will be shown here.";
                                }
                            }
                            else
                            {
                                _logger.LogInformation($"No tests found for category {candidateCategory}. Not showing any tests as fallback.");

                                // We no longer show all tests as fallback
                                // Only show tests that match the user's category
                                tests = new List<Test>();
                                ViewBag.InfoMessage = $"No tests are currently available for your category '{candidateCategory}'. Please check back later or contact support.";
                            }
                        }

                        ViewBag.UserDomain = candidate.Category;
                        return View(tests);
                    }
                }
                else if (userRole == "SpecialUser" && userId != null)
                {
                    // For special users, get their category from the SpecialUser record
                    var specialUserSapId = userId;
                    var specialUser = await _context.SpecialUsers.FindAsync(specialUserSapId);

                    if (specialUser != null)
                    {
                        try
                        {
                            // Get the special user's category
                            var specialUserCategory = specialUser.Category;

                            // Log the special user's category
                            _logger.LogInformation($"Special user {specialUserSapId} has category: {(specialUserCategory ?? "NULL")}");

                            List<Test> tests = new List<Test>();

                            try
                            {
                                // Get all test IDs (no need to filter by IsDeleted since we're now doing hard deletes)
                                var allTestIds = await _context.Tests
                                    .AsNoTracking()
                                    .Select(t => t.Id)
                                    .ToListAsync();

                                _logger.LogInformation($"Found {allTestIds.Count} non-deleted tests for special user");

                                // Show all tests regardless of booking status for special users
                                var availableTestIds = allTestIds.ToList();

                                _logger.LogInformation($"After filtering, {availableTestIds.Count} tests are available for special user");

                                // Now fetch the full test objects based on category
                                if (availableTestIds.Any())
                                {
                                    if (string.IsNullOrEmpty(specialUserCategory))
                                    {
                                        // If special user has no category, don't show any tests
                                        _logger.LogInformation($"Special user {specialUserSapId} has no category, not showing any tests");
                                        tests = new List<Test>();
                                        ViewBag.InfoMessage = "You need to have a category assigned to see available tests. Please contact your administrator to set your category.";
                                    }
                                    else
                                    {
                                        // For special users, show tests that match their category exactly
                                        _logger.LogInformation($"Special user {specialUserSapId} has {specialUserCategory} category, showing only {specialUserCategory} tests");

                                        // Use exact matching for special user categories
                                        tests = await _context.Tests
                                            .AsNoTracking()
                                            .Where(t => availableTestIds.Contains(t.Id) &&
                                                   t.Domain != null &&
                                                   t.Domain == specialUserCategory)
                                            .ToListAsync();

                                        _logger.LogInformation($"Found {tests.Count} tests for special user {specialUserCategory} category using exact matching");

                                        if (tests.Count == 0)
                                        {
                                            ViewBag.InfoMessage = $"No tests are currently available for your category '{specialUserCategory}'. Only tests specifically designed for {specialUserCategory} will be shown here.";
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error fetching tests for special user {SpecialUserSapId}", specialUserSapId);
                                tests = new List<Test>();
                            }

                            // Log the number of tests found
                            _logger.LogInformation($"Found {tests.Count} tests for special user {specialUserSapId} with category {specialUser.Category}");

                            ViewBag.UserDomain = specialUser.Category;
                            ViewBag.IsSpecialUser = true;
                            return View(tests);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing special user {SpecialUserSapId}", specialUserSapId);
                            ViewBag.InfoMessage = "Error loading tests. Please try again later.";
                            return View(new List<Test>());
                        }
                    }
                }

                return View(new List<Test>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Test Index action");
                return View(new List<Test>());
            }
        }

        // View action for uploading questions has been removed in favor of using category questions only

        // View action for showing test instructions
        [HttpGet]
        [Route("Test/Instructions/{id}")]
        [Authorize(Roles = "Candidate,SpecialUser")]
        public async Task<IActionResult> Instructions(int id)
        {
            try
            {
                // First check if the user can take the test at this time
                // We'll do this by checking the same conditions as in the Take action
                // Get the test
                var test = await _context.Tests.FindAsync(id);
                if (test == null)
                {
                    return NotFound("The test you're looking for doesn't exist.");
                }

                // Get the user ID for booking check
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return RedirectToAction("Login", "Auth");
                }
                // CRITICAL FIX: Use userId directly as SapId since it's now a string
                var candidateId = userId;

                // Check if the user has a booking for this test
                // CRITICAL FIX: Get the most recent confirmed booking first, then any other booking
                var booking = await _context.TestBookings
                    .Where(tb => tb.TestId == id && tb.UserSapId == userId)
                    .OrderByDescending(tb => tb.Status == "Confirmed" ? 1 : 0) // Confirmed bookings first
                    .ThenByDescending(tb => tb.BookedAt) // Then by most recent
                    .FirstOrDefaultAsync();

                // Special users don't need bookings - they can take tests directly
                if (booking == null && !User.IsInRole("SpecialUser"))
                {
                    return RedirectToAction("Index", new { error = "You haven't booked this test yet. Please book a slot first." });
                }

                // Check if the booking is confirmed (payment completed)
                if (booking.Status != "Confirmed")
                {
                    _logger.LogWarning("User attempted to view test instructions with unconfirmed booking. BookingId: {BookingId}, Status: {Status}",
                        booking.Id, booking.Status);
                    return RedirectToAction("Index", new { error = "Your booking payment is not confirmed. Please complete the payment process before accessing the test instructions." });
                }

                // With time slots removed, we don't need to enforce time restrictions
                var now = DateTime.Now;
                var bookingDate = booking.BookingDate.HasValue ? booking.BookingDate.Value.Date : now.Date;
                var currentDate = now.Date;

                // Handle nullable DateTime fields
                DateTime bookingStartDateTime = now.AddHours(-1); // Default to 1 hour ago
                DateTime bookingEndDateTime = now.AddHours(48);   // Default to 48 hours from now

                // If the booking has specific times, use them
                if (booking.StartTime.HasValue && booking.EndTime.HasValue)
                {
                    bookingStartDateTime = booking.StartTime.Value;
                    bookingEndDateTime = booking.EndTime.Value;
                }

                // Check if the test is in the future
                bool isFutureTest = now < bookingStartDateTime;
                _logger.LogInformation("Checking if test is in the future. Current time: {CurrentTime}, Start time: {StartTime}, IsFutureTest: {IsFutureTest}", now, bookingStartDateTime, isFutureTest);

                if (isFutureTest)
                {
                    // Calculate time remaining for display
                    TimeSpan timeRemaining;
                    // With time slots removed, we'll use a simple calculation
                    timeRemaining = bookingStartDateTime - now;

                    string timeRemainingMessage = "";
                    if (timeRemaining.Days > 0)
                    {
                        timeRemainingMessage += $"{timeRemaining.Days} day{(timeRemaining.Days != 1 ? "s" : "")} ";
                    }
                    if (timeRemaining.Hours > 0)
                    {
                        timeRemainingMessage += $"{timeRemaining.Hours} hour{(timeRemaining.Hours != 1 ? "s" : "")} ";
                    }
                    if (timeRemaining.Minutes > 0)
                    {
                        timeRemainingMessage += $"{timeRemaining.Minutes} minute{(timeRemaining.Minutes != 1 ? "s" : "")} ";
                    }
                    timeRemainingMessage += $"until your scheduled test time";

                    TempData["ErrorMessage"] = $"You cannot start this test before the scheduled time. Please wait {timeRemainingMessage}.";
                    return RedirectToAction("ScheduledTest", new { id });
                }

                // Check if the test is expired
                bool isExpired = now > bookingEndDateTime;
                _logger.LogInformation("Checking if test is expired. Current time: {CurrentTime}, End time: {EndTime}, IsExpired: {IsExpired}", now, bookingEndDateTime, isExpired);

                if (isExpired)
                {
                    // With time slots removed, we'll use a simpler approach
                    string slotDisplayTime = "Anytime";

                    // If the booking has specific times, use them for display purposes
                    if (booking != null && booking.StartTime.HasValue && booking.EndTime.HasValue)
                    {
                        slotDisplayTime = $"{Utilities.TimeZoneHelper.ToIst(booking.StartTime.Value).ToString("hh:mm tt")} - {Utilities.TimeZoneHelper.ToIst(booking.EndTime.Value).ToString("hh:mm tt")}";
                    }

                    TempData["ErrorMessage"] = $"Your test has ended. Please contact support if you need assistance.";
                    return RedirectToAction("ScheduledTest", new { id });
                }

                // If we get here, the user is allowed to take the test
                // Load the test with questions
                test = await _context.Tests
                    .Include(t => t.Questions)
                        .ThenInclude(q => q.AnswerOptions)
                    .FirstOrDefaultAsync(t => t.Id == id);

                if (test == null)
                {
                    return NotFound("The test you're looking for doesn't exist.");
                }

                // Check if the test is deleted if the IsDeleted property exists
                try
                {
                    if (test.IsDeleted)
                    {
                        return NotFound("The test you're looking for has been deleted.");
                    }
                }
                catch
                {
                    // IsDeleted property might not exist yet, ignore the error
                }

                // Check if the user has just paid (coming from payment flow)
                bool hasJustPaid = TempData["JustPaid"] != null;

                // If the user has just paid, set a success message
                if (hasJustPaid)
                {
                    ViewBag.SuccessMessage = "Payment successful! You can now take your test.";
                    // Keep the JustPaid flag for the next request
                    TempData.Keep("JustPaid");
                }

                // Check if the test is schedule-restricted and if it's currently available
                if (test.IsScheduleRestricted && test.ScheduledStartTime.HasValue && test.ScheduledEndTime.HasValue)
                {
                    DateTime utcNow = DateTime.UtcNow;
                    // If the user has just paid, bypass the time restrictions
                    if (!hasJustPaid && (utcNow < test.ScheduledStartTime.Value || utcNow > test.ScheduledEndTime.Value))
                    {
                        // Convert times to IST for display
                        var istTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
                        var nowIST = TimeZoneInfo.ConvertTimeFromUtc(utcNow, istTimeZone);
                        var testStartTimeIST = TimeZoneInfo.ConvertTimeFromUtc(test.ScheduledStartTime.Value, istTimeZone);
                        var testEndTimeIST = TimeZoneInfo.ConvertTimeFromUtc(test.ScheduledEndTime.Value, istTimeZone);

                        if (nowIST < testStartTimeIST)
                        {
                            ViewBag.ErrorMessage = $"This test is not available yet. It will be available from {Utilities.TimeZoneHelper.FormatIstDateTime(testStartTimeIST)} IST to {Utilities.TimeZoneHelper.FormatIstDateTime(testEndTimeIST)} IST.";
                        }
                        else
                        {
                            ViewBag.ErrorMessage = $"This test is no longer available. It was available from {Utilities.TimeZoneHelper.FormatIstDateTime(testStartTimeIST)} IST to {Utilities.TimeZoneHelper.FormatIstDateTime(testEndTimeIST)} IST.";
                        }

                        ViewBag.ScheduledStartTime = testStartTimeIST;
                        ViewBag.ScheduledEndTime = testEndTimeIST;
                        ViewBag.TimeZone = "IST";
                        return View("TestNotAvailable", test);
                    }

                    // If the user is a candidate, check if they've booked this test
                    if (User.IsInRole("Candidate"))
                    {
                        // Check if the user has booked this test
                        string userIdInRole = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                        if (string.IsNullOrEmpty(userIdInRole))
                        {
                            return RedirectToAction("Login", "Account");
                        }

                        var hasBookedSlot = await _context.TestBookings
                            .AnyAsync(tb => tb.TestId == id && tb.UserSapId == userIdInRole);

                        // Check if the user has just paid (coming from payment flow)
                        bool userJustPaid = TempData["JustPaid"] != null;
                        bool paymentSuccessful = false;

                        if (!hasBookedSlot && !userJustPaid && !paymentSuccessful)
                        {
                            ViewBag.ErrorMessage = "You need to book a slot for this test before you can take it.";
                            return View("TestNotAvailable", test);
                        }

                        // Check if the user has a pending booking that needs to be updated
                        var pendingBooking = await _context.TestBookings
                            .Where(b => b.TestId == id && b.UserSapId == userIdInRole && b.Status == "Pending")
                            .OrderByDescending(b => b.BookedAt)
                            .FirstOrDefaultAsync();

                        if (pendingBooking != null && (userJustPaid || paymentSuccessful))
                        {
                            _logger.LogInformation($"Updating existing pending booking {pendingBooking.Id} for user {userIdInRole} for test {id} after payment");

                            // Update the existing booking to Confirmed status
                            pendingBooking.Status = "Confirmed";
                            pendingBooking.UpdatedAt = Utilities.TimeZoneHelper.GetCurrentIstTime();
                            pendingBooking.StatusReason = "Payment confirmed";

                            await _context.SaveChangesAsync();
                            _logger.LogInformation($"Successfully updated booking with ID {pendingBooking.Id} to Confirmed status after payment");

                            // Verify the booking was updated
                            _context.ChangeTracker.Clear();
                            var verifyBooking = await _context.TestBookings.FindAsync(pendingBooking.Id);
                            if (verifyBooking != null && verifyBooking.Status == "Confirmed") {
                                _logger.LogInformation($"Verified booking {verifyBooking.Id} was updated to Confirmed status");
                            } else {
                                _logger.LogWarning("Could not verify booking was updated to Confirmed status");
                            }
                        }
                        // If the user doesn't have a confirmed booking but has just paid, check for any pending booking first
                        else if (!hasBookedSlot && (userJustPaid || paymentSuccessful))
                        {
                            _logger.LogInformation($"No confirmed booking found. Checking for any pending bookings for user {userIdInRole} for test {id}");

                            // First check if there's a pending booking that we can update instead of creating a new one
                            var anyPendingBooking = await _context.TestBookings
                                .Where(tb => tb.TestId == id && tb.UserSapId == userIdInRole && tb.Status == "Pending")
                                .OrderByDescending(tb => tb.BookedAt)
                                .FirstOrDefaultAsync();

                            if (anyPendingBooking != null)
                            {
                                _logger.LogInformation($"Found pending booking (ID: {anyPendingBooking.Id}) for test {id}. Updating to Confirmed status instead of creating a new booking.");
                                anyPendingBooking.Status = "Confirmed";
                                anyPendingBooking.UpdatedAt = Utilities.TimeZoneHelper.GetCurrentIstTime();
                                anyPendingBooking.StatusReason = "Updated to confirmed after payment";

                                await _context.SaveChangesAsync();
                                _logger.LogInformation($"Successfully updated booking with ID {anyPendingBooking.Id} to Confirmed status after payment");

                                // Verify the booking was updated
                                _context.ChangeTracker.Clear();
                                var verifyBooking = await _context.TestBookings.FindAsync(anyPendingBooking.Id);
                                if (verifyBooking != null && verifyBooking.Status == "Confirmed") {
                                    _logger.LogInformation($"Verified booking {verifyBooking.Id} was updated to Confirmed status");
                                } else {
                                    _logger.LogWarning("Could not verify booking was updated to Confirmed status");
                                }
                            }
                            else
                            {
                                _logger.LogInformation($"No pending booking found. Creating new booking for user {userIdInRole} for test {id} after payment");

                                // Get booking details from TempData
                                DateTime bookingDateValue = DateTime.Today;
                                DateTime startTimeValue = DateTime.Now;
                                DateTime endTimeValue = DateTime.Now.AddHours(2);
                                int slotNumberValue = 1;

                                if (TempData.TryGetValue("BookedDate", out var bookedDateObj) && bookedDateObj != null)
                                {
                                    if (DateTime.TryParse(bookedDateObj.ToString(), out DateTime parsedDate))
                                    {
                                        bookingDateValue = parsedDate.Date;
                                        _logger.LogInformation($"Using booking date from TempData: {bookingDateValue:yyyy-MM-dd}");
                                    }
                                }

                                if (TempData.TryGetValue("BookedStartTime", out var startTimeObj) && startTimeObj != null)
                                {
                                    if (DateTime.TryParse(startTimeObj.ToString(), out DateTime parsedStartTime))
                                    {
                                        startTimeValue = bookingDateValue.Date.Add(parsedStartTime.TimeOfDay);
                                        _logger.LogInformation($"Using start time from TempData: {startTimeValue:HH:mm:ss}");
                                    }
                                }

                                if (TempData.TryGetValue("BookedEndTime", out var endTimeObj) && endTimeObj != null)
                                {
                                    if (DateTime.TryParse(endTimeObj.ToString(), out DateTime parsedEndTime))
                                    {
                                        endTimeValue = bookingDateValue.Date.Add(parsedEndTime.TimeOfDay);
                                        _logger.LogInformation($"Using end time from TempData: {endTimeValue:HH:mm:ss}");
                                    }
                                }

                                if (TempData.TryGetValue("BookedSlotNumber", out var slotNumberObj) && slotNumberObj != null)
                                {
                                    if (int.TryParse(slotNumberObj.ToString(), out int parsedSlotNumber))
                                    {
                                        slotNumberValue = parsedSlotNumber;
                                        _logger.LogInformation($"Using slot number from TempData: {slotNumberValue}");
                                    }
                                }

                                // Get the user's SAP ID
                                string? userSapId = null;
                                var user = await _context.Users.FindAsync(userIdInRole);
                                if (user != null && !string.IsNullOrEmpty(user.SapId))
                                {
                                    userSapId = user.SapId;
                                    _logger.LogInformation($"Using SAP ID from user: {userSapId}");
                                }

                                // Check if this is a retake test
                                bool isRetakeTest = Request.Query.ContainsKey("retake") &&
                                    Request.Query["retake"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase);

                                // Create a new booking
                                var newBooking = new TestBooking
                                {
                                    TestId = id,
                                    UserSapId = userSapId ?? userIdInRole,
                                    BookedAt = Utilities.TimeZoneHelper.GetCurrentIstTime(),
                                    BookingDate = bookingDateValue,
                                    StartTime = startTimeValue,
                                    EndTime = endTimeValue,
                                    SlotNumber = slotNumberValue,
                                    Status = "Confirmed", // Set status to Confirmed since payment is successful
                                    StatusReason = "Created directly as confirmed after payment"
                                    // IsRetake flag removed
                                };

                                _context.TestBookings.Add(newBooking);

                                // Increment the user count for this test
                                test.CurrentUserCount++;

                                await _context.SaveChangesAsync();
                                _logger.LogInformation($"Successfully created booking with ID {newBooking.Id} after payment");

                                // Verify the booking was created
                                _context.ChangeTracker.Clear();
                                var verifyBooking = await _context.TestBookings.FirstOrDefaultAsync(b => b.Id == newBooking.Id);
                                if (verifyBooking != null) {
                                    _logger.LogInformation($"Verified booking exists with ID: {verifyBooking.Id}");
                                } else {
                                    _logger.LogWarning("Could not verify booking exists after creation");
                                }
                            }
                        }
                    }
                }

                return View(test);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Instructions action");
                return RedirectToAction(nameof(Index));
            }
        }

        // View action for taking a test
        [HttpGet]
        [Route("Test/Take/{id}")]
        [Authorize(Roles = "Candidate,SpecialUser")]
        public async Task<IActionResult> Take(int id)
        {
            // Set flag to include code execution scripts
            ViewData["IncludeCodeExecution"] = true;

            try
            {
                // Get the test
                var test = await _context.Tests.FindAsync(id);
                if (test == null)
                {
                    return NotFound("The test you're looking for doesn't exist.");
                }

                // Get the user ID for booking check
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return RedirectToAction("Login", "Auth");
                }
                // CRITICAL FIX: Use userId directly as SapId since it's now a string
                var candidateId = userId;

                // Check if the user has a booking for this test
                // CRITICAL FIX: Get the most recent confirmed booking first, then any other booking
                var booking = await _context.TestBookings
                    .Where(tb => tb.TestId == id && tb.UserSapId == userId)
                    .OrderByDescending(tb => tb.Status == "Confirmed" ? 1 : 0) // Confirmed bookings first
                    .ThenByDescending(tb => tb.BookedAt) // Then by most recent
                    .FirstOrDefaultAsync();

                // Special users don't need bookings - they can take tests directly
                if (booking == null && !User.IsInRole("SpecialUser"))
                {
                    return RedirectToAction("Index", new { error = "You haven't booked this test yet. Please book a slot first." });
                }

                // If the user has a booking, check if it's confirmed
                if (booking != null)
                {
                    // Check if the user has just paid or is coming from payment page
                    bool isJustPaid = TempData["JustPaid"] != null;
                    bool fromPayment = Request.Query.ContainsKey("fromPayment") && Request.Query["fromPayment"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase);

                    // Check for payment confirmation in the database
                    bool hasSuccessfulPayment = false;

                    // Check if there's a successful payment record for this user
                    var successfulPayment = await _context.Payments
                        .Where(p => p.UserSapId == userId && p.Status == "Completed")
                        .OrderByDescending(p => p.CreatedAt)
                        .FirstOrDefaultAsync();

                    if (successfulPayment != null)
                    {
                        _logger.LogInformation($"Found successful payment record for user {candidateId}: PaymentId={successfulPayment.Id}, CreatedAt={successfulPayment.CreatedAt}");
                        hasSuccessfulPayment = true;
                    }

                    // Always update booking status to Confirmed if coming from payment or has successful payment
                    if ((isJustPaid || fromPayment || hasSuccessfulPayment) && booking.Status != "Confirmed")
                    {
                        // Update booking status to Confirmed
                        booking.Status = "Confirmed";
                        booking.UpdatedAt = DateTime.UtcNow;
                        booking.StatusReason = "Payment confirmed";
                        await _context.SaveChangesAsync();
                        _logger.LogInformation($"Updated booking status to Confirmed for BookingId={booking.Id} in Take action after payment");
                    }
                    else if (booking.Status != "Confirmed")
                    {
                        // Double-check if there's a successful payment record
                        if (hasSuccessfulPayment)
                        {
                            // Update booking status to Confirmed
                            booking.Status = "Confirmed";
                            booking.UpdatedAt = DateTime.UtcNow;
                            booking.StatusReason = "Payment verified from database";
                            await _context.SaveChangesAsync();
                            _logger.LogInformation($"Updated booking status to Confirmed based on payment record for BookingId={booking.Id}");
                        }
                        else
                        {
                            _logger.LogWarning("User attempted to take test with unconfirmed booking. BookingId: {BookingId}, Status: {Status}",
                                booking.Id, booking.Status);

                            // CRITICAL FIX: Redirect to ScheduledTest instead of Index to allow user to complete payment
                            return RedirectToAction("ScheduledTest", new { id, error = "Your booking payment is not confirmed. Please complete the payment process before taking the test." });
                        }
                    }
                }

                // If the user doesn't have a booking, that's okay - they can still take the test
                // as long as it's within the test's time duration

                // STRICT TIME ENFORCEMENT: Check if the current time is within the scheduled slot
                var now = DateTime.Now;

                // Time slots have been completely removed
                // We don't need to calculate start and end times anymore
                // Just set default values for backward compatibility
                DateTime bookingStartDateTime = DateTime.MinValue;
                DateTime bookingEndDateTime = DateTime.MaxValue;
                int slotNumber = 0;
                string slotDisplayTime = "Available Anytime"; // Default value

                if (booking != null)
                {
                    // Log the booking information for debugging
                    _logger.LogInformation("User has a booking for test ID: {TestId}, Status: {Status}",
                        id, booking.Status);
                }
                else
                {
                    // If the user doesn't have a booking, skip to the next section
                    goto SkipTimeSlotChecking;
                }

                // This section is now handled in the if/else block above

                // We already set these variables above

                // With time slots removed, we don't need to check if the test is in the future or expired
                // We only need to check if the booking is confirmed

                // Log the current state for debugging
                _logger.LogInformation("Test booking status check - Current time: {CurrentTime}, Booking status: {Status}",
                    now, booking?.Status ?? "No booking");

                // Only check if the booking is confirmed (skip for special users without bookings)
                if (booking != null && booking.Status != "Confirmed")
                {
                    _logger.LogWarning("User attempted to start test {TestId} without confirmed payment. Booking status: {Status}",
                        id, booking.Status);

                    TempData["ErrorMessage"] = "Your payment is not confirmed. Please complete the payment process to start the test.";
                    return RedirectToAction("ScheduledTest", new { id });
                }
            SkipTimeSlotChecking:
                // Check if the user has already taken this test
                var username = User.Identity?.Name;
                var testResults = await _context.TestResults
                    .Where(tr => tr.TestId == id && tr.Username == username)
                    .OrderByDescending(tr => tr.SubmittedAt)
                    .ToListAsync();

                var testResult = testResults.FirstOrDefault();

                // Check if this is a retake attempt
                bool isRetake = Request.Query.ContainsKey("retake") && Request.Query["retake"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase);
                bool isFromPayment = Request.Query.ContainsKey("fromPayment") && Request.Query["fromPayment"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase);

                // Check if the test was taken recently (within the last 24 hours)
                bool recentlyTaken = false;
                if (testResult != null)
                {
                    recentlyTaken = (DateTime.Now - testResult.SubmittedAt).TotalHours < 24;
                    _logger.LogInformation($"Test {id} was taken by {username} at {testResult.SubmittedAt}. Hours since submission: {(DateTime.Now - testResult.SubmittedAt).TotalHours}. Recently taken: {recentlyTaken}");
                }

                // Only redirect if the test was taken more than 24 hours ago and this is not a retake attempt
                if (testResult != null && !recentlyTaken && !isRetake && !isFromPayment)
                {
                    // User has already taken this test, redirect to the result page
                    _logger.LogInformation($"User {username} attempted to take test {id} again, redirecting to result {testResult.Id}");
                    return RedirectToAction("Result", new { id = testResult.Id, message = "You have already taken this test. Here are your results." });
                }

                if (isRetake)
                {
                    _logger.LogInformation($"User {username} is retaking test {id}");
                    ViewBag.IsRetake = true;
                }

                if (isFromPayment)
                {
                    _logger.LogInformation($"User {username} is taking test {id} after payment");
                    ViewBag.FromPayment = true;
                }

                // Time slots have been completely removed
                // We only need to check if the booking is confirmed (skip for special users without bookings)
                _logger.LogInformation("Time slots removed. Current time: {CurrentTime}. Checking if booking is confirmed.", now);

                if (booking != null && booking.Status != "Confirmed")
                {
                    _logger.LogWarning("User {Username} attempted to take test {TestId} without confirmed payment. Current time: {CurrentTime}, Status: {Status}",
                        username, id, now, booking.Status);

                    TempData["ErrorMessage"] = "Your payment is not confirmed. Please complete the payment process to start the test.";
                    return RedirectToAction("ScheduledTest", new { id = id });
                }

                // If we get here, the booking is confirmed and the user can take the test

                // We already have slotDisplayTime from earlier

                // Time slot restrictions have been removed
                // Users can take tests anytime after booking and payment
                // Just log the attempt for tracking purposes
                _logger.LogInformation("User {Username} is starting test {TestId}. Current time: {CurrentTime}",
                    username, id, now);

                // Check if the user just booked this test
                bool justBooked = TempData["TestBooked"] != null && TempData["BookedTestId"] != null && (int)TempData["BookedTestId"] == id;

                // With time slots removed, users can take tests anytime after booking and payment
                // Just log the attempt for tracking purposes
                _logger.LogInformation("User {Username} is starting test {TestId}. Current time: {CurrentTime}",
                    username, id, now);

                // Update the test with scheduling information
                test = await _context.Tests
                    .FirstOrDefaultAsync(t => t.Id == id);

                if (test == null)
                {
                    return NotFound("The test you're looking for doesn't exist.");
                }

                // With time slots removed, we don't need to check if the test is schedule-restricted
                // Just check if the user has a booking for this test
                var testBooking = await _context.TestBookings
                    .FirstOrDefaultAsync(tb => tb.TestId == id && tb.UserSapId == userId);

                // Log the attempt for tracking purposes
                var currentTime = Utilities.TimeZoneHelper.GetCurrentIstTime();
                _logger.LogInformation($"User {username} is starting test {id}. Current time: {currentTime}");

                // Load questions for the test
                // Note: We already have the test object from earlier

                // Load questions either from the Questions table or from CategoryQuestions
                if (test != null && test.CategoryQuestionsId.HasValue)
                {
                    // Load questions from CategoryQuestions
                    var categoryQuestions = await _context.CategoryQuestions
                        .FirstOrDefaultAsync(cq => cq.Id == test.CategoryQuestionsId);

                    if (categoryQuestions != null)
                    {
                        // Deserialize questions from JSON using the shared comprehensive options
                        var allQuestions = JsonSerializer.Deserialize<List<QuestionDto>>(categoryQuestions.QuestionsJson, _jsonOptions);

                        _logger.LogInformation("Take method - Deserialized {AllQuestionsCount} questions from CategoryQuestions JSON for test {TestId}",
                            allQuestions?.Count ?? 0, id);

                        if (allQuestions == null)
                        {
                            _logger.LogError("Take method - Deserialization returned null for test {TestId}, CategoryQuestionsId: {CategoryQuestionsId}",
                                id, test.CategoryQuestionsId);
                            allQuestions = new List<QuestionDto>();
                        }

                        // Use a consistent seed based on the test ID to ensure the same questions are selected
                        // This ensures the questions match what will be evaluated during submission
                        var random = new Random(test.Id); // Use test ID as seed for consistent selection
                        var selectedQuestions = allQuestions
                            .OrderBy(q => random.Next())
                            .Take(Math.Min(test.QuestionCount, allQuestions.Count))
                            .ToList();

                        _logger.LogInformation($"Selected {selectedQuestions.Count} questions for test {id} using consistent seed {test.Id}");

                        // Convert QuestionDto to InMemoryQuestion objects for the view
                        test.Questions = new List<InMemoryQuestion>();
                        for (int i = 0; i < selectedQuestions.Count; i++)
                        {
                            var q = selectedQuestions[i];
                            var questionId = 10000 + i; // Use consistent ID generation

                            var question = new InMemoryQuestion {
                                Id = questionId,
                                Text = q.Text,
                                Title = q.Title ?? q.Text.Substring(0, Math.Min(q.Text.Length, 100)),
                                Type = q.Type,
                                TestId = test.Id,
                                AnswerOptions = new List<InMemoryAnswerOption>()
                            };

                            // Add answer options
                            if (q.AnswerOptions != null)
                            {
                                for (int j = 0; j < q.AnswerOptions.Count; j++)
                                {
                                    var o = q.AnswerOptions[j];
                                    var optionId = 100000 + (i * 100) + j; // Use consistent ID generation

                                    question.AnswerOptions.Add(new InMemoryAnswerOption {
                                        Id = optionId,
                                        Text = o.Text,
                                        IsCorrect = o.IsCorrect
                                    });
                                }
                            }

                            test.Questions.Add(question);
                        }
                    }
                }
                else if (test != null)
                {
                    // If CategoryQuestionsId is not set, the test doesn't have any questions
                    // Initialize an empty collection
                    test.Questions = new List<InMemoryQuestion>();
                    _logger.LogWarning("Test {TestId} does not have CategoryQuestionsId set. No questions will be displayed.", id);
                }

                if (test == null)
                {
                    return NotFound("The test you're looking for doesn't exist.");
                }

                // Check if the test is deleted if the IsDeleted property exists
                try
                {
                    if (test.IsDeleted)
                    {
                        return NotFound("The test you're looking for has been deleted.");
                    }
                }
                catch
                {
                    // IsDeleted property might not exist yet, ignore the error
                }

                // Time slots have been completely removed
                // We don't need to check if the test is schedule-restricted

                // Check if the user has just paid (coming from payment flow)
                bool justPaid = TempData["JustPaid"] != null;

                // If the user has just paid, set a success message
                if (justPaid)
                {
                    ViewBag.SuccessMessage = "Payment successful! You can now take your test.";
                    // Keep the JustPaid flag for the next request
                    TempData.Keep("JustPaid");
                }

                // Log the attempt for tracking purposes
                var nowIST = Utilities.TimeZoneHelper.GetCurrentIstTime();
                _logger.LogInformation("User {Username} is taking test {TestId} at {CurrentTime}",
                    User.Identity?.Name, test.Id, nowIST);

                // CRITICAL FIX: Do not create TestResult records when starting a test
                // TestResult records should only be created when the test is actually submitted
                // This prevents the auto-submission issue where tests appear as completed immediately
                _logger.LogInformation("Test {TestId} started by user {Username}. TestResult will be created only upon submission.",
                    id, User.Identity?.Name);

                return View(test);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error taking test: {ErrorMessage}", ex.Message);
                return StatusCode(500, new { message = "Error taking test: " + ex.Message });
            }
        }

        [HttpGet]
        [Route("Test/view-uploads")]
        [Authorize(Roles = "Organization")]
        public IActionResult ViewUploads()
        {
            var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            var files = Directory.GetFiles(uploadsFolder)
                .Select(f => new FileInfo(f))
                .Select(f => new
                {
                    Name = f.Name,
                    Size = f.Length,
                    LastModified = f.LastWriteTime,
                    Path = $"/uploads/{f.Name}"
                })
                .ToList();

            return View(files);
        }

        // View action for showing a scheduled test
        [HttpGet]
        [Route("Test/ScheduledTest/{id}")]
        [Authorize(Roles = "Candidate,SpecialUser")]
        public async Task<IActionResult> ScheduledTest(int id, string message = null, string error = null, bool fromPayment = false)
        {
            try
            {
                var test = await _context.Tests.FindAsync(id);
                if (test == null)
                {
                    return RedirectToAction("Index", new { error = "Test not found" });
                }

                // Pass any messages to the view
                if (!string.IsNullOrEmpty(message))
                {
                    ViewBag.SuccessMessage = message;
                }

                // Pass any error messages to the view
                if (!string.IsNullOrEmpty(error))
                {
                    TempData["ErrorMessage"] = error;
                }

                // Set flag if user is coming from payment page
                ViewBag.FromPayment = fromPayment;

                // Check if the user has a booking for this test
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return RedirectToAction("Login", "Auth");
                }

                // CRITICAL FIX: Get the most recent confirmed booking first, then any other booking
                var booking = await _context.TestBookings
                    .Where(tb => tb.TestId == id && tb.UserSapId == userId)
                    .OrderByDescending(tb => tb.Status == "Confirmed" ? 1 : 0) // Confirmed bookings first
                    .ThenByDescending(tb => tb.BookedAt) // Then by most recent
                    .FirstOrDefaultAsync();

                // CRITICAL DEBUG: Log all bookings for this user and test
                var allBookings = await _context.TestBookings
                    .Where(tb => tb.TestId == id && tb.UserSapId == userId)
                    .OrderByDescending(tb => tb.BookedAt)
                    .ToListAsync();

                _logger.LogInformation($"CRITICAL DEBUG: Found {allBookings.Count} bookings for test {id} and user {userId}");
                foreach (var b in allBookings)
                {
                    _logger.LogInformation($"CRITICAL DEBUG: Booking ID={b.Id}, Status={b.Status}, BookedAt={b.BookedAt}, Selected={b.Id == booking?.Id}");
                }

                if (booking != null)
                {
                    _logger.LogInformation($"CRITICAL DEBUG: Selected booking ID={booking.Id}, Status={booking.Status}, BookedAt={booking.BookedAt}");
                }

                // Check if the user has just paid or is coming from payment page
                bool justPaid = TempData["JustPaid"] != null;

                // Check for payment confirmation in the database
                bool hasSuccessfulPayment = false;
                if (booking != null)
                {
                    // Check if there's a successful payment record for this user and test
                    var successfulPayment = await _context.Payments
                        .Where(p => p.UserSapId == userId && p.Status == "Completed")
                        .OrderByDescending(p => p.CreatedAt)
                        .FirstOrDefaultAsync();

                    if (successfulPayment != null)
                    {
                        _logger.LogInformation($"Found successful payment record for user {userId}: PaymentId={successfulPayment.Id}, CreatedAt={successfulPayment.CreatedAt}");
                        hasSuccessfulPayment = true;
                    }
                }

                if (booking != null)
                {
                    // If the user has a booking, check if it's confirmed, if they just paid, or if there's a successful payment record
                    if (booking.Status != "Confirmed" && (justPaid || fromPayment || hasSuccessfulPayment))
                    {
                        // Update booking status to Confirmed
                        booking.Status = "Confirmed";
                        booking.UpdatedAt = Utilities.TimeZoneHelper.GetCurrentIstTime();
                        booking.StatusReason = "Payment confirmed";
                        await _context.SaveChangesAsync();
                        _logger.LogInformation($"Updated booking status to Confirmed for BookingId={booking.Id} in ScheduledTest action");

                        // Set the updated status in ViewBag
                        ViewBag.BookingStatus = "Confirmed";
                        ViewBag.JustPaid = true;
                        // Clear any payment warning since payment is now confirmed
                        ViewBag.PaymentWarning = null;
                    }
                    else
                    {
                        // Set the booking status in ViewBag to control the payment warning display
                        ViewBag.BookingStatus = booking.Status;

                        // If the booking is already confirmed, clear any payment warning
                        if (booking.Status == "Confirmed")
                        {
                            ViewBag.PaymentWarning = null;
                            _logger.LogInformation("CRITICAL DEBUG: Booking is confirmed, clearing PaymentWarning. BookingId: {BookingId}, Status: {Status}", booking.Id, booking.Status);
                        }

                        // If the user has a booking and there's a successful payment but status isn't Confirmed, update it
                        if (booking.Status != "Confirmed" && hasSuccessfulPayment)
                        {
                            booking.Status = "Confirmed";
                            booking.UpdatedAt = Utilities.TimeZoneHelper.GetCurrentIstTime();
                            booking.StatusReason = "Payment verified from database";
                            await _context.SaveChangesAsync();
                            _logger.LogInformation($"Updated booking status to Confirmed based on payment record for BookingId={booking.Id}");
                            ViewBag.BookingStatus = "Confirmed";
                            // Clear any payment warning since payment is now confirmed
                            ViewBag.PaymentWarning = null;
                        }
                        // If the user has a booking, check if it's confirmed
                        // Only show payment warning if the booking status in ViewBag is not confirmed
                        else if (ViewBag.BookingStatus != "Confirmed")
                        {
                            _logger.LogWarning("User attempted to start test with unconfirmed booking. BookingId: {BookingId}, Status: {Status}, ViewBag.BookingStatus: {ViewBagStatus}",
                                booking.Id, booking.Status, (string)ViewBag.BookingStatus);
                            ViewBag.PaymentWarning = "Your booking payment is not confirmed. Please complete the payment process before starting the test.";
                        }
                    }

                    // Get the booking details
                    var bookingDate = booking.BookingDate.HasValue ? booking.BookingDate.Value.Date : DateTime.Today;
                    var startTime = booking.StartTime;
                    var endTime = booking.EndTime;
                    var slotNumber = booking.SlotNumber;
                    var now = DateTime.Now;
                    var currentDate = now.Date;

                    // Log the booking details for debugging
                    _logger.LogInformation($"Booking details - Date: {bookingDate}, StartTime: {startTime}, EndTime: {endTime}, SlotNumber: {slotNumber}, Status: {booking.Status}");

                    // Set the booking details for the view
                    // Use the exact booking date without timezone conversion
                    ViewBag.BookedDate = bookingDate.ToString("dddd, MMMM d, yyyy");
                    ViewBag.BookedStartTime = startTime.HasValue ? Utilities.TimeZoneHelper.ToIst(startTime.Value).ToString("hh:mm tt") : "Anytime";
                    ViewBag.BookedEndTime = endTime.HasValue ? Utilities.TimeZoneHelper.ToIst(endTime.Value).ToString("hh:mm tt") : "Anytime";
                    ViewBag.BookedSlotNumber = slotNumber;
                    ViewBag.UserSapId = booking.UserSapId;
                    ViewBag.BookedAt = booking.BookedAt; // Add BookedAt to ViewBag

                    // With time slots removed, we'll use a simpler approach
                    string slotDisplayTime = "Available Anytime";

                    // If the booking has specific times, use them for display purposes
                    if (startTime.HasValue && endTime.HasValue)
                    {
                        slotDisplayTime = $"{Utilities.TimeZoneHelper.ToIst(startTime.Value).ToString("hh:mm tt")} - {Utilities.TimeZoneHelper.ToIst(endTime.Value).ToString("hh:mm tt")}";
                    }

                    ViewBag.SlotDisplayTime = slotDisplayTime;

                    // With time slots completely removed, we don't need to check start/end times
                    // We only need to check if the booking is confirmed

                    // Log the booking status for debugging
                    _logger.LogInformation($"Booking status check - Now: {now}, Status: {booking.Status}");

                    // Set default values for ViewBag properties
                    ViewBag.IsFutureTest = false; // No future tests with time slots removed
                    ViewBag.IsExpired = false;    // No expired tests with time slots removed

                    // Check if this is a retake test from query parameters
                    bool isRetakeFromQuery = Request.Query.ContainsKey("retake") &&
                                            Request.Query["retake"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase);

                    // Check if this is from a payment flow
                    bool isFromPaymentFlow = fromPayment || justPaid || hasSuccessfulPayment;

                    // Make isRetake available in the Take action
                    ViewBag.IsRetake = isRetakeFromQuery;

                    // For retake tests with confirmed payment, override the expired status if within the valid time window
                    if ((isRetakeFromQuery || isFromPaymentFlow) && booking.Status == "Confirmed")
                    {
                        // If the test was just booked and paid for, it shouldn't be marked as expired
                        if (ViewBag.IsExpired && (DateTime.Now - booking.BookedAt).TotalHours < 24)
                        {
                            _logger.LogInformation("Overriding expired status for recently booked and paid test");
                            ViewBag.IsExpired = false;
                        }
                    }

                    // Set ViewBag properties for the view
                    ViewBag.IsRetakeFromQuery = isRetakeFromQuery;
                    ViewBag.IsFromPaymentFlow = isFromPaymentFlow;

                    // Check if the user just booked this test
                    bool justBooked = TempData["TestBooked"] != null && TempData["BookedTestId"] != null && (int)TempData["BookedTestId"] == id;

                    // Even if the user just booked the test, enforce time restrictions
                    if (justBooked)
                    {
                        _logger.LogInformation($"User just booked test ID: {test.Id}. Still enforcing time restrictions.");
                        ViewBag.JustBooked = true;
                        // Do not set ViewBag.IsTestTime = true here, let the normal time checks below handle it
                    }
                    // CRITICAL FIX: With time slots removed, we only need to check if the booking is confirmed
                    // All confirmed bookings are available to take immediately

                    // For retake tests or just completed payments, always set as available
                    if ((ViewBag.IsRetakeFromQuery || ViewBag.IsFromPaymentFlow) && booking.Status == "Confirmed")
                    {
                        _logger.LogInformation($"Retake test or payment just completed. Setting test as available for test ID: {test.Id}");
                        ViewBag.IsTestTime = true;
                        ViewBag.IsExpired = false;
                    }
                    // For all other confirmed bookings, also set as available
                    else if (booking.Status == "Confirmed")
                    {
                        _logger.LogInformation($"Booking is confirmed. Setting test as available for test ID: {test.Id}");
                        ViewBag.IsTestTime = true;
                        ViewBag.IsExpired = false;
                    }
                    // For pending or failed payments, set as not available
                    else
                    {
                        _logger.LogInformation($"Booking is not confirmed. Status: {booking.Status}. Setting test as unavailable for test ID: {test.Id}");
                        ViewBag.IsTestTime = false;
                    }

                    // CRITICAL FIX: Force IsTestTime to true for all confirmed bookings
                    // This ensures the Start Test button is enabled when it should be
                    if (booking.Status == "Confirmed")
                    {
                        _logger.LogInformation($"CRITICAL FIX: Forcing IsTestTime to true for confirmed booking. Test ID: {test.Id}");
                        ViewBag.IsTestTime = true;
                    }

                    // If the test is in the future, set the target date for the countdown
                    if (ViewBag.IsFutureTest)
                    {
                        // Use the startTime variable which is already safely set
                        DateTime targetDate = startTime.HasValue ? startTime.Value : DateTime.Now.AddHours(24);
                        ViewBag.TargetDateString = targetDate.ToString("yyyy-MM-ddTHH:mm:ss");

                        // Calculate time remaining
                        TimeSpan timeRemaining = startTime.HasValue ? startTime.Value - now : TimeSpan.FromHours(24);
                        ViewBag.DaysRemaining = timeRemaining.Days;
                        ViewBag.HoursRemaining = timeRemaining.Hours;
                        ViewBag.MinutesRemaining = timeRemaining.Minutes;
                        ViewBag.SecondsRemaining = timeRemaining.Seconds;

                        // Format a human-readable time remaining message
                        string timeRemainingMessage = "";
                        if (timeRemaining.Days > 0)
                        {
                            timeRemainingMessage += $"{timeRemaining.Days} day{(timeRemaining.Days != 1 ? "s" : "")} ";
                        }
                        if (timeRemaining.Hours > 0)
                        {
                            timeRemainingMessage += $"{timeRemaining.Hours} hour{(timeRemaining.Hours != 1 ? "s" : "")} ";
                        }
                        if (timeRemaining.Minutes > 0)
                        {
                            timeRemainingMessage += $"{timeRemaining.Minutes} minute{(timeRemaining.Minutes != 1 ? "s" : "")} ";
                        }
                        timeRemainingMessage += $"until your scheduled test time";
                        ViewBag.TimeRemainingMessage = timeRemainingMessage;
                    }
                }
                else
                {
                    // No booking found - redirect to BookSlot action
                    _logger.LogWarning("No booking found for test ID: {TestId} and user ID: {UserId}", id, userId);
                    return RedirectToAction("BookSlot");
                }

                // Check if the user has already taken this test
                var username = User.Identity?.Name;
                var testResults = await _context.TestResults
                    .Where(tr => tr.TestId == id && tr.Username == username)
                    .OrderByDescending(tr => tr.SubmittedAt)
                    .ToListAsync();

                var testResult = testResults.FirstOrDefault();

                // Check if this is a retake attempt
                bool isRetake = Request.Query.ContainsKey("retake") && Request.Query["retake"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase);

                // Check if the test was taken recently (within the last 24 hours)
                bool recentlyTaken = false;
                if (testResult != null)
                {
                    recentlyTaken = (DateTime.Now - testResult.SubmittedAt).TotalHours < 24;
                    _logger.LogInformation($"Test {id} was taken by {username} at {testResult.SubmittedAt}. Hours since submission: {(DateTime.Now - testResult.SubmittedAt).TotalHours}. Recently taken: {recentlyTaken}");

                    // Set ViewBag properties for the view
                    ViewBag.HasTestResult = true;
                    ViewBag.TestResultId = testResult.Id;
                    ViewBag.RecentlyTaken = recentlyTaken;
                    ViewBag.LastSubmittedAt = testResult.SubmittedAt;
                }

                // Check if this is a retake test
                bool isRetakeTest = Request.Query.ContainsKey("retake") &&
                                   Request.Query["retake"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase);

                if (isRetakeTest)
                {
                    _logger.LogInformation($"User {username} is retaking test {id}");
                    ViewBag.IsRetake = true; // Keep this for backward compatibility
                }

                // Set success message if coming from payment flow
                if (justPaid || fromPayment || hasSuccessfulPayment)
                {
                    _logger.LogInformation($"User coming from payment flow or has successful payment: justPaid={justPaid}, fromPayment={fromPayment}, hasSuccessfulPayment={hasSuccessfulPayment}");

                    // Set success message and flags
                    ViewBag.SuccessMessage = "Payment successful! You can now start your test during your scheduled time slot.";
                    ViewBag.JustPaid = true;
                    ViewBag.FromPayment = true; // Ensure FromPayment is set to true

                    // Keep the JustPaid flag for the next request
                    TempData.Keep("JustPaid");

                    // Clear any payment warning since payment is now confirmed
                    ViewBag.PaymentWarning = null;
                }

                // CRITICAL DEBUG: Log all ViewBag values before returning the view
                _logger.LogInformation("CRITICAL DEBUG: Final ViewBag values - BookingStatus: {BookingStatus}, PaymentWarning: {PaymentWarning}, JustPaid: {JustPaid}, FromPayment: {FromPayment}",
                    (string)ViewBag.BookingStatus, (string)ViewBag.PaymentWarning, (bool?)ViewBag.JustPaid, (bool?)ViewBag.FromPayment);

                return View(test);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ScheduledTest action");
                return RedirectToAction("Index", new { error = "An error occurred while loading your scheduled test" });
            }
        }

        // Upload questions functionality has been removed in favor of using category questions only

        // View action for starting a test based on scheduled slot
        [HttpGet]
        [Route("Test/Start/{id}")]
        [Authorize(Roles = "Candidate,SpecialUser")]
        public async Task<IActionResult> Start(int id)
        {
            // Redirect to the Take action which has all the time validation logic
            return RedirectToAction("Take", new { id });
        }

        // Legacy Start action implementation - removed and replaced with redirect to Take

        // View action for booking a test slot without specifying a test ID
        [HttpGet]
        [Route("Test/BookSlot")]
        [Authorize(Roles = "Candidate,SpecialUser")]
        public IActionResult BookSlot()
        {
            // Redirect to the test selection page
            _logger.LogInformation("User accessed BookSlot without test ID - redirecting to test selection page.");

            // Redirect to the Index action
            return RedirectToAction("Index");
        }

        // View action for booking a test slot
        [HttpGet]
        [Route("Test/BookSlot/{id}")]
        [Authorize(Roles = "Candidate,SpecialUser")]
        public async Task<IActionResult> BookSlot(int id, bool fromPayment = false, bool paymentFailed = false)
        {
            try
            {
                _logger.LogInformation($"BookSlot action called with id={id}, fromPayment={fromPayment}, paymentFailed={paymentFailed}");

                var test = await _context.Tests.FindAsync(id);
                if (test == null)
                {
                    _logger.LogWarning($"Test with ID {id} not found");
                    return RedirectToAction("Index", new { error = "Test not found" });
                }

                // Check if the user has already booked this test
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("User ID not found in claims");
                    return RedirectToAction("Login", "Auth");
                }

                // CRITICAL FIX: Use userId directly as SapId since it's now a string
                var candidateId = userId;
                _logger.LogInformation($"Processing booking for user {candidateId}, test {id}");

                // Allow multiple bookings for the same test
                // Only log existing bookings for informational purposes
                if (!paymentFailed)
                {
                    // Check if there is a Pending or Confirmed booking (for logging only)
                    var existingBooking = await _context.TestBookings
                        .FirstOrDefaultAsync(tb => tb.TestId == id && tb.UserSapId == userId &&
                                           (tb.Status == "Pending" || tb.Status == "Confirmed"));

                    if (existingBooking != null)
                    {
                        _logger.LogInformation($"User {candidateId} already has a booking for test {id} with status {existingBooking.Status}, but will be allowed to book again");
                    }

                    // Check for any existing bookings (including pending ones) and mark them as superseded
                    var oldBookings = await _context.TestBookings
                        .Where(tb => tb.TestId == id && tb.UserSapId == userId)
                        .ToListAsync();

                    if (oldBookings.Any())
                    {
                        _logger.LogInformation($"Found {oldBookings.Count} existing bookings for test {id} by user {candidateId}. Marking as superseded.");
                        foreach (var oldBooking in oldBookings)
                        {
                            oldBooking.Status = "Superseded";
                            oldBooking.UpdatedAt = Utilities.TimeZoneHelper.GetCurrentIstTime();
                            oldBooking.StatusReason = "Replaced by new booking";
                        }
                        await _context.SaveChangesAsync();
                    }
                }

                // Check if there are any failed bookings for this test
                var failedBookings = await _context.TestBookings
                    .Where(tb => tb.TestId == id && tb.UserSapId == userId &&
                           (tb.Status == "Failed" || tb.Status == "Cancelled"))
                    .ToListAsync();

                if (failedBookings.Any())
                {
                    _logger.LogInformation($"Found {failedBookings.Count} failed/cancelled bookings for test {id}. User {candidateId} will be allowed to book again.");

                    // If this is a payment failure, show a specific message
                    if (paymentFailed)
                    {
                        ViewBag.PaymentFailed = true;
                        if (string.IsNullOrEmpty(Request.Query["error"]))
                        {
                            ViewBag.Error = "Your previous payment attempt was not successful. Please try booking again.";
                        }
                    }
                }

                // Pass any error or message from query parameters to the view
                if (!string.IsNullOrEmpty(Request.Query["error"]))
                {
                    ViewBag.Error = Request.Query["error"];
                }

                if (!string.IsNullOrEmpty(Request.Query["message"]))
                {
                    ViewBag.Message = Request.Query["message"];
                }

                // If coming from a successful payment, show a success message
                if (fromPayment)
                {
                    ViewBag.SuccessMessage = "Payment successful! You can now book another test slot.";
                    _logger.LogInformation("User redirected to BookSlot after successful payment for test {TestId}", id);
                }

                // Clear any retake-related session variables
                try
                {
                    HttpContext.Session.Remove("IsReattempt");
                    _logger.LogInformation("Cleared IsReattempt from session for test {TestId}", id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error clearing IsReattempt from session for test {TestId}", id);
                    // Continue anyway
                }

                return View(test);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in BookSlot action for test ID {TestId}", id);

                // Add more detailed error information
                string errorMessage = "An error occurred while loading the booking page";

                // Log inner exception details if available
                if (ex.InnerException != null)
                {
                    _logger.LogError(ex.InnerException, "Inner exception details");
                    errorMessage += ": " + ex.Message;
                }

                // Check for specific exception types
                if (ex is InvalidOperationException)
                {
                    _logger.LogError("InvalidOperationException detected - likely a database query issue");
                    errorMessage = "Unable to retrieve test information. Please try again later.";
                }
                else if (ex is MySqlException || ex.Message.Contains("database") || ex.Message.Contains("SQL") || ex.Message.Contains("MySql"))
                {
                    _logger.LogError("Database exception detected");
                    errorMessage = "Database connection issue. Please try again later.";
                }
                else if (ex.Message.Contains("session") || ex.Message.Contains("Session"))
                {
                    _logger.LogError("Session-related exception detected");
                    errorMessage = "Your session may have expired. Please refresh the page and try again.";
                }

                // Store error in TempData for debugging
                try
                {
                    TempData["BookingError"] = ex.Message;
                    TempData["BookingErrorStack"] = ex.StackTrace?.Substring(0, Math.Min(ex.StackTrace?.Length ?? 0, 500));
                }
                catch { /* Ignore errors storing in TempData */ }

                return RedirectToAction("Index", new { error = errorMessage });
            }
        }

        // BookSlotForSpecialUser action removed - special users now use the regular BookSlot action

        // Process the booking - time slots completely removed
       
        // [Authorize(Roles = "Candidate,SpecialUser")]
        // public async Task<IActionResult> ProcessBooking(int id, string selectedDate)
        // {
        //     try
        //     {
        //         var test = await _context.Tests.FindAsync(id);
        //         if (test == null)
        //         {
        //             return RedirectToAction("Index", new { error = "Test not found" });
        //         }

        //         // Check if the user is authenticated and get their ID
        //         var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        //         if (string.IsNullOrEmpty(userId))
        //         {
        //             return RedirectToAction("Login", "Auth");
        //         }

        //         // Check if the user is a SpecialUser
        //         bool isSpecialUser = User.IsInRole("SpecialUser");

        //         // CRITICAL FIX: Use userId directly as SapId since it's now a string
        //         var candidateId = userId;

        //         // Allow multiple bookings for the same test
        //         // Only log existing bookings for informational purposes
        //         var existingBooking = await _context.TestBookings
        //                 .FirstOrDefaultAsync(tb => tb.TestId == id && tb.UserSapId == userId &&
        //                                    (tb.Status == "Pending" || tb.Status == "Confirmed"));

        //         if (existingBooking != null)
        //         {
        //             _logger.LogInformation($"User {candidateId} already has a booking for test {id} with status {existingBooking.Status}, but will be allowed to book again");
        //         }

        //         // Check for superseded, abandoned, or failed bookings and mark them as superseded
        //         // Only mark non-confirmed and non-completed bookings as superseded
        //         var oldBookings = await _context.TestBookings
        //             .Where(tb => tb.TestId == id && tb.UserSapId == userId &&
        //                    tb.Status != "Superseded" && tb.Status != "Confirmed" && tb.Status != "Completed")
        //             .ToListAsync();

        //         if (oldBookings.Any())
        //         {
        //             _logger.LogInformation($"Found {oldBookings.Count} old bookings for test {id} by user {candidateId}. Marking as superseded.");
        //             foreach (var oldBooking in oldBookings)
        //             {
        //                 oldBooking.Status = "Superseded";
        //                 oldBooking.UpdatedAt = Utilities.TimeZoneHelper.GetCurrentIstTime();
        //                 oldBooking.StatusReason = "Replaced by new booking";
        //             }
        //             await _context.SaveChangesAsync();
        //         }

        //         // Check if there are any failed bookings for this test
        //         var failedBooking = await _context.TestBookings
        //             .FirstOrDefaultAsync(tb => tb.TestId == id && tb.UserSapId == userId && tb.Status == "Failed");
        //         if (failedBooking != null)
        //         {
        //             _logger.LogInformation($"Found failed booking (ID: {failedBooking.Id}) for test {id}. User {candidateId} will be allowed to book again.");
        //         }

        //         // Parse the selected date (if provided, otherwise use today)
        //         DateTime bookingDate = DateTime.Today;
        //         if (!string.IsNullOrEmpty(selectedDate))
        //         {
        //             if (!DateTime.TryParse(selectedDate, out bookingDate))
        //             {
        //                 _logger.LogWarning($"Invalid date format: {selectedDate}, using today's date instead");
        //                 bookingDate = DateTime.Today;
        //             }
        //         }

        //         // CRITICAL FIX: Ensure we're using the date exactly as selected without any timezone conversion
        //         // Do NOT modify the date in any way - keep it exactly as parsed
        //         _logger.LogInformation($"Selected booking date (no timezone conversion): {bookingDate:yyyy-MM-dd}");

        //         // Validate that the booking date is not more than 7 days in the future
        //         if (bookingDate.Date > DateTime.Today.AddDays(7))
        //         {
        //             return RedirectToAction("BookSlot", new { id, error = "You can only book a test up to 7 days in advance" });
        //         }

        //         // Set default slot number to 0 (no slot)
        //         int? slotNumber = 0;

        //         // Always set start and end times to valid values
        //         // Start time is current time
        //         DateTime? startTime = DateTime.Now;

        //         // End time is start time + test duration
        //         DateTime? endTime = startTime.Value.AddMinutes(test.DurationMinutes);

        //         // With time slots completely removed, we don't need to check slot availability
        //         // We'll just check if the test has reached its maximum capacity
        //         int totalBookings;
        //         try
        //         {
        //             totalBookings = await _context.TestBookings
        //                 .Where(tb => tb.TestId == id && tb.Status == "Confirmed")
        //                 .CountAsync();
        //         }
        //         catch (Exception ex)
        //         {
        //             _logger.LogError(ex, "Error counting bookings for test {TestId}", id);
        //             totalBookings = 0;
        //         }

        //         const int maxUsersPerTest = 1000; // Max 1000 users per test
        //         if (totalBookings >= maxUsersPerTest)
        //         {
        //             return RedirectToAction("BookSlot", new { id, error = "This test has reached its maximum capacity. Please try another test." });
        //         }

        //         // Get the user's SAP ID
        //         string? userSapId = null;
        //         var user = await _context.Users.FindAsync(candidateId);
        //         if (user != null && !string.IsNullOrEmpty(user.SapId))
        //         {
        //             userSapId = user.SapId;
        //             _logger.LogInformation("User {UserId} has SAP ID: {SapId}", candidateId, userSapId);
        //         }
        //         else
        //         {
        //             _logger.LogWarning("User {UserId} does not have a SAP ID", candidateId);
        //         }

        //         // Check if there are multiple existing pending bookings for this test and user
        //         var existingPendingBookings = await _context.TestBookings
        //             .Where(tb => tb.TestId == id && tb.UserSapId == (userSapId ?? candidateId.ToString()) && tb.Status == "Pending")
        //             .OrderByDescending(tb => tb.BookedAt)
        //             .ToListAsync();

        //         TestBooking pendingBooking;

        //         // If there are multiple pending bookings, mark all but the most recent as superseded
        //         if (existingPendingBookings.Count > 1)
        //         {
        //             _logger.LogWarning($"Found {existingPendingBookings.Count} pending bookings for test {id} by user {candidateId}. Marking all but the most recent as superseded.");

        //             // Keep the most recent booking
        //             var mostRecentBooking = existingPendingBookings.First();

        //             // Mark all others as superseded
        //             for (int i = 1; i < existingPendingBookings.Count; i++)
        //             {
        //                 var oldBooking = existingPendingBookings[i];
        //                 oldBooking.Status = "Superseded";
        //                 oldBooking.UpdatedAt = Utilities.TimeZoneHelper.GetCurrentIstTime();
        //                 oldBooking.StatusReason = "Duplicate pending booking superseded";
        //                 _logger.LogInformation($"Marking booking {oldBooking.Id} as superseded (duplicate pending)");
        //             }

        //             // Save changes to mark old bookings as superseded
        //             await _context.SaveChangesAsync();

        //             // Use the most recent booking
        //             existingPendingBookings = new List<TestBooking> { mostRecentBooking };
        //         }

        //         // If there's exactly one pending booking, use it
        //         if (existingPendingBookings.Count == 1)
        //         {
        //             pendingBooking = existingPendingBookings.First();
        //             _logger.LogInformation($"Found existing pending booking {pendingBooking.Id} for test {id} by user {candidateId}. Reusing it.");
        //         }
        //         else // If there are no pending bookings (or none after superseding duplicates), create a new one
        //         {
        //             // Create the booking
        //             var booking = new TestBooking
        //             {
        //                 TestId = id,
        //                 UserSapId = userSapId ?? candidateId.ToString(), // Use userSapId if available, otherwise candidateId
        //                 BookedAt = Utilities.TimeZoneHelper.GetCurrentIstTime(),
        //                 BookingDate = Utilities.TimeZoneHelper.ToIst(bookingDate.Date),
        //                 StartTime = Utilities.TimeZoneHelper.ToIst(startTime.Value),
        //                 EndTime = Utilities.TimeZoneHelper.ToIst(endTime.Value),
        //                 SlotNumber = slotNumber ?? 0, // Use 0 if slotNumber is null
        //                 Status = isSpecialUser ? "Confirmed" : "Pending", // Mark as confirmed for special users, pending for others
        //                 StatusReason = "Created directly as confirmed after payment"
        //             };

        //             _context.TestBookings.Add(booking);
        //             await _context.SaveChangesAsync();
        //             _logger.LogInformation($"Created new booking {booking.Id} for test {id} by user {candidateId} with status {booking.Status}");

        //             pendingBooking = booking;
        //         }

        //         // If it's a special user, we've already confirmed the booking. Redirect to MyBookings.
        //         if (isSpecialUser)
        //         {
        //             // Remove the certificate initiation logic from here
        //             // This will now happen after the test is submitted in the Submit action

        //             TempData["SuccessMessage"] = "Slot booked successfully!";
        //             return RedirectToAction("MyBookings", "Test");
        //         }
        //         else // For regular users, redirect to the payment gateway
        //         {
        //             // Store booking details in TempData for the PaymentController
        //             TempData["BookedTestId"] = id;
        //             TempData["BookedDate"] = bookingDate.ToString("yyyy-MM-dd");
        //             TempData["BookedStartTime"] = startTime.Value.ToString("HH:mm:ss");
        //             TempData["BookedEndTime"] = endTime.Value.ToString("HH:mm:ss");
        //             TempData["BookedSlotNumber"] = slotNumber ?? 0;
        //             TempData["UserSapIdForPayment"] = userSapId ?? candidateId.ToString(); // Ensure SAP ID is passed for payment
        //             TempData["JustPaid"] = false; // Reset this flag
        //             _logger.LogInformation("Stored booking details in TempData for payment redirect");

        //             // Redirect to payment gateway with relevant parameters
        //             _logger.LogInformation($"Redirecting to payment gateway with parameters: TestId={id}, Date={bookingDate:yyyy-MM-dd}");

        //         // Use the RazorpayInitiate route directly with all parameters in the query string
        //         // This bypasses any session-related issues
        //         return RedirectToAction("RazorpayInitiate", "Payment", new
        //         {
        //             testId = id,
        //             date = bookingDate.ToString("yyyy-MM-dd"),
        //             startTime = startTime.Value.ToString("HH:mm:ss"),
        //             endTime = endTime.Value.ToString("HH:mm:ss"),
        //                 slotNumber = slotNumber ?? 0, // Use 0 to indicate no slot
        //                 userSapIdParam = userSapId ?? candidateId.ToString() // Ensure SAP ID is passed for payment
        //         });
        //         }
        //     }
        //     catch (Exception ex)
        //     {
        //         _logger.LogError(ex, "Error in ProcessBooking action");
        //         return RedirectToAction("BookSlot", new { id, error = "An error occurred while processing your booking" });
        //     }
        // }
        [HttpPost]
        [Route("Test/ProcessBooking/{id}")]
        [Authorize(Roles = "Candidate,SpecialUser")]
        public async Task<IActionResult> ProcessBooking(int id, string selectedDate)
        {
            try
            {
                var test = await _context.Tests.FindAsync(id);
                if (test == null)
                {
                    return RedirectToAction("Index", new { error = "Test not found" });
                }

                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return RedirectToAction("Login", "Auth");
                }

                var candidateId = userId;
                bool isSpecialUser = User.IsInRole("SpecialUser");

                // Supersede old incomplete bookings
                var oldBookings = await _context.TestBookings
                    .Where(tb => tb.TestId == id && tb.UserSapId == userId &&
                                 tb.Status != "Superseded" && tb.Status != "Confirmed" && tb.Status != "Completed")
                    .ToListAsync();

                foreach (var oldBooking in oldBookings)
                {
                    oldBooking.Status = "Superseded";
                    oldBooking.UpdatedAt = Utilities.TimeZoneHelper.GetCurrentIstTime();
                    oldBooking.StatusReason = "Replaced by new booking";
                }

                if (oldBookings.Any())
                {
                    await _context.SaveChangesAsync();
                }

                DateTime bookingDate = DateTime.Today;
                if (!string.IsNullOrEmpty(selectedDate) && !DateTime.TryParse(selectedDate, out bookingDate))
                {
                    _logger.LogWarning($"Invalid date format: {selectedDate}, using today's date instead");
                    bookingDate = DateTime.Today;
                }

                if (bookingDate.Date > DateTime.Today.AddDays(7))
                {
                    return RedirectToAction("BookSlot", new { id, error = "You can only book a test up to 7 days in advance" });
                }

                int? slotNumber = 0;
                DateTime? startTime = DateTime.Now;
                DateTime? endTime = startTime.Value.AddMinutes(test.DurationMinutes);

                int totalBookings = await _context.TestBookings
                    .Where(tb => tb.TestId == id && tb.Status == "Confirmed")
                    .CountAsync();

                const int maxUsersPerTest = 1000;
                if (totalBookings >= maxUsersPerTest)
                {
                    return RedirectToAction("BookSlot", new { id, error = "This test has reached its maximum capacity. Please try another test." });
                }

                string? userSapId = null;
                var user = await _context.Users.FindAsync(candidateId);
                if (user != null && !string.IsNullOrEmpty(user.SapId))
                {
                    userSapId = user.SapId;
                    _logger.LogInformation("User {UserId} has SAP ID: {SapId}", candidateId, userSapId);
                }
                else
                {
                    _logger.LogWarning("User {UserId} does not have a SAP ID", candidateId);
                }

                var booking = new TestBooking
                {
                    TestId = id,
                    UserSapId = userSapId ?? candidateId.ToString(),
                    BookedAt = Utilities.TimeZoneHelper.GetCurrentIstTime(),
                    BookingDate = Utilities.TimeZoneHelper.ToIst(bookingDate.Date),
                    StartTime = Utilities.TimeZoneHelper.ToIst(startTime.Value),
                    EndTime = Utilities.TimeZoneHelper.ToIst(endTime.Value),
                    SlotNumber = slotNumber ?? 0,
                    Status = "Confirmed",
                    StatusReason = "Confirmed booking without payment"
                };

                _context.TestBookings.Add(booking);
                await _context.SaveChangesAsync();
                _logger.LogInformation($"Created new booking {booking.Id} for test {id} by user {candidateId}");

                TempData["SuccessMessage"] = "Slot booked successfully!";
                return RedirectToAction("MyBookings", "Test");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ProcessBooking action");
                return RedirectToAction("BookSlot", new { id, error = "An error occurred while processing your booking" });
            }
        }

        // Check test availability (time slots completely removed)
        [HttpGet]
        [Route("Test/CheckSlotAvailability")]
        [Authorize(Roles = "Candidate")]
        public async Task<IActionResult> CheckSlotAvailability(int testId, string date, int slotNumber = 0)
        {
            try
            {
                // For backward compatibility, we still accept the slotNumber parameter
                // but we don't use it for availability checking anymore

                if (!DateTime.TryParse(date, out DateTime bookingDate))
                {
                    return Ok(new { isAvailable = true, message = "Date format invalid, but tests are available anytime", currentCount = 0, maxCount = 1000 });
                }

                // CRITICAL FIX: Do NOT modify the booking date in any way
                // Just log it for debugging purposes
                _logger.LogInformation($"Checking test availability for date: {bookingDate:yyyy-MM-dd}");

                // Count total bookings for this test
                int totalBookings;
                try
                {
                    totalBookings = await _context.TestBookings
                        .Where(tb => tb.TestId == testId && tb.Status == "Confirmed")
                        .CountAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error counting bookings for test {TestId}", testId);
                    totalBookings = 0;
                }

                const int maxUsersPerTest = 1000; // Max 1000 users per test
                bool isAvailable = totalBookings < maxUsersPerTest;

                // Time slots completely removed - all tests are available anytime
                return Ok(new {
                    isAvailable = true,
                    message = "Time slots removed - tests are available anytime after booking",
                    currentCount = totalBookings,
                    maxCount = maxUsersPerTest
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking test availability");
                return Ok(new { isAvailable = true, message = "Error checking availability, but time slots are removed and tests are available anytime after booking", currentCount = 0, maxCount = 1000 });
            }
        }

        // Legacy method - kept for backward compatibility
        [HttpGet]
        [Route("Test/CheckTimeAvailability")]
        [Authorize(Roles = "Candidate")]
        public async Task<IActionResult> CheckTimeAvailability(int testId, string date, string startTime, string endTime)
        {
            try
            {
                if (!DateTime.TryParse(date, out DateTime bookingDate))
                {
                    return Ok(new { isAvailable = false, message = "Invalid date format" });
                }

                // CRITICAL FIX: Do NOT modify the booking date in any way
                // Just log it for debugging purposes
                _logger.LogInformation($"Checking time availability for date: {bookingDate:yyyy-MM-dd}");

                if (!DateTime.TryParse(startTime, out DateTime start) || !DateTime.TryParse(endTime, out DateTime end))
                {
                    return Ok(new { isAvailable = false, message = "Invalid time format" });
                }

                // Combine the date with the time
                start = bookingDate.Date.Add(start.TimeOfDay);
                end = bookingDate.Date.Add(end.TimeOfDay);

                // Check if the time slot is available (no overlapping bookings)
                var overlappingBookings = await _context.TestBookings
                    .Where(tb => tb.TestId == testId && tb.BookingDate.HasValue && tb.BookingDate.Value.Date == bookingDate.Date &&
                           ((tb.StartTime.HasValue && tb.EndTime.HasValue && tb.StartTime.Value <= start && tb.EndTime.Value > start) ||
                            (tb.StartTime.HasValue && tb.EndTime.HasValue && tb.StartTime.Value < end && tb.EndTime.Value >= end) ||
                            (tb.StartTime.HasValue && tb.EndTime.HasValue && tb.StartTime.Value >= start && tb.EndTime.Value <= end)))
                    .CountAsync();

                bool isAvailable = overlappingBookings < 200; // Max 200 users per time slot

                return Ok(new { isAvailable, currentCount = overlappingBookings, maxCount = 200 });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking time slot availability");
                return Ok(new { isAvailable = false, message = "Error checking availability" });
            }
        }

        [HttpDelete]
        [Route("Test/delete/{id}")]
        [Authorize(Roles = "Organization")]
        public async Task<IActionResult> DeleteTest(int id)
        {
            try
            {
                // Check if user is organization
                var userSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (userSapId == null)
                {
                    return Json(new { success = false, message = "Unauthorized: Organization access required." });
                }

                // Get test with related data
                var test = await _context.Tests
                    .FirstOrDefaultAsync(t => t.Id == id);

                if (test == null)
                {
                    return Json(new { success = false, message = "Test not found." });
                }

                // Verify ownership - only allow organizations to delete their own tests
                if (test.CreatedBySapId != userSapId)
                {
                    return Json(new { success = false, message = "You can only delete tests that you created." });
                }

                try
                {
                    // Step 1: Delete test bookings using raw SQL
                    try
                    {
                        // Use direct SQL to avoid Entity Framework issues
                        await _context.Database.ExecuteSqlRawAsync(
                            "DELETE FROM testbookings WHERE TestId = {0}", id);
                        _logger.LogInformation($"Test bookings for test {id} deleted successfully using raw SQL");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error deleting test bookings for test {id}. Continuing with other deletions...");
                        // Continue with the rest of the deletion process
                    }

                    // Step 2: Delete test results using raw SQL
                    try
                    {
                        // Use direct SQL to avoid Entity Framework issues
                        await _context.Database.ExecuteSqlRawAsync(
                            "DELETE FROM testresult WHERE TestId = {0}", id);
                        _logger.LogInformation($"Test results for test {id} deleted successfully using raw SQL");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error deleting test results for test {id}. Continuing with other deletions...");
                        // Continue with the rest of the deletion process
                    }

                    // Step 3: Hard delete the test (completely remove from database)
                    try
                    {
                        // Use direct SQL to avoid Entity Framework issues
                        await _context.Database.ExecuteSqlRawAsync(
                            "DELETE FROM Tests WHERE Id = {0}", id);
                        _logger.LogInformation($"Test {id} hard-deleted successfully using raw SQL");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error hard-deleting test {id} using SQL. Trying alternative method...");

                        try
                        {
                            // Fallback to Entity Framework if raw SQL fails
                            _context.Tests.Remove(test);
                            await _context.SaveChangesAsync();
                            _logger.LogInformation($"Test {id} hard-deleted successfully using Entity Framework");
                        }
                        catch (Exception innerEx)
                        {
                            _logger.LogError(innerEx, $"Both methods failed to hard-delete test {id}");
                            return Json(new { success = false, message = "An error occurred while deleting the test." });
                        }
                    }

                    return Json(new { success = true, message = "Test permanently deleted from the database." });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error deleting test {id} and its related data");
                    return Json(new { success = false, message = "An error occurred while deleting the test and its related data." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in DeleteTest action for test {id}");
                return Json(new { success = false, message = "An unexpected error occurred while deleting the test." });
            }
        }



        // Action to check if a user has already booked a test
        [HttpGet]
        [Route("Test/CheckBooking/{id}")]
        [Authorize(Roles = "Candidate,SpecialUser")]
        public async Task<IActionResult> CheckBooking(int id)
        {
            try
            {
                // Get the current user SAP ID
                var userSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userSapId))
                {
                    return Json(new { success = false, message = "User not found" });
                }

                // Get all bookings for this user and test
                var bookings = await _context.TestBookings
                    .AsNoTracking()
                    .Where(tb => tb.UserSapId == userSapId && tb.TestId == id)
                    .OrderByDescending(tb => tb.BookedAt)
                    .ToListAsync();

                // Return the bookings
                return Json(new {
                    success = true,
                    hasBookings = bookings.Any(),
                    bookingsCount = bookings.Count,
                    bookings = bookings.Select(b => new {
                        id = b.Id,
                        testId = b.TestId,
                        status = b.Status,
                        bookedAt = b.BookedAt,
                        bookingDate = b.BookingDate,
                        // Time slots completely removed, but we'll keep these fields for backward compatibility
                        startTime = b.StartTime,
                        endTime = b.EndTime,
                        slotNumber = b.SlotNumber
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in CheckBooking action");
                return Json(new { success = false, message = "An error occurred while checking bookings" });
            }
        }

        // Create a reusable JsonSerializerOptions instance with comprehensive options
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions {
            ReferenceHandler = ReferenceHandler.Preserve,
            MaxDepth = 64,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        // GET endpoint for auto-submitting tests when a user clicks back and then tries to start again
        [HttpGet]
        [Route("Test/Submit/{id}")]
        [Authorize]
        public async Task<IActionResult> AutoSubmit(int id, bool autoSubmit = false)
        {
            try
            {
                if (!autoSubmit)
                {
                    return RedirectToAction("Take", new { id });
                }

                _logger.LogInformation("Auto-submitting test ID: {TestId} for user: {Username}", id, User.Identity?.Name ?? "Anonymous");

                // Create empty answers dictionary
                Dictionary<string, string> answers = new Dictionary<string, string>();

                // Get the test
                _logger.LogInformation("Auto-submit: Looking up test with ID: {TestId}", id);
                var test = await _context.Tests
                    .FirstOrDefaultAsync(t => t.Id == id);

                if (test == null)
                {
                    _logger.LogError("Auto-submit: Test not found with ID: {TestId}. This is causing the submission to fail.", id);

                    // Try to find the test even if it's marked as deleted
                    var deletedTest = await _context.Tests
                        .IgnoreQueryFilters() // This will ignore any global query filters
                        .FirstOrDefaultAsync(t => t.Id == id);

                    if (deletedTest != null && deletedTest.IsDeleted)
                    {
                        _logger.LogWarning("Auto-submit: Found test with ID: {TestId} but it's marked as deleted. IsDeleted: {IsDeleted}, DeletedAt: {DeletedAt}",
                            id, deletedTest.IsDeleted, deletedTest.DeletedAt);
                        // Use the deleted test anyway to allow submission
                        test = deletedTest;
                    }
                    else
                    {
                        // Check if there are any tests in the database
                        var testCount = await _context.Tests.CountAsync();
                        _logger.LogWarning("Auto-submit: Total tests in database: {TestCount}", testCount);

                        // Check if there are any tests with IDs close to the requested ID
                        var nearbyTests = await _context.Tests
                            .Where(t => t.Id >= id - 5 && t.Id <= id + 5)
                            .Select(t => new { t.Id, t.Title })
                            .ToListAsync();

                        if (nearbyTests.Any())
                        {
                            _logger.LogWarning("Auto-submit: Found tests with IDs close to {TestId}: {NearbyTests}",
                                id, JsonSerializer.Serialize(nearbyTests));
                        }

                        // Return a more user-friendly error
                        return RedirectToAction("Index", new {
                            error = $"Test with ID {id} not found. Please contact support if this issue persists."
                        });
                    }
                }

                // Check if user has already submitted this test
                var username = User.Identity?.Name ?? "Anonymous";

                // Check for recent submissions (within the last 60 seconds) to prevent duplicates
                var recentSubmissionTimeThreshold = Utilities.TimeZoneHelper.GetCurrentIstTime().AddSeconds(-60);
                var recentSubmission = await _context.TestResults
                    .Where(r => r.TestId == id &&
                           r.Username == username &&
                           r.SubmittedAt >= recentSubmissionTimeThreshold)
                    .OrderByDescending(r => r.SubmittedAt)
                    .FirstOrDefaultAsync();

                // If there's a recent submission, redirect to the existing result
                if (recentSubmission != null)
                {
                    _logger.LogWarning("Detected duplicate auto-submission for test {TestId} by user {Username}. Using existing result {ResultId}",
                        id, username, recentSubmission.Id);

                    return RedirectToAction("Result", new { id = recentSubmission.Id });
                }

                // Get the user's SAP ID
                string? sapId = null;
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var currentUsername = User.Identity?.Name;

                _logger.LogInformation("Getting SAP ID for auto-submit: UserId={UserId}, Username={Username}", userId, currentUsername);

                if (!string.IsNullOrEmpty(userId))
                {
                    // CRITICAL FIX: Use userId directly as SapId since it's now a string
                    var user = await _context.Users.FindAsync(userId);
                    if (user != null && !string.IsNullOrEmpty(user.SapId))
                    {
                        sapId = user.SapId;
                        _logger.LogInformation("Found SAP ID for auto-submit by UserId: {SapId}", sapId);
                    }
                    else if (!string.IsNullOrEmpty(currentUsername))
                    {
                        // Try to find by username as fallback
                        user = await _context.Users.FirstOrDefaultAsync(u => u.Username == currentUsername);
                        if (user != null && !string.IsNullOrEmpty(user.SapId))
                        {
                            sapId = user.SapId;
                            _logger.LogInformation("Found SAP ID for auto-submit by Username: {SapId}", sapId);
                        }
                    }
                }

                // CRITICAL FIX: If sapId is still null, use the userId from claims as fallback
                if (string.IsNullOrEmpty(sapId) && !string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("SAP ID is null for auto-submit, using UserId from claims as fallback: {UserId}", userId);
                    sapId = userId;
                }

                // Get the current time for the end time
                var currentTime = Utilities.TimeZoneHelper.GetCurrentIstTime();

                // Find any existing submission for this test by this user
                var existingResult = await _context.TestResults
                    .Where(r => r.TestId == id && r.Username == username)
                    .OrderByDescending(r => r.SubmittedAt)
                    .FirstOrDefaultAsync();

                // Count all existing submissions to determine attempt number
                var existingSubmissions = await _context.TestResults
                    .Where(r => r.TestId == id && r.Username == username)
                    .CountAsync();

                // Get all test results for this test and user
                var allResults = await _context.TestResults
                    .Where(r => r.TestId == id && r.Username == username)
                    .ToListAsync();

                // Find the highest attempt number
                int highestAttemptNumber = 0;
                if (allResults.Any())
                {
                    highestAttemptNumber = allResults.Max(r => r.AttemptNumber);
                }

                // Always increment by 1 from the highest attempt number
                int attemptNumber = highestAttemptNumber + 1;

                _logger.LogInformation("Calculated attempt number for auto-submit for user {Username} on test {TestId}: {AttemptNumber} (based on highest existing attempt {HighestAttempt})",
                    username, id, attemptNumber, highestAttemptNumber);

                // Log all existing test results for debugging
                foreach (var tr in allResults)
                {
                    _logger.LogInformation("Existing test result: ID={ResultId}, AttemptNumber={AttemptNumber}, SubmittedAt={SubmittedAt}",
                        tr.Id, tr.AttemptNumber, tr.SubmittedAt);
                }

                _logger.LogInformation("Setting attempt number for user {Username} on test {TestId}: {AttemptNumber}",
                    username, id, attemptNumber);

                _logger.LogInformation("User {Username} is auto-submitting test {TestId}. Previous attempts: {AttemptCount}, Current attempt: {CurrentAttempt}",
                    username, id, existingSubmissions, attemptNumber);

                TestResult result;

                // If an existing result exists, update it instead of creating a new one
                if (existingResult != null)
                {
                    _logger.LogInformation("Updating existing test result for auto-submission of test {TestId} by user {Username}. Result ID: {ResultId}",
                        id, username, existingResult.Id);

                    // Update the existing result with 0 score for auto-submission
                    existingResult.TotalQuestions = 0;
                    existingResult.CorrectAnswers = 0;
                    existingResult.Score = 0;
                    existingResult.McqScore = 0;
                    existingResult.CodingScore = 0;
                    existingResult.SubmittedAt = currentTime;
                    existingResult.EndTime = currentTime; // Record the end time when the test is completed

                    // Fix StartTime if it's invalid (after EndTime) or not set
                    if (!existingResult.StartTime.HasValue || existingResult.StartTime.Value > currentTime)
                    {
                        // Use a more reasonable start time for auto-submission
                        var estimatedDuration = Math.Min(test.DurationMinutes, 10); // Cap at 10 minutes for realistic timing
                        var reasonableStartTime = currentTime.AddMinutes(-estimatedDuration);
                        existingResult.StartTime = reasonableStartTime;
                        _logger.LogInformation("Fixed invalid StartTime for auto-submitted test result {ResultId}. Estimated duration: {Duration} minutes, Set to {StartTime}",
                            existingResult.Id, estimatedDuration, existingResult.StartTime);
                    }

                    existingResult.UserSapId = sapId;
                    existingResult.AttemptNumber = attemptNumber; // Set the attempt number

                    result = existingResult;
                }
                else
                {
                    // Create a test result with 0 score
                    _logger.LogInformation("Creating new test result for auto-submission of test {TestId} by user {Username}", id, username);

                    result = new TestResult
                    {
                        TestId = id,
                        Username = username,
                        TotalQuestions = 0,
                        CorrectAnswers = 0,
                        Score = 0,
                        McqScore = 0,
                        CodingScore = 0,
                        SubmittedAt = currentTime,
                        StartTime = currentTime.AddMinutes(-Math.Min(test.DurationMinutes, 10)), // Use reasonable start time, cap at 10 minutes
                        EndTime = currentTime, // Record the end time when the test is completed
                        UserSapId = sapId,
                        AttemptNumber = attemptNumber // Set the attempt number
                        // AutoSubmitted property will be set if the column exists in the database
                    };

                    _context.TestResults.Add(result);
                }

                // CRITICAL FIX: Update the booking status to Completed
                try
                {
                    // Find the booking for this test
                    var booking = await _context.TestBookings
                        .Where(b => b.TestId == id && b.UserSapId == sapId &&
                               (b.Status == "Confirmed" || b.Status == "Completed"))
                        .OrderByDescending(b => b.BookedAt)
                        .FirstOrDefaultAsync();

                    if (booking != null)
                    {
                        _logger.LogInformation($"Updating booking {booking.Id} status to Completed after auto-submission");
                        booking.Status = "Completed";
                        booking.UpdatedAt = Utilities.TimeZoneHelper.GetCurrentIstTime();
                        booking.StatusReason = "Test auto-completed by system";
                    }
                    else
                    {
                        _logger.LogWarning($"No booking found for test {id} and user {sapId} to mark as completed during auto-submission");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating booking status to Completed for auto-submitted test {TestId}", id);
                    // Continue with test submission even if booking update fails
                }

                await _context.SaveChangesAsync();

                // Verify the attempt number was saved correctly
                var savedResult = await _context.TestResults.FindAsync(result.Id);

                // Log the final attempt number after saving
                _logger.LogInformation("Final attempt number saved for auto-submit test {TestId} by user {Username}: {AttemptNumber}, Verified: {VerifiedAttemptNumber}",
                    id, username, result.AttemptNumber, savedResult?.AttemptNumber ?? 0);

                // Send test result email notification for auto-submitted test
                try
                {
                    // Get user information for the email
                    if (!string.IsNullOrEmpty(userId))
                    {
                        // CRITICAL FIX: Use userId directly as SapId since it's now a string
                        var user = await _context.Users.FindAsync(userId);
                        if (user != null)
                        {
                            // For auto-submitted tests, score is 0
                            double scorePercentage = 0;

                            // Get the passing score from the test model (though auto-submitted tests always fail)
                            int passingScore = test.PassingScore;

                            // Auto-submitted tests always fail
                            bool isPassed = false; // scorePercentage is 0, so it will always be less than passingScore

                            // Send the email notification
                            await _emailService.SendTestResultEmailAsync(
                                user.Email,
                                $"{user.FirstName} {user.LastName}" ?? username,
                                test.Title,
                                0, // No correct answers
                                0, // No total questions answered
                                scorePercentage,
                                isPassed
                            );

                            _logger.LogInformation("Auto-submit test result email sent to {Email} for test {TestId}. Score: 0%, Passed: false",
                                user.Email, id);
                        }
                        else
                        {
                            _logger.LogWarning("User not found with ID {UserId} when sending auto-submit test result email", userId);
                        }
                    }
                }
                catch (Exception emailEx)
                {
                    // Log the error but don't fail the test submission
                    _logger.LogError(emailEx, "Error sending auto-submit test result email for test {TestId}", id);
                }

                // Set a TempData flag to indicate this was an auto-submitted test
                TempData["AutoSubmitted"] = true;

                // Redirect to the result page with forceLatest=true to ensure we show the latest attempt
                return RedirectToAction("Result", new { id = result.Id, message = "Your test was automatically submitted because you navigated away from the test page.", forceLatest = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error auto-submitting test {TestId}", id);
                return RedirectToAction("Index", new { error = "An error occurred while submitting your test." });
            }
        }

        [HttpPost]
        [Route("Test/Submit/{id}")]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(int id, [FromBody] object data)
        {
            try
            {
                _logger.LogInformation("Received test submission for test ID: {TestId} from user: {Username}", id, User.Identity?.Name ?? "Anonymous");

                // Check if the data is a dictionary with a security violation or auto-submission flag
                bool isSecurityViolation = false;
                bool isAutoSubmitting = false;
                Dictionary<string, string> answers = new Dictionary<string, string>();

                if (data is JsonElement jsonElement)
                {
                    // Extract answers and check for security violation and auto-submission flags
                    foreach (var property in jsonElement.EnumerateObject())
                    {
                        if (property.Name == "isSecurityViolation" && property.Value.ValueKind == JsonValueKind.True)
                        {
                            isSecurityViolation = true;
                            _logger.LogWarning("Security violation detected in test submission for test ID: {TestId}", id);
                        }
                        else if (property.Name == "isAutoSubmitting" && property.Value.ValueKind == JsonValueKind.True)
                        {
                            isAutoSubmitting = true;
                            _logger.LogInformation("Auto-submission detected for test ID: {TestId}", id);
                        }
                        else if (property.Name.StartsWith("question_"))
                        {
                            answers[property.Name] = property.Value.GetString() ?? "";
                        }
                    }
                }
                else if (data is Dictionary<string, string> answerDict)
                {
                    answers = answerDict;
                }

                _logger.LogInformation("Received {Count} answers", answers?.Count ?? 0);

                if (answers == null || answers.Count == 0)
                {
                    _logger.LogWarning("No answers provided for test ID: {TestId}", id);
                    if (isSecurityViolation || isAutoSubmitting)
                    {
                        // For security violations or auto-submissions, continue with empty answers
                        answers = new Dictionary<string, string>();
                        _logger.LogWarning("Continuing with empty answers due to {Reason}",
                            isSecurityViolation ? "security violation" : "auto-submission");
                    }
                    else
                    {
                        return BadRequest(new { success = false, message = "No answers provided" });
                    }
                }

                // Get the test without loading questions - include tests that might be marked as deleted
                _logger.LogInformation("Looking up test with ID: {TestId}", id);
                var test = await _context.Tests
                    .FirstOrDefaultAsync(t => t.Id == id);

                if (test == null)
                {
                    // Log more details about the test lookup failure
                    _logger.LogError("Test not found with ID: {TestId}. This is causing the submission to fail.", id);

                    // Try to find the test even if it's marked as deleted
                    var deletedTest = await _context.Tests
                        .IgnoreQueryFilters() // This will ignore any global query filters
                        .FirstOrDefaultAsync(t => t.Id == id);

                    if (deletedTest != null && deletedTest.IsDeleted)
                    {
                        _logger.LogWarning("Found test with ID: {TestId} but it's marked as deleted. IsDeleted: {IsDeleted}, DeletedAt: {DeletedAt}",
                            id, deletedTest.IsDeleted, deletedTest.DeletedAt);
                        // Use the deleted test anyway to allow submission
                        test = deletedTest;
                    }
                    else
                    {
                        // Check if there are any tests in the database
                        var testCount = await _context.Tests.CountAsync();
                        _logger.LogWarning("Total tests in database: {TestCount}", testCount);

                        // Check if there are any tests with IDs close to the requested ID
                        var nearbyTests = await _context.Tests
                            .Where(t => t.Id >= id - 5 && t.Id <= id + 5)
                            .Select(t => new { t.Id, t.Title })
                            .ToListAsync();

                        if (nearbyTests.Any())
                        {
                            _logger.LogWarning("Found tests with IDs close to {TestId}: {NearbyTests}",
                                id, JsonSerializer.Serialize(nearbyTests));
                        }
                    }
                }

                // Load questions either from the Questions table or from CategoryQuestions
                if (test != null && test.CategoryQuestionsId.HasValue)
                {
                    // Load questions from CategoryQuestions
                    var categoryQuestions = await _context.CategoryQuestions
                        .FirstOrDefaultAsync(cq => cq.Id == test.CategoryQuestionsId);

                    if (categoryQuestions != null)
                    {
                        _logger.LogInformation("Found CategoryQuestions with ID {CategoryQuestionsId} for test {TestId}", test.CategoryQuestionsId, id);

                        // Deserialize questions from JSON using the shared options
                        var allQuestions = JsonSerializer.Deserialize<List<QuestionDto>>(categoryQuestions.QuestionsJson, _jsonOptions);

                        _logger.LogInformation("Submit method - Deserialized {AllQuestionsCount} questions from CategoryQuestions JSON for test {TestId}",
                            allQuestions?.Count ?? 0, id);

                        if (allQuestions == null)
                        {
                            _logger.LogError("Submit method - Deserialization returned null for test {TestId}, CategoryQuestionsId: {CategoryQuestionsId}",
                                id, test.CategoryQuestionsId);
                            allQuestions = new List<QuestionDto>();
                        }

                        // Use a consistent seed based on the test ID to ensure the same questions are selected
                        // This ensures the questions match what was shown to the user
                        var random = new Random(test.Id); // Use test ID as seed for consistent selection
                        var selectedQuestions = allQuestions
                            .OrderBy(q => random.Next())
                            .Take(Math.Min(test.QuestionCount, allQuestions.Count))
                            .ToList();

                        _logger.LogInformation($"Selected {selectedQuestions.Count} questions for test {id} using consistent seed {test.Id} (requested: {test.QuestionCount})");

                        // Create a dictionary to map question IDs to their answers
                        var questionAnswers = new Dictionary<int, string>();
                        foreach (var key in answers.Keys)
                        {
                            if (key.StartsWith("question_") && int.TryParse(key.Substring(9), out int questionId))
                            {
                                questionAnswers[questionId] = answers[key];
                            }
                        }

                        // Convert QuestionDto to InMemoryQuestion objects for the view
                        test.Questions = new List<InMemoryQuestion>();
                        for (int i = 0; i < selectedQuestions.Count; i++)
                        {
                            var q = selectedQuestions[i];
                            var questionId = 10000 + i; // Use the same ID generation logic as in Take method

                            var question = new InMemoryQuestion {
                                Id = questionId,
                                Text = q.Text,
                                Title = q.Title ?? q.Text.Substring(0, Math.Min(q.Text.Length, 100)),
                                Type = q.Type,
                                TestId = test.Id,
                                AnswerOptions = new List<InMemoryAnswerOption>()
                            };

                            // Add answer options
                            if (q.AnswerOptions != null)
                            {
                                for (int j = 0; j < q.AnswerOptions.Count; j++)
                                {
                                    var o = q.AnswerOptions[j];
                                    var optionId = 100000 + (i * 100) + j; // Use the same ID generation logic as in Take method

                                    question.AnswerOptions.Add(new InMemoryAnswerOption {
                                        Id = optionId,
                                        Text = o.Text,
                                        IsCorrect = o.IsCorrect
                                    });
                                }
                            }

                            test.Questions.Add(question);
                        }
                    }
                    else
                    {
                        _logger.LogError("CategoryQuestions not found with ID {CategoryQuestionsId} for test {TestId}", test.CategoryQuestionsId, id);
                        test.Questions = new List<InMemoryQuestion>();
                    }
                }
                else if (test != null)
                {
                    // If CategoryQuestionsId is not set, the test doesn't have any questions
                    // Initialize an empty collection
                    test.Questions = new List<InMemoryQuestion>();
                    _logger.LogWarning("Test {TestId} does not have CategoryQuestionsId set. No questions will be evaluated.", id);
                }

                if (test == null)
                {
                    _logger.LogWarning("Test not found with ID: {TestId} after all lookup attempts", id);
                    return NotFound(new {
                        success = false,
                        message = "Test not found with ID: " + id + ". Please contact support if this issue persists.",
                        errorCode = "TEST_NOT_FOUND"
                    });
                }

                // Check if user has already submitted this test
                var username = User.Identity?.Name ?? "Anonymous";

                // Check for recent submissions (within the last 60 seconds) to prevent duplicates from auto-submissions
                var recentSubmissionTimeThreshold = Utilities.TimeZoneHelper.GetCurrentIstTime().AddSeconds(-60);
                var recentSubmission = await _context.TestResults
                    .Where(r => r.TestId == id &&
                           r.Username == username &&
                           r.SubmittedAt >= recentSubmissionTimeThreshold)
                    .OrderByDescending(r => r.SubmittedAt)
                    .FirstOrDefaultAsync();

                // If there's a recent submission, check if it's a complete submission or just a placeholder
                // This prevents duplicate submissions but allows updating incomplete results
                if (recentSubmission != null)
                {
                    _logger.LogInformation("Found recent submission for test {TestId} by user {Username}. ResultId: {ResultId}, TotalQuestions: {TotalQuestions}, CorrectAnswers: {CorrectAnswers}, Score: {Score}",
                        id, username, recentSubmission.Id, recentSubmission.TotalQuestions, recentSubmission.CorrectAnswers, recentSubmission.Score);

                    // If the recent submission has TotalQuestions = 0, it's just a placeholder created when the test was started
                    // We should update it instead of treating it as a duplicate
                    if (recentSubmission.TotalQuestions == 0)
                    {
                        _logger.LogInformation("Found incomplete test result (TotalQuestions=0) for test {TestId} by user {Username}. Will update it instead of creating new one.",
                            id, username);
                        // Continue with the normal submission process to update this result
                    }
                    else
                    {
                        _logger.LogWarning("Detected duplicate submission for test {TestId} by user {Username}. Using existing result {ResultId} submitted at {SubmittedAt}",
                            id, username, recentSubmission.Id, recentSubmission.SubmittedAt);

                        // Get the attempt number for this submission
                        int submissionAttemptNumber = recentSubmission.AttemptNumber;

                        // Log the attempt number for debugging
                        _logger.LogInformation("Duplicate submission detected. Using attempt number: {AttemptNumber} for test {TestId} by user {Username}",
                            submissionAttemptNumber, id, username);

                        return Ok(new {
                            success = true,
                            redirectUrl = $"/Test/Result/{recentSubmission.Id}?forceLatest=true",
                            isDuplicate = true,
                            message = "Your test was already submitted.",
                            attemptNumber = submissionAttemptNumber,
                            score = recentSubmission.Score
                        });
                    }
                }

                // Find any existing submission for this test by this user (regardless of time)
                // Prioritize incomplete results (TotalQuestions = 0) over complete ones
                var existingResult = await _context.TestResults
                    .Where(r => r.TestId == id && r.Username == username)
                    .OrderBy(r => r.TotalQuestions == 0 ? 0 : 1) // Incomplete results first
                    .ThenByDescending(r => r.SubmittedAt) // Then by most recent
                    .FirstOrDefaultAsync();

                // Count all existing submissions
                var existingSubmissions = await _context.TestResults
                    .Where(r => r.TestId == id && r.Username == username)
                    .CountAsync();

                // Get all complete test results for this test and user (TotalQuestions > 0)
                var allCompleteResults = await _context.TestResults
                    .Where(r => r.TestId == id && r.Username == username && r.TotalQuestions > 0)
                    .ToListAsync();

                // Find the highest attempt number from complete results only
                int highestAttemptNumber = 0;
                if (allCompleteResults.Any())
                {
                    highestAttemptNumber = allCompleteResults.Max(r => r.AttemptNumber);
                }

                // Always increment by 1 from the highest attempt number
                int attemptNumber = highestAttemptNumber + 1;

                _logger.LogInformation("Calculated attempt number for user {Username} on test {TestId}: {AttemptNumber} (based on {CompleteResultsCount} complete results, highest attempt {HighestAttempt})",
                    username, id, attemptNumber, allCompleteResults.Count, highestAttemptNumber);

                // Log all existing test results for debugging
                var allTestResults = await _context.TestResults
                    .Where(r => r.TestId == id && r.Username == username)
                    .OrderByDescending(r => r.SubmittedAt)
                    .ToListAsync();

                foreach (var tr in allTestResults)
                {
                    _logger.LogInformation("Existing test result: ID={ResultId}, AttemptNumber={AttemptNumber}, SubmittedAt={SubmittedAt}",
                        tr.Id, tr.AttemptNumber, tr.SubmittedAt);
                }

                // Log the number of existing submissions but don't limit attempts
                _logger.LogInformation("User {Username} is submitting test {TestId}. Previous attempts: {AttemptCount}, Current attempt: {CurrentAttempt}",
                    username, id, existingSubmissions, attemptNumber);

                // Add additional logging to track attempt number
                _logger.LogInformation("Setting AttemptNumber={AttemptNumber} for test {TestId} by user {Username}",
                    attemptNumber, id, username);

                int mcqCorrect = 0;
                int totalMcq = test.Questions.Count;
                var evaluationDetails = new List<string>();

                // Log the question count for debugging
                _logger.LogInformation("Test {TestId} has {QuestionCount} questions loaded for evaluation", id, totalMcq);

                // If no questions are loaded, this is a problem
                if (totalMcq == 0)
                {
                    _logger.LogError("No questions found for test {TestId}. CategoryQuestionsId: {CategoryQuestionsId}, HasUploadedFile: {HasUploadedFile}",
                        id, test.CategoryQuestionsId, test.HasUploadedFile);
                }

                // Evaluate MCQ questions
                foreach (var question in test.Questions.Where(q => q.Type == QuestionType.MultipleChoice))
                {
                    var questionNumber = test.Questions.ToList().IndexOf(question) + 1;
                    var selectedOptionId = answers.GetValueOrDefault($"question_{question.Id}");

                    if (selectedOptionId != null)
                    {
                        var selectedOption = question.AnswerOptions.FirstOrDefault(a => a.Id.ToString() == selectedOptionId);
                        var isCorrect = selectedOption != null && selectedOption.IsCorrect;
                        if (isCorrect)
                        {
                            mcqCorrect++;
                        }
                        evaluationDetails.Add($"MCQ {questionNumber}: Selected: {selectedOption?.Text ?? "None"} - Correct: {isCorrect}");
                    }
                    else
                    {
                        evaluationDetails.Add($"MCQ {questionNumber}: No answer selected");
                    }
                }

                // Calculate total score - now the total score is equal to the number of correct answers
                double totalScore = mcqCorrect;

                // Get the user's SAP ID
                string? sapId = null;
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
                var currentUsername = User.Identity?.Name;

                _logger.LogInformation("Getting SAP ID for user submission: UserId={UserId}, UserRole={UserRole}, Username={Username}", userId, userRole, currentUsername);

                if (!string.IsNullOrEmpty(userId))
                {
                    if (userRole == "SpecialUser")
                    {
                        var specialUser = await _context.SpecialUsers.FindAsync(userId);
                        if (specialUser != null && !string.IsNullOrEmpty(specialUser.UsersSapId))
                        {
                            sapId = specialUser.UsersSapId;
                            _logger.LogInformation("Found SAP ID for SpecialUser: {SapId}", sapId);
                        }
                    }
                    else // Candidate or other roles
                    {
                        var user = await _context.Users.FindAsync(userId);
                        if (user != null && !string.IsNullOrEmpty(user.SapId))
                        {
                            sapId = user.SapId;
                            _logger.LogInformation("Found SAP ID for User by UserId: {SapId}", sapId);
                        }
                        else
                        {
                            // If user not found by SapId, try to find by Username
                            if (!string.IsNullOrEmpty(currentUsername))
                            {
                                user = await _context.Users.FirstOrDefaultAsync(u => u.Username == currentUsername);
                                if (user != null && !string.IsNullOrEmpty(user.SapId))
                                {
                                    sapId = user.SapId;
                                    _logger.LogInformation("Found SAP ID for User by Username: {SapId}", sapId);
                                }
                                else
                                {
                                    _logger.LogWarning("User not found by UserId or Username. UserId={UserId}, Username={Username}", userId, currentUsername);
                                }
                            }
                        }
                    }
                }

                // CRITICAL FIX: If sapId is still null, use the userId from claims as fallback
                if (string.IsNullOrEmpty(sapId) && !string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("SAP ID is null, using UserId from claims as fallback: {UserId}", userId);
                    sapId = userId;
                }

                // Get the current time for the end time
                var currentTime = Utilities.TimeZoneHelper.GetCurrentIstTime();

                TestResult result;

                // If an existing result exists, update it instead of creating a new one
                if (existingResult != null)
                {
                    _logger.LogInformation("Updating existing test result for test {TestId} by user {Username}. Result ID: {ResultId}",
                        id, username, existingResult.Id);

                    // Update the existing result
                    existingResult.TotalQuestions = totalMcq;
                    existingResult.CorrectAnswers = mcqCorrect;
                    existingResult.Score = totalScore;
                    existingResult.McqScore = totalScore;
                    existingResult.CodingScore = 0;
                    existingResult.SubmittedAt = currentTime;
                    existingResult.EndTime = currentTime; // Record the end time when the test is completed

                    // Fix StartTime if it's invalid (after EndTime) or not set
                    if (!existingResult.StartTime.HasValue || existingResult.StartTime.Value > currentTime)
                    {
                        // For existing results without proper StartTime, estimate based on a reasonable duration
                        // Use a short duration for realistic timing (most tests are completed quickly)
                        var estimatedDuration = Math.Min(test.DurationMinutes, 10); // Cap at 10 minutes for realistic timing
                        var reasonableStartTime = currentTime.AddMinutes(-estimatedDuration);
                        existingResult.StartTime = reasonableStartTime;
                        _logger.LogInformation("Fixed invalid StartTime for test result {ResultId}. Estimated duration: {Duration} minutes, Set StartTime to {StartTime}",
                            existingResult.Id, estimatedDuration, existingResult.StartTime);
                    }

                    existingResult.UserSapId = sapId;
                    existingResult.AttemptNumber = attemptNumber; // Set the attempt number

                    result = existingResult;
                }
                else
                {
                    // Create a new result
                    _logger.LogInformation("Creating new test result for test {TestId} by user {Username}", id, username);

                    result = new TestResult
                    {
                        TestId = id,
                        Username = username,
                        TotalQuestions = totalMcq,
                        CorrectAnswers = mcqCorrect,
                        Score = totalScore,
                        McqScore = totalScore, // Set McqScore to the same value as Score since we only have MCQ questions now
                        CodingScore = 0, // Set CodingScore to 0 since we don't have coding questions anymore
                        SubmittedAt = currentTime,
                        StartTime = currentTime.AddMinutes(-Math.Min(test.DurationMinutes, 10)), // Use reasonable start time, cap at 10 minutes
                        EndTime = currentTime, // Record the end time when the test is completed
                        UserSapId = sapId, // Add the SAP ID from the user
                        AttemptNumber = attemptNumber // Set the attempt number
                    };

                    _context.TestResults.Add(result);
                }

                // No need to increment the user count here as it's already done when the booking is created

                // Log information about the test and organization
                if (!string.IsNullOrEmpty(test.CreatedBySapId))
                {
                    var organizationId = test.CreatedBySapId;
                    _logger.LogInformation("Test created by organization ID: {OrganizationId}, test ID: {TestId}, username: {Username}", organizationId, id, username);

                    // We'll update the organization test results in the OrganizationResults action instead
                    // This avoids the foreign key constraint error
                    _logger.LogInformation("Skipping organization test result update to avoid foreign key constraint error");
                }

                // CRITICAL FIX: Update the booking status to Completed
                try
                {
                    // Find the booking for this test
                    var booking = await _context.TestBookings
                        .Where(b => b.TestId == id && b.UserSapId == sapId &&
                               (b.Status == "Confirmed" || b.Status == "Completed"))
                        .OrderByDescending(b => b.BookedAt)
                        .FirstOrDefaultAsync();

                    if (booking != null)
                    {
                        _logger.LogInformation($"Updating booking {booking.Id} status to Completed after test submission");
                        booking.Status = "Completed";
                        booking.UpdatedAt = Utilities.TimeZoneHelper.GetCurrentIstTime();
                        booking.StatusReason = "Test completed by user";
                    }
                    else
                    {
                        _logger.LogWarning($"No booking found for test {id} and user {sapId} to mark as completed");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating booking status to Completed for test {TestId}", id);
                    // Continue with test submission even if booking update fails
                }

                await _context.SaveChangesAsync();

                // Verify the attempt number was saved correctly
                var savedResult = await _context.TestResults.FindAsync(result.Id);

                // Log the final attempt number after saving
                _logger.LogInformation("Final attempt number saved for test {TestId} by user {Username}: {AttemptNumber}, Verified: {VerifiedAttemptNumber}",
                    id, username, result.AttemptNumber, savedResult?.AttemptNumber ?? 0);

                // Send test result email notification
                try
                {
                    // Get user information for the email
                    if (!string.IsNullOrEmpty(userId))
                    {
                        // CRITICAL FIX: Use userId directly as SapId since it's now a string
                        var user = await _context.Users.FindAsync(userId);
                        if (user == null)
                        {
                            // If user not found by SapId, try to find by Username
                            // CRITICAL FIX: Use userIdentityName instead of username to avoid naming conflict
                            var userIdentityName = User.Identity?.Name;
                            if (!string.IsNullOrEmpty(userIdentityName))
                            {
                                user = await _context.Users.FirstOrDefaultAsync(u => u.Username == userIdentityName);
                            }
                        }

                        if (user != null)
                        {
                            // Calculate percentage score
                            double scorePercentage = totalMcq > 0 ? (mcqCorrect * 100.0 / totalMcq) : 0;

                            // Get the passing score from the test model
                            int passingScore = test.PassingScore; // Use the PassingScore property from the Test model

                            bool isPassed = scorePercentage >= passingScore;

                            // Send the email notification
                            await _emailService.SendTestResultEmailAsync(
                                user.Email,
                                $"{user.FirstName} {user.LastName}" ?? username,
                                test.Title,
                                mcqCorrect,
                                totalMcq,
                                scorePercentage,
                                isPassed
                            );

                            _logger.LogInformation("Test result email sent to {Email} for test {TestId}. Score: {Score}%, Passed: {Passed}",
                                user.Email, id, scorePercentage, isPassed);
                        }
                        else
                        {
                            _logger.LogWarning("User not found with ID {UserId} when sending test result email", userId);
                        }
                    }
                }
                catch (Exception emailEx)
                {
                    // Log the error but don't fail the test submission
                    _logger.LogError(emailEx, "Error sending test result email for test {TestId}", id);
                }

                return Ok(new {
                    success = true,
                    redirectUrl = $"/Test/Result/{result.Id}?forceLatest=true",
                    evaluationDetails = evaluationDetails,
                    score = totalScore,
                    mcqCorrect = mcqCorrect,
                    totalMcq = totalMcq,
                    attemptNumber = attemptNumber // Include the attempt number in the response
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting test {TestId}", id);
                return StatusCode(500, new { success = false, message = "Error submitting test: " + ex.Message });
            }
        }

        [HttpGet]
        [Route("Test/Result/{id}")]
        [Authorize]
        public async Task<IActionResult> Result(int id, string? message = null, bool forceLatest = false)
        {
            try
            {
                _logger.LogInformation("Attempting to retrieve test result with ID: {ResultId}", id);

                var result = await _context.TestResults
                    .Include(r => r.Test)
                    .FirstOrDefaultAsync(r => r.Id == id);

                // If forceLatest is true or the result is not found, try to find the latest attempt
                if (forceLatest || result == null)
                {
                    // If we have a result, we can use its TestId to find the latest attempt
                    // Otherwise, we'll need to extract the TestId from the URL parameters
                    int testId = result?.TestId ?? 0;

                    // If we couldn't get a TestId from the result, check if the id parameter might be a TestId
                    if (testId == 0)
                    {
                        // Check if there's a test with this ID
                        var test = await _context.Tests.FindAsync(id);
                        if (test != null)
                        {
                            testId = test.Id;
                            _logger.LogInformation("No result found with ID {ResultId}, but found test with same ID. Using TestId: {TestId}", id, testId);
                        }
                    }

                    if (testId > 0)
                    {
                        var currentUsername = User.Identity?.Name;

                        // Find the latest attempt for this test by this user
                        var latestResult = await _context.TestResults
                            .Include(r => r.Test)
                            .Where(r => r.TestId == testId && r.Username == currentUsername)
                            .OrderByDescending(r => r.AttemptNumber) // Order by attempt number first
                            .ThenByDescending(r => r.SubmittedAt)    // Then by submission time
                            .FirstOrDefaultAsync();

                        if (latestResult != null && latestResult.Id != id)
                        {
                            _logger.LogInformation("Redirecting from result ID {ResultId} to latest result ID {LatestResultId} for test {TestId}",
                                id, latestResult.Id, testId);

                            // Redirect to the latest result
                            return RedirectToAction("Result", new { id = latestResult.Id, message });
                        }
                    }
                }

                if (result == null)
                {
                    _logger.LogWarning("Test result with ID {ResultId} not found", id);

                    // Check if the user has any test results using SAP ID for proper data isolation
                    var currentUserSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    var hasAnyResults = await _context.TestResults
                        .AnyAsync(r => r.UserSapId == currentUserSapId);

                    if (hasAnyResults)
                    {
                        _logger.LogInformation("User {UserSapId} has other test results, redirecting to History", currentUserSapId);
                        return RedirectToAction("History", new { error = $"Test result with ID {id} was not found. Here are your other test results." });
                    }

                    _logger.LogInformation("User {UserSapId} has no test results, redirecting to Index", currentUserSapId);
                    return RedirectToAction("Index", new { error = $"Test result with ID {id} was not found." });
                }

            // Set a flag to indicate that the test has been completed
            // This will be used to prevent redirection loops
            TempData["TestCompleted"] = true;

            // Preserve the TestRecreated flag if it exists
            if (TempData.ContainsKey("TestRecreated"))
            {
                // Keep the flag for the view to use, but make sure it persists
                TempData.Keep("TestRecreated");
            }

            // If a message was passed, display it to the user
            if (!string.IsNullOrEmpty(message))
            {
                ViewBag.Message = message;
            }

            // Check if this was an auto-submitted test
            if (TempData.ContainsKey("AutoSubmitted"))
            {
                ViewBag.AutoSubmitted = true;
                // Remove the flag so it doesn't persist to other views
                TempData.Remove("AutoSubmitted");
            }

            // Get the count of attempts for this test by this user
            var username = User.Identity?.Name;

            // Use the same method to count attempts as in OrganizationResults
            // Only count complete attempts (TotalQuestions > 0)
            var allUserAttempts = await _context.TestResults
                .Where(tr => tr.TestId == result.TestId && tr.Username == username && tr.TotalQuestions > 0)
                .OrderByDescending(tr => tr.SubmittedAt)
                .ToListAsync();

            var attemptCount = allUserAttempts.Count;

            // Log detailed information about all attempts
            _logger.LogInformation("Result page: Found {AttemptCount} attempts for test {TestId} by user {Username}. Current result attempt number: {CurrentAttemptNumber}",
                attemptCount, result.TestId, username, result.AttemptNumber);

            foreach (var attempt in allUserAttempts)
            {
                _logger.LogInformation("Found attempt: ID={ResultId}, AttemptNumber={AttemptNumber}, TotalQuestions={TotalQuestions}, CorrectAnswers={CorrectAnswers}, Score={Score}, SubmittedAt={SubmittedAt}",
                    attempt.Id, attempt.AttemptNumber, attempt.TotalQuestions, attempt.CorrectAnswers, attempt.Score, attempt.SubmittedAt);
            }

            ViewBag.AttemptCount = attemptCount;

            // Store all attempts in ViewBag for consistency with OrganizationResults
            ViewBag.AllAttempts = allUserAttempts;

            // Log the result information
            _logger.LogInformation("Showing test result for test ID: {TestId}, result ID: {ResultId}, attempt {AttemptCount} of {MaxAttempts}",
                result.TestId, id, attemptCount, result.Test.MaxAttempts);

            // Make sure Score and CorrectAnswers are consistent
            if (result.Score != result.CorrectAnswers)
            {
                _logger.LogWarning("Score and CorrectAnswers are inconsistent for result ID {ResultId}. Score: {Score}, CorrectAnswers: {CorrectAnswers}. Using CorrectAnswers as the source of truth.",
                    id, result.Score, result.CorrectAnswers);

                // Update the Score to match CorrectAnswers for consistency
                result.Score = result.CorrectAnswers;
            }

            // Check if this is a special user and add enhanced features
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            if (userRole == "SpecialUser")
            {
                ViewBag.IsSpecialUser = true;
                ViewBag.ScoreRating = result.GetScoreRating();
                ViewBag.ScorePercentage = result.GetScorePercentage();

                // Check if certificate has been purchased using the certificate service
                try
                {
                    var hasPurchased = await _certificateService.HasPurchasedCertificateAsync(id);
                    ViewBag.HasPurchasedCertificate = hasPurchased;

                    if (hasPurchased)
                    {
                        var purchase = await _certificateService.GetCertificatePurchaseAsync(id);
                        ViewBag.CertificateUrl = purchase?.CertificateUrl;
                    }
                    else
                    {
                        ViewBag.CertificateUrl = null;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error checking certificate status for test result {ResultId}", id);
                    ViewBag.HasPurchasedCertificate = false;
                    ViewBag.CertificateUrl = null;
                }

                _logger.LogInformation("Special user viewing result {ResultId} with rating {Rating} and percentage {Percentage}%",
                    id, result.GetScoreRating(), result.GetScorePercentage());
            }
            else
            {
                ViewBag.IsSpecialUser = false;
            }

            // CRITICAL FIX: Check and fix invalid StartTime before displaying
            if (result.StartTime.HasValue && result.EndTime.HasValue && result.StartTime.Value > result.EndTime.Value)
            {
                _logger.LogWarning("Found invalid StartTime for test result {ResultId}. StartTime: {StartTime}, EndTime: {EndTime}. Fixing...",
                    id, result.StartTime, result.EndTime);

                // Calculate reasonable StartTime based on test duration (cap at 10 minutes)
                var testDuration = result.Test?.DurationMinutes ?? 10; // Default to 10 minutes if not available
                var reasonableDuration = Math.Min(testDuration, 10); // Cap at 10 minutes maximum for realistic timing
                result.StartTime = result.EndTime.Value.AddMinutes(-reasonableDuration);

                // Save the fix to database
                try
                {
                    _context.TestResults.Update(result);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Fixed invalid StartTime for test result {ResultId}. New StartTime: {StartTime}",
                        id, result.StartTime);
                }
                catch (Exception fixEx)
                {
                    _logger.LogError(fixEx, "Error fixing StartTime for test result {ResultId}", id);
                    // Continue displaying the result even if we can't fix it
                }
            }

            return View(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error displaying test result with ID {ResultId}", id);
            return RedirectToAction("History", new { error = $"An error occurred while displaying the test result: {ex.Message}" });
        }
        }

        // View action for test history
        [HttpGet]
        [Route("Test/History")]
        [Authorize(Roles = "Candidate,SpecialUser")]
        public async Task<IActionResult> History(string error = null, string message = null)
        {
            try
            {
                _logger.LogInformation("Loading test history for user");

                // Get the current user's SAP ID and username
                var currentUserSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var currentUsername = User.FindFirst(ClaimTypes.Name)?.Value;

                if (string.IsNullOrEmpty(currentUserSapId))
                {
                    _logger.LogWarning("User SAP ID not found in claims");
                    return RedirectToAction("Login", "Auth");
                }

                _logger.LogInformation("Current user SAP ID from claims: {UserSapId}, Username: {Username}", currentUserSapId, currentUsername);

                // First, try to get test results by UserSapId
                var testResults = await _context.TestResults
                    .Include(r => r.Test)
                    .Where(r => r.UserSapId == currentUserSapId && r.Test != null && !r.Test.IsDeleted)
                    .OrderByDescending(r => r.SubmittedAt)
                    .ToListAsync();

                _logger.LogInformation("Found {Count} test results by UserSapId for user {UserSapId}", testResults.Count, currentUserSapId);

                // If no results found by UserSapId, try searching by Username as fallback
                if (!testResults.Any() && !string.IsNullOrEmpty(currentUsername))
                {
                    _logger.LogInformation("No results found by UserSapId, trying to search by Username: {Username}", currentUsername);

                    testResults = await _context.TestResults
                        .Include(r => r.Test)
                        .Where(r => r.Username == currentUsername && r.Test != null && !r.Test.IsDeleted)
                        .OrderByDescending(r => r.SubmittedAt)
                        .ToListAsync();

                    _logger.LogInformation("Found {Count} test results by Username for user {Username}", testResults.Count, currentUsername);

                    // If we found results by username but not by UserSapId, we need to update the UserSapId in those records
                    if (testResults.Any())
                    {
                        _logger.LogWarning("Found test results by Username but not by UserSapId. This indicates a data inconsistency that should be fixed.");

                        // Update the UserSapId in the test results to match the current user's SAP ID
                        foreach (var result in testResults)
                        {
                            if (result.UserSapId != currentUserSapId)
                            {
                                _logger.LogInformation("Updating test result {ResultId} UserSapId from '{OldSapId}' to '{NewSapId}'",
                                    result.Id, result.UserSapId, currentUserSapId);
                                result.UserSapId = currentUserSapId;
                            }
                        }

                        try
                        {
                            await _context.SaveChangesAsync();
                            _logger.LogInformation("Successfully updated UserSapId for {Count} test results", testResults.Count);
                        }
                        catch (Exception updateEx)
                        {
                            _logger.LogError(updateEx, "Failed to update UserSapId in test results");
                            // Continue anyway, the user can still see their results
                        }
                    }
                }

                // Debug: Log all test results in the database for this username to help diagnose the issue
                var allResultsForUsername = await _context.TestResults
                    .Where(r => r.Username == currentUsername)
                    .Select(r => new { r.Id, r.Username, r.UserSapId, r.TestId, r.SubmittedAt })
                    .ToListAsync();

                _logger.LogInformation("Debug: All test results for username '{Username}': {Results}",
                    currentUsername, string.Join(", ", allResultsForUsername.Select(r => $"ID:{r.Id},UserSapId:{r.UserSapId},TestId:{r.TestId}")));

                // Pass any error or success messages
                if (!string.IsNullOrEmpty(error))
                {
                    TempData["ErrorMessage"] = error;
                }
                if (!string.IsNullOrEmpty(message))
                {
                    TempData["SuccessMessage"] = message;
                }

                // Pass the user's SAP ID to the view for display
                ViewBag.UserSapId = currentUserSapId;

                return View(testResults);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading test history");
                TempData["ErrorMessage"] = "An error occurred while loading your test history.";
                return RedirectToAction("Index");
            }
        }

        // Debug action to check user data consistency
        [HttpGet]
        [Route("Test/DebugUserData")]
        [Authorize]
        public async Task<IActionResult> DebugUserData()
        {
            try
            {
                var currentUserSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var currentUsername = User.FindFirst(ClaimTypes.Name)?.Value;
                var currentUserRole = User.FindFirst(ClaimTypes.Role)?.Value;

                _logger.LogInformation("Debug: Current user claims - SapId: {SapId}, Username: {Username}, Role: {Role}",
                    currentUserSapId, currentUsername, currentUserRole);

                // Check user record
                var userRecord = await _context.Users.FindAsync(currentUserSapId);
                var userByUsername = await _context.Users.FirstOrDefaultAsync(u => u.Username == currentUsername);

                // Check test results
                var resultsBySapId = await _context.TestResults
                    .Where(r => r.UserSapId == currentUserSapId)
                    .Select(r => new { r.Id, r.Username, r.UserSapId, r.TestId, r.SubmittedAt })
                    .ToListAsync();

                var resultsByUsername = await _context.TestResults
                    .Where(r => r.Username == currentUsername)
                    .Select(r => new { r.Id, r.Username, r.UserSapId, r.TestId, r.SubmittedAt })
                    .ToListAsync();

                return Json(new
                {
                    claims = new
                    {
                        sapId = currentUserSapId,
                        username = currentUsername,
                        role = currentUserRole
                    },
                    userRecord = userRecord != null ? new
                    {
                        sapId = userRecord.SapId,
                        username = userRecord.Username,
                        email = userRecord.Email
                    } : null,
                    userByUsername = userByUsername != null ? new
                    {
                        sapId = userByUsername.SapId,
                        username = userByUsername.Username,
                        email = userByUsername.Email
                    } : null,
                    testResultsBySapId = resultsBySapId,
                    testResultsByUsername = resultsByUsername,
                    dataConsistency = new
                    {
                        userRecordExists = userRecord != null,
                        userByUsernameExists = userByUsername != null,
                        sapIdMatches = userRecord?.SapId == currentUserSapId,
                        usernameMatches = userRecord?.Username == currentUsername,
                        resultsBySapIdCount = resultsBySapId.Count,
                        resultsByUsernameCount = resultsByUsername.Count
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in DebugUserData");
                return Json(new { error = ex.Message });
            }
        }

        // One-time fix for invalid StartTime values
        [HttpGet]
        [Route("Test/FixInvalidStartTimes")]
        [Authorize(Roles = "Admin,Organization,SpecialUser")]
        public async Task<IActionResult> FixInvalidStartTimes()
        {
            try
            {
                // Find all test results with unrealistic time spans (more than 1 hour duration)
                var invalidResults = await _context.TestResults
                    .Include(r => r.Test)
                    .Where(r => r.StartTime.HasValue && r.EndTime.HasValue)
                    .ToListAsync();

                // Filter for results with unrealistic durations (more than 10 minutes)
                var resultsToFix = invalidResults
                    .Where(r => (r.EndTime.Value - r.StartTime.Value).TotalMinutes > 10)
                    .ToList();

                _logger.LogInformation("Found {Count} test results with unrealistic time spans (> 10 minutes)", resultsToFix.Count);

                int fixedCount = 0;
                foreach (var result in resultsToFix)
                {
                    var testDuration = result.Test?.DurationMinutes ?? 10;
                    var reasonableDuration = Math.Min(testDuration, 10); // Cap at 10 minutes
                    var oldStartTime = result.StartTime;
                    var oldDuration = result.EndTime.Value - result.StartTime.Value;

                    result.StartTime = result.EndTime.Value.AddMinutes(-reasonableDuration);

                    _logger.LogInformation("Fixing test result {ResultId}: Old duration: {OldDuration} minutes, New duration: {NewDuration} minutes, Old StartTime: {OldStartTime}, New StartTime: {NewStartTime}, EndTime: {EndTime}",
                        result.Id, oldDuration.TotalMinutes, reasonableDuration, oldStartTime, result.StartTime, result.EndTime);

                    fixedCount++;
                }

                if (fixedCount > 0)
                {
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Successfully fixed {FixedCount} test results with unrealistic time spans", fixedCount);
                }

                return Json(new {
                    success = true,
                    message = $"Fixed {fixedCount} test results with invalid StartTime values",
                    fixedCount = fixedCount,
                    totalChecked = invalidResults.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fixing invalid StartTime values");
                return Json(new { success = false, message = "Error fixing invalid StartTime values: " + ex.Message });
            }
        }

        // We only support MCQ questions

        [HttpPost]
        [Authorize(Roles = "Organization")]
        public async Task<IActionResult> Create([FromBody] Test test)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    return Json(new { success = false, message = "Invalid test data" });
                }

                var userSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

                if (userRole == "Organization" && userSapId != null)
                {
                    test.CreatedBySapId = userSapId;
                }
                else if (!User.IsInRole("Admin"))
                {
                    return Json(new { success = false, message = "Unauthorized" });
                }

                // Ensure the test is immediately visible to users
                test.HasUploadedFile = true;

                // Set default passing score if not provided
                if (test.PassingScore <= 0 || test.PassingScore > 100)
                {
                    test.PassingScore = 60; // Default passing score is 60%
                }

                _context.Tests.Add(test);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Test created successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating test");
                return Json(new { success = false, message = "An error occurred while creating the test" });
            }
        }

        // Share functionality removed

        // Access functionality removed

        // StartShared functionality removed

        [HttpGet]
        [Route("Test/Details/{id}")]
        [Authorize(Roles = "Organization")]
        public async Task<IActionResult> Details(int id)
        {
            try
            {
                var test = await _context.Tests
                    .Include(t => t.Questions)
                        .ThenInclude(q => q.AnswerOptions)
                    .FirstOrDefaultAsync(t => t.Id == id);

                if (test == null)
                {
                    return NotFound("The test you're looking for doesn't exist.");
                }

                // Check if the test is deleted if the IsDeleted property exists
                try
                {
                    if (test.IsDeleted)
                    {
                        return NotFound("The test you're looking for has been deleted.");
                    }
                }
                catch
                {
                    // IsDeleted property might not exist yet, ignore the error
                }

                return View(test);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Test Details action");
                return RedirectToAction(nameof(Index));
            }
        }

        // Debug action to check authentication before accessing OrganizationResults
        [HttpGet]
        [Route("Test/DebugAuth")]
        public IActionResult DebugAuth()
        {
            var authInfo = new
            {
                IsAuthenticated = User.Identity?.IsAuthenticated ?? false,
                UserName = User.Identity?.Name ?? "Anonymous",
                Role = User.FindFirst(ClaimTypes.Role)?.Value ?? "None",
                UserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "None",
                Email = User.FindFirst(ClaimTypes.Email)?.Value ?? "None",
                Claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList(),
                RequestPath = Request.Path,
                RequestHeaders = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString())
            };

            return Json(authInfo);
        }

        [HttpGet]
        [Route("Test/OrganizationResults")]
        [Authorize(Roles = "Organization")]
        public async Task<IActionResult> OrganizationResults(int page = 1, int pageSize = 50)
        {
            try
            {
                var organizationSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(organizationSapId))
                {
                    return RedirectToAction("Login", "Auth");
                }

                _logger.LogInformation("Loading organization results for organization: {OrganizationSapId}", organizationSapId);

                // Get all tests created by this organization
                var organizationTests = await _context.Tests
                    .Where(t => t.CreatedBySapId == organizationSapId)
                    .Select(t => t.Id)
                    .ToListAsync();

                if (!organizationTests.Any())
                {
                    _logger.LogInformation("No tests found for organization: {OrganizationSapId}", organizationSapId);
                    ViewBag.SummaryResults = new List<dynamic>();
                    ViewBag.DetailedResults = new List<TestResult>();
                    return View();
                }

                // Get all test results for tests created by this organization
                var allTestResults = await _context.TestResults
                    .Include(tr => tr.Test)
                    .Where(tr => organizationTests.Contains(tr.TestId) && tr.TotalQuestions > 0)
                    .OrderByDescending(tr => tr.SubmittedAt)
                    .ToListAsync();

                _logger.LogInformation("Found {Count} test results for organization tests", allTestResults.Count);

                // Group by test and user to create summary results
                var summaryResults = allTestResults
                    .GroupBy(tr => new { tr.TestId, tr.Username })
                    .Select(g => new
                    {
                        TestId = g.Key.TestId,
                        TestTitle = g.First().Test?.Title ?? "Unknown Test",
                        Username = g.Key.Username,
                        UserSapId = g.First().UserSapId,
                        LastAttemptDate = g.Max(tr => tr.SubmittedAt),
                        BestScore = g.Max(tr => tr.Score),
                        TotalAttempts = g.Count(),
                        AverageScore = g.Average(tr => tr.Score),
                        Status = g.Max(tr => tr.Score) >= 60 ? "Passed" : "Failed",
                        Attempts = g.OrderByDescending(tr => tr.SubmittedAt).ToList(),
                        StartTime = g.OrderByDescending(tr => tr.SubmittedAt).First().StartTime,
                        EndTime = g.OrderByDescending(tr => tr.SubmittedAt).First().EndTime
                    })
                    .OrderByDescending(r => r.LastAttemptDate)
                    .ToList();

                // Apply pagination
                var totalCount = summaryResults.Count;
                var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
                var pagedResults = summaryResults
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                // Set pagination ViewBag properties
                ViewBag.CurrentPage = page;
                ViewBag.PageSize = pageSize;
                ViewBag.TotalPages = totalPages;
                ViewBag.TotalCount = totalCount;
                ViewBag.HasPreviousPage = page > 1;
                ViewBag.HasNextPage = page < totalPages;

                ViewBag.SummaryResults = pagedResults;
                ViewBag.DetailedResults = allTestResults;

                _logger.LogInformation("Returning {Count} summary results (page {Page} of {TotalPages})",
                    pagedResults.Count, page, totalPages);

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OrganizationResults action");
                return RedirectToAction("Index", new { error = "An error occurred while loading organization results" });
            }
        }

        [HttpGet]
        [Route("Test/RegenerateResults")]
        [Authorize(Roles = "Organization")]
        public async Task<IActionResult> RegenerateResults()
        {
            try
            {
                var organizationSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(organizationSapId))
                {
                    return RedirectToAction("Login", "Auth");
                }

                _logger.LogInformation("Regenerating results for organization: {OrganizationSapId}", organizationSapId);

                // Simply redirect back to OrganizationResults to refresh the data
                return RedirectToAction("OrganizationResults", new { message = "Results have been refreshed successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in RegenerateResults action");
                return RedirectToAction("OrganizationResults", new { error = "An error occurred while refreshing results." });
            }
        }

        [HttpGet]
        [Route("Test/ExportDailyResults")]
        [Authorize(Roles = "Organization")]
        public async Task<IActionResult> ExportDailyResults()
        {
            try
            {
                var organizationSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(organizationSapId))
                {
                    return RedirectToAction("Login", "Auth");
                }

                _logger.LogInformation("Exporting daily results for organization: {OrganizationSapId}", organizationSapId);

                var today = DateTime.Today;
                var tomorrow = today.AddDays(1);

                // Get all tests created by this organization
                var organizationTests = await _context.Tests
                    .Where(t => t.CreatedBySapId == organizationSapId)
                    .Select(t => t.Id)
                    .ToListAsync();

                // Get today's test results
                var todaysResults = await _context.TestResults
                    .Include(tr => tr.Test)
                    .Where(tr => organizationTests.Contains(tr.TestId) &&
                                tr.SubmittedAt >= today &&
                                tr.SubmittedAt < tomorrow &&
                                tr.TotalQuestions > 0)
                    .OrderByDescending(tr => tr.SubmittedAt)
                    .ToListAsync();

                if (!todaysResults.Any())
                {
                    return RedirectToAction("OrganizationResults", new { message = "No test results found for today." });
                }

                // For now, redirect back with a message. In the future, this could generate an actual Excel file
                return RedirectToAction("OrganizationResults", new { message = $"Found {todaysResults.Count} test results for today. Export functionality will be implemented soon." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ExportDailyResults action");
                return RedirectToAction("OrganizationResults", new { error = "An error occurred while exporting results." });
            }
        }

        [HttpGet]
        [Route("Test/Create")]
        [Authorize(Roles = "Organization")]
        public IActionResult Create()
        {
            try
            {
                // Always allow organizations to create tests
                ViewBag.CanCreateMcq = true;
                ViewBag.CanCreateCoding = true;


                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Create action");
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpGet]
        [Route("Test/ReAttempt/{id}")]
        [Authorize(Roles = "Candidate")]
        public async Task<IActionResult> ReAttempt(int id)
        {
            try
            {
                // Get the test
                var test = await _context.Tests.FindAsync(id);
                if (test == null)
                {
                    return RedirectToAction("Index", new { error = "Test not found" });
                }

                // Get the current user ID
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    return RedirectToAction("Login", "Auth");
                }

                // CRITICAL FIX: Use userId directly as SapId since it's now a string
                var candidateId = userId;
                var username = User.Identity?.Name;

                // Set the IsReattempt flag in session
                HttpContext.Session.SetString("IsReattempt", "true");
                HttpContext.Session.SetString("ReattemptTestId", id.ToString());

                // Redirect to the test page
                return RedirectToAction("Index", new { isReattempt = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ReAttempt action for test ID {TestId}", id);
                return RedirectToAction("Index", new { error = "An error occurred while processing your request." });
            }
        }

        // View action for My Bookings page
        [HttpGet]
        [Route("Test/MyBookings")]
        [Authorize(Roles = "Candidate,SpecialUser")]
        public async Task<IActionResult> MyBookings()
        {
            try
            {
                // Get the current user SAP ID
                var userSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userSapId))
                {
                    return RedirectToAction("Login", "Auth");
                }

                // Get all bookings for this user
                var bookings = await _context.TestBookings
                    .Include(tb => tb.Test) // Include Test details
                    .Where(tb => tb.UserSapId == userSapId)
                    .ToListAsync(); // Fetch data into memory

                // Order the bookings in memory
                bookings = bookings.OrderBy(tb => tb.BookingDate)
                                   .ThenBy(tb => tb.StartTime)
                                   .ToList();

                // Get all test results for this user to determine which tests have been completed
                var testResults = await _context.TestResults
                    .Where(tr => tr.UserSapId == userSapId)
                    .ToListAsync();

                // Categorize bookings into past tests (tests that have been taken)
                var pastBookings = new List<TestBooking>();
                var currentTime = Utilities.TimeZoneHelper.GetCurrentIstTime();

                foreach (var booking in bookings)
                {
                    // Check if this booking has a corresponding test result for THIS USER
                    var hasTestResult = testResults.Any(tr => tr.TestId == booking.TestId &&
                                                             tr.UserSapId == userSapId &&
                                                             tr.SubmittedAt >= booking.BookedAt);

                    // A booking is considered "past" ONLY if:
                    // 1. The user has actually taken this test (has a test result)
                    // We remove the expired logic since we only want tests the user has completed
                    if (hasTestResult)
                    {
                        // Ensure CanStartTest is set to false for past bookings
                        booking.CanStartTest = false;
                        pastBookings.Add(booking);
                    }
                }

                // Pass the data to the view
                ViewBag.AllBookings = bookings;
                ViewBag.TestResults = testResults;
                ViewBag.PastBookings = pastBookings;

                _logger.LogInformation("MyBookings: Found {BookingCount} bookings, {ResultCount} test results, and {PastCount} past bookings for user {UserSapId}",
                    bookings.Count, testResults.Count, pastBookings.Count, userSapId);

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in MyBookings action");
                return RedirectToAction("Index", new { error = "An error occurred while loading your bookings" });
            }
        }

        // Session validation endpoint for AJAX calls
        [HttpHead]
        [HttpGet]
        [Route("Test/SessionCheck")]
        public IActionResult SessionCheck()
        {
            var userSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userSapId))
            {
                return Unauthorized();
            }

            // Refresh session activity
            HttpContext.Session.SetString("LastActivity", DateTime.UtcNow.ToString());
            return Ok();
        }
    }
}
