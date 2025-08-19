using System;
using System.Security.Cryptography;
using OnlineAssessment.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;

namespace OnlineAssessment.Web.Services
{
    public interface IOtpService
    {
        Task<string> GenerateOtpAsync(string email);
        Task<bool> ValidateOtpAsync(string email, string otpCode);
    }

    public class OtpService : IOtpService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<OtpService> _logger;
        private readonly bool _isDevelopment;

        public OtpService(AppDbContext context, ILogger<OtpService> logger, IWebHostEnvironment env)
        {
            _context = context;
            _logger = logger;
            _isDevelopment = env.IsDevelopment();
        }

        public async Task<string> GenerateOtpAsync(string email)
        {
            // Generate a 6-digit OTP
            string otp = GenerateRandomOtp();
            _logger.LogInformation($"Generated OTP: {otp} for email: {email}");

            // Log the OTP in development mode for debugging
            if (_isDevelopment)
            {
                _logger.LogInformation($"OTP for {email}: {otp}");
            }

            // Find the user by email
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
            var organization = await _context.Organizations.FirstOrDefaultAsync(o => o.Email.ToLower() == email.ToLower());

            _logger.LogInformation($"User found: {user != null}, Organization found: {organization != null}");

            // For development mode, create a test user if none exists
            if (_isDevelopment && user == null && organization == null)
            {
                _logger.LogInformation($"Creating test user for development mode with email: {email}");
                user = new User
                {
                    Email = email,
                    Username = email.Split('@')[0],
                    PasswordHash = "development_mode_hash",
                    Role = UserRole.Candidate
                };
                _context.Users.Add(user);
                await _context.SaveChangesAsync();
            }
            else if (user == null && organization == null)
            {
                _logger.LogWarning($"OTP generation failed: Account with email {email} not found");
                throw new Exception("Account not found");
            }

            if (user != null)
            {
                // Set OTP and expiry (10 minutes from now) for user
                user.OtpCode = otp;
                user.OtpExpiry = DateTime.UtcNow.AddMinutes(10);
                user.IsOtpVerified = false;

                _logger.LogInformation($"OTP generated for user {email}");
            }
            else if (organization != null)
            {
                // Set OTP and expiry (10 minutes from now) for organization
                organization.OtpCode = otp;
                organization.OtpExpiry = DateTime.UtcNow.AddMinutes(10);
                organization.IsOtpVerified = false;

                _logger.LogInformation($"OTP generated for organization {email}");
            }

            await _context.SaveChangesAsync();
            return otp;
        }

        public async Task<bool> ValidateOtpAsync(string email, string otpCode)
        {
            _logger.LogInformation($"Validating OTP: {otpCode} for email: {email}");

            // Log validation attempt in development mode
            if (_isDevelopment)
            {
                _logger.LogInformation($"Validating OTP for {email}: {otpCode}");
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
            var organization = await _context.Organizations.FirstOrDefaultAsync(o => o.Email.ToLower() == email.ToLower());

            _logger.LogInformation($"User found: {user != null}, Organization found: {organization != null}");

            if (user == null && organization == null)
            {
                _logger.LogWarning($"OTP validation failed: Account with email {email} not found");
                return false;
            }

            if (user != null)
            {
                _logger.LogInformation($"User OTP: {user.OtpCode}, Input OTP: {otpCode}");
                _logger.LogInformation($"User OTP Expiry: {user.OtpExpiry}, Current Time: {DateTime.UtcNow}");

                // Check if OTP is valid and not expired for user
                if (user.OtpCode == otpCode && user.OtpExpiry.HasValue && user.OtpExpiry.Value > DateTime.UtcNow)
                {
                    // Mark OTP as verified
                    user.IsOtpVerified = true;
                    user.OtpCode = null; // Clear OTP after successful validation
                    user.OtpExpiry = null;

                    await _context.SaveChangesAsync();

                    _logger.LogInformation($"OTP validated successfully for user {email}");
                    return true;
                }

                _logger.LogWarning($"OTP validation failed for user {email}: Invalid or expired OTP");
                _logger.LogWarning($"OTP match: {user.OtpCode == otpCode}, OTP has expiry: {user.OtpExpiry.HasValue}, OTP not expired: {user.OtpExpiry.HasValue && user.OtpExpiry.Value > DateTime.UtcNow}");
                return false;
            }
            else if (organization != null)
            {
                _logger.LogInformation($"Organization OTP: {organization.OtpCode}, Input OTP: {otpCode}");
                _logger.LogInformation($"Organization OTP Expiry: {organization.OtpExpiry}, Current Time: {DateTime.UtcNow}");

                // Check if OTP is valid and not expired for organization
                if (organization.OtpCode == otpCode && organization.OtpExpiry.HasValue && organization.OtpExpiry.Value > DateTime.UtcNow)
                {
                    // Mark OTP as verified
                    organization.IsOtpVerified = true;
                    organization.OtpCode = null; // Clear OTP after successful validation
                    organization.OtpExpiry = null;

                    await _context.SaveChangesAsync();

                    _logger.LogInformation($"OTP validated successfully for organization {email}");
                    return true;
                }

                _logger.LogWarning($"OTP validation failed for organization {email}: Invalid or expired OTP");
                _logger.LogWarning($"OTP match: {organization.OtpCode == otpCode}, OTP has expiry: {organization.OtpExpiry.HasValue}, OTP not expired: {organization.OtpExpiry.HasValue && organization.OtpExpiry.Value > DateTime.UtcNow}");
                return false;
            }

            return false;
        }

        private string GenerateRandomOtp()
        {
            // Generate a 6-digit random number
            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] randomNumber = new byte[4];
                rng.GetBytes(randomNumber);
                int value = BitConverter.ToInt32(randomNumber, 0);

                // Ensure positive number and take modulo to get 6 digits
                value = Math.Abs(value) % 1000000;

                // Pad with leading zeros if necessary
                return value.ToString("D6");
            }
        }
    }
}
