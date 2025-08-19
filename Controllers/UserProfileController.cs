using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using OnlineAssessment.Web.Models;
using System.Security.Claims;

namespace OnlineAssessment.Web.Controllers
{
    [Authorize]
    public class UserProfileController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ILogger<UserProfileController> _logger;

        public UserProfileController(AppDbContext context, ILogger<UserProfileController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        [Route("UserProfile")]
        [Route("UserProfile/Index")]
        public async Task<IActionResult> Index()
        {
            try
            {
                var userSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
                var userName = User.Identity?.Name;
                var userEmail = User.FindFirst(ClaimTypes.Email)?.Value;

                _logger.LogInformation($"UserProfile Index - CALLED! User: {userName}, SapId: {userSapId}, Role: {userRole}, Email: {userEmail}");

                // If SapId is missing but we have email, try to find the user by email
                if (string.IsNullOrEmpty(userSapId) && !string.IsNullOrEmpty(userEmail) && !string.IsNullOrEmpty(userRole))
                {
                    _logger.LogWarning($"SapId claim is missing. Attempting to find user by email: {userEmail}");

                    try
                    {
                        if (userRole == "Candidate")
                        {
                            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == userEmail.ToLower());
                            if (user != null)
                            {
                                userSapId = user.SapId;
                                _logger.LogInformation($"Found user by email. SapId: {userSapId}");
                            }
                        }
                        else if (userRole == "Organization")
                        {
                            var org = await _context.Organizations.FirstOrDefaultAsync(o => o.Email.ToLower() == userEmail.ToLower());
                            if (org != null)
                            {
                                userSapId = org.SapId;
                                _logger.LogInformation($"Found organization by email. SapId: {userSapId}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error finding user by email");
                    }
                }

                if (string.IsNullOrEmpty(userSapId) || string.IsNullOrEmpty(userRole))
                {
                    _logger.LogWarning("User not authenticated or missing claims. Redirecting to login.");
                    return RedirectToAction("Login", "Auth");
                }

                // Handle differently based on role
                if (userRole == "Candidate")
                {
                    var user = await _context.Users.FindAsync(userSapId);
                    if (user == null)
                    {
                        return NotFound();
                    }

                    return View(user);
                }
                else if (userRole == "SpecialUser")
                {
                    var specialUser = await _context.SpecialUsers.FindAsync(userSapId);
                    if (specialUser == null)
                    {
                        return NotFound();
                    }

                    return View("SpecialUserProfile", specialUser);
                }
                else if (userRole == "Organization")
                {
                    // Organizations should use their own profile page, not the user profile page
                    return RedirectToAction("TokenManagement", "Organization");
                }

                return RedirectToAction("Login", "Auth");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user profile");
                return RedirectToAction("Index", "Home");
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetUserInfo()
        {
            try
            {
                var userSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
                var userName = User.Identity?.Name;
                var userEmail = User.FindFirst(ClaimTypes.Email)?.Value;

                _logger.LogInformation($"GetUserInfo - User: {userName}, SapId: {userSapId}, Role: {userRole}, Email: {userEmail}, IsAuthenticated: {User.Identity?.IsAuthenticated}");

                // If SapId is missing but we have email, try to find the user by email
                if (string.IsNullOrEmpty(userSapId) && !string.IsNullOrEmpty(userEmail) && !string.IsNullOrEmpty(userRole))
                {
                    _logger.LogWarning($"GetUserInfo - SapId claim is missing. Attempting to find user by email: {userEmail}");

                    try
                    {
                        if (userRole == "Candidate")
                        {
                            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == userEmail.ToLower());
                            if (user != null)
                            {
                                userSapId = user.SapId;
                                _logger.LogInformation($"GetUserInfo - Found user by email. SapId: {userSapId}");
                            }
                        }
                        else if (userRole == "SpecialUser")
                        {
                            var specialUser = await _context.SpecialUsers.FirstOrDefaultAsync(s => s.Email.ToLower() == userEmail.ToLower());
                            if (specialUser != null)
                            {
                                userSapId = specialUser.UsersSapId;
                                _logger.LogInformation($"GetUserInfo - Found special user by email. SapId: {userSapId}");
                            }
                        }
                        else if (userRole == "Organization")
                        {
                            var org = await _context.Organizations.FirstOrDefaultAsync(o => o.Email.ToLower() == userEmail.ToLower());
                            if (org != null)
                            {
                                userSapId = org.SapId;
                                _logger.LogInformation($"GetUserInfo - Found organization by email. SapId: {userSapId}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "GetUserInfo - Error finding user by email");
                    }
                }

                if (string.IsNullOrEmpty(userSapId) || string.IsNullOrEmpty(userRole))
                {
                    _logger.LogWarning("GetUserInfo - User not authenticated or missing claims");
                    return Json(new { success = false, message = "User not authenticated" });
                }

                // Handle differently based on role
                if (userRole == "Candidate")
                {
                    var user = await _context.Users.FindAsync(userSapId);
                    if (user == null)
                    {
                        return Json(new { success = false, message = "User not found" });
                    }

                    // For candidate users, include candidate-specific fields
                    return Json(new
                    {
                        success = true,
                        user = new
                        {
                            username = user.Username,
                            email = user.Email,
                            firstName = user.FirstName,
                            lastName = user.LastName,
                            photoUrl = user.PhotoUrl,
                            role = user.Role.ToString(),
                            keySkills = user.KeySkills,
                            employment = user.Employment,
                            education = user.Education,
                            category = user.Category
                        }
                    });
                }
                else if (userRole == "SpecialUser")
                {
                    var specialUser = await _context.SpecialUsers.FindAsync(userSapId);
                    if (specialUser == null)
                    {
                        return Json(new { success = false, message = "Special user not found" });
                    }

                    // For special users, return special user data
                    return Json(new
                    {
                        success = true,
                        user = new
                        {
                            username = specialUser.Username,
                            email = specialUser.Email,
                            fullName = specialUser.FullName,
                            role = "SpecialUser",
                            category = specialUser.Category,
                            education = specialUser.Education,
                            employment = specialUser.Employment,
                            mobileNumber = specialUser.MobileNumber,
                            description = specialUser.Description,
                            isActive = specialUser.IsActive
                        }
                    });
                }
                else if (userRole == "Organization")
                {
                    var organization = await _context.Organizations.FindAsync(userSapId);
                    if (organization == null)
                    {
                        return Json(new { success = false, message = "Organization not found" });
                    }

                    // For organizations, return organization data
                    return Json(new
                    {
                        success = true,
                        user = new
                        {
                            username = organization.Username,
                            email = organization.Email,
                            role = "Organization"
                        },
                        organization = new
                        {
                            name = organization.Name,
                            contactPerson = organization.ContactPerson,
                            email = organization.Email,
                            phoneNumber = organization.PhoneNumber,
                            address = organization.Address,
                            website = organization.Website,
                            description = organization.Description,
                            logoUrl = organization.LogoUrl
                        }
                    });
                }

                // Fallback response
                return Json(new { success = false, message = "Invalid user role" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user info");
                return Json(new { success = false, message = "An error occurred" });
            }
        }
    }
}
