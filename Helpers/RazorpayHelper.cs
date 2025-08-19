using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace OnlineAssessment.Web.Helpers
{
    /// <summary>
    /// Static helper class for Razorpay payment gateway integration
    /// </summary>
    public static class RazorpayHelper
    {
        // Razorpay configuration
        private static string KeyId { get; set; } = string.Empty;
        private static string KeySecret { get; set; } = string.Empty;
        private static string WebhookSecret { get; set; } = string.Empty;
        private static bool IsProduction { get; set; }

        // Application URL for dynamic callback URL generation
        private static string ApplicationUrl { get; set; } = string.Empty;
        private static string CallbackUrl { get; set; } = string.Empty;
        private static string WebhookUrl { get; set; } = string.Empty;

        /// <summary>
        /// Initializes the RazorpayHelper with configuration
        /// </summary>
        public static void Initialize(IConfiguration configuration)
        {
            KeyId = configuration["Razorpay:KeyId"] ?? string.Empty;
            KeySecret = configuration["Razorpay:KeySecret"] ?? string.Empty;
            WebhookSecret = configuration["Razorpay:WebhookSecret"] ?? string.Empty;
            IsProduction = configuration.GetValue<bool>("Razorpay:IsProduction", false);
            ApplicationUrl = configuration["ApplicationUrl"] ?? string.Empty;

            // Set default URLs based on application URL
            CallbackUrl = $"{ApplicationUrl}/Payment/RazorpayCallback";
            WebhookUrl = $"{ApplicationUrl}/Payment/RazorpayWebhook";
        }

        /// <summary>
        /// Ensures the helper is initialized
        /// </summary>
        private static void EnsureInitialized()
        {
            if (string.IsNullOrEmpty(KeyId))
            {
                throw new InvalidOperationException("RazorpayHelper is not initialized. Call Initialize method first.");
            }
        }

        /// <summary>
        /// Gets the Razorpay Key ID
        /// </summary>
        public static string GetKeyId()
        {
            EnsureInitialized();
            return KeyId;
        }

        /// <summary>
        /// Gets the Razorpay Key Secret
        /// </summary>
        public static string GetKeySecret()
        {
            EnsureInitialized();
            return KeySecret;
        }

        /// <summary>
        /// Gets the callback URL for Razorpay
        /// </summary>
        public static string GetCallbackUrl()
        {
            EnsureInitialized();
            return CallbackUrl;
        }

        /// <summary>
        /// Gets the webhook URL for Razorpay
        /// </summary>
        public static string GetWebhookUrl()
        {
            EnsureInitialized();
            return WebhookUrl;
        }

        /// <summary>
        /// Generates a unique transaction ID
        /// </summary>
        public static string GenerateTransactionId()
        {
            return $"txn_{DateTime.UtcNow.Ticks}";
        }

        /// <summary>
        /// Prepares a Razorpay order request with all required parameters
        /// </summary>
        public static Dictionary<string, object> PrepareOrderRequest(string txnid, string amount, string productinfo, string firstname, string email, string phone, string? testId = null)
        {
            EnsureInitialized();

            // Convert amount to paise (multiply by 100)
            var amountInPaise = (int)(decimal.Parse(amount) * 100);

            // Create order request
            var orderRequest = new Dictionary<string, object>
            {
                ["amount"] = amountInPaise,
                ["currency"] = "INR",
                ["receipt"] = txnid,
                ["notes"] = new Dictionary<string, string>
                {
                    ["productInfo"] = productinfo,
                    ["customerName"] = firstname,
                    ["customerEmail"] = email,
                    ["customerPhone"] = phone,
                    ["testId"] = testId ?? string.Empty
                }
            };

            return orderRequest;
        }

        /// <summary>
        /// Prepares Razorpay checkout options
        /// </summary>
        public static Dictionary<string, object> PrepareCheckoutOptions(string orderId, string amount, string productinfo, string firstname, string email, string phone, string? testId = null)
        {
            EnsureInitialized();

            // Convert amount to paise (multiply by 100)
            var amountInPaise = (int)(decimal.Parse(amount) * 100);

            // Create checkout options
            var options = new Dictionary<string, object>
            {
                ["key"] = KeyId,
                ["amount"] = amountInPaise,
                ["currency"] = "INR",
                ["name"] = "Online Assessment",
                ["description"] = productinfo,
                ["order_id"] = orderId,
                ["prefill"] = new Dictionary<string, string>
                {
                    ["name"] = firstname,
                    ["email"] = email,
                    ["contact"] = phone
                },
                ["notes"] = new Dictionary<string, string>
                {
                    ["testId"] = testId ?? string.Empty,
                    ["productInfo"] = productinfo,
                    ["customerName"] = firstname,
                    ["customerEmail"] = email,
                    ["customerPhone"] = phone
                },
                ["theme"] = new Dictionary<string, string>
                {
                    ["color"] = "#3399cc"
                },
                ["callback_url"] = $"{ApplicationUrl}/Payment/RazorpayCallback",
                ["redirect"] = true
            };

            return options;
        }

        /// <summary>
        /// Verifies Razorpay payment signature
        /// </summary>
        public static bool VerifyPaymentSignature(string orderId, string paymentId, string signature)
        {
            EnsureInitialized();

            try
            {
                // Generate the expected signature
                string payload = $"{orderId}|{paymentId}";
                string expectedSignature = GenerateSignature(payload, KeySecret);

                // Compare the signatures
                return string.Equals(expectedSignature, signature, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Verifies Razorpay webhook signature
        /// </summary>
        public static bool VerifyWebhookSignature(string payload, string signature, string timestamp)
        {
            EnsureInitialized();

            try
            {
                // Generate the expected signature
                string data = $"{timestamp}|{payload}";
                string expectedSignature = GenerateSignature(data, WebhookSecret);

                // Compare the signatures
                return string.Equals(expectedSignature, signature, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Generates HMAC SHA256 signature
        /// </summary>
        private static string GenerateSignature(string payload, string secret)
        {
            using (HMACSHA256 hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
            {
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }
    }
}
