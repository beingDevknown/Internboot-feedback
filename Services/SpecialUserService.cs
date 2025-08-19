using Microsoft.EntityFrameworkCore;
using OnlineAssessment.Web.Models;
using OnlineAssessment.Web.Models.DTOs;
using System.Security.Cryptography;
using System.Text;

namespace OnlineAssessment.Web.Services
{
    public interface ISpecialUserService
    {
        Task<(bool Success, string Message, SpecialUserResponse? User)> CreateSpecialUserAsync(string organizationSapId, CreateSpecialUserRequest request, string createdBy);
        Task<(bool Success, string Message)> UpdateSpecialUserAsync(string specialUserSapId, string organizationSapId, UpdateSpecialUserRequest request);
        Task<(bool Success, string Message)> ChangePasswordAsync(string specialUserSapId, string organizationSapId, string newPassword);
        Task<(bool Success, string Message)> DeleteSpecialUserAsync(string specialUserSapId, string organizationSapId);
        Task<List<SpecialUserResponse>> GetSpecialUsersAsync(string organizationSapId);
        Task<SpecialUserResponse?> GetSpecialUserAsync(string specialUserSapId, string organizationSapId);
        Task<SpecialUser?> AuthenticateSpecialUserAsync(string email, string password);
        Task<bool> IsEmailAvailableAsync(string email);
        Task<bool> IsUsernameAvailableAsync(string username);
    }

    public class SpecialUserService : ISpecialUserService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<SpecialUserService> _logger;
        private readonly ISapIdGeneratorService _sapIdGeneratorService;

        public SpecialUserService(AppDbContext context, ILogger<SpecialUserService> logger, ISapIdGeneratorService sapIdGeneratorService)
        {
            _context = context;
            _logger = logger;
            _sapIdGeneratorService = sapIdGeneratorService;
        }

        public async Task<(bool Success, string Message, SpecialUserResponse? User)> CreateSpecialUserAsync(
            string organizationSapId, CreateSpecialUserRequest request, string createdBy)
        {
            try
            {
                // Check if email is already taken
                if (!await IsEmailAvailableAsync(request.Email))
                {
                    return (false, "Email is already registered", null);
                }

                // Generate unique SapId for the special user using special user prefix
                var sapId = await _sapIdGeneratorService.GenerateUniqueSpecialUserIdAsync();

                // Hash the password
                var passwordHash = HashPassword(request.Password);

                // Create the special user
                var specialUser = new SpecialUser
                {
                    UsersSapId = sapId,
                    Email = request.Email,
                    Username = request.Username,
                    FullName = request.FullName,
                    PasswordHash = passwordHash,
                    OrganizationSapId = organizationSapId,
                    MobileNumber = request.MobileNumber,
                    Education = request.Education,
                    Employment = request.Employment,
                    Category = request.Category,
                    Description = request.Description,
                    CreatedBy = createdBy,
                    CreatedAt = Utilities.TimeZoneHelper.GetCurrentIstTime()
                };

                _context.SpecialUsers.Add(specialUser);
                await _context.SaveChangesAsync();

                // Get organization name for response
                var organization = await _context.Organizations.FindAsync(organizationSapId);
                var response = new SpecialUserResponse
                {
                    SapId = specialUser.UsersSapId,
                    Email = specialUser.Email,
                    Username = specialUser.Username,
                    FullName = specialUser.FullName,
                    MobileNumber = specialUser.MobileNumber,
                    Education = specialUser.Education,
                    Employment = specialUser.Employment,
                    Category = specialUser.Category,
                    Description = specialUser.Description,
                    CreatedAt = specialUser.CreatedAt,
                    LastLoginAt = specialUser.LastLoginAt,
                    IsActive = specialUser.IsActive,
                    CreatedBy = specialUser.CreatedBy,
                    OrganizationName = organization?.Name ?? "Unknown"
                };

                _logger.LogInformation("Special user {Email} created successfully for organization {OrganizationSapId}",
                    request.Email, organizationSapId);

                return (true, "Special user created successfully", response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating special user {Email} for organization {OrganizationSapId}",
                    request.Email, organizationSapId);
                return (false, "An error occurred while creating the special user", null);
            }
        }

        public async Task<(bool Success, string Message)> UpdateSpecialUserAsync(
            string specialUserSapId, string organizationSapId, UpdateSpecialUserRequest request)
        {
            try
            {
                var specialUser = await _context.SpecialUsers
                    .FirstOrDefaultAsync(su => su.UsersSapId == specialUserSapId && su.OrganizationSapId == organizationSapId);

                if (specialUser == null)
                {
                    return (false, "Special user not found");
                }

                // Update the special user (email cannot be changed)
                specialUser.Username = request.Username;
                specialUser.FullName = request.FullName;
                specialUser.MobileNumber = request.MobileNumber;
                specialUser.Education = request.Education;
                specialUser.Employment = request.Employment;
                specialUser.Category = request.Category;
                specialUser.Description = request.Description;
                specialUser.IsActive = request.IsActive;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Special user {SpecialUserSapId} updated successfully", specialUserSapId);
                return (true, "Special user updated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating special user {SpecialUserSapId}", specialUserSapId);
                return (false, "An error occurred while updating the special user");
            }
        }

