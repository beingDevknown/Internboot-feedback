using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace OnlineAssessment.Web.Services
{
    public interface IRateLimitingService
    {
        bool IsAllowed(string email, string ipAddress);
        void RecordAttempt(string email, string ipAddress);
    }

    public class RateLimitingService : IRateLimitingService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<RateLimitingService> _logger;
        
        // Track attempts by email
        private readonly ConcurrentDictionary<string, List<DateTime>> _emailAttempts = new();
        
        // Track attempts by IP address
        private readonly ConcurrentDictionary<string, List<DateTime>> _ipAttempts = new();
        
        // Configuration values
        private readonly int _maxRequestsPerEmail;
        private readonly int _maxRequestsPerIp;
        private readonly TimeSpan _timeWindow;
        
        // Timer for cleanup of old entries
        private readonly Timer _cleanupTimer;

        public RateLimitingService(IConfiguration configuration, ILogger<RateLimitingService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            
            // Load configuration values
            var rateLimitingSection = _configuration.GetSection("RateLimiting");
            _maxRequestsPerEmail = rateLimitingSection.GetValue<int>("MaxRequestsPerEmail");
            _maxRequestsPerIp = rateLimitingSection.GetValue<int>("MaxRequestsPerIp");
            int timeWindowMinutes = rateLimitingSection.GetValue<int>("TimeWindowMinutes");
            _timeWindow = TimeSpan.FromMinutes(timeWindowMinutes);
            
            // Set up cleanup timer (runs every 10 minutes)
            _cleanupTimer = new Timer(CleanupOldEntries, null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
            
            _logger.LogInformation($"Rate limiting initialized: {_maxRequestsPerEmail} per email, {_maxRequestsPerIp} per IP in {timeWindowMinutes} minutes");
        }

        public bool IsAllowed(string email, string ipAddress)
        {
            // Check if email is allowed
            if (!IsEmailAllowed(email))
            {
                _logger.LogWarning($"Rate limit exceeded for email: {email}");
                return false;
            }
            
            // Check if IP is allowed
            if (!IsIpAllowed(ipAddress))
            {
                _logger.LogWarning($"Rate limit exceeded for IP: {ipAddress}");
                return false;
            }
            
            return true;
        }

        public void RecordAttempt(string email, string ipAddress)
        {
            // Record email attempt
            _emailAttempts.AddOrUpdate(
                email,
                new List<DateTime> { DateTime.UtcNow },
                (_, attempts) =>
                {
                    attempts.Add(DateTime.UtcNow);
                    return attempts;
                });
            
            // Record IP attempt
            _ipAttempts.AddOrUpdate(
                ipAddress,
                new List<DateTime> { DateTime.UtcNow },
                (_, attempts) =>
                {
                    attempts.Add(DateTime.UtcNow);
                    return attempts;
                });
        }

        private bool IsEmailAllowed(string email)
        {
            if (string.IsNullOrEmpty(email))
                return false;
                
            if (!_emailAttempts.TryGetValue(email, out var attempts))
                return true;
                
            // Count attempts within the time window
            int recentAttempts = CountRecentAttempts(attempts);
            return recentAttempts < _maxRequestsPerEmail;
        }

        private bool IsIpAllowed(string ipAddress)
        {
            if (string.IsNullOrEmpty(ipAddress))
                return false;
                
            if (!_ipAttempts.TryGetValue(ipAddress, out var attempts))
                return true;
                
            // Count attempts within the time window
            int recentAttempts = CountRecentAttempts(attempts);
            return recentAttempts < _maxRequestsPerIp;
        }

        private int CountRecentAttempts(List<DateTime> attempts)
        {
            DateTime cutoff = DateTime.UtcNow.Subtract(_timeWindow);
            lock (attempts)
            {
                // Remove old attempts
                attempts.RemoveAll(time => time < cutoff);
                
                // Return count of remaining attempts
                return attempts.Count;
            }
        }

        private void CleanupOldEntries(object state)
        {
            try
            {
                DateTime cutoff = DateTime.UtcNow.Subtract(_timeWindow);
                
                // Cleanup email attempts
                foreach (var email in _emailAttempts.Keys)
                {
                    if (_emailAttempts.TryGetValue(email, out var attempts))
                    {
                        lock (attempts)
                        {
                            attempts.RemoveAll(time => time < cutoff);
                            if (attempts.Count == 0)
                            {
                                _emailAttempts.TryRemove(email, out _);
                            }
                        }
                    }
                }
                
                // Cleanup IP attempts
                foreach (var ip in _ipAttempts.Keys)
                {
                    if (_ipAttempts.TryGetValue(ip, out var attempts))
                    {
                        lock (attempts)
                        {
                            attempts.RemoveAll(time => time < cutoff);
                            if (attempts.Count == 0)
                            {
                                _ipAttempts.TryRemove(ip, out _);
                            }
                        }
                    }
                }
                
                _logger.LogDebug($"Rate limiting cleanup completed. Tracking {_emailAttempts.Count} emails and {_ipAttempts.Count} IPs");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during rate limiting cleanup: {ex.Message}");
            }
        }
    }
}
