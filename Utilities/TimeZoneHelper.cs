using System;

namespace OnlineAssessment.Web.Utilities
{
    public static class TimeZoneHelper
    {
        private static readonly TimeZoneInfo IstTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

        /// <summary>
        /// Converts time to IST (Indian Standard Time)
        /// </summary>
        public static DateTime ToIst(this DateTime time)
        {
            // CRITICAL FIX: If this is a booking date (with or without time), preserve it exactly as is
            // This ensures that dates selected by users are not affected by timezone conversions
            if (time.Date == time || // Pure date with no time component
                time.Hour == 0 && time.Minute == 0 && time.Second == 0 || // Midnight
                time.Hour == 9 || time.Hour == 12 || time.Hour == 15 || time.Hour == 18) // Standard slot times
            {
                // This is a booking date or slot time, preserve it exactly as is
                return DateTime.SpecifyKind(time, DateTimeKind.Unspecified);
            }

            // If the time is already in UTC, convert it to IST
            if (time.Kind == DateTimeKind.Utc)
            {
                return TimeZoneInfo.ConvertTimeFromUtc(time, IstTimeZone);
            }

            // For other times, convert from local to IST
            return TimeZoneInfo.ConvertTime(time, IstTimeZone);
        }

        /// <summary>
        /// Converts IST (Indian Standard Time) to UTC
        /// </summary>
        public static DateTime ToUtc(this DateTime istTime)
        {
            if (istTime.Kind == DateTimeKind.Utc)
            {
                // Already UTC, return as is
                return istTime;
            }

            // Specify that this time is in IST time zone
            var istDateTime = DateTime.SpecifyKind(istTime, DateTimeKind.Unspecified);

            // Convert to UTC
            return TimeZoneInfo.ConvertTimeToUtc(istDateTime, IstTimeZone);
        }

        /// <summary>
        /// Gets the current time in IST
        /// </summary>
        public static DateTime GetCurrentIstTime()
        {
            // Always use UTC as the source for current time to ensure consistency
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IstTimeZone);
        }

        /// <summary>
        /// Format a DateTime as a string in IST format
        /// </summary>
        public static string FormatIstDateTime(this DateTime? dateTime, string format = "yyyy-MM-dd HH:mm:ss")
        {
            if (!dateTime.HasValue)
                return string.Empty;

            // Apply the same logic as ToIst but for nullable DateTime
            DateTime time = dateTime.Value;
            DateTime istTime;

            // CRITICAL FIX: If this is a booking date (with or without time), preserve it exactly as is
            // This ensures that dates selected by users are not affected by timezone conversions
            if (time.Date == time || // Pure date with no time component
                time.Hour == 0 && time.Minute == 0 && time.Second == 0 || // Midnight
                time.Hour == 9 || time.Hour == 12 || time.Hour == 15 || time.Hour == 18) // Standard slot times
            {
                // This is a booking date or slot time, preserve it exactly as is
                istTime = DateTime.SpecifyKind(time, DateTimeKind.Unspecified);
            }
            else if (time.Kind == DateTimeKind.Utc)
            {
                istTime = TimeZoneInfo.ConvertTimeFromUtc(time, IstTimeZone);
            }
            else
            {
                istTime = TimeZoneInfo.ConvertTime(time, IstTimeZone);
            }

            return istTime.ToString(format);
        }
    }
}
