using Microsoft.EntityFrameworkCore;
using OnlineAssessment.Web.Models;

namespace OnlineAssessment.Web.Models
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) {}

        public DbSet<User> Users { get; set; }
        public DbSet<Test> Tests { get; set; }
        // Question and AnswerOption tables removed as they're no longer used
        // Questions are now stored in CategoryQuestions as JSON
        public DbSet<TestResult> TestResults { get; set; }
        // OrganizationTestResult table removed as it's redundant with TestResult
        public DbSet<Payment> Payments { get; set; }
        public DbSet<CategoryQuestions> CategoryQuestions { get; set; }
        public DbSet<Organization> Organizations { get; set; }
        public DbSet<TestBooking> TestBookings { get; set; }
        public DbSet<SpecialUser> SpecialUsers { get; set; }
        public DbSet<Feedback> Feedbacks { get; set; }


        public DbSet<CertificatePurchase> CertificatePurchases { get; set; }
        // SuperOrganization table has been removed, using Organization with IsSuperOrganization flag instead

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // ✅ Store UserRole Enum as a string in the database
            modelBuilder.Entity<User>()
                .Property(u => u.Role)
                .HasConversion<string>();

            // Add unique constraint on User.Email
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique()
                .HasDatabaseName("IX_Users_Email_Unique");

            // Add unique constraint on User.Username
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique()
                .HasDatabaseName("IX_Users_Username_Unique");

            // Add unique constraint on User.MobileNumber
            modelBuilder.Entity<User>()
                .HasIndex(u => u.MobileNumber)
                .IsUnique()
                .HasDatabaseName("IX_Users_MobileNumber_Unique");

            // Configure User-Organization relationship
            modelBuilder.Entity<User>()
                .HasOne<Organization>()
                .WithMany()
                .HasForeignKey(u => u.OrganizationSapId)
                .OnDelete(DeleteBehavior.SetNull);

            // Add unique constraint on Organization.OrganizationToken
            modelBuilder.Entity<Organization>()
                .HasIndex(o => o.OrganizationToken)
                .IsUnique()
                .HasDatabaseName("IX_Organizations_Token_Unique");

            // Add unique constraint on Organization.Email
            modelBuilder.Entity<Organization>()
                .HasIndex(o => o.Email)
                .IsUnique()
                .HasDatabaseName("IX_Organizations_Email_Unique");

            // Add unique constraint on Organization.Username
            modelBuilder.Entity<Organization>()
                .HasIndex(o => o.Username)
                .IsUnique()
                .HasDatabaseName("IX_Organizations_Username_Unique");

            // Add unique constraint on Organization.PhoneNumber
            modelBuilder.Entity<Organization>()
                .HasIndex(o => o.PhoneNumber)
                .IsUnique()
                .HasDatabaseName("IX_Organizations_PhoneNumber_Unique");

            // Ignore the AutoSubmitted property in TestResult to avoid database issues
            modelBuilder.Entity<TestResult>()
                .Ignore(tr => tr.AutoSubmitted);

            // Configure TestResult relationships to support both Users and SpecialUsers
            // Remove foreign key constraints to allow references to both Users and SpecialUsers tables
            // The relationships are handled manually in the service layer

            // Configure relationships without foreign key constraints
            modelBuilder.Entity<TestResult>()
                .HasOne(tr => tr.User)
                .WithMany()
                .HasPrincipalKey(u => u.SapId)
                .HasForeignKey(tr => tr.UserSapId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            modelBuilder.Entity<TestResult>()
                .HasOne(tr => tr.SpecialUser)
                .WithMany()
                .HasPrincipalKey(su => su.UsersSapId)
                .HasForeignKey(tr => tr.UserSapId)
                .OnDelete(DeleteBehavior.NoAction)
                .IsRequired(false);

            // Configure SpecialUser primary key
            modelBuilder.Entity<SpecialUser>()
                .HasKey(su => su.UsersSapId);

            // Add unique constraint on SpecialUser.Email
            modelBuilder.Entity<SpecialUser>()
                .HasIndex(su => su.Email)
                .IsUnique()
                .HasDatabaseName("IX_SpecialUsers_Email_Unique");

            // Configure foreign key relationship for SpecialUser to Organization
            modelBuilder.Entity<SpecialUser>()
                .HasOne(su => su.Organization)
                .WithMany()
                .HasForeignKey(su => su.OrganizationSapId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure CertificatePurchase relationships
            modelBuilder.Entity<CertificatePurchase>()
                .HasOne(cp => cp.TestResult)
                .WithMany()
                .HasForeignKey(cp => cp.TestResultId)
                .OnDelete(DeleteBehavior.Cascade);

            // Note: UserSapId foreign key constraint removed to allow references to both Users and SpecialUsers tables
            // The relationship is handled manually in the service layer

            // Add unique constraint to prevent duplicate certificate purchases for the same test result
            modelBuilder.Entity<CertificatePurchase>()
                .HasIndex(cp => cp.TestResultId)
                .IsUnique()
                .HasDatabaseName("IX_CertificatePurchases_TestResultId_Unique");



            // Add a composite index on Category and CreatedBySapId for faster category questions queries
            modelBuilder.Entity<CategoryQuestions>()
                .HasIndex(cq => new { cq.Category, cq.CreatedBySapId })
                .HasDatabaseName("IX_CategoryQuestions_Category_CreatedBySapId");

            // Add an index on QuestionsJson to improve full-text search performance
            modelBuilder.Entity<CategoryQuestions>()
                .HasIndex(cq => cq.CreatedAt)
                .HasDatabaseName("IX_CategoryQuestions_CreatedAt");

            // Remove unique constraint on TestBookings to allow multiple bookings
            // Create a non-unique index for performance
            modelBuilder.Entity<TestBooking>()
                .HasIndex(tb => new { tb.TestId, tb.UserSapId, tb.Status })
                .HasDatabaseName("IX_TestBookings_TestId_UserSapId_Status");

         modelBuilder.Entity<Feedback>()
                .HasOne(f => f.User)
    .WithMany()  // or .WithMany(u => u.Feedbacks) if you add a collection in User
    .HasForeignKey(f => f.Username)  // Use Username as FK now
    .HasPrincipalKey(u => u.Username)  // Reference User.Username as PK for this FK
    .OnDelete(DeleteBehavior.Cascade);



            base.OnModelCreating(modelBuilder);
        }
    }
}
