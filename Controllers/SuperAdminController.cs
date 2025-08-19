using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineAssessment.Web.Models;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace OnlineAssessment.Web.Controllers
{
    public class SuperAdminController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;
        private readonly ILogger<SuperAdminController> _logger;

        public SuperAdminController(AppDbContext context, IConfiguration config, ILogger<SuperAdminController> logger)
        {
            _context = context;
            _config = config;
            _logger = logger;
        }

        // Special endpoint for super admin login
        [HttpGet]
        [Route("SuperAdmin/Login")]
        public IActionResult Login()
        {
            return View();
        }

        // Dashboard for super admin
        [HttpGet]
        [Route("SuperAdmin/Dashboard")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Dashboard()
        {
            try
            {
                // Get statistics for the dashboard
                var stats = new
                {
                    TotalOrganizations = await _context.Organizations.CountAsync(o => !o.IsSuperOrganization),
                    TotalTests = await _context.Tests.CountAsync(),
                    TotalUsers = await _context.Users.CountAsync(),
                    RecentOrganizations = await _context.Organizations
                        .Where(o => !o.IsSuperOrganization)
                        .OrderByDescending(o => o.CreatedAt)
                        .Take(10)
                        .ToListAsync()
                };

                return View(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading super admin dashboard");
                return View("Error");
            }
        }

        // View all organizations
        [HttpGet]
        [Route("SuperAdmin/Organizations")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Organizations()
        {
            try
            {
                var organizations = await _context.Organizations
                    .Where(o => !o.IsSuperOrganization)
                    .OrderByDescending(o => o.CreatedAt)
                    .ToListAsync();

                return View(organizations);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading organizations list");
                return View("Error");
            }
        }

        // View all users
        [HttpGet]
        [Route("SuperAdmin/Users")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Users()
        {
            try
            {
                var users = await _context.Users
                    .OrderByDescending(u => u.SapId)
                    .ToListAsync();

                return View(users);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading users list");
                return View("Error");
            }
        }

        // User details
        [HttpGet]
        [Route("SuperAdmin/UserDetails/{sapId}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UserDetails(string sapId)
        {
            try
            {
                var user = await _context.Users.FindAsync(sapId);
                if (user == null)
                {
                    return NotFound();
                }

                // CRITICAL FIX: Get test results for this user by SAP ID for proper data isolation
                var testResults = await _context.TestResults
                    .Where(tr => tr.UserSapId == user.SapId)
                    .Include(tr => tr.Test)
                    .OrderByDescending(tr => tr.SubmittedAt)
                    .ToListAsync();

                ViewBag.TestResults = testResults;
                ViewBag.TestCount = testResults.Count;

                return View(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading user details");
                return View("Error");
            }
        }

        // Add organization form
        [HttpGet]
        [Route("SuperAdmin/AddOrganization")]
        [Authorize(Roles = "Admin")]
        public IActionResult AddOrganization()
        {
            return View();
        }

        // Add organization action
        [HttpPost]
        [Route("SuperAdmin/AddOrganization")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AddOrganization(Organization organization, string password)
        {
            try
            {
                if (string.IsNullOrEmpty(password))
                {
                    ModelState.AddModelError("Password", "Password is required");
                    return View(organization);
                }

                // Check if organization with same email already exists
                var existingOrg = await _context.Organizations
                    .FirstOrDefaultAsync(o => o.Email == organization.Email);

                if (existingOrg != null)
                {
                    ModelState.AddModelError("Email", "An organization with this email already exists");
                    return View(organization);
                }

                // Hash the password
                organization.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
                organization.CreatedAt = DateTime.UtcNow;
                organization.Username = organization.Name; // Set username same as name
                organization.Role = "Organization";
                organization.IsSuperOrganization = false;

                // Set default logo if none is provided
                if (string.IsNullOrWhiteSpace(organization.LogoUrl))
                {
                    organization.LogoUrl = "/images/default-profile.svg";
                }

                _context.Organizations.Add(organization);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Organization added successfully";
                return RedirectToAction("Organizations");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding organization");
                ModelState.AddModelError("", "An error occurred while adding the organization");
                return View(organization);
            }
        }

        // Delete organization
        [HttpPost]
        [Route("SuperAdmin/DeleteOrganization/{sapId}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteOrganization(string sapId)
        {
            try
            {
                var organization = await _context.Organizations.FindAsync(sapId);
                if (organization == null)
                {
                    return NotFound();
                }

                // Delete related data
                var tests = await _context.Tests.Where(t => t.CreatedBySapId == sapId).ToListAsync();
                _context.Tests.RemoveRange(tests);

                _context.Organizations.Remove(organization);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Organization deleted successfully";
                return RedirectToAction("Organizations");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting organization");
                TempData["ErrorMessage"] = "An error occurred while deleting the organization";
                return RedirectToAction("Organizations");
            }
        }

        // Organization details
        [HttpGet]
        [Route("SuperAdmin/OrganizationDetails/{sapId}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> OrganizationDetails(string sapId)
        {
            try
            {
                var organization = await _context.Organizations.FindAsync(sapId);
                if (organization == null)
                {
                    return NotFound();
                }

                // Get tests created by this organization
                var tests = await _context.Tests
                    .Where(t => t.CreatedBySapId == sapId)
                    .OrderByDescending(t => t.CreatedAt)
                    .ToListAsync();

                ViewBag.Tests = tests;
                ViewBag.TestCount = tests.Count;

                return View(organization);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading organization details");
                return View("Error");
            }
        }

        // API endpoint for super admin login
        [HttpPost]
        [Route("super-admin/login")]
        public async Task<IActionResult> LoginApi([FromBody] LoginRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { message = "Email and password are required." });
            }

            try
            {
                // Check if the username is 'Admin' and password is 'admin123'
                if (request.Email != "admin@system.com")
                {
                    return BadRequest(new { message = "Invalid username or password." });
                }

                // Find the super organization
                var superOrg = await _context.Organizations.FirstOrDefaultAsync(o => o.IsSuperOrganization);
                if (superOrg == null)
                {
                    return BadRequest(new { message = "Super organization not found. Please contact system administrator." });
                }

                // Verify password is 'admin123'
                bool isPasswordValid = request.Password == "admin123" || BCrypt.Net.BCrypt.Verify(request.Password, superOrg.PasswordHash);
                if (!isPasswordValid)
                {
                    return BadRequest(new { message = "Invalid username or password." });
                }

                // Generate JWT token
                var jwtSecret = _config["JWT:Secret"];
                if (string.IsNullOrEmpty(jwtSecret) || Encoding.UTF8.GetBytes(jwtSecret).Length < 32)
                {
                    return StatusCode(500, new { message = "JWT Secret is invalid or too short." });
                }

                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
                var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

                var claims = new[]
                {
                    new Claim(ClaimTypes.Name, "Admin"),
                    new Claim(ClaimTypes.Role, "SuperAdmin"),
                    new Claim(ClaimTypes.NameIdentifier, superOrg.SapId),
                    new Claim(ClaimTypes.Email, superOrg.Email),
                    new Claim("OrganizationName", superOrg.Name)
                };

                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(claims),
                    Expires = DateTime.UtcNow.AddHours(1),
                    Issuer = _config["JWT:Issuer"],
                    Audience = _config["JWT:Audience"],
                    SigningCredentials = credentials
                };

                var tokenHandler = new JwtSecurityTokenHandler();
                var token = tokenHandler.CreateToken(tokenDescriptor);
                var tokenString = tokenHandler.WriteToken(token);

                // Set authentication cookie
                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
                };

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity),
                    authProperties);

                // Update last login time
                superOrg.LastLoginAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                return Ok(new { token = tokenString, redirectUrl = "/super-admin/dashboard" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during super admin login");
                return StatusCode(500, new { message = "An error occurred during login." });
            }
        }

        // Change password form
        [HttpGet]
        [Route("super-admin/change-password")]
        [Authorize(Roles = "SuperAdmin")]
        public IActionResult ChangePassword()
        {
            return View();
        }

        // Change password action
        [HttpPost]
        [Route("super-admin/change-password")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
        {
            if (string.IsNullOrEmpty(currentPassword) || string.IsNullOrEmpty(newPassword) || string.IsNullOrEmpty(confirmPassword))
            {
                ModelState.AddModelError("", "All fields are required");
                return View();
            }

            if (newPassword != confirmPassword)
            {
                ModelState.AddModelError("", "New password and confirm password do not match");
                return View();
            }

            try
            {
                // Get super organization
                var superOrg = await _context.Organizations.FirstOrDefaultAsync(o => o.IsSuperOrganization);
                if (superOrg == null)
                {
                    return RedirectToAction("Login");
                }

                // Verify current password
                bool isPasswordValid = BCrypt.Net.BCrypt.Verify(currentPassword, superOrg.PasswordHash);
                if (!isPasswordValid)
                {
                    ModelState.AddModelError("", "Current password is incorrect");
                    return View();
                }

                // Update password
                superOrg.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Password changed successfully";
                return RedirectToAction("Dashboard");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error changing password");
                ModelState.AddModelError("", "An error occurred while changing the password");
                return View();
            }
        }
    }
}
