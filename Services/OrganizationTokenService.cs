using Microsoft.EntityFrameworkCore;
using OnlineAssessment.Web.Models;
using System.Security.Cryptography;
using System.Text;

namespace OnlineAssessment.Web.Services
{
    public class OrganizationTokenService : IOrganizationTokenService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<OrganizationTokenService> _logger;

        public OrganizationTokenService(AppDbContext context, ILogger<OrganizationTokenService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<string> GenerateOrganizationTokenAsync(string organizationSapId)
        {
            try
            {
                var organization = await _context.Organizations.FindAsync(organizationSapId);
                if (organization == null)
                {
                    throw new ArgumentException("Organization not found", nameof(organizationSapId));
                }

                // Generate a unique token
                string token = GenerateUniqueToken();

                // Update organization with new token
                organization.OrganizationToken = token;
                organization.TokenGeneratedAt = DateTime.UtcNow;
                organization.IsTokenActive = true;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Generated new token for organization {OrganizationSapId}", organizationSapId);
                return token;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating token for organization {OrganizationSapId}", organizationSapId);
                throw;
            }
        }

        public async Task<Organization?> ValidateOrganizationTokenAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return null;

            try
            {
                var organization = await _context.Organizations
                    .FirstOrDefaultAsync(o => o.OrganizationToken == token && o.IsTokenActive);

                if (organization != null)
                {
                    _logger.LogInformation("Valid token found for organization {OrganizationSapId}", organization.SapId);
                }

                return organization;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating organization token");
                return null;
            }
        }

        public async Task<Organization> GetOrCreateDefaultOrganizationAsync()
        {
            try
            {
                // Look for TCS organization as the default organization
                var tcsOrg = await _context.Organizations
                    .FirstOrDefaultAsync(o => o.Name == "TCS" || o.SapId == "1000010000");

                if (tcsOrg == null)
                {
                    // If TCS doesn't exist, create it as the default organization
                    tcsOrg = new Organization
                    {
                        SapId = "1000010000",
                        Name = "TCS",
                        Email = "TCS@gmail.com",
                        ContactPerson = "TCS Administrator",
                        Username = "TCS",
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("tcs123"), // Default password
                        Description = "TCS - Default organization for users without organization tokens",
                        CreatedAt = DateTime.UtcNow,
                        IsTokenActive = false // Default org doesn't use tokens
                    };

                    _context.Organizations.Add(tcsOrg);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation("Created TCS as default organization");
                }

                return tcsOrg;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting or creating TCS default organization");
                throw;
            }
        }

        public async Task<bool> DeactivateTokenAsync(string organizationSapId)
        {
            try
            {
                var organization = await _context.Organizations.FindAsync(organizationSapId);
                if (organization == null)
                    return false;

                organization.IsTokenActive = false;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Deactivated token for organization {OrganizationSapId}", organizationSapId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deactivating token for organization {OrganizationSapId}", organizationSapId);
                return false;
            }
        }

        public async Task<string> RegenerateTokenAsync(string organizationSapId)
        {
            try
            {
                // First deactivate the old token
                await DeactivateTokenAsync(organizationSapId);

                // Then generate a new one
                return await GenerateOrganizationTokenAsync(organizationSapId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error regenerating token for organization {OrganizationSapId}", organizationSapId);
                throw;
            }
        }

        private string GenerateUniqueToken()
        {
            // Generate a cryptographically secure random token
            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] tokenBytes = new byte[32]; // 256 bits
                rng.GetBytes(tokenBytes);

                // Convert to base64 and make it URL-safe
                string token = Convert.ToBase64String(tokenBytes)
                    .Replace('+', '-')
                    .Replace('/', '_')
                    .Replace("=", "");

                // Add a prefix to make it recognizable
                return $"ORG_{token}";
            }
        }
    }
}
