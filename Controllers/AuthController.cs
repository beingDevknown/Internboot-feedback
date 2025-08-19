using BCrypt.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using OnlineAssessment.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using OnlineAssessment.Web.Services;
using OnlineAssessment.Web.Models.DTOs;

namespace OnlineAssessment.Web.Controllers
{
    public class AuthController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;
        private readonly IOtpService _otpService;
        private readonly IEmailService _emailService;
        private readonly IRateLimitingService _rateLimitingService;
        private readonly IPasswordResetService _passwordResetService;
        private readonly ISapIdGeneratorService _sapIdGeneratorService;
        private readonly ISpecialUserService _specialUserService;
        private readonly ILogger<AuthController> _logger;
        private readonly IOrganizationTokenService _organizationTokenService;

        public AuthController(AppDbContext context, IConfiguration config, IOtpService otpService,
            IEmailService emailService, IRateLimitingService rateLimitingService, IPasswordResetService passwordResetService,
            ISapIdGeneratorService sapIdGeneratorService, ISpecialUserService specialUserService, ILogger<AuthController> logger,
            IOrganizationTokenService organizationTokenService)
        {
            _context = context;
            _config = config;
            _otpService = otpService;
            _emailService = emailService;
            _rateLimitingService = rateLimitingService;
            _passwordResetService = passwordResetService;
            _sapIdGeneratorService = sapIdGeneratorService;
            _specialUserService = specialUserService;
            _logger = logger;
            _organizationTokenService = organizationTokenService;
        }

        // View action for registration page (candidates only)
        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        // View action for organization registration page
        [HttpGet]
        public IActionResult OrganizationRegister()
        {
            return View();
        }

        // View action for login page - now uses password-based login
        [HttpGet]
        public IActionResult Login()
        {
            // If user is already authenticated, redirect to Test page
            if (User.Identity?.IsAuthenticated == true)
            {
                _logger.LogInformation($"User {User.Identity.Name} is already authenticated. Redirecting to Test page.");
                return RedirectToAction("Index", "Test");
            }

            return View();
        }

        // View action for organization login page
        [HttpGet]
        public IActionResult OrganizationLogin()
        {
            return View();
        }

        // Access denied page
        [HttpGet]
        public IActionResult AccessDenied()
        {
            ViewBag.Message = "You don't have permission to access this resource.";
            ViewBag.IsAuthenticated = User.Identity?.IsAuthenticated ?? false;
            ViewBag.UserRole = User.FindFirst(ClaimTypes.Role)?.Value ?? "None";
            ViewBag.UserName = User.Identity?.Name ?? "Anonymous";
            ViewBag.UserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "None";

            return View();
        }

        // Debug authentication status
        [HttpGet]
        public IActionResult AuthStatus()
        {
            var authInfo = new
            {
                IsAuthenticated = User.Identity?.IsAuthenticated ?? false,
                UserName = User.Identity?.Name ?? "Anonymous",
                Role = User.FindFirst(ClaimTypes.Role)?.Value ?? "None",
                UserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "None",
                Email = User.FindFirst(ClaimTypes.Email)?.Value ?? "None",
                Claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList()
            };

            return Json(authInfo);
        }

        // View action for OTP login page
        [HttpGet]
        public IActionResult OtpLogin()
        {
            return View();
        }

        // Test email view
        [HttpGet]
        [Route("Auth/test-email-page")]
        public IActionResult TestEmailPage()
        {
            return View("TestEmail");
        }

        // Test endpoint for email sending
        [HttpGet]
        [Route("Auth/test-email")]
        public async Task<IActionResult> TestEmail(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                return BadRequest("Email is required");
            }

