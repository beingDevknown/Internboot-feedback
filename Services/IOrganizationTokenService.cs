using OnlineAssessment.Web.Models;

namespace OnlineAssessment.Web.Services
{
    public interface IOrganizationTokenService
    {
        /// <summary>
        /// Generates a unique organization token for the given organization
        /// </summary>
        /// <param name="organizationSapId">The SAP ID of the organization</param>
        /// <returns>The generated token</returns>
        Task<string> GenerateOrganizationTokenAsync(string organizationSapId);

        /// <summary>
        /// Validates an organization token and returns the organization if valid
        /// </summary>
        /// <param name="token">The token to validate</param>
        /// <returns>The organization if token is valid, null otherwise</returns>
        Task<Organization?> ValidateOrganizationTokenAsync(string token);

        /// <summary>
        /// Gets or creates the default organization for users without tokens
        /// </summary>
        /// <returns>The default organization</returns>
        Task<Organization> GetOrCreateDefaultOrganizationAsync();

        /// <summary>
        /// Deactivates an organization token
        /// </summary>
        /// <param name="organizationSapId">The SAP ID of the organization</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> DeactivateTokenAsync(string organizationSapId);

        /// <summary>
        /// Regenerates a new token for an organization (deactivates old one)
        /// </summary>
        /// <param name="organizationSapId">The SAP ID of the organization</param>
        /// <returns>The new token</returns>
        Task<string> RegenerateTokenAsync(string organizationSapId);
    }
}
