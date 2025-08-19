using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineAssessment.Web.Models
{
    public class TestBooking
    {
        [Key]
        public int Id { get; set; }

        public int TestId { get; set; }

        // User SAP ID is now the foreign key to User table
        [Required]
        public string UserSapId { get; set; }

        public DateTime BookedAt { get; set; } = Utilities.TimeZoneHelper.GetCurrentIstTime();

        // Optional booking date (no longer required)
        public DateTime? BookingDate { get; set; }

        // These properties are kept for backward compatibility but will be removed from the database
        [NotMapped]
        public DateTime? StartTime { get; set; }

        [NotMapped]
        public DateTime? EndTime { get; set; }

        // This property is kept for backward compatibility but will be removed from the database
        [NotMapped]
        public int? SlotNumber { get; set; }

        // Status of the booking (Pending, Confirmed, Cancelled, Failed, Superseded, etc)
        public string Status { get; set; } = "Pending";

        // Transaction ID from payment gateway
        public string? TransactionId { get; set; }

        // When the booking was last updated
        public DateTime? UpdatedAt { get; set; }

        // Reason for status change (e.g., "Payment failed", "Duplicate booking", etc.)
        public string? StatusReason { get; set; }

        [ForeignKey("TestId")]
        public virtual Test? Test { get; set; }

        // Removed foreign key constraint to User since we now support both regular users and special users
        // UserSapId can reference either users.SapId or specialusers.SapId
        // Navigation property removed to avoid foreign key constraint issues

        // Non-database property to indicate whether the test can be started
        // Now defaults to true for confirmed bookings regardless of time
        [NotMapped]
        public bool CanStartTest { get; set; }

        // Helper method to determine if a test can be started
        public bool CanStart()
        {
            // A test can be started if the booking is confirmed, regardless of time
            return Status == "Confirmed" || Status == "Completed";
        }
    }
}
