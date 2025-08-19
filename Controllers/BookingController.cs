using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineAssessment.Web.Models;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using System;

namespace OnlineAssessment.Web.Controllers
{
    public class BookingController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILogger<BookingController> _logger;

        public BookingController(AppDbContext context, ILogger<BookingController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // API endpoint to book a test slot - DISABLED, use TestController.ProcessBooking instead
        [HttpPost]
        [Route("Booking/BookSlot/{id}")]
        [Authorize(Roles = "Admin")] // Restrict to admin only to prevent accidental use
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BookSlot(int id, string selectedDate, int selectedSlot)
        {
            try
            {
                _logger.LogInformation($"BookSlot called for test ID: {id}");

                // Check if user is authenticated
                if (!User.Identity.IsAuthenticated)
                {
                    _logger.LogWarning("User is not authenticated");
                    return RedirectToAction("Login", "Account", new { returnUrl = $"/Test" });
                }

                // Check if user is a candidate
                if (!User.IsInRole("Candidate"))
                {
                    _logger.LogWarning($"User is not a candidate. Roles: {string.Join(", ", User.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value))}");
                    return RedirectToAction("Index", "Test", new { error = "Only candidates can book test slots" });
                }

                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                _logger.LogInformation($"User ID from claims: {userId}");

                if (string.IsNullOrEmpty(userId))
                {
                    return RedirectToAction("Login", "Account");
                }

                var candidateId = int.Parse(userId);
                _logger.LogInformation($"Parsed candidate ID: {candidateId}");

                var test = await _context.Tests.FindAsync(id);
                _logger.LogInformation($"Test found: {test != null}");

                if (test == null)
                {
                    return RedirectToAction("Index", "Test", new { error = "Test not found" });
                }

                _logger.LogInformation($"Test details - Title: {test.Title}, Domain: {test.Domain}, StartTime: {test.ScheduledStartTime}, EndTime: {test.ScheduledEndTime}, CurrentUsers: {test.CurrentUserCount}, MaxUsers: {test.MaxUsersPerSlot}");

                // Allow multiple bookings for the same test
                // Only log existing bookings for informational purposes
                var existingBooking = await _context.TestBookings
                    .FirstOrDefaultAsync(tb => tb.TestId == id && tb.UserSapId == userId && tb.Status != "Failed");
                _logger.LogInformation($"Existing booking found: {existingBooking != null}, Status: {existingBooking?.Status ?? "N/A"}");

                if (existingBooking != null)
                {
                    _logger.LogInformation($"User already has a booking for test {id} with status {existingBooking.Status}, but will be allowed to book again");
                }

                // Check if there are any failed bookings for this test
                var failedBooking = await _context.TestBookings
                    .FirstOrDefaultAsync(tb => tb.TestId == id && tb.UserSapId == userId && tb.Status == "Failed");
                if (failedBooking != null)
                {
                    _logger.LogInformation($"Found failed booking (ID: {failedBooking.Id}) for this test. User will be allowed to book again.");
                }

                // Check if the user has already booked any other slot
                var hasAnyBooking = await _context.TestBookings
                    .AnyAsync(tb => tb.UserSapId == userId);
                _logger.LogInformation($"User has other bookings: {hasAnyBooking}");

                // Allow users to book multiple tests without payment
                if (hasAnyBooking)
                {
                    _logger.LogInformation("User has other bookings but will be allowed to book this test without payment");
                }

                // Check if the test is full
                if (test.CurrentUserCount >= test.MaxUsersPerSlot)
                {
                    _logger.LogInformation($"Test is full: {test.CurrentUserCount}/{test.MaxUsersPerSlot}");
                    return RedirectToAction("Index", "Test", new { error = "This test is full. Please contact the organization for assistance." });
                }

                // Create a new booking
                // Parse the selected date from the form; fallback to today's date if parsing fails
                _logger.LogInformation($"[DEBUG] selectedDate from form: '{selectedDate}'");
                DateTime bookingDate;
                if (!DateTime.TryParse(selectedDate, out bookingDate))
                {
                    bookingDate = Utilities.TimeZoneHelper.GetCurrentIstTime().Date;
                }
                _logger.LogInformation($"[DEBUG] Parsed bookingDate to save: {bookingDate:yyyy-MM-dd}");

                // Map slot number to time range
                TimeSpan startTime, endTime;
                switch (selectedSlot)
                {
                    case 1:
                        startTime = new TimeSpan(9, 0, 0); endTime = new TimeSpan(11, 0, 0); break;
                    case 2:
                        startTime = new TimeSpan(12, 0, 0); endTime = new TimeSpan(14, 0, 0); break;
                    case 3:
                        startTime = new TimeSpan(15, 0, 0); endTime = new TimeSpan(17, 0, 0); break;
                    case 4:
                        startTime = new TimeSpan(18, 0, 0); endTime = new TimeSpan(20, 0, 0); break;
                    default:
                        startTime = new TimeSpan(12, 0, 0); endTime = new TimeSpan(14, 0, 0); break;
                }
                var startDateTime = bookingDate.Date + startTime;
                var endDateTime = bookingDate.Date + endTime;

                // Fetch user SAP ID
                var user = await _context.Users.FindAsync(candidateId);
                string userSapId = user?.SapId ?? string.Empty;

                var booking = new TestBooking
                {
                    TestId = id,
                    UserSapId = userSapId,
                    BookedAt = Utilities.TimeZoneHelper.GetCurrentIstTime(),
                    BookingDate = bookingDate,
                    StartTime = Utilities.TimeZoneHelper.ToIst(startDateTime),
                    EndTime = Utilities.TimeZoneHelper.ToIst(endDateTime),
                    SlotNumber = selectedSlot
                };
                _logger.LogInformation($"Created new booking: TestId={booking.TestId}, UserSapId={booking.UserSapId}");

                _context.TestBookings.Add(booking);
                _logger.LogInformation("Added booking to context");

                // Increment the user count for this test
                test.CurrentUserCount++;
                _logger.LogInformation($"Incremented user count to {test.CurrentUserCount}");

                await _context.SaveChangesAsync();
                _logger.LogInformation("Changes saved to database");

                // Set a flag to indicate the test is ready to be taken
                TempData["TestBooked"] = true;
                TempData["BookedTestId"] = id;

                // Redirect to the test page with a success message
                return RedirectToAction("ScheduledTest", "Test", new { id = id, message = "Slot booked successfully! You can now access the test during the scheduled time." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in BookSlot action: {ex.Message}", ex);
                var innerMsg = ex.InnerException != null ? ex.InnerException.Message : string.Empty;
                var innerInnerMsg = (ex.InnerException != null && ex.InnerException.InnerException != null) ? ex.InnerException.InnerException.Message : string.Empty;
                var errorMsg = $"An error occurred while booking the slot: {ex.Message} {innerMsg} {innerInnerMsg}";
                return RedirectToAction("Index", "Test", new { error = errorMsg });
            }
        }
    }
}
