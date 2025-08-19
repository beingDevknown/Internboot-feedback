using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OnlineAssessment.Web.Models;

namespace OnlineAssessment.Web.Services
{
    public interface IPasswordResetService
    {
        Task<string> GeneratePasswordResetTokenAsync(string email);
        Task<bool> ValidatePasswordResetTokenAsync(string email, string token);
        Task<bool> ResetPasswordAsync(string email, string token, string newPassword);
    }

    public class PasswordResetService : IPasswordResetService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<PasswordResetService> _logger;

        public PasswordResetService(AppDbContext context, ILogger<PasswordResetService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<string> GeneratePasswordResetTokenAsync(string email)
        {
            // Generate a random token
            string token = GenerateRandomToken();
            
            // Find the user by email
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
            
            if (user == null)
            {
                _logger.LogWarning($"Password reset token generation failed: User with email {email} not found");
                throw new Exception("User not found");
            }

            // Set token and expiry (1 hour from now)
            user.OtpCode = token; // Reusing OTP field for password reset
            user.OtpExpiry = DateTime.UtcNow.AddHours(1);
            
            await _context.SaveChangesAsync();
            
            _logger.LogInformation($"Password reset token generated for user {email}");
            return token;
        }

        public async Task<bool> ValidatePasswordResetTokenAsync(string email, string token)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
            
            if (user == null)
            {
                _logger.LogWarning($"Password reset token validation failed: User with email {email} not found");
                return false;
            }

            // Check if token is valid and not expired
            if (user.OtpCode == token && user.OtpExpiry.HasValue && user.OtpExpiry.Value > DateTime.UtcNow)
            {
                _logger.LogInformation($"Password reset token validated successfully for user {email}");
                return true;
            }
            
            _logger.LogWarning($"Password reset token validation failed for user {email}: Invalid or expired token");
            return false;
        }

        public async Task<bool> ResetPasswordAsync(string email, string token, string newPassword)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
            
            if (user == null)
            {
                _logger.LogWarning($"Password reset failed: User with email {email} not found");
                return false;
            }

            // Validate token first
            if (user.OtpCode != token || !user.OtpExpiry.HasValue || user.OtpExpiry.Value <= DateTime.UtcNow)
            {
                _logger.LogWarning($"Password reset failed for user {email}: Invalid or expired token");
                return false;
            }

            // Hash the new password
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            
            // Clear the token
            user.OtpCode = null;
            user.OtpExpiry = null;
            
            await _context.SaveChangesAsync();
            
            _logger.LogInformation($"Password reset successfully for user {email}");
            return true;
        }

        private string GenerateRandomToken()
        {
            // Generate a random token (32 characters)
            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] tokenData = new byte[16]; // 16 bytes = 32 hex characters
                rng.GetBytes(tokenData);
                return BitConverter.ToString(tokenData).Replace("-", "").ToLower();
            }
        }
    }
}
