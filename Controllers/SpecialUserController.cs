using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnlineAssessment.Web.Models.DTOs;
using OnlineAssessment.Web.Services;
using System.Security.Claims;

namespace OnlineAssessment.Web.Controllers
{
    [Authorize(Roles = "Organization,SpecialUser")]
    public class SpecialUserController : Controller
    {
        private readonly ISpecialUserService _specialUserService;
        private readonly ILogger<SpecialUserController> _logger;

        public SpecialUserController(ISpecialUserService specialUserService, ILogger<SpecialUserController> logger)
        {
            _specialUserService = specialUserService;
            _logger = logger;
        }

        // GET: SpecialUser/Index
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            try
            {
                var userSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

                if (string.IsNullOrEmpty(userSapId))
                {
                    return RedirectToAction("Login", "Auth");
                }

                if (userRole == "SpecialUser")
                {
                    // Special users should see their own profile, redirect to UserProfile
                    return RedirectToAction("Index", "UserProfile");
                }
                else if (userRole == "Organization")
                {
                    // Organizations can see all their special users
                    var specialUsers = await _specialUserService.GetSpecialUsersAsync(userSapId);
                    return View(specialUsers);
                }
                else
                {
                    return RedirectToAction("Login", "Auth");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading special users");
                TempData["ErrorMessage"] = "An error occurred while loading special users.";
                return View(new List<SpecialUserResponse>());
            }
        }

        // GET: SpecialUser/Create
        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        // POST: SpecialUser/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateSpecialUserRequest request)
        {
            _logger.LogInformation("Create special user request received for email: {Email}", request.Email);

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Model state is invalid for special user creation");
                foreach (var error in ModelState.Values.SelectMany(v => v.Errors))
                {
                    _logger.LogWarning("Model validation error: {Error}", error.ErrorMessage);
                }
                return View(request);
            }

            try
            {
                var organizationSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var createdBy = User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown";

                _logger.LogInformation("Organization SAP ID: {OrganizationSapId}, Created by: {CreatedBy}", organizationSapId, createdBy);

                if (string.IsNullOrEmpty(organizationSapId))
                {
                    _logger.LogWarning("Organization SAP ID is null or empty, redirecting to login");
                    return RedirectToAction("Login", "Auth");
                }

                var (success, message, user) = await _specialUserService.CreateSpecialUserAsync(
                    organizationSapId, request, createdBy);

                if (success)
                {
                    _logger.LogInformation("Special user created successfully: {Email}", request.Email);
                    TempData["SuccessMessage"] = message;
                    return RedirectToAction("Index");
                }
                else
                {
                    _logger.LogWarning("Failed to create special user: {Message}", message);
                    ModelState.AddModelError("", message);
                    return View(request);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating special user for email: {Email}", request.Email);
                ModelState.AddModelError("", "An error occurred while creating the special user.");
                return View(request);
            }
        }

        // GET: SpecialUser/Edit/5
        [HttpGet]
        public async Task<IActionResult> Edit(string id)
        {
            try
            {
                var organizationSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(organizationSapId))
                {
                    return RedirectToAction("Login", "Auth");
                }

                var specialUser = await _specialUserService.GetSpecialUserAsync(id, organizationSapId);
                if (specialUser == null)
                {
                    return NotFound();
                }

                var updateRequest = new UpdateSpecialUserRequest
                {
                    Username = specialUser.Username,
                    FullName = specialUser.FullName,
                    MobileNumber = specialUser.MobileNumber,
                    Education = specialUser.Education,
                    Employment = specialUser.Employment,
                    Category = specialUser.Category,
                    Description = specialUser.Description,
                    IsActive = specialUser.IsActive
                };

                ViewBag.SpecialUserSapId = id;
                ViewBag.Email = specialUser.Email;
                return View(updateRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading special user for edit");
                return NotFound();
            }
        }

        // POST: SpecialUser/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, UpdateSpecialUserRequest request)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.SpecialUserSapId = id;
                return View(request);
            }

            try
            {
                var organizationSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(organizationSapId))
                {
                    return RedirectToAction("Login", "Auth");
                }

                var (success, message) = await _specialUserService.UpdateSpecialUserAsync(id, organizationSapId, request);

                if (success)
                {
                    TempData["SuccessMessage"] = message;
                    return RedirectToAction("Index");
                }
                else
                {
                    ModelState.AddModelError("", message);
                    ViewBag.SpecialUserSapId = id;
                    return View(request);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating special user");
                ModelState.AddModelError("", "An error occurred while updating the special user.");
                ViewBag.SpecialUserSapId = id;
                return View(request);
            }
        }

        // GET: SpecialUser/ChangePassword/5
        [HttpGet]
        public async Task<IActionResult> ChangePassword(string id)
        {
            try
            {
                var organizationSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(organizationSapId))
                {
                    return RedirectToAction("Login", "Auth");
                }

                var specialUser = await _specialUserService.GetSpecialUserAsync(id, organizationSapId);
                if (specialUser == null)
                {
                    return NotFound();
                }

                ViewBag.SpecialUserSapId = id;
                ViewBag.Username = specialUser.Username;
                ViewBag.Email = specialUser.Email;
                return View(new ChangeSpecialUserPasswordRequest());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading change password page");
                return NotFound();
            }
        }

        // POST: SpecialUser/ChangePassword/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(string id, ChangeSpecialUserPasswordRequest request)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.SpecialUserSapId = id;
                return View(request);
            }

            try
            {
                var organizationSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(organizationSapId))
                {
                    return RedirectToAction("Login", "Auth");
                }

                var (success, message) = await _specialUserService.ChangePasswordAsync(id, organizationSapId, request.NewPassword);

                if (success)
                {
                    TempData["SuccessMessage"] = message;
                    return RedirectToAction("Index");
                }
                else
                {
                    ModelState.AddModelError("", message);
                    ViewBag.SpecialUserSapId = id;
                    return View(request);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error changing special user password");
                ModelState.AddModelError("", "An error occurred while changing the password.");
                ViewBag.SpecialUserSapId = id;
                return View(request);
            }
        }

        // POST: SpecialUser/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                var organizationSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(organizationSapId))
                {
                    return RedirectToAction("Login", "Auth");
                }

                var (success, message) = await _specialUserService.DeleteSpecialUserAsync(id, organizationSapId);

                if (success)
                {
                    TempData["SuccessMessage"] = message;
                }
                else
                {
                    TempData["ErrorMessage"] = message;
                }

                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting special user");
                TempData["ErrorMessage"] = "An error occurred while deleting the special user.";
                return RedirectToAction("Index");
            }
        }

        // API endpoint to check if email is available
        [HttpGet]
        [Route("SpecialUser/api/check-email")]
        public async Task<IActionResult> CheckEmailAvailability(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return BadRequest(new { available = false, message = "Email is required" });
            }

            try
            {
                var isAvailable = await _specialUserService.IsEmailAvailableAsync(email);
                return Ok(new { available = isAvailable });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking email availability");
                return StatusCode(500, new { available = false, message = "Error checking availability" });
            }
        }

        // API endpoint to check if username is available
        [HttpGet]
        [Route("SpecialUser/api/check-username")]
        public async Task<IActionResult> CheckUsernameAvailability(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return BadRequest(new { available = false, message = "Username is required" });
            }

            try
            {
                var isAvailable = await _specialUserService.IsUsernameAvailableAsync(username);
                return Ok(new { available = isAvailable });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking username availability");
                return StatusCode(500, new { available = false, message = "Error checking availability" });
            }
        }
    }
}
