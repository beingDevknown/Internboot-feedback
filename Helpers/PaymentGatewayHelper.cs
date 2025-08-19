using System;
using Microsoft.AspNetCore.Mvc;

namespace OnlineAssessment.Web.Helpers
{
    /// <summary>
    /// Helper class for payment gateway operations
    /// </summary>
    public static class PaymentGatewayHelper
    {
        /// <summary>
        /// Generates a URL to open the payment gateway
        /// </summary>
        /// <param name="urlHelper">The URL helper to generate URLs</param>
        /// <param name="testId">The ID of the test to book</param>
        /// <param name="date">The booking date (yyyy-MM-dd)</param>
        /// <param name="startTime">The start time (HH:mm)</param>
        /// <param name="endTime">The end time (HH:mm)</param>
        /// <param name="slotNumber">The slot number</param>
        /// <param name="isReattempt">Whether this is a reattempt</param>
        /// <returns>A URL to open the payment gateway</returns>
        public static string GetPaymentGatewayUrl(
            IUrlHelper urlHelper,
            int testId,
            string date,
            string startTime,
            string endTime,
            int slotNumber,
            bool isReattempt = false)
        {
            return urlHelper.Action("OpenPaymentGateway", "Payment", new
            {
                testId,
                date,
                startTime,
                endTime,
                slotNumber,
                isReattempt
            });
        }
    }
}
