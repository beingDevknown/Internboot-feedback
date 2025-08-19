using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OnlineAssessment.Web.Models;
using OnlineAssessment.Web.Services;
using OnlineAssessment.Web.Helpers;
using System.Security.Claims;
using System.Data.Common;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using Newtonsoft.Json;
using System.Text;
using System.Text.Json;

namespace OnlineAssessment.Web.Controllers
{
    [Authorize]
    public class PaymentController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILogger<PaymentController> _logger;
        private readonly RazorpayService _razorpayService;
        private readonly IEmailService _emailService;

        public PaymentController(AppDbContext context, ILogger<PaymentController> logger, RazorpayService razorpayService, IEmailService emailService)
        {
            _context = context;
            _logger = logger;
            _razorpayService = razorpayService;
            _emailService = emailService;
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Index(int? testId = null, string? date = null, string? startTime = null, string? endTime = null, int? slotNumber = null, string? userSapIdParam = null)
        {
            // Check if user is authenticated
            var userSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userSapId))
            {
                return RedirectToAction("Login", "Auth");
            }

            var user = await _context.Users.FindAsync(userSapId);
            if (user == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            // Store the booking details in Session for use after payment
            if (testId.HasValue)
            {
                HttpContext.Session.SetInt32("PendingTestId", testId.Value);
                _logger.LogInformation($"[SessionSet] PendingTestId: {testId.Value}");
            }
            if (!string.IsNullOrEmpty(date)) {
                HttpContext.Session.SetString("PendingDate", date);
                _logger.LogInformation($"[SessionSet] PendingDate: {date}");
            }
            if (!string.IsNullOrEmpty(startTime)) {
                HttpContext.Session.SetString("PendingStartTime", startTime);
                _logger.LogInformation($"[SessionSet] PendingStartTime: {startTime}");
            }
            if (!string.IsNullOrEmpty(endTime)) {
                HttpContext.Session.SetString("PendingEndTime", endTime);
                _logger.LogInformation($"[SessionSet] PendingEndTime: {endTime}");
            }

            // Store the user SAP ID in the session
            HttpContext.Session.SetString("PendingUserSapId", userSapId);
            _logger.LogInformation($"[SessionSet] PendingUserSapId: {userSapId}");
            if (slotNumber.HasValue) {
                HttpContext.Session.SetInt32("PendingSlotNumber", slotNumber.Value);
                _logger.LogInformation($"[SessionSet] PendingSlotNumber: {slotNumber.Value}");
            }
            if (!string.IsNullOrEmpty(userSapIdParam)) {
                HttpContext.Session.SetString("PendingUserSapId", userSapIdParam);
                _logger.LogInformation($"[SessionSet] PendingUserSapId: {userSapIdParam}");
            }
            // Clear any retake-related session variables
            HttpContext.Session.Remove("IsReattempt");
            _logger.LogInformation("[SessionSet] Cleared IsReattempt");
            // Log all session values after setting
            _logger.LogInformation($"[SessionSet] Summary: PendingTestId={HttpContext.Session.GetInt32("PendingTestId")}, PendingDate={HttpContext.Session.GetString("PendingDate")}, PendingStartTime={HttpContext.Session.GetString("PendingStartTime")}, PendingEndTime={HttpContext.Session.GetString("PendingEndTime")}, PendingSlotNumber={HttpContext.Session.GetInt32("PendingSlotNumber")}, PendingUserSapId={HttpContext.Session.GetString("PendingUserSapId")}");


            // No need to keep Session values as they persist until cleared or expired.

            // Get the specific test if provided, otherwise get the first test
            Test test;
            if (testId.HasValue)
            {
                test = await _context.Tests.FindAsync(testId.Value);
            }
            else
            {
                test = await _context.Tests.FirstOrDefaultAsync();
            }

            if (test == null)
            {
                // Create a default test if none exists
                test = new Test
                {
                    Title = "General Assessment Test",
                    Description = "This is a general assessment test for candidates.",
                    DurationMinutes = 60,
                    MaxAttempts = 1
                };
            }

            ViewBag.Test = test;
            ViewBag.Amount = test.Price; // Use the test's price instead of hardcoded value
            ViewBag.UserName = user.FirstName + " " + user.LastName;
            ViewBag.IsAdditionalSlot = true; // This is for booking an additional slot

            return View();
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> RazorpayInitiate(int? testId = null, string? date = null, string? startTime = null, string? endTime = null, int? slotNumber = null, string? userSapIdParam = null)
        {
            // Log all incoming parameters
            _logger.LogInformation($"[RazorpayInitiate] Incoming parameters: TestId: {testId}, Date: {date}, StartTime: {startTime}, EndTime: {endTime}, SlotNumber: {slotNumber}, UserSapIdParam: {userSapIdParam}");

            // First try to get values from URL parameters
            // If not available, fall back to session values
            string? pendingDate = date;
            string? pendingStartTime = startTime;
            string? pendingEndTime = endTime;
            string userSapId = string.Empty;

            try
            {
                // Try to get values from session if not provided in URL
                if (!testId.HasValue)
                {
                    testId = HttpContext.Session.GetInt32("PendingTestId");
                    _logger.LogInformation($"[RazorpayInitiate] Got testId from session: {testId}");
                }

                if (string.IsNullOrEmpty(pendingDate))
                {
                    pendingDate = HttpContext.Session.GetString("PendingDate");
                    _logger.LogInformation($"[RazorpayInitiate] Got date from session: {pendingDate}");
                }

                if (string.IsNullOrEmpty(pendingStartTime))
                {
                    pendingStartTime = HttpContext.Session.GetString("PendingStartTime");
                    _logger.LogInformation($"[RazorpayInitiate] Got startTime from session: {pendingStartTime}");
                }

                if (string.IsNullOrEmpty(pendingEndTime))
                {
                    pendingEndTime = HttpContext.Session.GetString("PendingEndTime");
                    _logger.LogInformation($"[RazorpayInitiate] Got endTime from session: {pendingEndTime}");
                }

                if (!slotNumber.HasValue)
                {
                    slotNumber = HttpContext.Session.GetInt32("PendingSlotNumber");
                    _logger.LogInformation($"[RazorpayInitiate] Got slotNumber from session: {slotNumber}");
                }

                if (string.IsNullOrEmpty(userSapIdParam))
                {
                    userSapId = HttpContext.Session.GetString("PendingUserSapId") ?? string.Empty;
                    _logger.LogInformation($"[RazorpayInitiate] Got userSapId from session: {userSapId}");
                }
                else
                {
                    userSapId = userSapIdParam;
                }

                // Remove any retake-related session variables
                try
                {
                    HttpContext.Session.Remove("IsReattempt");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[RazorpayInitiate] Error removing IsReattempt from session");
                }

                // Try to store all values in session for later use
                try
                {
                    if (testId.HasValue)
                    {
                        HttpContext.Session.SetInt32("PendingTestId", testId.Value);
                    }

                    if (!string.IsNullOrEmpty(pendingDate))
                    {
                        HttpContext.Session.SetString("PendingDate", pendingDate);
                    }

                    if (!string.IsNullOrEmpty(pendingStartTime))
                    {
                        HttpContext.Session.SetString("PendingStartTime", pendingStartTime);
                    }

                    if (!string.IsNullOrEmpty(pendingEndTime))
                    {
                        HttpContext.Session.SetString("PendingEndTime", pendingEndTime);
                    }

                    if (slotNumber.HasValue)
                    {
                        HttpContext.Session.SetInt32("PendingSlotNumber", slotNumber.Value);
                    }

                    if (!string.IsNullOrEmpty(userSapId))
                    {
                        HttpContext.Session.SetString("PendingUserSapId", userSapId);
                    }

                    // No longer storing IsReattempt in session
                }
                catch (Exception sessionEx)
                {
                    _logger.LogWarning(sessionEx, "[RazorpayInitiate] Error storing values in session. Continuing with URL parameters.");
                }
            }
            catch (Exception sessionEx)
            {
                _logger.LogWarning(sessionEx, "[RazorpayInitiate] Error reading from session. Continuing with URL parameters.");
            }

            // If userSapId is still empty, get it from the current user
            if (string.IsNullOrEmpty(userSapId))
            {
                userSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
                _logger.LogInformation($"[RazorpayInitiate] Got userSapId from claims: {userSapId}");
            }

            _logger.LogInformation($"[RazorpayInitiate] Final parameters: TestId: {testId}, Date: {pendingDate}, StartTime: {pendingStartTime}, EndTime: {pendingEndTime}, SlotNumber: {slotNumber}, UserSapId: {userSapId}");

            if (!testId.HasValue || string.IsNullOrEmpty(pendingDate))
            {
                _logger.LogWarning("[RazorpayInitiate] Missing required booking data (testId or date)");
                TempData["PaymentError"] = "Missing required booking data. Please try booking again.";
                return RedirectToAction("Index", "Test", new { error = TempData["PaymentError"] });
            }

            // Set default values for startTime and endTime if they're not provided
            // This is needed because time slots have been removed
            if (string.IsNullOrEmpty(pendingStartTime))
            {
                pendingStartTime = DateTime.Now.ToString("HH:mm:ss");
                _logger.LogInformation($"[RazorpayInitiate] Using default startTime: {pendingStartTime}");
            }

            if (string.IsNullOrEmpty(pendingEndTime))
            {
                // Set end time to start time + test duration (or default 60 minutes)
                var defaultEndTime = DateTime.Now.AddMinutes(60);
                pendingEndTime = defaultEndTime.ToString("HH:mm:ss");
                _logger.LogInformation($"[RazorpayInitiate] Using default endTime: {pendingEndTime}");
            }

            // Set default slot number if not provided
            if (!slotNumber.HasValue)
            {
                slotNumber = 0; // Default slot number is 0 (no slot)
                _logger.LogInformation($"[RazorpayInitiate] Using default slotNumber: {slotNumber}");
            }

            // Get the test to use its price
            var test = await _context.Tests.FindAsync(testId.Value);
            if (test == null)
            {
                _logger.LogWarning($"[PaymentError] Test with ID {testId.Value} not found");
                TempData["PaymentError"] = "Test not found. Please try booking again.";
                return RedirectToAction("Index", "Test", new { error = TempData["PaymentError"] });
            }

            // Use the test's price for payment
            string amount = test.Price.ToString("F2"); // Format with 2 decimal places
            string productinfo = $"TestBooking_{testId}";
            string firstname = User.Identity?.Name ?? "User";
            string email = User.FindFirst(ClaimTypes.Email)?.Value ?? "test@example.com";

            // Fetch the user's actual mobile number from the database
            string phone = "9999999999"; // Default fallback
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                var user = await _context.Users.FindAsync(userId);
                if (user != null && !string.IsNullOrEmpty(user.MobileNumber))
                {
                    phone = user.MobileNumber;
                    _logger.LogInformation($"Using user's mobile number from database: {phone}");
                }
                else
                {
                    _logger.LogWarning($"User {userId} does not have a mobile number in database, using default: {phone}");
                }
            }

            // Use the helper method to generate a transaction ID
            string txnid = _razorpayService.GenerateTransactionId();

            // --- Create a pending booking in the database ---
            var userSapIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userSapIdStr))
            {
                // Parse the booking date
                DateTime bookingDate;
                if (!DateTime.TryParse(pendingDate, out bookingDate))
                {
                    bookingDate = DateTime.Today;
                    _logger.LogWarning($"[RazorpayInitiate] Could not parse booking date: {pendingDate}, using today's date");
                }

                // Parse the start time
                DateTime parsedStartTime;
                try
                {
                    // Try to parse as TimeSpan first
                    if (TimeSpan.TryParse(pendingStartTime, out TimeSpan startTimeSpan))
                    {
                        parsedStartTime = bookingDate.Date.Add(startTimeSpan);
                    }
                    else
                    {
                        // Try to parse as full DateTime
                        if (!DateTime.TryParse(pendingStartTime, out parsedStartTime))
                        {
                            // Use current time as fallback
                            parsedStartTime = DateTime.Now;
                            _logger.LogWarning($"[RazorpayInitiate] Could not parse start time: {pendingStartTime}, using current time");
                        }
                    }
                }
                catch (Exception ex)
                {
                    parsedStartTime = DateTime.Now;
                    _logger.LogWarning(ex, $"[RazorpayInitiate] Error parsing start time: {pendingStartTime}, using current time");
                }

                // Parse the end time
                DateTime parsedEndTime;
                try
                {
                    // Try to parse as TimeSpan first
                    if (TimeSpan.TryParse(pendingEndTime, out TimeSpan endTimeSpan))
                    {
                        parsedEndTime = bookingDate.Date.Add(endTimeSpan);
                    }
                    else
                    {
                        // Try to parse as full DateTime
                        if (!DateTime.TryParse(pendingEndTime, out parsedEndTime))
                        {
                            // Use start time + 60 minutes as fallback
                            parsedEndTime = parsedStartTime.AddMinutes(60);
                            _logger.LogWarning($"[RazorpayInitiate] Could not parse end time: {pendingEndTime}, using start time + 60 minutes");
                        }
                    }
                }
                catch (Exception ex)
                {
                    parsedEndTime = parsedStartTime.AddMinutes(60);
                    _logger.LogWarning(ex, $"[RazorpayInitiate] Error parsing end time: {pendingEndTime}, using start time + 60 minutes");
                }

                // Check if there's an existing pending booking for this test and user
                var existingPendingBooking = await _context.TestBookings
                    .Where(tb => tb.TestId == testId.Value && tb.UserSapId == userSapIdStr && tb.Status == "Pending")
                    .OrderByDescending(tb => tb.BookedAt)
                    .FirstOrDefaultAsync();

                TestBooking pendingBooking;

                if (existingPendingBooking != null)
                {
                    // Update the existing pending booking instead of creating a new one
                    _logger.LogInformation($"Found existing pending booking (ID: {existingPendingBooking.Id}) for test {testId.Value}. Updating it instead of creating a new one.");

                    existingPendingBooking.BookingDate = bookingDate;
                    existingPendingBooking.StartTime = parsedStartTime;
                    existingPendingBooking.EndTime = parsedEndTime;
                    existingPendingBooking.SlotNumber = slotNumber.Value;
                    existingPendingBooking.UpdatedAt = Utilities.TimeZoneHelper.GetCurrentIstTime();
                    existingPendingBooking.StatusReason = "Updated for payment gateway";
                    existingPendingBooking.TransactionId = txnid;

                    pendingBooking = existingPendingBooking;

                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"Updated existing pending booking with txnid {txnid} for user {userSapIdStr}");
                }
                else
                {
                    // Create a new pending booking only if one doesn't exist
                    pendingBooking = new TestBooking
                    {
                        TestId = testId.Value,
                        UserSapId = userSapIdStr,
                        BookingDate = bookingDate,
                        StartTime = parsedStartTime,
                        EndTime = parsedEndTime,
                        SlotNumber = slotNumber.Value,
                        BookedAt = Utilities.TimeZoneHelper.GetCurrentIstTime(),
                        UpdatedAt = Utilities.TimeZoneHelper.GetCurrentIstTime(),
                        Status = "Pending",
                        StatusReason = "Initial booking from payment gateway",
                        TransactionId = txnid
                    };

                    // Log the booking creation
                    _logger.LogInformation($"Creating new pending booking for test {testId.Value}");
                    _context.TestBookings.Add(pendingBooking);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"Created new pending booking with txnid {txnid} for user {userSapIdStr}");
                }
            }
            else
            {
                _logger.LogWarning("[BookingError] Could not determine user SAP ID for pending booking");
                TempData["PaymentError"] = "User session invalid. Please login again.";
                return RedirectToAction("Index", "Test", new { error = TempData["PaymentError"] });
            }

            // Prepare the Razorpay order request
            var orderRequest = _razorpayService.PrepareOrderRequest(txnid, amount, productinfo, firstname, email, phone, testId.ToString());

            // Ensure the notes field is properly populated
            if (!orderRequest.ContainsKey("notes"))
            {
                orderRequest["notes"] = new Dictionary<string, string>
                {
                    ["productInfo"] = productinfo,
                    ["customerName"] = firstname,
                    ["customerEmail"] = email,
                    ["customerPhone"] = phone,
                    ["testId"] = testId.ToString()
                };
            }
            else if (orderRequest["notes"] is Dictionary<string, string> notes)
            {
                if (!notes.ContainsKey("testId"))
                {
                    notes["testId"] = testId.ToString();
                }
            }

            // Create order with Razorpay
            var (success, orderId, errorMessage) = await _razorpayService.CreateOrderAsync(orderRequest);

            if (success)
            {
                // Prepare checkout options for Razorpay
                var checkoutOptions = _razorpayService.PrepareCheckoutOptions(orderId, amount, productinfo, firstname, email, phone, testId.ToString());

                // Create model for view
                var model = new RazorpayRequestModel
                {
                    Parameters = orderRequest,
                    OrderId = orderId,
                    CheckoutOptions = checkoutOptions
                };

                // Store testId in ViewBag as a fallback
                ViewBag.TestId = testId;
                ViewBag.TransactionId = txnid;

                // Log the payment request for debugging
                _logger.LogInformation($"Initiating Razorpay payment: TxnID={txnid}, OrderId={orderId}, Amount={amount}, Product={productinfo}, TestId={testId}");

                return View("RazorpayInitiate", model);
            }
            else
            {
                // Handle error
                _logger.LogError($"Razorpay order creation failed: {errorMessage}");
                TempData["PaymentError"] = $"Payment initiation failed: {errorMessage}. Please try again.";
                return RedirectToAction("Index", "Test", new { error = TempData["PaymentError"] });
            }
        }
        [HttpPost]
        [Route("/Payment/RazorpayCallback")]
        [AllowAnonymous] // Allow anonymous access for Razorpay callbacks
        public async Task<IActionResult> RazorpayCallback()
        {
            _logger.LogInformation("Razorpay callback received");

            try
            {
                // Get payment details from form
                var razorpayPaymentId = Request.Form["razorpay_payment_id"].ToString();
                var razorpayOrderId = Request.Form["razorpay_order_id"].ToString();
                var razorpaySignature = Request.Form["razorpay_signature"].ToString();
                var testId = Request.Form["testId"].ToString();

                _logger.LogInformation("Razorpay callback parameters: PaymentId={PaymentId}, OrderId={OrderId}, Signature={Signature}, TestId={TestId}",
                    razorpayPaymentId, razorpayOrderId, razorpaySignature, testId);

                // Verify signature
                bool isValid = _razorpayService.VerifyPaymentSignature(razorpayOrderId, razorpayPaymentId, razorpaySignature);
                if (!isValid)
                {
                    _logger.LogWarning("Razorpay signature verification failed");
                    return RedirectToAction("Failure", new { error = "Invalid payment signature" });
                }

                // Extract the test ID from the form data
                int testIdValue = 0;
                if (!string.IsNullOrEmpty(testId) && int.TryParse(testId, out testIdValue))
                {
                    _logger.LogInformation("Extracted test ID from form data: {TestId}", testIdValue);
                }
                else
                {
                    _logger.LogWarning("Could not parse test ID from form data: {TestId}", testId);
                }

                // Get the user ID from the session or claims
                string userSapId = null;
                if (User.Identity?.IsAuthenticated == true)
                {
                    userSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    _logger.LogInformation("Got user SAP ID from claims: {UserSapId}", userSapId);
                }
                else
                {
                    userSapId = HttpContext.Session.GetString("PendingUserSapId");
                    _logger.LogInformation("Got user SAP ID from session: {UserSapId}", userSapId);
                }

                // Find the booking using multiple strategies
                _logger.LogInformation("Looking for pending bookings to update after payment");

                TestBooking booking = null;

                // Strategy 1: Try to find by transaction ID if it's stored in the receipt field of the order
                if (!string.IsNullOrEmpty(razorpayOrderId))
                {
                    var bookingsByTxnId = await _context.TestBookings
                        .Where(b => b.Status == "Pending" && b.TransactionId != null && razorpayOrderId.Contains(b.TransactionId))
                        .OrderByDescending(b => b.BookedAt)
                        .ToListAsync();

                    if (bookingsByTxnId.Any())
                    {
                        booking = bookingsByTxnId.First();
                        _logger.LogInformation("Found booking by transaction ID: BookingId={BookingId}, TestId={TestId}, UserSapId={UserSapId}",
                            booking.Id, booking.TestId, booking.UserSapId);
                    }
                    else
                    {
                        _logger.LogInformation("No bookings found by transaction ID match");
                    }
                }

                // Strategy 2: If we have a test ID and user ID, try to find by those
                if (booking == null && testIdValue > 0 && !string.IsNullOrEmpty(userSapId))
                {
                    var bookingsByTestAndUser = await _context.TestBookings
                        .Where(b => b.Status == "Pending" && b.TestId == testIdValue && b.UserSapId == userSapId)
                        .OrderByDescending(b => b.BookedAt)
                        .ToListAsync();

                    if (bookingsByTestAndUser.Any())
                    {
                        // If there are multiple pending bookings for the same test and user, mark all but the most recent as superseded
                        if (bookingsByTestAndUser.Count > 1)
                        {
                            _logger.LogWarning("Found {Count} pending bookings for test {TestId} and user {UserSapId}. Marking all but the most recent as superseded.",
                                bookingsByTestAndUser.Count, testIdValue, userSapId);

                            // Keep the most recent booking
                            booking = bookingsByTestAndUser.First();

                            // Mark all others as superseded
                            for (int i = 1; i < bookingsByTestAndUser.Count; i++)
                            {
                                var oldBooking = bookingsByTestAndUser[i];
                                oldBooking.Status = "Superseded";
                                oldBooking.UpdatedAt = Utilities.TimeZoneHelper.GetCurrentIstTime();
                                oldBooking.StatusReason = "Duplicate booking superseded during payment";
                                _logger.LogInformation("Marking booking {BookingId} as superseded (duplicate)", oldBooking.Id);
                            }

                            // Save changes to mark old bookings as superseded
                            await _context.SaveChangesAsync();
                        }
                        else
                        {
                            booking = bookingsByTestAndUser.First();
                        }

                        _logger.LogInformation("Found booking by test ID and user ID: BookingId={BookingId}, TestId={TestId}, UserSapId={UserSapId}",
                            booking.Id, booking.TestId, booking.UserSapId);
                    }
                    else
                    {
                        _logger.LogInformation("No bookings found by test ID and user ID match");
                    }
                }

                // Strategy 3: If we have a test ID, try to find by just that
                if (booking == null && testIdValue > 0)
                {
                    var bookingsByTest = await _context.TestBookings
                        .Where(b => b.Status == "Pending" && b.TestId == testIdValue)
                        .OrderByDescending(b => b.BookedAt)
                        .ToListAsync();

                    if (bookingsByTest.Any())
                    {
                        booking = bookingsByTest.First();
                        _logger.LogInformation("Found booking by test ID: BookingId={BookingId}, TestId={TestId}, UserSapId={UserSapId}",
                            booking.Id, booking.TestId, booking.UserSapId);
                    }
                    else
                    {
                        _logger.LogInformation("No bookings found by test ID match");
                    }
                }

                // Strategy 4: If we have a user ID, try to find by just that
                if (booking == null && !string.IsNullOrEmpty(userSapId))
                {
                    var bookingsByUser = await _context.TestBookings
                        .Where(b => b.Status == "Pending" && b.UserSapId == userSapId)
                        .OrderByDescending(b => b.BookedAt)
                        .ToListAsync();

                    if (bookingsByUser.Any())
                    {
                        booking = bookingsByUser.First();
                        _logger.LogInformation("Found booking by user ID: BookingId={BookingId}, TestId={TestId}, UserSapId={UserSapId}",
                            booking.Id, booking.TestId, booking.UserSapId);
                    }
                    else
                    {
                        _logger.LogInformation("No bookings found by user ID match");
                    }
                }

                // Strategy 5: Last resort - get the most recent pending booking
                if (booking == null)
                {
                    var mostRecentBooking = await _context.TestBookings
                        .Where(b => b.Status == "Pending")
                        .OrderByDescending(b => b.BookedAt)
                        .FirstOrDefaultAsync();

                    if (mostRecentBooking != null)
                    {
                        booking = mostRecentBooking;
                        _logger.LogInformation("Found most recent pending booking as last resort: BookingId={BookingId}, TestId={TestId}, UserSapId={UserSapId}",
                            booking.Id, booking.TestId, booking.UserSapId);
                    }
                    else
                    {
                        _logger.LogWarning("No pending bookings found at all");
                        return RedirectToAction("Failure", new { error = "Booking not found" });
                    }
                }

                // Update the transaction ID if it's not already set
                if (booking.TransactionId == null)
                {
                    booking.TransactionId = razorpayPaymentId;
                    _logger.LogInformation("Updated transaction ID for booking {BookingId} to {TxnId}", booking.Id, razorpayPaymentId);
                }

                // CRITICAL FIX: Verify payment status before marking as completed
                // In the callback, we need to verify the payment with Razorpay
                var paymentStatus = await _razorpayService.VerifyPaymentStatusAsync(razorpayPaymentId);
                _logger.LogInformation("Payment status from Razorpay for payment ID {PaymentId}: {Status}", razorpayPaymentId, paymentStatus);

                if (paymentStatus.Equals("captured", StringComparison.OrdinalIgnoreCase) ||
                    paymentStatus.Equals("authorized", StringComparison.OrdinalIgnoreCase))
                {
                    // CRITICAL FIX: Mark as Confirmed instead of Completed
                    // Confirmed status means the payment is successful and the test is available to take
                    _logger.LogInformation("Updating booking status from {OldStatus} to Confirmed for BookingId={BookingId}", booking.Status, booking.Id);
                    booking.Status = "Confirmed";
                    booking.UpdatedAt = Utilities.TimeZoneHelper.GetCurrentIstTime();
                    booking.StatusReason = "Payment confirmed";

                    // Check if this is a retake booking
                    var isReattemptSessionValue = HttpContext.Session.GetString("IsReattempt");
                    if (!string.IsNullOrEmpty(isReattemptSessionValue) && isReattemptSessionValue.Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        // IsRetake flag removed
                        _logger.LogInformation("Retake flag is now stored in TempData/Session instead of database");

                        // Store retake information in session for persistence
                        HttpContext.Session.SetString($"RetakeBooking_{booking.Id}", "true");
                        HttpContext.Session.SetString($"RetakeBookingTestId_{booking.Id}", booking.TestId.ToString());
                        _logger.LogInformation("Set RetakeBooking_{0}=true and RetakeBookingTestId_{0}={1} in session", booking.Id, booking.TestId);

                        // Also store test-specific retake flag
                        HttpContext.Session.SetString($"RetakeTest_{booking.TestId}", "true");
                        _logger.LogInformation("Set RetakeTest_{0}=true in session", booking.TestId);
                    }

                    try {
                        var result = await _context.SaveChangesAsync();
                        _logger.LogInformation("Updated booking status to Completed for BookingId={BookingId}, SaveChanges result: {Result}", booking.Id, result);

                        // Double-check that the status was actually updated
                        var updatedBooking = await _context.TestBookings.FindAsync(booking.Id);
                        if (updatedBooking != null) {
                            _logger.LogInformation("After update, booking {BookingId} status is now: {Status}", updatedBooking.Id, updatedBooking.Status);
                        } else {
                            _logger.LogWarning("Could not find booking {BookingId} after update");
                        }
                    } catch (Exception ex) {
                        _logger.LogError(ex, "Error updating booking status to Completed for BookingId={BookingId}", booking.Id);
                        return RedirectToAction("Failure", new { error = "Error updating booking status" });
                    }
                }
                else
                {
                    // If payment status is not successful, mark as failed
                    _logger.LogWarning("Payment verification failed. Status: {Status}. Marking booking as Failed", paymentStatus);
                    booking.Status = "Failed";

                    try {
                        await _context.SaveChangesAsync();
                        _logger.LogInformation("Updated booking status to Failed for BookingId={BookingId}", booking.Id);
                    } catch (Exception ex) {
                        _logger.LogError(ex, "Error updating booking status to Failed for BookingId={BookingId}", booking.Id);
                    }

                    return RedirectToAction("Failure", new { error = "Payment verification failed" });
                }

                // Create payment record
                var test = await _context.Tests.FindAsync(booking.TestId);
                if (test == null)
                {
                    _logger.LogError("Test not found for TestId: {TestId} in booking {BookingId}", booking.TestId, booking.Id);
                    return RedirectToAction("Failure", new { error = "Test not found" });
                }
                decimal amount = test.Price;

                // Validate UserSapId exists
                var user = await _context.Users.FindAsync(booking.UserSapId);
                if (user == null)
                {
                    // Try to find in SpecialUsers if not found in Users
                    var specialUser = await _context.SpecialUsers.FindAsync(booking.UserSapId);
                    if (specialUser == null)
                    {
                        _logger.LogError("User not found for UserSapId: {UserSapId}", booking.UserSapId);
                        return RedirectToAction("Failure", new { error = "User not found" });
                    }
                }

                // Validate TransactionId
                if (string.IsNullOrEmpty(razorpayPaymentId))
                {
                    _logger.LogError("Transaction ID is null or empty for booking: {BookingId}", booking.Id);
                    return RedirectToAction("Failure", new { error = "Invalid transaction ID" });
                }

                var payment = new Payment
                {
                    UserSapId = booking.UserSapId,
                    Amount = amount,
                    Currency = "INR",
                    Status = booking.Status, // Use the booking status (Completed or Failed)
                    CreatedAt = Utilities.TimeZoneHelper.GetCurrentIstTime(),
                    PaidAt = booking.Status == "Completed" ? Utilities.TimeZoneHelper.GetCurrentIstTime() : null, // Only set PaidAt if payment was completed
                    TransactionId = razorpayPaymentId
                };
                _context.Payments.Add(payment);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Created payment record with status {Status} for BookingId={BookingId}, PaymentId={PaymentId}",
                    payment.Status, booking.Id, payment.Id);

                // Send payment receipt email
                await SendPaymentReceiptEmail(booking.UserSapId, booking.TestId, booking, payment);

                // Store booking info in TempData for the MyBookings action
                TempData["BookedTestId"] = booking.TestId;
                TempData["JustPaid"] = true;

                // Check if this is a retake booking
                bool isReattempt = false;
                var isReattemptSession = HttpContext.Session.GetString("IsReattempt");
                if (!string.IsNullOrEmpty(isReattemptSession) && isReattemptSession.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    isReattempt = true;
                    TempData["IsRetakeBooking"] = true;
                    _logger.LogInformation($"Set IsRetakeBooking flag in TempData for test {booking.TestId}");
                }

                TempData["SuccessMessage"] = "Payment successful! Your booking has been completed. You can now start your test.";

                // Redirect to MyBookings page instead of Success page
                return RedirectToAction("MyBookings", "Test");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Razorpay callback");
                return RedirectToAction("Failure", new { error = "Error processing payment callback" });
            }
        }

        [HttpPost]
        [Route("Payment/RazorpayWebhook")]
        [AllowAnonymous] // Allow anonymous access for Razorpay webhooks
        public async Task<IActionResult> RazorpayWebhook()
        {
            _logger.LogInformation("Razorpay webhook received");

            try
            {
                // Read request body
                using var reader = new StreamReader(Request.Body);
                var body = await reader.ReadToEndAsync();
                _logger.LogInformation("Razorpay webhook body: {Body}", body);

                // Get Razorpay signature headers
                var razorpaySignature = Request.Headers["X-Razorpay-Signature"].ToString();
                if (string.IsNullOrEmpty(razorpaySignature))
                {
                    _logger.LogWarning("Razorpay webhook missing X-Razorpay-Signature header");
                    return BadRequest("Missing signature header");
                }

                // Get timestamp header
                var timestamp = Request.Headers["X-Razorpay-Event-Time"].ToString();
                if (string.IsNullOrEmpty(timestamp))
                {
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                }

                // Validate webhook signature
                bool isValid = _razorpayService.VerifyWebhookSignature(body, razorpaySignature, timestamp);
                if (!isValid)
                {
                    _logger.LogWarning("Razorpay webhook signature validation failed");
                    return BadRequest("Invalid signature");
                }

                // Parse webhook payload
                var webhookData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(body);
                if (webhookData == null)
                {
                    _logger.LogWarning("Razorpay webhook payload parsing failed");
                    return BadRequest("Invalid payload");
                }

                // Extract event type
                if (!webhookData.TryGetValue("event", out var eventObj) || eventObj == null)
                {
                    _logger.LogWarning("Razorpay webhook missing event type");
                    return BadRequest("Missing event type");
                }

                string eventType = eventObj.ToString();
                _logger.LogInformation("Razorpay webhook event type: {EventType}", eventType);

                // Handle different event types
                if (eventType == "payment.authorized" || eventType == "payment.captured")
                {
                    // Extract payment details
                    if (!webhookData.TryGetValue("payload", out var payloadObj) || payloadObj == null)
                    {
                        _logger.LogWarning("Razorpay webhook missing payload");
                        return BadRequest("Missing payload");
                    }

                    var payloadJson = System.Text.Json.JsonSerializer.Serialize(payloadObj);
                    var payload = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(payloadJson);

                    if (payload == null || !payload.TryGetValue("payment", out var paymentObj))
                    {
                        _logger.LogWarning("Razorpay webhook missing payment data");
                        return BadRequest("Missing payment data");
                    }

                    var paymentJson = System.Text.Json.JsonSerializer.Serialize(paymentObj);
                    var payment = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(paymentJson);

                    if (payment == null)
                    {
                        _logger.LogWarning("Razorpay webhook invalid payment data");
                        return BadRequest("Invalid payment data");
                    }

                    // Extract payment ID and order ID
                    string paymentId = payment.TryGetValue("id", out var idObj) ? idObj.ToString() : "";

                    if (payment.TryGetValue("order_id", out var orderIdObj) && orderIdObj != null)
                    {
                        string orderId = orderIdObj.ToString();

                        // Find the booking by transaction ID (which is stored in the receipt field of the order)
                        var bookings = await _context.TestBookings
                            .Where(b => b.Status == "Pending")
                            .OrderByDescending(b => b.BookedAt)
                            .Take(10)
                            .ToListAsync();

                        TestBooking booking = null;
                        foreach (var b in bookings)
                        {
                            if (b.TransactionId != null && orderId.Contains(b.TransactionId))
                            {
                                booking = b;
                                break;
                            }
                        }

                        if (booking != null)
                        {
                            // CRITICAL FIX: Verify payment status before marking as completed
                            // Check if the payment status is successful
                            string paymentStatus = payment.TryGetValue("status", out var statusObj) ? statusObj.ToString() : "";

                            if (paymentStatus.Equals("captured", StringComparison.OrdinalIgnoreCase) ||
                                paymentStatus.Equals("authorized", StringComparison.OrdinalIgnoreCase))
                            {
                                // CRITICAL FIX: Mark as Confirmed instead of Completed
                                // Confirmed status means the payment is successful and the test is available to take
                                booking.Status = "Confirmed";
                                booking.UpdatedAt = Utilities.TimeZoneHelper.GetCurrentIstTime();
                                booking.StatusReason = "Payment confirmed via webhook";

                                // Check if this is a retake booking
                                var isReattemptWebhook = HttpContext.Session.GetString("IsReattempt");
                                if (!string.IsNullOrEmpty(isReattemptWebhook) && isReattemptWebhook.Equals("true", StringComparison.OrdinalIgnoreCase))
                                {
                                    // IsRetake flag removed
                                    _logger.LogInformation("Retake flag is now stored in TempData/Session instead of database");
                                }

                                await _context.SaveChangesAsync();
                                _logger.LogInformation("Updated booking status to Completed for BookingId={BookingId} via webhook. Payment status: {Status}",
                                    booking.Id, paymentStatus);
                            }
                            else
                            {
                                // If payment status is not successful, mark as failed
                                booking.Status = "Failed";
                                await _context.SaveChangesAsync();
                                _logger.LogWarning("Updated booking status to Failed for BookingId={BookingId} via webhook. Payment status: {Status}",
                                    booking.Id, paymentStatus);

                                // Skip creating a payment record and sending email for failed payments
                                return Ok("Webhook processed successfully - payment not successful");
                            }

                            // Create payment record
                            var test = await _context.Tests.FindAsync(booking.TestId);
                            if (test == null)
                            {
                                _logger.LogError("Test not found for TestId: {TestId} in booking {BookingId} during webhook processing", booking.TestId, booking.Id);
                                return BadRequest("Test not found");
                            }
                            decimal amount = test.Price;

                            var paymentRecord = new Payment
                            {
                                UserSapId = booking.UserSapId,
                                Amount = amount,
                                Currency = "INR",
                                Status = booking.Status, // Use the booking status (Completed or Failed)
                                CreatedAt = Utilities.TimeZoneHelper.GetCurrentIstTime(),
                                PaidAt = booking.Status == "Completed" ? Utilities.TimeZoneHelper.GetCurrentIstTime() : null, // Only set PaidAt if payment was completed
                                TransactionId = paymentId
                            };
                            _context.Payments.Add(paymentRecord);
                            await _context.SaveChangesAsync();
                            _logger.LogInformation("Created payment record with status {Status} for BookingId={BookingId}, PaymentId={PaymentId} via webhook", paymentRecord.Status, booking.Id, paymentRecord.Id);

                            // Send payment receipt email
                            await SendPaymentReceiptEmail(booking.UserSapId, booking.TestId, booking, paymentRecord);
                        }
                        else
                        {
                            _logger.LogWarning("No booking found with transaction ID related to order {OrderId}", orderId);
                        }
                    }
                }
                else if (eventType == "payment.failed")
                {
                    // Handle failed payment
                    // Extract payment details
                    if (!webhookData.TryGetValue("payload", out var payloadObj) || payloadObj == null)
                    {
                        _logger.LogWarning("Razorpay webhook missing payload");
                        return BadRequest("Missing payload");
                    }

                    var payloadJson = System.Text.Json.JsonSerializer.Serialize(payloadObj);
                    var payload = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(payloadJson);

                    if (payload == null || !payload.TryGetValue("payment", out var paymentObj))
                    {
                        _logger.LogWarning("Razorpay webhook missing payment data");
                        return BadRequest("Missing payment data");
                    }

                    var paymentJson = System.Text.Json.JsonSerializer.Serialize(paymentObj);
                    var payment = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(paymentJson);

                    if (payment == null)
                    {
                        _logger.LogWarning("Razorpay webhook invalid payment data");
                        return BadRequest("Invalid payment data");
                    }

                    // Extract order ID
                    if (payment.TryGetValue("order_id", out var orderIdObj) && orderIdObj != null)
                    {
                        string orderId = orderIdObj.ToString();

                        // Find the booking by transaction ID
                        var bookings = await _context.TestBookings
                            .Where(b => b.Status == "Pending")
                            .OrderByDescending(b => b.BookedAt)
                            .Take(10)
                            .ToListAsync();

                        TestBooking booking = null;
                        foreach (var b in bookings)
                        {
                            if (b.TransactionId != null && orderId.Contains(b.TransactionId))
                            {
                                booking = b;
                                break;
                            }
                        }

                        if (booking != null)
                        {
                            // Update booking status
                            booking.Status = "Failed";
                            await _context.SaveChangesAsync();
                            _logger.LogInformation("Updated booking status to Failed for BookingId={BookingId} via webhook", booking.Id);
                        }
                        else
                        {
                            _logger.LogWarning("No booking found with transaction ID related to order {OrderId}", orderId);
                        }
                    }
                }

                return Ok("Webhook processed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Razorpay webhook");
                return StatusCode(500, "Error processing webhook");
            }
        }

        [AcceptVerbs("GET", "POST")]
        [Route("Payment/Success")]
        [AllowAnonymous] // Allow anonymous access for Razorpay callbacks and redirects
        public async Task<IActionResult> Success(string txnid = null, string status = null)
        {
            _logger.LogInformation("Payment success callback received with txnid: {TxnId}, status: {Status}", txnid, status);

            // Try to fetch the pending booking using multiple strategies
            _logger.LogInformation("Payment success callback received with txnid: {TxnId}, status: {Status}", txnid, status);

            TestBooking booking = null;

            // Strategy 1: Try to find by transaction ID
            if (!string.IsNullOrEmpty(txnid))
            {
                _logger.LogInformation("Looking for booking with transaction ID: {TxnId}", txnid);
                booking = await _context.TestBookings.FirstOrDefaultAsync(b => b.TransactionId == txnid);

                if (booking != null)
                {
                    _logger.LogInformation("Found booking by transaction ID: BookingId={BookingId}, TestId={TestId}, UserSapId={UserSapId}",
                        booking.Id, booking.TestId, booking.UserSapId);
                }
                else
                {
                    _logger.LogInformation("No booking found by transaction ID match");
                }
            }

            // Strategy 2: Try to find by user ID and pending status
            if (booking == null)
            {
                // Get the user ID from the session or claims
                string userSapId = null;
                if (User.Identity?.IsAuthenticated == true)
                {
                    userSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    _logger.LogInformation("Got user SAP ID from claims: {UserSapId}", userSapId);
                }
                else
                {
                    userSapId = HttpContext.Session.GetString("PendingUserSapId");
                    _logger.LogInformation("Got user SAP ID from session: {UserSapId}", userSapId);
                }

                if (!string.IsNullOrEmpty(userSapId))
                {
                    var bookingsByUser = await _context.TestBookings
                        .Where(b => b.Status == "Pending" && b.UserSapId == userSapId)
                        .OrderByDescending(b => b.BookedAt)
                        .ToListAsync();

                    if (bookingsByUser.Any())
                    {
                        booking = bookingsByUser.First();
                        _logger.LogInformation("Found booking by user ID: BookingId={BookingId}, TestId={TestId}, UserSapId={UserSapId}",
                            booking.Id, booking.TestId, booking.UserSapId);
                    }
                    else
                    {
                        _logger.LogInformation("No bookings found by user ID match");
                    }
                }
            }

            // Strategy 3: Try to find by test ID from session
            if (booking == null)
            {
                var pendingTestId = HttpContext.Session.GetInt32("PendingTestId");
                if (pendingTestId.HasValue)
                {
                    _logger.LogInformation("Got test ID from session: {TestId}", pendingTestId.Value);

                    var bookingsByTest = await _context.TestBookings
                        .Where(b => b.Status == "Pending" && b.TestId == pendingTestId.Value)
                        .OrderByDescending(b => b.BookedAt)
                        .ToListAsync();

                    if (bookingsByTest.Any())
                    {
                        booking = bookingsByTest.First();
                        _logger.LogInformation("Found booking by test ID: BookingId={BookingId}, TestId={TestId}, UserSapId={UserSapId}",
                            booking.Id, booking.TestId, booking.UserSapId);
                    }
                    else
                    {
                        _logger.LogInformation("No bookings found by test ID match");
                    }
                }
            }

            // Strategy 4: Last resort - get the most recent pending booking
            if (booking == null)
            {
                _logger.LogWarning("No booking found with specific criteria, trying to find most recent pending booking");

                booking = await _context.TestBookings
                    .Where(b => b.Status == "Pending")
                    .OrderByDescending(b => b.BookedAt)
                    .FirstOrDefaultAsync();

                if (booking != null)
                {
                    _logger.LogInformation("Found most recent pending booking as fallback: BookingId={BookingId}, UserSapId={UserSapId}, TestId={TestId}, Status={Status}",
                        booking.Id, booking.UserSapId, booking.TestId, booking.Status);
                }
            }

            // If we found a booking, update it
            if (booking != null)
            {
                // Update the transaction ID if it's not already set
                if (string.IsNullOrEmpty(booking.TransactionId) && !string.IsNullOrEmpty(txnid))
                {
                    booking.TransactionId = txnid;
                    _logger.LogInformation("Updated transaction ID for booking {BookingId} to {TxnId}", booking.Id, txnid);
                }

                _logger.LogInformation("Processing booking: BookingId={BookingId}, TestId={TestId}, UserSapId={UserSapId}, Status={Status}",
                    booking.Id, booking.TestId, booking.UserSapId, booking.Status);

                    // CRITICAL FIX: Verify payment status before marking as completed
                    // Check if the payment status is successful
                    bool isPaymentSuccessful = false;

                    // First check the status parameter if provided
                    if (!string.IsNullOrEmpty(status))
                    {
                        isPaymentSuccessful = status.Equals("success", StringComparison.OrdinalIgnoreCase);
                        _logger.LogInformation("Payment status from callback parameter: {Status}, isSuccessful: {IsSuccessful}",
                            status, isPaymentSuccessful);
                    }

                    // If transaction ID is provided, verify with Razorpay
                    if (!string.IsNullOrEmpty(txnid))
                    {
                        var paymentStatus = await _razorpayService.VerifyPaymentStatusAsync(txnid);
                        isPaymentSuccessful = paymentStatus.Equals("captured", StringComparison.OrdinalIgnoreCase) ||
                                             paymentStatus.Equals("authorized", StringComparison.OrdinalIgnoreCase);
                        _logger.LogInformation("Payment status from Razorpay for payment ID {PaymentId}: {Status}, isSuccessful: {IsSuccessful}",
                            txnid, paymentStatus, isPaymentSuccessful);
                    }

                    if (isPaymentSuccessful)
                    {
                        // CRITICAL FIX: Mark as Confirmed instead of Completed
                        // Confirmed status means the payment is successful and the test is available to take
                        _logger.LogInformation("Updating booking status from {OldStatus} to Confirmed for BookingId={BookingId}", booking.Status, booking.Id);
                        booking.Status = "Confirmed";
                        booking.UpdatedAt = Utilities.TimeZoneHelper.GetCurrentIstTime();
                        booking.StatusReason = "Payment confirmed";

                        // Check if this is a retake booking
                        var isReattemptSessionValue = HttpContext.Session.GetString("IsReattempt");
                        if (!string.IsNullOrEmpty(isReattemptSessionValue) && isReattemptSessionValue.Equals("true", StringComparison.OrdinalIgnoreCase))
                        {
                            // IsRetake flag removed
                            _logger.LogInformation("Retake flag is now stored in TempData/Session instead of database");
                        }

                        try {
                            var result = await _context.SaveChangesAsync();
                            _logger.LogInformation("Updated booking status to Completed for BookingId={BookingId}, SaveChanges result: {Result}", booking.Id, result);

                            // Double-check that the status was actually updated
                            var updatedBooking = await _context.TestBookings.FindAsync(booking.Id);
                            if (updatedBooking != null) {
                                _logger.LogInformation("After update, booking {BookingId} status is now: {Status}", updatedBooking.Id, updatedBooking.Status);
                            } else {
                                _logger.LogWarning("Could not find booking {BookingId} after update");
                            }
                        } catch (Exception ex) {
                            _logger.LogError(ex, "Error updating booking status to Completed for BookingId={BookingId}", booking.Id);
                            return RedirectToAction("Failure", new { error = "Error updating booking status" });
                        }
                    }
                    else
                    {
                        // If payment status is not successful, mark as failed
                        _logger.LogWarning("Payment verification failed. Marking booking as Failed");
                        booking.Status = "Failed";

                        try {
                            await _context.SaveChangesAsync();
                            _logger.LogInformation("Updated booking status to Failed for BookingId={BookingId}", booking.Id);
                        } catch (Exception ex) {
                            _logger.LogError(ex, "Error updating booking status to Failed for BookingId={BookingId}", booking.Id);
                        }

                        return RedirectToAction("Failure", new { error = "Payment verification failed" });
                    }

                    // Get the test to use its price
                    var test = await _context.Tests.FindAsync(booking.TestId);
                    if (test == null)
                    {
                        _logger.LogError("Test not found for TestId: {TestId} in booking {BookingId} during success processing", booking.TestId, booking.Id);
                        return RedirectToAction("Failure", new { error = "Test not found" });
                    }
                    decimal amount = test.Price;

                    // Create payment record
                    var payment = new Payment
                    {
                        UserSapId = booking.UserSapId,
                        Amount = amount,
                        Currency = "INR",
                        Status = booking.Status, // Use the booking status (Completed or Failed)
                        CreatedAt = Utilities.TimeZoneHelper.GetCurrentIstTime(),
                        PaidAt = booking.Status == "Completed" ? Utilities.TimeZoneHelper.GetCurrentIstTime() : null, // Only set PaidAt if payment was completed
                        TransactionId = txnid
                    };
                    _context.Payments.Add(payment);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Created payment record with status {Status} for BookingId={BookingId}, PaymentId={PaymentId}",
                        payment.Status, booking.Id, payment.Id);

                    // Store booking info in TempData for the MyBookings action
                    TempData["BookedTestId"] = booking.TestId;
                    TempData["JustPaid"] = true;

                    // Check if this is a retake booking
                    bool isReattempt = false;
                    var isReattemptSession = HttpContext.Session.GetString("IsReattempt");
                    if (!string.IsNullOrEmpty(isReattemptSession) && isReattemptSession.Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        isReattempt = true;
                        TempData["IsRetakeBooking"] = true;
                        _logger.LogInformation($"Set IsRetakeBooking flag in TempData for test {booking.TestId}");

                        // Also set booking-specific TempData
                        TempData[$"IsRetakeBooking_{booking.Id}"] = "True";
                        TempData[$"RetakeTestId_{booking.Id}"] = booking.TestId.ToString();
                        _logger.LogInformation($"Set IsRetakeBooking_{booking.Id}=True and RetakeTestId_{booking.Id}={booking.TestId} in TempData");

                        // Store in session for persistence across requests
                        HttpContext.Session.SetString($"RetakeBooking_{booking.Id}", "true");
                        HttpContext.Session.SetString($"RetakeBookingTestId_{booking.Id}", booking.TestId.ToString());
                        HttpContext.Session.SetString($"RetakeTest_{booking.TestId}", "true");
                        _logger.LogInformation($"Set RetakeBooking_{booking.Id}=true, RetakeBookingTestId_{booking.Id}={booking.TestId}, and RetakeTest_{booking.TestId}=true in session");
                    }

                    _logger.LogInformation($"Stored BookedTestId={booking.TestId} in TempData, IsReattempt={isReattempt}");

                    // Send payment receipt email
                    await SendPaymentReceiptEmail(booking.UserSapId, booking.TestId, booking, payment);

                    TempData["SuccessMessage"] = "Payment successful! Your booking has been completed. You can now start your test.";

                    // Clean up payment session values
                    HttpContext.Session.Remove("PendingTestId");
                    HttpContext.Session.Remove("PendingDate");
                    HttpContext.Session.Remove("PendingStartTime");
                    HttpContext.Session.Remove("PendingEndTime");
                    HttpContext.Session.Remove("PendingSlotNumber");
                    HttpContext.Session.Remove("PendingUserSapId");
                    HttpContext.Session.Remove("IsReattempt");
                    _logger.LogInformation("[SessionCleanup] Cleared all payment-related session values after payment success.");

                    // Redirect to MyBookings page to show the booked test with Start Test button
                    _logger.LogInformation($"Redirecting to MyBookings page after successful payment for test {booking.TestId}");
                    return RedirectToAction("MyBookings", "Test", new { fromPayment = true });
                }


            // For GET requests without a specific booking, redirect to MyBookings
            TempData["SuccessMessage"] = "Payment successful! You can now access your scheduled test.";

            // Clean up payment session values
            HttpContext.Session.Remove("PendingTestId");
            HttpContext.Session.Remove("PendingDate");
            HttpContext.Session.Remove("PendingStartTime");
            HttpContext.Session.Remove("PendingEndTime");
            HttpContext.Session.Remove("PendingSlotNumber");
            HttpContext.Session.Remove("PendingUserSapId");
            HttpContext.Session.Remove("IsReattempt");
            _logger.LogInformation("[SessionCleanup] Cleared all payment-related session values after payment success.");

            // Redirect to MyBookings page to show all booked tests
            _logger.LogInformation("Redirecting to MyBookings page after successful payment (fallback case)");
            return RedirectToAction("MyBookings", "Test", new { fromPayment = true });
        }

        [HttpGet]
        [Route("Payment/ReturnToBooking")]
        [AllowAnonymous] // Allow anonymous access for payment gateway returns
        public async Task<IActionResult> ReturnToBooking(string testId = null)
        {
            _logger.LogInformation("Return to Booking button clicked. TestId: {TestId}", testId);

            // Try to get the test ID from the query parameter or session
            int? pendingTestId = null;

            // First try to parse the testId from the query parameter
            if (!string.IsNullOrEmpty(testId) && int.TryParse(testId, out int parsedTestId))
            {
                pendingTestId = parsedTestId;
                _logger.LogInformation("Using testId {TestId} from query parameter", pendingTestId);
            }
            else
            {
                // If not available in the query, try to get it from the session
                pendingTestId = HttpContext.Session.GetInt32("PendingTestId");
                _logger.LogInformation("Using testId {TestId} from session", pendingTestId);
            }

            // Get the user SAP ID from session or claims
            string userSapId = HttpContext.Session.GetString("PendingUserSapId");
            if (string.IsNullOrEmpty(userSapId) && User.Identity?.IsAuthenticated == true)
            {
                userSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!string.IsNullOrEmpty(userSapId))
                {
                    _logger.LogInformation("Using userSapId {UserSapId} from claims", userSapId);
                }
            }

            // If we have both testId and userSapId, try to find and update any pending bookings
            if (pendingTestId.HasValue && !string.IsNullOrEmpty(userSapId))
            {
                try
                {
                    // Find any pending bookings for this test and user
                    var pendingBookings = await _context.TestBookings
                        .Where(b => b.TestId == pendingTestId.Value && b.UserSapId == userSapId &&
                               (b.Status == "Pending" || b.Status == "Processing"))
                        .ToListAsync();

                    // Mark all pending bookings as Abandoned
                    foreach (var booking in pendingBookings)
                    {
                        booking.Status = "Abandoned";
                        booking.StatusReason = "User returned from payment gateway without completing payment";
                        booking.UpdatedAt = Utilities.TimeZoneHelper.GetCurrentIstTime();
                        _logger.LogInformation("Updated booking status to Abandoned for BookingId: {BookingId}", booking.Id);
                    }

                    if (pendingBookings.Any())
                    {
                        await _context.SaveChangesAsync();
                        _logger.LogInformation("Successfully updated {Count} bookings to Abandoned status", pendingBookings.Count);
                    }
                    else
                    {
                        _logger.LogWarning("No pending bookings found for test {TestId} and user {UserSapId}", pendingTestId, userSapId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating booking status to Abandoned");
                }
            }
            else
            {
                _logger.LogWarning("Missing testId or userSapId. Cannot update booking status. TestId: {TestId}, UserSapId: {UserSapId}",
                    pendingTestId, userSapId ?? "null");
            }

            // Clean up payment session values
            HttpContext.Session.Remove("PendingTestId");
            HttpContext.Session.Remove("PendingDate");
            HttpContext.Session.Remove("PendingStartTime");
            HttpContext.Session.Remove("PendingEndTime");
            HttpContext.Session.Remove("PendingSlotNumber");
            HttpContext.Session.Remove("PendingUserSapId");
            HttpContext.Session.Remove("IsReattempt");
            _logger.LogInformation("[SessionCleanup] Cleared all payment-related session values after returning from payment gateway.");

            // Redirect to BookSlot page with the test ID if available
            if (pendingTestId.HasValue)
            {
                _logger.LogInformation("Redirecting to BookSlot page for test {TestId}", pendingTestId.Value);
                return RedirectToAction("BookSlot", "Test", new { id = pendingTestId.Value });
            }
            else
            {
                _logger.LogInformation("Redirecting to BookSlot page without test ID");
                return RedirectToAction("BookSlot", "Test");
            }
        }

        [AcceptVerbs("GET", "POST")]
        [Route("Payment/Failure")]
        [AllowAnonymous] // Allow anonymous access for Razorpay callbacks and redirects
        public async Task<IActionResult> Failure(string txnid = null, string testId = null, string status = null, string error = null)
        {
            _logger.LogWarning("Payment failure callback received via GET, txnid: {TxnId}, testId: {TestId}, status: {Status}, error: {Error}",
                txnid, testId, status, error);

            try
            {
                // Get user SAP ID from session
                var userSapId = HttpContext.Session.GetString("PendingUserSapId");
                if (string.IsNullOrEmpty(userSapId))
                {
                    _logger.LogWarning("No user SAP ID found in session");
                    TempData["ErrorMessage"] = "Session expired. Please try booking again.";
                    return RedirectToAction("Index", "Test");
                }

                // Validate user exists
                var user = await _context.Users.FindAsync(userSapId);
                if (user == null)
                {
                    // Try to find in SpecialUsers if not found in Users
                    var specialUser = await _context.SpecialUsers.FindAsync(userSapId);
                    if (specialUser == null)
                    {
                        _logger.LogError("User not found for UserSapId: {UserSapId}", userSapId);
                        TempData["ErrorMessage"] = "User not found";
                        return RedirectToAction("Index", "Test");
                    }
                }

                // Get test ID from session if not provided
                if (string.IsNullOrEmpty(testId))
                {
                    testId = HttpContext.Session.GetString("PendingTestId");
                    _logger.LogInformation("Using testId {TestId} from session", testId);
                }

                if (string.IsNullOrEmpty(testId))
                {
                    _logger.LogWarning("No test ID found in session or parameters");
                    TempData["ErrorMessage"] = "Test information not found. Please try booking again.";
                    return RedirectToAction("Index", "Test");
                }

                // Generate a transaction ID if none exists
                if (string.IsNullOrEmpty(txnid))
                {
                    txnid = _razorpayService.GenerateTransactionId();
                    _logger.LogInformation("Generated new transaction ID: {TxnId} for failed payment", txnid);
                }

                // Get the test to use its price
                var test = await _context.Tests.FindAsync(int.Parse(testId));
                if (test == null)
                {
                    _logger.LogError("Test not found for TestId: {TestId} during failure processing", testId);
                    TempData["ErrorMessage"] = "Test not found";
                    return RedirectToAction("Index", "Test");
                }
                decimal amount = test.Price;

                // Create payment record
                var payment = new Payment
                {
                    UserSapId = userSapId,
                    Amount = amount,
                    Currency = "INR",
                    Status = "Failed",
                    CreatedAt = Utilities.TimeZoneHelper.GetCurrentIstTime(),
                    TransactionId = txnid
                };

                _context.Payments.Add(payment);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Created failed payment record with ID: {PaymentId}", payment.Id);

                // Clear session data
                HttpContext.Session.Remove("PendingTestId");
                HttpContext.Session.Remove("PendingUserSapId");

                TempData["ErrorMessage"] = error ?? "Payment failed. Please try again.";
                return RedirectToAction("Index", "Test");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating booking status to Failed or creating failed payment record");
                TempData["ErrorMessage"] = "An error occurred while processing the payment failure. Please try again.";
                return RedirectToAction("Index", "Test");
            }
        }

        [HttpGet]
        [Route("Payment/OpenPaymentGatewayForm")]
        [Authorize]
        IActionResult OpenPaymentGatewayForm()
        {
            // Display the form to open the payment gateway
            return View("OpenPaymentGateway");
        }

        [HttpGet]
        [Route("Payment/PaymentGatewayExample")]
        [Authorize]
        IActionResult PaymentGatewayExample()
        {
            // Display the example page
            return View("PaymentGatewayExample");
        }

        [HttpGet]
        [Route("Payment/OpenPaymentGateway")]
        [Authorize]
        IActionResult OpenPaymentGateway(int? testId = null, string? date = null, string? startTime = null, string? endTime = null, int? slotNumber = null, bool isReattempt = false)
        {
            _logger.LogInformation("OpenPaymentGateway called with parameters: TestId={TestId}, Date={Date}, StartTime={StartTime}, EndTime={EndTime}, SlotNumber={SlotNumber}, IsReattempt={IsReattempt}",
                testId, date, startTime, endTime, slotNumber, isReattempt);

            try
            {
                // Validate required parameters
                if (!testId.HasValue)
                {
                    _logger.LogWarning("OpenPaymentGateway missing required parameter: testId");
                    TempData["PaymentError"] = "Missing test ID. Please try booking again.";
                    return RedirectToAction("Index", "Test", new { error = TempData["PaymentError"] });
                }

                if (string.IsNullOrEmpty(date))
                {
                    _logger.LogWarning("OpenPaymentGateway missing required parameter: date");
                    TempData["PaymentError"] = "Missing booking date. Please try booking again.";
                    return RedirectToAction("Index", "Test", new { error = TempData["PaymentError"] });
                }

                if (string.IsNullOrEmpty(startTime) || string.IsNullOrEmpty(endTime))
                {
                    _logger.LogWarning("OpenPaymentGateway missing required parameters: startTime or endTime");
                    TempData["PaymentError"] = "Missing time slot information. Please try booking again.";
                    return RedirectToAction("Index", "Test", new { error = TempData["PaymentError"] });
                }

                if (!slotNumber.HasValue)
                {
                    _logger.LogWarning("OpenPaymentGateway missing required parameter: slotNumber");
                    TempData["PaymentError"] = "Missing slot number. Please try booking again.";
                    return RedirectToAction("Index", "Test", new { error = TempData["PaymentError"] });
                }

                // Try to store parameters in session as a backup, but don't rely on it
                try
                {
                    HttpContext.Session.SetInt32("PendingTestId", testId.Value);
                    HttpContext.Session.SetString("PendingDate", date);
                    HttpContext.Session.SetString("PendingStartTime", startTime);
                    HttpContext.Session.SetString("PendingEndTime", endTime);
                    HttpContext.Session.SetInt32("PendingSlotNumber", slotNumber.Value);
                    // Always set the IsReattempt flag, whether true or false
                    if (isReattempt)
                    {
                        HttpContext.Session.SetString("IsReattempt", "true");
                        _logger.LogInformation("Set IsReattempt=true in session for test {TestId}", testId);
                    }
                    else
                    {
                        HttpContext.Session.SetString("IsReattempt", "false");
                        _logger.LogInformation("Set IsReattempt=false in session for test {TestId}", testId);
                    }
                    _logger.LogInformation("Successfully stored payment parameters in session.");
                }
                catch (Exception sessionEx)
                {
                    // Log the error but continue - we'll use query parameters instead
                    _logger.LogWarning(sessionEx, "Error storing payment parameters in session. Will use query parameters instead.");
                }

                // Get user information for the payment
                var userSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!string.IsNullOrEmpty(userSapId))
                {
                    try
                    {
                        HttpContext.Session.SetString("PendingUserSapId", userSapId);
                    }
                    catch (Exception sessionEx)
                    {
                        _logger.LogWarning(sessionEx, "Error storing user SAP ID in session.");
                    }
                }

                _logger.LogInformation("Redirecting to RazorpayInitiate with query parameters.");

                // Redirect to RazorpayInitiate with all parameters in the query string
                // This ensures the data is passed even if session is not working
                return RedirectToAction("RazorpayInitiate", new
                {
                    testId,
                    date,
                    startTime,
                    endTime,
                    slotNumber,
                    isReattempt,
                    userSapIdParam = userSapId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OpenPaymentGateway action");
                TempData["PaymentError"] = "An error occurred while opening the payment gateway. Please try booking again.";
                return RedirectToAction("Index", "Test", new { error = TempData["PaymentError"] });
            }
        }

        [HttpGet]
        [Route("Payment/TestRazorpay")]
        [AllowAnonymous] // Allow anonymous access for testing
        public async Task<IActionResult> TestRazorpay()
        {
            try
            {
                // Generate test data
                string txnid = _razorpayService.GenerateTransactionId();
                string amount = "1.00";
                string testId = "123"; // Test ID for testing
                string productinfo = $"TestBooking_{testId}";
                string firstname = "Test User";
                string email = "test@example.com";

                // Try to get actual user's phone number for testing
                string phone = "9999999999"; // Default fallback
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!string.IsNullOrEmpty(userId))
                {
                    var user = await _context.Users.FindAsync(userId);
                    if (user != null && !string.IsNullOrEmpty(user.MobileNumber))
                    {
                        phone = user.MobileNumber;
                        _logger.LogInformation($"TEST: Using user's mobile number from database: {phone}");
                    }
                }

                // Prepare the Razorpay order request
                var orderRequest = _razorpayService.PrepareOrderRequest(txnid, amount, productinfo, firstname, email, phone, testId);

                // Create order with Razorpay
                var (success, orderId, errorMessage) = await _razorpayService.CreateOrderAsync(orderRequest);

                if (success)
                {
                    // Prepare checkout options for Razorpay
                    var checkoutOptions = _razorpayService.PrepareCheckoutOptions(orderId, amount, productinfo, firstname, email, phone, testId);

                    // Create model for view
                    var model = new RazorpayRequestModel
                    {
                        Parameters = orderRequest,
                        OrderId = orderId,
                        CheckoutOptions = checkoutOptions
                    };

                    // Log the test payment request
                    _logger.LogInformation($"TEST Razorpay payment: TxnID={txnid}, OrderId={orderId}, Amount={amount}, Product={productinfo}");

                    // Return the view with the model
                    return View("RazorpayInitiate", model);
                }
                else
                {
                    // Handle error
                    _logger.LogError($"TEST Razorpay payment initiation failed: {errorMessage}");
                    return Json(new {
                        success = false,
                        message = $"Razorpay test failed: {errorMessage}"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing Razorpay integration");
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        private async Task SendPaymentReceiptEmail(string userSapId, int testId, TestBooking booking, Payment payment)
        {
            try
            {
                // Get user details
                var user = await _context.Users.FindAsync(userSapId);
                if (user == null)
                {
                    _logger.LogWarning("Cannot send payment receipt email: User not found with SapId {UserSapId}", userSapId);
                    return;
                }

                // Get test details
                var test = await _context.Tests.FindAsync(testId);
                if (test == null)
                {
                    _logger.LogWarning("Cannot send payment receipt email: Test not found with ID {TestId}", testId);
                    return;
                }

                // Format dates and times
                string bookingDate = booking.BookingDate.HasValue ? booking.BookingDate.Value.ToString("dddd, MMMM d, yyyy") : DateTime.Today.ToString("dddd, MMMM d, yyyy");
                string startTime = booking.StartTime.HasValue ? booking.StartTime.Value.ToString("h:mm tt") : "Anytime";
                string endTime = booking.EndTime.HasValue ? booking.EndTime.Value.ToString("h:mm tt") : "Anytime";

                // Generate a transaction ID using the helper method
                string transactionId = $"TXN-{payment.Id}-{_razorpayService.GenerateTransactionId().Substring(0, 10)}";

                // Send the email
                await _emailService.SendPaymentReceiptEmailAsync(
                    user.Email,
                    user.Username,
                    payment.Amount,
                    test.Title,
                    bookingDate,
                    startTime,
                    endTime,
                    transactionId
                );

                _logger.LogInformation("Payment receipt email sent to {Email} for test {TestId}", user.Email, testId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending payment receipt email for user {UserSapId} and test {TestId}", userSapId, testId);
            }
        }
    }
}