            try
            {
                // Generate a test OTP
                string testOtp = "123456";

                // Send test email
                await _emailService.SendOtpEmailAsync(email, testOtp);

                return Ok($"Test email sent to {email} with OTP: {testOtp}");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Failed to send email: {ex.Message}");
            }
        }

        // API endpoint for registration
        [HttpPost]
        [Route("Auth/api/register")]
        public async Task<IActionResult> RegisterApi([FromBody] RegisterRequest request)
        {
            return await RegisterUser(request);
        }

        // API endpoint for registration with file upload
        [HttpPost]
        [Route("Auth/api/register-with-file")]
        public async Task<IActionResult> RegisterWithFile([FromForm] RegisterRequest request, IFormFile? profilePicture)
        {
            try
            {
                // Handle profile picture upload if provided
                if (profilePicture != null && profilePicture.Length > 0)
                {
                    // Check file size - limit to 5MB
                    if (profilePicture.Length > 5 * 1024 * 1024)
                    {
                        return BadRequest(new { message = "Profile picture size must be less than 5MB." });
                    }

                    // Check file type
                    var allowedTypes = new[] { "image/jpeg", "image/png", "image/gif" };
                    if (!allowedTypes.Contains(profilePicture.ContentType.ToLower()))
                    {
                        return BadRequest(new { message = "Only JPEG, PNG, and GIF images are allowed." });
                    }

                    try
                    {
                        // Save the file to wwwroot/uploads/profiles
                        var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "profiles");

                        // Create directory if it doesn't exist
                        if (!Directory.Exists(uploadsFolder))
                        {
                            Directory.CreateDirectory(uploadsFolder);
                        }

                        // Generate unique filename
                        var uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(profilePicture.FileName);
                        var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                        using (var fileStream = new FileStream(filePath, FileMode.Create))
                        {
                            await profilePicture.CopyToAsync(fileStream);
                        }

                        // Set the profile picture URL
                        request.ProfilePicture = "/uploads/profiles/" + uniqueFileName;
                    }
                    catch (Exception ex)
                    {
                        // Log the error but continue with registration with default profile picture
                        Console.Error.WriteLine($"Profile picture upload error: {ex.Message}");
                        Console.Error.WriteLine($"Stack Trace: {ex.StackTrace}");

                        // Set default profile picture instead of null
                        request.ProfilePicture = "/images/default-profile.svg";
                    }
                }
                else
                {
                    // If no profile picture is provided, explicitly set the default image
                    request.ProfilePicture = "/images/default-profile.svg";
                }

                return await RegisterUser(request);
            }
            catch (Exception ex)
            {
                // Log the error
                Console.Error.WriteLine($"Registration with file error: {ex.Message}");
                Console.Error.WriteLine($"Stack Trace: {ex.StackTrace}");

                // Return a proper JSON error response
                return StatusCode(500, new { message = "An error occurred during registration: " + ex.Message });
            }
        }

        // Common method for user registration
        private async Task<IActionResult> RegisterUser(RegisterRequest request)
        {
            try
            {
                if (request == null ||
                    string.IsNullOrWhiteSpace(request.Username) ||
                    string.IsNullOrWhiteSpace(request.Email) ||
                    string.IsNullOrWhiteSpace(request.FirstName) ||
                    string.IsNullOrWhiteSpace(request.LastName) ||
                    string.IsNullOrWhiteSpace(request.MobileNumber) ||
                    string.IsNullOrWhiteSpace(request.KeySkills) ||
                    string.IsNullOrWhiteSpace(request.Employment) ||
                    string.IsNullOrWhiteSpace(request.Education) ||
                    string.IsNullOrWhiteSpace(request.InternshipDuration) ||
                    string.IsNullOrWhiteSpace(request.Category))
                {
                    return BadRequest(new { success = false, message = "All fields except profile picture are mandatory. Please fill in all required information." });
                }

                // Check if username already exists in Users table
                if (await _context.Users.AnyAsync(u => u.Username.ToLower() == request.Username.ToLower()))
                {
                    return BadRequest(new { success = false, message = "Username already exists. Please choose a different one." });
                }

                // Check if username already exists in Organizations table
                if (await _context.Organizations.AnyAsync(o => o.Username.ToLower() == request.Username.ToLower()))
                {
                    return BadRequest(new { success = false, message = "Username already exists. Please choose a different one." });
                }

                // Check if email already exists in Users table
                if (await _context.Users.AnyAsync(u => u.Email.ToLower() == request.Email.ToLower()))
                {
                    return BadRequest(new { success = false, message = "Email address already in use. Please use a different email." });
                }

                // Check if email already exists in Organizations table
                if (await _context.Organizations.AnyAsync(o => o.Email.ToLower() == request.Email.ToLower()))
                {
                    return BadRequest(new { success = false, message = "Email address already in use. Please use a different email." });
                }

                // Check if phone number already exists in Users table
                if (await _context.Users.AnyAsync(u => u.MobileNumber == request.MobileNumber))
                {
                    return BadRequest(new { success = false, message = "Phone number already in use. Please use a different phone number." });
                }

                // Check if phone number already exists in Organizations table
                if (await _context.Organizations.AnyAsync(o => o.PhoneNumber == request.MobileNumber))
                {
                    return BadRequest(new { success = false, message = "Phone number already in use. Please use a different phone number." });
                }
            }
            catch (Exception ex)
            {
                // Log the exception
                Console.WriteLine($"Error during registration validation: {ex.Message}");
                return StatusCode(500, new { success = false, message = "An error occurred during registration. Please try again later." });
            }

            // Validate password
            if (string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { success = false, message = "Password is required. Please provide a password." });
            }

            // Force role to be Candidate - users can only be candidates
            request.Role = "Candidate";

            // Validate role and convert to Enum
            if (!Enum.TryParse<UserRole>(request.Role, true, out var userRole))
            {
                return BadRequest(new { message = "Invalid role provided. Only Candidate role is allowed for user registration." });
            }

            // Hash the password securely
            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);

            // Set default profile picture if none is provided
            if (string.IsNullOrWhiteSpace(request.ProfilePicture))
            {
                request.ProfilePicture = "/images/default-profile.svg";
            }

            // Parse enums if provided
            EmploymentStatus? employment = null;
            if (!string.IsNullOrWhiteSpace(request.Employment) &&
                Enum.TryParse<EmploymentStatus>(request.Employment, true, out var employmentStatus))
            {
                employment = employmentStatus;
            }

            EducationLevel? education = null;
            if (!string.IsNullOrWhiteSpace(request.Education) &&
                Enum.TryParse<EducationLevel>(request.Education, true, out var educationLevel))
            {
                education = educationLevel;
            }

            // Handle organization token for candidates
            Organization? targetOrganization = null;

            if (!string.IsNullOrWhiteSpace(request.OrganizationToken))
            {
                // Validate the organization token
                targetOrganization = await _organizationTokenService.ValidateOrganizationTokenAsync(request.OrganizationToken);
                if (targetOrganization == null)
                {
                    return BadRequest(new { success = false, message = "Invalid organization token. Please check the token and try again." });
                }
            }
            else
            {
                // No token provided, assign to default organization
                targetOrganization = await _organizationTokenService.GetOrCreateDefaultOrganizationAsync();
            }

            // Generate a unique SAP ID for the user
            string sapId = await _sapIdGeneratorService.GenerateUniqueIdAsync();

            // Create a User record (only candidates can register through this endpoint)
            var user = new User
            {
                SapId = sapId, // SapId is now the primary key
                Username = request.Username,
                Email = request.Email,
                PasswordHash = hashedPassword,
                Role = userRole,
                FirstName = request.FirstName,
                LastName = request.LastName,
                MobileNumber = request.MobileNumber,
                PhotoUrl = request.ProfilePicture,
                KeySkills = request.KeySkills,
                Employment = employment,
                Education = education,
                Category = request.Category,
                InternshipDuration = request.InternshipDuration,
                OrganizationSapId = targetOrganization.SapId// Link to organization
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            _logger.LogInformation("User {Username} registered and linked to organization {OrganizationName}",
                user.Username, targetOrganization.Name);

            // Generate JWT token for automatic login
            var jwtSecret = _config["JWT:Secret"];
            if (string.IsNullOrEmpty(jwtSecret) || Encoding.UTF8.GetBytes(jwtSecret).Length < 32)
            {
                return StatusCode(500, new { message = "JWT Secret is invalid or too short." });
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            // Create claims for the candidate user
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, "Candidate"),
                new Claim(ClaimTypes.NameIdentifier, user.SapId),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim("SapId", user.SapId) // Add SapId as a claim
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

            return Ok(new {
                success = true,
                message = "User registered and logged in successfully.",
                token = tokenString,
                redirectUrl = "/Test"
            });
        }

        // API endpoint for organization registration
        [HttpPost]
        [Route("Auth/api/register-organization")]
        public async Task<IActionResult> RegisterOrganization([FromBody] RegisterRequest request)
        {
            try
            {
                if (request == null ||
                    string.IsNullOrWhiteSpace(request.Username) ||
                    string.IsNullOrWhiteSpace(request.Email) ||
                    string.IsNullOrWhiteSpace(request.Password))
                {
                    return BadRequest(new { success = false, message = "Username, email, and password are required for organization registration." });
                }

                // Check if username already exists in Organizations table
                if (await _context.Organizations.AnyAsync(o => o.Username.ToLower() == request.Username.ToLower()))
                {
                    return BadRequest(new { success = false, message = "Username already exists. Please choose a different one." });
                }

                // Check if username already exists in Users table
                if (await _context.Users.AnyAsync(u => u.Username.ToLower() == request.Username.ToLower()))
                {
                    return BadRequest(new { success = false, message = "Username already exists. Please choose a different one." });
                }

                // Check if email already exists in Organizations table
                if (await _context.Organizations.AnyAsync(o => o.Email.ToLower() == request.Email.ToLower()))
                {
                    return BadRequest(new { success = false, message = "Email address already in use. Please use a different email." });
                }

                // Check if email already exists in Users table
                if (await _context.Users.AnyAsync(u => u.Email.ToLower() == request.Email.ToLower()))
                {
                    return BadRequest(new { success = false, message = "Email address already in use. Please use a different email." });
                }

                // Check if phone number already exists (if provided)
                if (!string.IsNullOrWhiteSpace(request.MobileNumber))
                {
                    if (await _context.Organizations.AnyAsync(o => o.PhoneNumber == request.MobileNumber))
                    {
                        return BadRequest(new { success = false, message = "Phone number already in use. Please use a different phone number." });
                    }

                    if (await _context.Users.AnyAsync(u => u.MobileNumber == request.MobileNumber))
                    {
                        return BadRequest(new { success = false, message = "Phone number already in use. Please use a different phone number." });
                    }
                }

                // Hash the password securely
                var hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);

                // Generate a unique SAP ID for the organization
                string sapId = await _sapIdGeneratorService.GenerateUniqueOrganizationIdAsync();

                // Create an Organization record
                var organization = new Organization
                {
                    SapId = sapId,
                    Username = request.Username,
                    PasswordHash = hashedPassword,
                    Name = request.OrganizationName ?? request.Username,
                    Email = request.Email,
                    ContactPerson = request.ContactPerson ?? request.Username,
                    PhoneNumber = request.MobileNumber,
                    Address = request.Address,
                    Website = request.Website,
                    Description = request.Description,
                    LogoUrl = "/images/default-profile.svg",
                    CreatedAt = DateTime.UtcNow
                };

                _context.Organizations.Add(organization);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Organization {OrganizationName} registered successfully", organization.Name);

                // Generate JWT token for automatic login
                var jwtSecret = _config["JWT:Secret"];
                if (string.IsNullOrEmpty(jwtSecret) || Encoding.UTF8.GetBytes(jwtSecret).Length < 32)
                {
                    return StatusCode(500, new { message = "JWT Secret is invalid or too short." });
                }

                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
                var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

                // Create claims for the organization
                var claims = new[]
                {
                    new Claim(ClaimTypes.Name, organization.Username),
                    new Claim(ClaimTypes.Role, "Organization"),
                    new Claim(ClaimTypes.NameIdentifier, organization.SapId),
                    new Claim(ClaimTypes.Email, organization.Email),
                    new Claim("OrganizationName", organization.Name),
                    new Claim("SapId", organization.SapId)
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

                return Ok(new {
                    success = true,
                    message = "Organization registered and logged in successfully.",
                    token = tokenString,
                    redirectUrl = "/Test"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during organization registration");
                return StatusCode(500, new { success = false, message = "An error occurred during registration. Please try again later." });
            }
        }

        // API endpoint for login
        [HttpPost]
        [Route("Auth/login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { message = "Email and password are required." });
            }

            try
            {
                // First check if this is a user (candidate)
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower());
                if (user != null)
                {
                    // Verify password for user
                    bool userPasswordValid = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);
                    if (!userPasswordValid)
                    {
                        return BadRequest(new { message = "Invalid email or password." });
                    }

                    // Generate JWT token for user
                    string userJwtSecret = _config["JWT:Secret"];
                    if (string.IsNullOrEmpty(userJwtSecret) || Encoding.UTF8.GetBytes(userJwtSecret).Length < 32)
                    {
                        return StatusCode(500, new { message = "JWT Secret is invalid or too short." });
                    }

                    var userKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(userJwtSecret));
                    var userCredentials = new SigningCredentials(userKey, SecurityAlgorithms.HmacSha256);

                    var userClaims = new[]
                    {
                        new Claim(ClaimTypes.Name, user.Username),
                        new Claim(ClaimTypes.Role, user.Role.ToString()),
                        new Claim(ClaimTypes.Email, user.Email),
                        new Claim(ClaimTypes.NameIdentifier, user.SapId),
                        new Claim("SapId", user.SapId)
                    };

                    var userTokenDescriptor = new SecurityTokenDescriptor
                    {
                        Subject = new ClaimsIdentity(userClaims),
                        Expires = DateTime.UtcNow.AddHours(1),
                        SigningCredentials = userCredentials
                    };

                    var userTokenHandler = new JwtSecurityTokenHandler();
                    var userToken = userTokenHandler.CreateToken(userTokenDescriptor);
                    var userTokenString = userTokenHandler.WriteToken(userToken);

                    // Set authentication cookie
                    var userClaimsIdentity = new ClaimsIdentity(userClaims, CookieAuthenticationDefaults.AuthenticationScheme);
                    var userAuthProperties = new AuthenticationProperties
                    {
                        IsPersistent = true,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
                    };

                    await HttpContext.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        new ClaimsPrincipal(userClaimsIdentity),
                        userAuthProperties);

                    return Ok(new { token = userTokenString, redirectUrl = "/Test" });
                }

                // If user not found, check for special user
                var specialUser = await _specialUserService.AuthenticateSpecialUserAsync(request.Email, request.Password);
                if (specialUser != null)
                {
                    // Generate JWT token for special user
                    string specialUserJwtSecret = _config["JWT:Secret"];
                    if (string.IsNullOrEmpty(specialUserJwtSecret) || Encoding.UTF8.GetBytes(specialUserJwtSecret).Length < 32)
                    {
                        return StatusCode(500, new { message = "JWT Secret is invalid or too short." });
                    }

                    var specialUserKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(specialUserJwtSecret));
                    var specialUserCredentials = new SigningCredentials(specialUserKey, SecurityAlgorithms.HmacSha256);

                    var specialUserClaims = new[]
                    {
                        new Claim(ClaimTypes.Name, specialUser.Username),
                        new Claim(ClaimTypes.Role, "SpecialUser"),
                        new Claim(ClaimTypes.NameIdentifier, specialUser.UsersSapId),
                        new Claim(ClaimTypes.Email, specialUser.Email),
                        new Claim("OrganizationSapId", specialUser.OrganizationSapId),
                        new Claim("UserType", "Special")
                    };

                    var specialUserTokenDescriptor = new SecurityTokenDescriptor
                    {
                        Subject = new ClaimsIdentity(specialUserClaims),
                        Expires = DateTime.UtcNow.AddHours(1),
                        Issuer = _config["JWT:Issuer"],
                        Audience = _config["JWT:Audience"],
                        SigningCredentials = specialUserCredentials
                    };

                    var specialUserTokenHandler = new JwtSecurityTokenHandler();
                    var specialUserToken = specialUserTokenHandler.CreateToken(specialUserTokenDescriptor);
                    var specialUserTokenString = specialUserTokenHandler.WriteToken(specialUserToken);

                    // Set authentication cookie
                    var specialUserClaimsIdentity = new ClaimsIdentity(specialUserClaims, CookieAuthenticationDefaults.AuthenticationScheme);
                    var specialUserAuthProperties = new AuthenticationProperties
                    {
                        IsPersistent = true,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
                    };

                    await HttpContext.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        new ClaimsPrincipal(specialUserClaimsIdentity),
                        specialUserAuthProperties);

                    return Ok(new { token = specialUserTokenString, redirectUrl = "/Test" });
                }

                // If special user not found, check for organization
                var organization = await _context.Organizations.FirstOrDefaultAsync(o => o.Email.ToLower() == request.Email.ToLower());
                if (organization == null)
                {
                    // Neither user, special user, nor organization found with this email
                    return BadRequest(new { message = "Invalid email or password." });
                }

                // Verify password for organization
                bool orgPasswordValid = BCrypt.Net.BCrypt.Verify(request.Password, organization.PasswordHash);
                if (!orgPasswordValid)
                {
                    return BadRequest(new { message = "Invalid email or password." });
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
                    new Claim(ClaimTypes.Name, organization.Username),
                    new Claim(ClaimTypes.Role, "Organization"),
                    new Claim(ClaimTypes.NameIdentifier, organization.SapId),
                    new Claim(ClaimTypes.Email, organization.Email),
                    new Claim("OrganizationName", organization.Name)
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

                // Update last login time for organization
                organization.LastLoginAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                return Ok(new { token = tokenString, redirectUrl = "/Test" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred during login." });
            }
        }

        [HttpPost]
        [Route("Auth/logout")]
        public async Task<IActionResult> Logout()
        {
            try
            {
                // Get the current user role before logout
                bool isSuperAdmin = User.IsInRole("Admin");
                bool isOrganization = User.IsInRole("Organization");

                // Clear all authentication cookies
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                // Clear any existing cookies
                foreach (var cookie in Request.Cookies.Keys)
                {
                    Response.Cookies.Delete(cookie);
                }

                // Return success response with redirect URL based on user role
                if (isSuperAdmin)
                {
                    return Ok(new { message = "Logged out successfully", redirectUrl = "/SuperAdmin/Login" });
                }
                else if (isOrganization)
                {
                    return Ok(new { message = "Logged out successfully", redirectUrl = "/Auth/OrganizationLogin" });
                }
                else
                {
                    return Ok(new { message = "Logged out successfully", redirectUrl = "/Auth/Register" });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred during logout" });
            }
        }

        // Super Admin Login endpoint
        [HttpPost]
        [Route("Auth/SuperAdminLogin")]
        public async Task<IActionResult> SuperAdminLogin([FromBody] LoginRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { success = false, message = "Email and password are required." });
            }

            try
            {
                // Check if this is the super admin account
                var superAdminEmail = _config["SuperAdmin:Email"] ?? "admin@system.com";
                var superAdminPassword = _config["SuperAdmin:Password"] ?? "admin123";

                if (request.Email.ToLower() == superAdminEmail.ToLower() && request.Password == superAdminPassword)
                {
                    // Generate JWT token
                    var jwtSecret = _config["JWT:Secret"];
                    if (string.IsNullOrEmpty(jwtSecret) || Encoding.UTF8.GetBytes(jwtSecret).Length < 32)
                    {
                        return StatusCode(500, new { success = false, message = "JWT Secret is invalid or too short." });
                    }

                    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
                    var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

                    var claims = new[]
                    {
                        new Claim(ClaimTypes.Name, "Admin"),
                        new Claim(ClaimTypes.Role, "Admin"),
                        new Claim(ClaimTypes.Email, request.Email)
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

                    return Ok(new { success = true, token = tokenString, redirectUrl = "/SuperAdmin/Dashboard" });
                }
                else
                {
                    return BadRequest(new { success = false, message = "Invalid email or password." });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "An error occurred during login." });
            }
        }



        // OTP Login - Request OTP
        [HttpPost]
        [Route("Auth/api/request-otp")]
        public async Task<IActionResult> RequestOtp([FromBody] OtpRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email))
            {
                return BadRequest(new { message = "Email is required." });
            }

            try
            {
                // Get client IP address
                string ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                // Check rate limiting
                if (!_rateLimitingService.IsAllowed(request.Email, ipAddress))
                {
                    return StatusCode(429, new { message = "Too many requests. Please try again later." });
                }

                // Check if account exists (either user or organization)
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower());
                var organization = await _context.Organizations.FirstOrDefaultAsync(o => o.Email.ToLower() == request.Email.ToLower());

                if (user == null && organization == null)
                {
                    // Don't reveal that the account doesn't exist for security reasons
                    return Ok(new { message = "If your email is registered, you will receive an OTP shortly." });
                }

                // Record this attempt for rate limiting
                _rateLimitingService.RecordAttempt(request.Email, ipAddress);

                try
                {
                    // Generate OTP
                    var otp = await _otpService.GenerateOtpAsync(request.Email);

                    // Send OTP via email
                    await _emailService.SendOtpEmailAsync(request.Email, otp);

                    return Ok(new { message = "OTP has been sent to your email." });
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new { message = "An error occurred while generating OTP." });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while processing your request." });
            }
        }

        // OTP Login - Verify OTP
        [HttpPost]
        [Route("Auth/api/verify-otp")]
        public async Task<IActionResult> VerifyOtp([FromBody] OtpVerificationRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.OtpCode))
            {
                return BadRequest(new { message = "Email and OTP code are required." });
            }

            try
            {
                try
                {
                    // Validate OTP
                    bool isValid = await _otpService.ValidateOtpAsync(request.Email, request.OtpCode);
                    if (!isValid)
                    {
                        return BadRequest(new { message = "Invalid or expired OTP." });
                    }
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new { message = "An error occurred while validating OTP." });
                }

                // Check if this is a user or organization
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower());
                var organization = await _context.Organizations.FirstOrDefaultAsync(o => o.Email.ToLower() == request.Email.ToLower());

                if (user == null && organization == null)
                {
                    return BadRequest(new { message = "Account not found." });
                }

                // Generate JWT token
                var jwtSecret = _config["JWT:Secret"];
                if (string.IsNullOrEmpty(jwtSecret) || Encoding.UTF8.GetBytes(jwtSecret).Length < 32)
                {
                    return StatusCode(500, new { message = "JWT Secret is invalid or too short." });
                }

                var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
                var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

                Claim[] claims;

                if (user != null) // This is a candidate user
                {
                    claims = new[]
                    {
                        new Claim(ClaimTypes.Name, user.Username),
                        new Claim(ClaimTypes.Role, user.Role.ToString()),
                        new Claim(ClaimTypes.NameIdentifier, user.SapId),
                        new Claim(ClaimTypes.Email, user.Email)
                    };
                }
                else // This is an organization
                {
                    claims = new[]
                    {
                        new Claim(ClaimTypes.Name, organization.Username),
                        new Claim(ClaimTypes.Role, "Organization"),
                        new Claim(ClaimTypes.NameIdentifier, organization.SapId),
                        new Claim(ClaimTypes.Email, organization.Email),
                        new Claim("OrganizationName", organization.Name)
                    };

                    // Update last login time for organization
                    organization.LastLoginAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }

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

                return Ok(new { token = tokenString, redirectUrl = "/Test" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while processing your request." });
            }
        }

        // View action for forgot password page
        [HttpGet]
        [Route("Auth/ForgotPassword")]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        // View action for reset password page
        [HttpGet]
        [Route("Auth/ResetPassword")]
        public IActionResult ResetPassword(string email, string token)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
            {
                return RedirectToAction("ForgotPassword");
            }

            ViewBag.Email = email;
            ViewBag.Token = token;
            return View();
        }

        // API endpoint for requesting password reset
        [HttpPost]
        [Route("Auth/api/forgot-password")]
        public async Task<IActionResult> ForgotPasswordApi([FromBody] ForgotPasswordRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email))
            {
                return BadRequest(new { message = "Email is required." });
            }

            try
            {
                // Get client IP address for rate limiting
                string ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                // Check rate limiting
                if (!_rateLimitingService.IsAllowed(request.Email, ipAddress))
                {
                    return StatusCode(429, new { message = "Too many requests. Please try again later." });
                }

                // Check if user exists
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower());
                if (user == null)
                {
                    // Don't reveal that the user doesn't exist for security reasons
                    return Ok(new { message = "If your email is registered, you will receive a password reset link shortly." });
                }

                // Record this attempt for rate limiting
                _rateLimitingService.RecordAttempt(request.Email, ipAddress);

                // Generate password reset token
                var token = await _passwordResetService.GeneratePasswordResetTokenAsync(request.Email);

                // Create reset URL
                var resetUrl = $"{Request.Scheme}://{Request.Host}/Auth/ResetPassword?email={Uri.EscapeDataString(request.Email)}&token={Uri.EscapeDataString(token)}";

                // Send password reset email
                await _emailService.SendPasswordResetEmailAsync(request.Email, token, resetUrl);

                return Ok(new { message = "Password reset instructions have been sent to your email." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while processing your request." });
            }
        }

        // API endpoint for resetting password
        [HttpPost]
        [Route("Auth/api/reset-password")]
        public async Task<IActionResult> ResetPasswordApi([FromBody] ResetPasswordRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.NewPassword))
            {
                return BadRequest(new { message = "Email, token, and new password are required." });
            }

            try
            {
                // Validate password strength
                if (request.NewPassword.Length < 8)
                {
                    return BadRequest(new { message = "Password must be at least 8 characters long." });
                }

                // Reset the password
                bool success = await _passwordResetService.ResetPasswordAsync(request.Email, request.Token, request.NewPassword);
                if (!success)
                {
                    return BadRequest(new { message = "Invalid or expired token." });
                }

                return Ok(new { message = "Password has been reset successfully. You can now log in with your new password." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An error occurred while processing your request." });
            }
        }

        // API endpoint for forgot password with phone verification
        [HttpPost]
        [Route("Auth/api/forgot-password-with-phone")]
        public async Task<IActionResult> ForgotPasswordWithPhoneApi([FromBody] ForgotPasswordWithPhoneRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.PhoneNumber) || string.IsNullOrWhiteSpace(request.NewPassword))
            {
                return BadRequest(new { message = "Email, phone number, and new password are required." });
            }

            try
            {
                // Check if user exists with both email and phone number
                var user = await _context.Users.FirstOrDefaultAsync(u =>
                    u.Email.ToLower() == request.Email.ToLower() &&
                    u.MobileNumber == request.PhoneNumber);

                if (user == null)
                {
                    return BadRequest(new { message = "No user found with the provided email and phone number combination." });
                }

                // Hash the new password
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);

                await _context.SaveChangesAsync();

                return Ok(new { message = "Password has been reset successfully. You can now login with your new password." });
            }
            catch (Exception ex)
            {
                // Log the error
                Console.Error.WriteLine($"Password Reset with Phone Error: {ex.Message}");
                Console.Error.WriteLine($"Stack Trace: {ex.StackTrace}");

                return StatusCode(500, new { message = "An error occurred while processing your request." });
            }
        }
    }

    // Request models
    public class ForgotPasswordRequest
    {
        public string Email { get; set; }
    }

    public class ForgotPasswordWithPhoneRequest
    {
        public string Email { get; set; }
        public string PhoneNumber { get; set; }
        public string NewPassword { get; set; }
    }

    public class ResetPasswordRequest
    {
        public string Email { get; set; }
        public string Token { get; set; }
        public string NewPassword { get; set; }
    }
}