        public async Task<(bool Success, string Message)> ChangePasswordAsync(
            string specialUserSapId, string organizationSapId, string newPassword)
        {
            try
            {
                var specialUser = await _context.SpecialUsers
                    .FirstOrDefaultAsync(su => su.UsersSapId == specialUserSapId && su.OrganizationSapId == organizationSapId);

                if (specialUser == null)
                {
                    return (false, "Special user not found");
                }

                // Hash the new password
                specialUser.PasswordHash = HashPassword(newPassword);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Password changed successfully for special user {SpecialUserSapId}", specialUserSapId);
                return (true, "Password changed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error changing password for special user {SpecialUserSapId}", specialUserSapId);
                return (false, "An error occurred while changing the password");
            }
        }

        public async Task<(bool Success, string Message)> DeleteSpecialUserAsync(string specialUserSapId, string organizationSapId)
        {
            try
            {
                var specialUser = await _context.SpecialUsers
                    .FirstOrDefaultAsync(su => su.UsersSapId == specialUserSapId && su.OrganizationSapId == organizationSapId);

                if (specialUser == null)
                {
                    return (false, "Special user not found");
                }

                _context.SpecialUsers.Remove(specialUser);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Special user {SpecialUserSapId} deleted successfully", specialUserSapId);
                return (true, "Special user deleted successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting special user {SpecialUserSapId}", specialUserSapId);
                return (false, "An error occurred while deleting the special user");
            }
        }

        public async Task<List<SpecialUserResponse>> GetSpecialUsersAsync(string organizationSapId)
        {
            try
            {
                var specialUsers = await _context.SpecialUsers
                    .Include(su => su.Organization)
                    .Where(su => su.OrganizationSapId == organizationSapId)
                    .OrderByDescending(su => su.CreatedAt)
                    .ToListAsync();

                return specialUsers.Select(su => new SpecialUserResponse
                {
                    SapId = su.UsersSapId,
                    Email = su.Email,
                    Username = su.Username,
                    FullName = su.FullName,
                    MobileNumber = su.MobileNumber,
                    Education = su.Education,
                    Employment = su.Employment,
                    Category = su.Category,
                    Description = su.Description,
                    CreatedAt = su.CreatedAt,
                    LastLoginAt = su.LastLoginAt,
                    IsActive = su.IsActive,
                    CreatedBy = su.CreatedBy,
                    OrganizationName = su.Organization?.Name ?? "Unknown"
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting special users for organization {OrganizationSapId}", organizationSapId);
                return new List<SpecialUserResponse>();
            }
        }

        public async Task<SpecialUserResponse?> GetSpecialUserAsync(string specialUserSapId, string organizationSapId)
        {
            try
            {
                var specialUser = await _context.SpecialUsers
                    .Include(su => su.Organization)
                    .FirstOrDefaultAsync(su => su.UsersSapId == specialUserSapId && su.OrganizationSapId == organizationSapId);

                if (specialUser == null)
                {
                    return null;
                }

                return new SpecialUserResponse
                {
                    SapId = specialUser.UsersSapId,
                    Email = specialUser.Email,
                    Username = specialUser.Username,
                    FullName = specialUser.FullName,
                    MobileNumber = specialUser.MobileNumber,
                    Education = specialUser.Education,
                    Employment = specialUser.Employment,
                    Category = specialUser.Category,
                    Description = specialUser.Description,
                    CreatedAt = specialUser.CreatedAt,
                    LastLoginAt = specialUser.LastLoginAt,
                    IsActive = specialUser.IsActive,
                    CreatedBy = specialUser.CreatedBy,
                    OrganizationName = specialUser.Organization?.Name ?? "Unknown"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting special user {SpecialUserSapId}", specialUserSapId);
                return null;
            }
        }

        public async Task<SpecialUser?> AuthenticateSpecialUserAsync(string email, string password)
        {
            try
            {
                var specialUser = await _context.SpecialUsers
                    .Include(su => su.Organization)
                    .FirstOrDefaultAsync(su => su.Email == email && su.IsActive);

                if (specialUser == null)
                {
                    return null;
                }

                // Verify password
                if (!VerifyPassword(password, specialUser.PasswordHash))
                {
                    return null;
                }

                // Update last login time
                specialUser.LastLoginAt = Utilities.TimeZoneHelper.GetCurrentIstTime();
                await _context.SaveChangesAsync();

                return specialUser;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error authenticating special user {Email}", email);
                return null;
            }
        }

        public async Task<bool> IsEmailAvailableAsync(string email)
        {
            try
            {
                // Check both regular users and special users
                var regularUserExists = await _context.Users
                    .AnyAsync(u => u.Email == email);

                var specialUserExists = await _context.SpecialUsers
                    .AnyAsync(su => su.Email == email);

                return !regularUserExists && !specialUserExists;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if email {Email} is available", email);
                return false;
            }
        }

        public async Task<bool> IsUsernameAvailableAsync(string username)
        {
            try
            {
                // Check both regular users and special users
                var regularUserExists = await _context.Users
                    .AnyAsync(u => u.Username == username);

                var specialUserExists = await _context.SpecialUsers
                    .AnyAsync(su => su.Username == username);

                return !regularUserExists && !specialUserExists;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if username {Username} is available", username);
                return false;
            }
        }

        private static string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(hashedBytes);
        }

        private static bool VerifyPassword(string password, string hash)
        {
            var passwordHash = HashPassword(password);
            return passwordHash == hash;
        }
    }
}
