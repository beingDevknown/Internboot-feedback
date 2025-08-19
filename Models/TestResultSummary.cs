using System;
using System.Collections.Generic;

namespace OnlineAssessment.Web.Models
{
    public class TestResultSummary
    {
        public int TestId { get; set; }
        
        public string TestTitle { get; set; }
        
        public string Username { get; set; }
        
        public string UserSapId { get; set; }
        
        public int TotalAttempts { get; set; }
        
        public double BestScore { get; set; }
        
        public double AverageScore { get; set; }
        
        public DateTime LastAttemptDate { get; set; }
        
        // Test time information
        public DateTime? StartTime { get; set; }
        
        public DateTime? EndTime { get; set; }
        
        // Collection of all attempts for this user and test
        public List<TestResult> Attempts { get; set; }
    }
}
