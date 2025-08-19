using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OnlineAssessment.Web.Helpers;

namespace OnlineAssessment.Web.Services
{
    /// <summary>
    /// Interface for Razorpay payment gateway integration
    /// </summary>
    public interface IRazorpayService
    {
        string GenerateTransactionId();
        Dictionary<string, object> PrepareOrderRequest(string txnid, string amount, string productinfo, string firstname, string email, string phone, string? testId = null);
        Dictionary<string, object> PrepareCheckoutOptions(string orderId, string amount, string productinfo, string firstname, string email, string phone, string? testId = null);
        Task<(bool Success, string OrderId, string ErrorMessage)> CreateOrderAsync(Dictionary<string, object> orderRequest);
        bool VerifyPaymentSignature(string orderId, string paymentId, string signature);
        bool VerifyWebhookSignature(string payload, string signature, string timestamp);
        Task<(bool Success, Dictionary<string, object> PaymentDetails, string ErrorMessage)> FetchPaymentAsync(string paymentId);
        Task<string> VerifyPaymentStatusAsync(string paymentId);
    }

    /// <summary>
    /// Service for Razorpay payment gateway integration
    /// </summary>
    public class RazorpayService : IRazorpayService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<RazorpayService> _logger;

        /// <summary>
        /// Constructor for RazorpayService
        /// </summary>
        public RazorpayService(IHttpClientFactory httpClientFactory, ILogger<RazorpayService> logger)
        {
            _httpClient = httpClientFactory.CreateClient("Razorpay");
            _logger = logger;
        }

        /// <summary>
        /// Generates a unique transaction ID
        /// </summary>
        public string GenerateTransactionId()
        {
            return RazorpayHelper.GenerateTransactionId();
        }

        /// <summary>
        /// Prepares a Razorpay order request with all required parameters
        /// </summary>
        public Dictionary<string, object> PrepareOrderRequest(string txnid, string amount, string productinfo, string firstname, string email, string phone, string? testId = null)
        {
            return RazorpayHelper.PrepareOrderRequest(txnid, amount, productinfo, firstname, email, phone, testId);
        }

        /// <summary>
        /// Prepares Razorpay checkout options
        /// </summary>
        public Dictionary<string, object> PrepareCheckoutOptions(string orderId, string amount, string productinfo, string firstname, string email, string phone, string? testId = null)
        {
            return RazorpayHelper.PrepareCheckoutOptions(orderId, amount, productinfo, firstname, email, phone, testId);
        }

        /// <summary>
        /// Creates a Razorpay order
        /// </summary>
        public async Task<(bool Success, string OrderId, string ErrorMessage)> CreateOrderAsync(Dictionary<string, object> orderRequest)
        {
            try
            {
                // Convert order request to JSON
                var payload = JsonSerializer.Serialize(orderRequest);
                _logger.LogInformation("Razorpay order request payload: {Payload}", payload);

                // Create the request content
                var content = new StringContent(payload, Encoding.UTF8, "application/json");

                // Set authentication headers
                string authString = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{RazorpayHelper.GetKeyId()}:{RazorpayHelper.GetKeySecret()}"));
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {authString}");

                // Send request to Razorpay API
                var response = await _httpClient.PostAsync("https://api.razorpay.com/v1/orders", content);
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Razorpay API response: {Response}", responseContent);

                // Check if the request was successful
                if (response.IsSuccessStatusCode)
                {
                    // Parse the response
                    var responseJson = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseContent);
                    if (responseJson != null && responseJson.TryGetValue("id", out var orderId))
                    {
                        string? orderIdStr = orderId.GetString();
                        return (true, orderIdStr ?? string.Empty, string.Empty);
                    }
                    else
                    {
                        return (false, string.Empty, "Order ID not found in response");
                    }
                }
                else
                {
                    return (false, string.Empty, $"Error creating order: {responseContent}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating Razorpay order");
                return (false, string.Empty, $"Error creating order: {ex.Message}");
            }
        }

        /// <summary>
        /// Verifies Razorpay payment signature
        /// </summary>
        public bool VerifyPaymentSignature(string orderId, string paymentId, string signature)
        {
            try
            {
                return RazorpayHelper.VerifyPaymentSignature(orderId, paymentId, signature);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying Razorpay payment signature");
                return false;
            }
        }

        /// <summary>
        /// Verifies Razorpay webhook signature
        /// </summary>
        public bool VerifyWebhookSignature(string payload, string signature, string timestamp)
        {
            try
            {
                return RazorpayHelper.VerifyWebhookSignature(payload, signature, timestamp);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying Razorpay webhook signature");
                return false;
            }
        }

        /// <summary>
        /// Fetches payment details from Razorpay
        /// </summary>
        public async Task<(bool Success, Dictionary<string, object> PaymentDetails, string ErrorMessage)> FetchPaymentAsync(string paymentId)
        {
            try
            {
                // Set authentication headers
                string authString = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{RazorpayHelper.GetKeyId()}:{RazorpayHelper.GetKeySecret()}"));
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {authString}");

                // Send request to Razorpay API
                var response = await _httpClient.GetAsync($"https://api.razorpay.com/v1/payments/{paymentId}");
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Razorpay payment fetch response: {Response}", responseContent);

                // Check if the request was successful
                if (response.IsSuccessStatusCode)
                {
                    // Parse the response
                    var paymentDetails = JsonSerializer.Deserialize<Dictionary<string, object>>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    return (true, paymentDetails ?? new Dictionary<string, object>(), string.Empty);
                }
                else
                {
                    return (false, new Dictionary<string, object>(), $"Error fetching payment: {responseContent}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching Razorpay payment");
                return (false, new Dictionary<string, object>(), $"Error fetching payment: {ex.Message}");
            }
        }

        /// <summary>
        /// Verifies the status of a payment with Razorpay
        /// </summary>
        /// <param name="paymentId">The Razorpay payment ID to verify</param>
        /// <returns>The payment status (captured, authorized, failed, etc.)</returns>
        public async Task<string> VerifyPaymentStatusAsync(string paymentId)
        {
            try
            {
                _logger.LogInformation("Verifying payment status for payment ID: {PaymentId}", paymentId);

                // Fetch the payment details from Razorpay
                var (success, paymentDetails, errorMessage) = await FetchPaymentAsync(paymentId);

                if (success && paymentDetails != null)
                {
                    // Extract the status from the payment details
                    if (paymentDetails.TryGetValue("status", out var statusObj) && statusObj != null)
                    {
                        string status = statusObj.ToString() ?? "unknown";
                        _logger.LogInformation("Payment status for {PaymentId}: {Status}", paymentId, status);
                        return status;
                    }
                    else
                    {
                        _logger.LogWarning("Payment status not found in response for payment ID: {PaymentId}", paymentId);
                        return "unknown";
                    }
                }
                else
                {
                    _logger.LogWarning("Failed to fetch payment details for payment ID: {PaymentId}. Error: {Error}",
                        paymentId, errorMessage);
                    return "error";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying payment status for payment ID: {PaymentId}", paymentId);
                return "error";
            }
        }
    }
}
