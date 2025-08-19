using System.Collections.Generic;
using System.Text.Json;

namespace OnlineAssessment.Web.Models
{
    /// <summary>
    /// Model for Razorpay payment request parameters
    /// </summary>
    public class RazorpayRequestModel
    {
        /// <summary>
        /// Dictionary containing all Razorpay parameters
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Razorpay order ID
        /// </summary>
        public string OrderId { get; set; } = string.Empty;

        /// <summary>
        /// Transaction ID
        /// </summary>
        public string TransactionId => Parameters.ContainsKey("receipt")
            ? Parameters["receipt"]?.ToString() ?? string.Empty
            : string.Empty;

        /// <summary>
        /// Payment amount in paise (multiply by 100)
        /// </summary>
        public int AmountInPaise => Parameters.ContainsKey("amount")
            ? int.Parse(Parameters["amount"]?.ToString() ?? "0")
            : 0;

        /// <summary>
        /// Payment amount in rupees
        /// </summary>
        public decimal Amount => AmountInPaise / 100m;

        /// <summary>
        /// Currency
        /// </summary>
        public string Currency => Parameters.ContainsKey("currency")
            ? Parameters["currency"]?.ToString() ?? "INR"
            : "INR";

        /// <summary>
        /// Test ID from notes
        /// </summary>
        public string TestId
        {
            get
            {
                if (Parameters.ContainsKey("notes") && Parameters["notes"] is Dictionary<string, string> notes)
                {
                    if (notes.ContainsKey("testId"))
                    {
                        return notes["testId"];
                    }
                }
                return string.Empty;
            }
        }

        /// <summary>
        /// Product info from notes
        /// </summary>
        public string ProductInfo
        {
            get
            {
                if (Parameters.ContainsKey("notes") && Parameters["notes"] is Dictionary<string, string> notes)
                {
                    if (notes.ContainsKey("productInfo"))
                    {
                        return notes["productInfo"];
                    }
                }
                return string.Empty;
            }
        }

        /// <summary>
        /// Customer name from notes
        /// </summary>
        public string CustomerName
        {
            get
            {
                if (Parameters.ContainsKey("notes") && Parameters["notes"] is Dictionary<string, string> notes)
                {
                    if (notes.ContainsKey("customerName"))
                    {
                        return notes["customerName"];
                    }
                }
                return string.Empty;
            }
        }

        /// <summary>
        /// Customer email from notes
        /// </summary>
        public string CustomerEmail
        {
            get
            {
                if (Parameters.ContainsKey("notes") && Parameters["notes"] is Dictionary<string, string> notes)
                {
                    if (notes.ContainsKey("customerEmail"))
                    {
                        return notes["customerEmail"];
                    }
                }
                return string.Empty;
            }
        }

        /// <summary>
        /// Customer phone from notes
        /// </summary>
        public string CustomerPhone
        {
            get
            {
                if (Parameters.ContainsKey("notes") && Parameters["notes"] is Dictionary<string, string> notes)
                {
                    if (notes.ContainsKey("customerPhone"))
                    {
                        return notes["customerPhone"];
                    }
                }
                return string.Empty;
            }
        }

        /// <summary>
        /// Checkout options for Razorpay
        /// </summary>
        public Dictionary<string, object> CheckoutOptions { get; set; } = new Dictionary<string, object>();
    }
}
