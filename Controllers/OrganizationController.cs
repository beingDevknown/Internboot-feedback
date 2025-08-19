using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineAssessment.Web.Models;
using OnlineAssessment.Web.Services;
using System.Security.Claims;

namespace OnlineAssessment.Web.Controllers
{
    [Authorize(Roles = "Organization")]
    public class OrganizationController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IOrganizationTokenService _organizationTokenService;
        private readonly ILogger<OrganizationController> _logger;

        public OrganizationController(
            AppDbContext context,
            IOrganizationTokenService organizationTokenService,
            ILogger<OrganizationController> logger)
        {
            _context = context;
            _organizationTokenService = organizationTokenService;
            _logger = logger;
        }

        // View for organization token management
        [HttpGet]
        public async Task<IActionResult> TokenManagement()
        {
            try
            {
                var organizationSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(organizationSapId))
                {
                    return RedirectToAction("Login", "Auth");
                }

                var organization = await _context.Organizations.FindAsync(organizationSapId);
                if (organization == null)
                {
                    return NotFound("Organization not found");
                }

                // Get users registered with this organization's token
                var registeredUsers = await _context.Users
                    .Where(u => u.OrganizationSapId == organizationSapId)
                    .Select(u => new
                    {
                        u.SapId,
                        u.Username,
                        u.Email,
                        u.FirstName,
                        u.LastName,
                        u.Category
                    })
                    .ToListAsync();

                ViewBag.Organization = organization;
                ViewBag.RegisteredUsers = registeredUsers;
                ViewBag.UserCount = registeredUsers.Count;

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading token management page");
                return View("Error");
            }
        }

        // API endpoint to generate organization token
        [HttpPost]
        [Route("Organization/api/generate-token")]
        public async Task<IActionResult> GenerateToken()
        {
            try
            {
                var organizationSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(organizationSapId))
                {
                    return Unauthorized(new { success = false, message = "Organization not authenticated" });
                }

                var token = await _organizationTokenService.GenerateOrganizationTokenAsync(organizationSapId);

                return Ok(new
                {
                    success = true,
                    token = token,
                    message = "Organization token generated successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating organization token");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Failed to generate token. Please try again."
                });
            }
        }

        // API endpoint to regenerate organization token
        [HttpPost]
        [Route("Organization/api/regenerate-token")]
        public async Task<IActionResult> RegenerateToken()
        {
            try
            {
                var organizationSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(organizationSapId))
                {
                    return Unauthorized(new { success = false, message = "Organization not authenticated" });
                }

                var newToken = await _organizationTokenService.RegenerateTokenAsync(organizationSapId);

                return Ok(new
                {
                    success = true,
                    token = newToken,
                    message = "Organization token regenerated successfully. The old token is now invalid."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error regenerating organization token");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Failed to regenerate token. Please try again."
                });
            }
        }

        // API endpoint to deactivate organization token
        [HttpPost]
        [Route("Organization/api/deactivate-token")]
        public async Task<IActionResult> DeactivateToken()
        {
            try
            {
                var organizationSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(organizationSapId))
                {
                    return Unauthorized(new { success = false, message = "Organization not authenticated" });
                }

                var success = await _organizationTokenService.DeactivateTokenAsync(organizationSapId);

                if (success)
                {
                    return Ok(new
                    {
                        success = true,
                        message = "Organization token deactivated successfully. New users cannot register with this token."
                    });
                }
                else
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "Failed to deactivate token."
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deactivating organization token");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Failed to deactivate token. Please try again."
                });
            }
        }

        // API endpoint to get organization statistics
        [HttpGet]
        [Route("Organization/api/stats")]
        public async Task<IActionResult> GetStats()
        {
            try
            {
                var organizationSapId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(organizationSapId))
                {
                    return Unauthorized(new { success = false, message = "Organization not authenticated" });
                }

                var userCount = await _context.Users
                    .CountAsync(u => u.OrganizationSapId == organizationSapId);

                var testCount = await _context.Tests
                    .CountAsync(t => t.CreatedBySapId == organizationSapId);

                var testResultCount = await _context.TestResults
                    .Include(tr => tr.User)
                    .CountAsync(tr => tr.User != null && tr.User.OrganizationSapId == organizationSapId);

                return Ok(new
                {
                    success = true,
                    stats = new
                    {
                        userCount = userCount,
                        testCount = testCount,
                        testResultCount = testResultCount
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting organization stats");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Failed to get statistics. Please try again."
                });
            }
        }
    }
}
