using Microsoft.EntityFrameworkCore;
using OnlineAssessment.Web.Models;

namespace OnlineAssessment.Web.Services
{
    public interface ISapIdGeneratorService
    {
        Task<string> GenerateUniqueIdAsync();
        Task<string> GenerateUniqueSpecialUserIdAsync();
        Task<string> GenerateUniqueOrganizationIdAsync();
    }

    public class SapIdGeneratorService : ISapIdGeneratorService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<SapIdGeneratorService> _logger;
        private readonly IConfiguration _configuration;
        private const int SAP_LENGTH = 10;

        // Get prefixes from configuration
        private string UserPrefix => _configuration["SapIdPrefixes:User"] ?? "1000";
        private string SpecialUserPrefix => _configuration["SapIdPrefixes:SpecialUser"] ?? "2000";
        private string OrganizationPrefix => _configuration["SapIdPrefixes:Organization"] ?? "3000";

        public SapIdGeneratorService(AppDbContext context, ILogger<SapIdGeneratorService> logger, IConfiguration configuration)
        {
            _context = context;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<string> GenerateUniqueIdAsync()
        {
            try
            {
                // Check if SapId column exists in the database
                string highestSapId = null;
                try
                {
                    // Try to get the highest SAP ID from the database
                    highestSapId = await _context.Users
                        .Where(u => u.SapId != null && u.SapId.StartsWith(UserPrefix))
                        .Select(u => u.SapId)
                        .OrderByDescending(id => id)
                        .FirstOrDefaultAsync();
                }
                catch (Exception ex)
                {
                    // If the column doesn't exist, this will throw an exception
                    _logger.LogWarning(ex, "SapId column might not exist yet. Using default starting number.");
                }

                int nextNumber;

                if (highestSapId != null)
                {
                    // Extract the numeric part and increment
                    if (int.TryParse(highestSapId.Substring(UserPrefix.Length), out int currentNumber))
                    {
                        nextNumber = currentNumber + 1;
                    }
                    else
                    {
                        // If parsing fails, start from a default number
                        nextNumber = 10000;
                    }
                }
                else
                {
                    // If no existing SAP IDs, start from a default number
                    nextNumber = 10000;
                }

                // Format the new SAP ID
                string newSapId = $"{UserPrefix}{nextNumber.ToString().PadLeft(SAP_LENGTH - UserPrefix.Length, '0')}";

                _logger.LogInformation($"Generated new SAP ID: {newSapId}");
                return newSapId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating unique SAP ID");
                // Fallback to a random ID if there's an error
                return $"{UserPrefix}{new Random().Next(10000, 99999).ToString().PadLeft(6, '0')}";
            }
        }

        public async Task<string> GenerateUniqueSpecialUserIdAsync()
        {
            try
            {
                // Check both regular users and special users to ensure global uniqueness
                string highestRegularSapId = null;
                string highestSpecialSapId = null;

                try
                {
                    // Get highest SAP ID from regular users with special user prefix
                    highestRegularSapId = await _context.Users
                        .Where(u => u.SapId != null && u.SapId.StartsWith(SpecialUserPrefix))
                        .Select(u => u.SapId)
                        .OrderByDescending(id => id)
                        .FirstOrDefaultAsync();

                    // Get highest SAP ID from special users
                    highestSpecialSapId = await _context.SpecialUsers
                        .Where(su => su.UsersSapId != null && su.UsersSapId.StartsWith(SpecialUserPrefix))
                        .Select(su => su.UsersSapId)
                        .OrderByDescending(id => id)
                        .FirstOrDefaultAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error checking existing SAP IDs. Using default starting number.");
                }

                int nextNumber = 10000; // Default starting number

                // Find the highest number from both tables
                var allHighestIds = new[] { highestRegularSapId, highestSpecialSapId }
                    .Where(id => !string.IsNullOrEmpty(id))
                    .ToList();

                if (allHighestIds.Any())
                {
                    int maxNumber = 0;
                    foreach (var id in allHighestIds)
                    {
                        if (int.TryParse(id.Substring(SpecialUserPrefix.Length), out int currentNumber))
                        {
                            maxNumber = Math.Max(maxNumber, currentNumber);
                        }
                    }
                    nextNumber = maxNumber > 0 ? maxNumber + 1 : 10000;
                }

                // Format the new special user SAP ID
                string newSapId = $"{SpecialUserPrefix}{nextNumber.ToString().PadLeft(SAP_LENGTH - SpecialUserPrefix.Length, '0')}";

                // Double-check uniqueness across both tables
                bool isUnique = false;
                int attempts = 0;
                while (!isUnique && attempts < 10)
                {
                    var existsInUsers = await _context.Users.AnyAsync(u => u.SapId == newSapId);
                    var existsInSpecialUsers = await _context.SpecialUsers.AnyAsync(su => su.UsersSapId == newSapId);

                    if (!existsInUsers && !existsInSpecialUsers)
                    {
                        isUnique = true;
                    }
                    else
                    {
                        nextNumber++;
                        newSapId = $"{SpecialUserPrefix}{nextNumber.ToString().PadLeft(SAP_LENGTH - SpecialUserPrefix.Length, '0')}";
                        attempts++;
                    }
                }

                if (!isUnique)
                {
                    // Fallback to random if we can't find a unique ID
                    newSapId = $"{SpecialUserPrefix}{new Random().Next(10000, 99999).ToString().PadLeft(6, '0')}";
                }

                _logger.LogInformation($"Generated new special user SAP ID: {newSapId}");
                return newSapId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating unique special user SAP ID");
                // Fallback to a random ID if there's an error
                return $"{SpecialUserPrefix}{new Random().Next(10000, 99999).ToString().PadLeft(6, '0')}";
            }
        }

        public async Task<string> GenerateUniqueOrganizationIdAsync()
        {
            try
            {
                // Check all tables to ensure global uniqueness
                string highestOrganizationSapId = null;
                string highestUserSapId = null;
                string highestSpecialUserSapId = null;

                try
                {
                    // Get highest SAP ID from organizations with organization prefix
                    highestOrganizationSapId = await _context.Organizations
                        .Where(o => o.SapId != null && o.SapId.StartsWith(OrganizationPrefix))
                        .Select(o => o.SapId)
                        .OrderByDescending(id => id)
                        .FirstOrDefaultAsync();

                    // Get highest SAP ID from users with organization prefix (shouldn't exist, but check for safety)
                    highestUserSapId = await _context.Users
                        .Where(u => u.SapId != null && u.SapId.StartsWith(OrganizationPrefix))
                        .Select(u => u.SapId)
                        .OrderByDescending(id => id)
                        .FirstOrDefaultAsync();

                    // Get highest SAP ID from special users with organization prefix (shouldn't exist, but check for safety)
                    highestSpecialUserSapId = await _context.SpecialUsers
                        .Where(su => su.UsersSapId != null && su.UsersSapId.StartsWith(OrganizationPrefix))
                        .Select(su => su.UsersSapId)
                        .OrderByDescending(id => id)
                        .FirstOrDefaultAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error checking existing organization SAP IDs. Using default starting number.");
                }

                int nextNumber = 10000; // Default starting number

                // Find the highest number from all tables
                var allHighestIds = new[] { highestOrganizationSapId, highestUserSapId, highestSpecialUserSapId }
                    .Where(id => !string.IsNullOrEmpty(id))
                    .ToList();

                if (allHighestIds.Any())
                {
                    int maxNumber = 0;
                    foreach (var id in allHighestIds)
                    {
                        if (int.TryParse(id.Substring(OrganizationPrefix.Length), out int currentNumber))
                        {
                            maxNumber = Math.Max(maxNumber, currentNumber);
                        }
                    }
                    nextNumber = maxNumber > 0 ? maxNumber + 1 : 10000;
                }

                // Format the new organization SAP ID
                string newSapId = $"{OrganizationPrefix}{nextNumber.ToString().PadLeft(SAP_LENGTH - OrganizationPrefix.Length, '0')}";

                // Double-check uniqueness across all tables
                bool isUnique = false;
                int attempts = 0;
                while (!isUnique && attempts < 10)
                {
                    var existsInUsers = await _context.Users.AnyAsync(u => u.SapId == newSapId);
                    var existsInSpecialUsers = await _context.SpecialUsers.AnyAsync(su => su.UsersSapId == newSapId);
                    var existsInOrganizations = await _context.Organizations.AnyAsync(o => o.SapId == newSapId);

                    if (!existsInUsers && !existsInSpecialUsers && !existsInOrganizations)
                    {
                        isUnique = true;
                    }
                    else
                    {
                        nextNumber++;
                        newSapId = $"{OrganizationPrefix}{nextNumber.ToString().PadLeft(SAP_LENGTH - OrganizationPrefix.Length, '0')}";
                        attempts++;
                    }
                }

                if (!isUnique)
                {
                    // Fallback to random if we can't find a unique ID
                    newSapId = $"{OrganizationPrefix}{new Random().Next(10000, 99999).ToString().PadLeft(6, '0')}";
                }

                _logger.LogInformation($"Generated new organization SAP ID: {newSapId}");
                return newSapId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating unique organization SAP ID");
                // Fallback to a random ID if there's an error
                return $"{OrganizationPrefix}{new Random().Next(10000, 99999).ToString().PadLeft(6, '0')}";
            }
        }
    }
}
