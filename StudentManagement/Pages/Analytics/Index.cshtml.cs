using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StudentManagement.Models;
using StudentManagement.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Logging;

namespace StudentManagement.Pages.Analytics
{
    public class IndexModel : PageModel
    {
        private readonly StudentService _studentService;
        private readonly GradeService _gradeService;
        private readonly FeeCalculationService _feeService;
        private readonly FirestoreDb _db;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(
            StudentService studentService,
            GradeService gradeService,
            FeeCalculationService feeService,
            FirestoreDb db,
            ILogger<IndexModel> logger)
        {
            _studentService = studentService;
            _gradeService = gradeService;
            _feeService = feeService;
            _db = db;
            _logger = logger;
        }

        // Fee Collection Analytics
        public double TotalCollected { get; set; }
        public double TotalOutstanding { get; set; }
        public double TotalExpected { get; set; }
        public double CollectionRate { get; set; }
        public List<GradeCollectionStats> CollectionByGrade { get; set; } = new();

        // Outstanding Balances
        public List<Student> OverdueStudents { get; set; } = new();
        public Dictionary<string, int> AgingBuckets { get; set; } = new();

        // Revenue Trends
        public List<RevenueDataPoint> MonthlyRevenue { get; set; } = new();
        public List<YearComparison> YearOverYearComparison { get; set; } = new();

        // Enrollment Statistics
        public int TotalActiveStudents { get; set; }
        public int NewAdmissionsThisYear { get; set; }
        public int WithdrawalsThisYear { get; set; }
        public int GraduatedThisYear { get; set; }
        public List<GradeEnrollment> EnrollmentByGrade { get; set; } = new();
        public List<EnrollmentTrend> EnrollmentHistory { get; set; } = new();

        // Demographics
        public List<AgeDistribution> AgeDistribution { get; set; } = new();
        public Dictionary<string, int> GradeDistribution { get; set; } = new();

        // Operational KPIs
        public double AverageBalancePerStudent { get; set; }
        public double AveragePaymentPerStudent { get; set; }
        public int StudentsWithFullPayment { get; set; }
        public int StudentsWithPartialPayment { get; set; }
        public int StudentsWithNoPayment { get; set; }

        // At-Risk Students
        public List<AtRiskStudent> AtRiskStudents { get; set; } = new();

        // Scholarship/Discount Impact
        public double TotalDiscountsApplied { get; set; }
        public int StudentsWithDiscounts { get; set; }

        public string CurrentAcademicYear { get; set; } = DateTime.Now.Year.ToString();
        public string CurrentTerm { get; set; } = "Term 1";

        public async Task OnGetAsync()
        {
            try
            {
                CurrentAcademicYear = DateTime.Now.Year.ToString();
                CurrentTerm = DetermineCurrentTerm();

                await LoadFeeCollectionDataAsync();
                await LoadOutstandingBalancesAsync();
                await LoadRevenueTrendsAsync();
                await LoadEnrollmentStatisticsAsync();
                await LoadDemographicsAsync();
                await LoadOperationalKPIsAsync();
                await LoadAtRiskStudentsAsync();
                await LoadDiscountImpactAsync();

                _logger.LogInformation("Analytics dashboard loaded successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading analytics data");
            }
        }

        private async Task LoadFeeCollectionDataAsync()
        {
            var activeStudents = await _studentService.GetStudentsAsync(statusFilter: "active", pageSize: 1000);
            var allStudents = await _studentService.GetStudentsAsync(statusFilter: "all", pageSize: 1000);

            TotalCollected = allStudents.Sum(s => s.TotalPaid);
            TotalOutstanding = allStudents.Sum(s => s.CurrentBalance);
            TotalExpected = TotalCollected + TotalOutstanding;
            CollectionRate = TotalExpected > 0 ? (TotalCollected / TotalExpected) * 100 : 0;

            // By grade
            var gradeGroups = allStudents
                .Where(s => !string.IsNullOrEmpty(s.Grade))
                .GroupBy(s => s.Grade)
                .OrderBy(g => g.Key);

            foreach (var group in gradeGroups)
            {
                var collected = group.Sum(s => s.TotalPaid);
                var outstanding = group.Sum(s => s.CurrentBalance);
                var expected = collected + outstanding;

                CollectionByGrade.Add(new GradeCollectionStats
                {
                    Grade = group.Key,
                    StudentCount = group.Count(),
                    Collected = collected,
                    Outstanding = outstanding,
                    Expected = expected,
                    CollectionRate = expected > 0 ? (collected / expected) * 100 : 0
                });
            }
        }

        private async Task LoadOutstandingBalancesAsync()
        {
            var allStudents = await _studentService.GetStudentsAsync(statusFilter: "all", pageSize: 1000);

            OverdueStudents = allStudents
                .Where(s => s.CurrentBalance > 0 && s.PaymentStatus != "Paid")
                .OrderByDescending(s => s.CurrentBalance)
                .Take(50)
                .ToList();

            // Aging buckets (simplified - based on enrollment year as proxy)
            var currentYear = DateTime.Now.Year;
            AgingBuckets = new Dictionary<string, int>
            {
                ["Current Term"] = allStudents.Count(s => s.LastTermRolloverKey?.Contains(currentYear.ToString()) == true && s.CurrentBalance > 0),
                ["1-2 Terms"] = allStudents.Count(s => s.CurrentBalance > 0 && (s.LastTermRolloverKey == null || !s.LastTermRolloverKey.Contains(currentYear.ToString()))),
                ["3+ Terms"] = allStudents.Count(s => s.CurrentBalance > 0 && s.EnrollmentYear < currentYear - 1)
            };
        }

        private async Task LoadRevenueTrendsAsync()
        {
            var allStudents = await _studentService.GetStudentsAsync(statusFilter: "all", pageSize: 1000);

            // Monthly revenue (based on CreatedAt as proxy for admission month)
            var monthlyData = allStudents
                .GroupBy(s => s.CreatedAt.ToDateTime().ToString("MMM yyyy"))
                .Select(g => new RevenueDataPoint
                {
                    Month = g.Key,
                    Revenue = g.Sum(s => s.TotalPaid),
                    NewStudents = g.Count()
                })
                .OrderBy(r => r.Month)
                .ToList();

            MonthlyRevenue = monthlyData;

            // Year over year
            var yearGroups = allStudents
                .GroupBy(s => s.CreatedAt.ToDateTime().Year)
                .OrderBy(g => g.Key);

            foreach (var group in yearGroups)
            {
                var prevYear = group.Key - 1;
                var prevGroup = allStudents.Where(s => s.CreatedAt.ToDateTime().Year == prevYear);

                YearOverYearComparison.Add(new YearComparison
                {
                    Year = group.Key,
                    TotalRevenue = group.Sum(s => s.TotalPaid),
                    StudentCount = group.Count(),
                    PreviousYearRevenue = prevGroup.Sum(s => s.TotalPaid),
                    GrowthRate = prevGroup.Any() ? 
                        ((group.Sum(s => s.TotalPaid) - prevGroup.Sum(s => s.TotalPaid)) / prevGroup.Sum(s => s.TotalPaid)) * 100 : 0
                });
            }
        }

        private async Task LoadEnrollmentStatisticsAsync()
        {
            var allStudents = await _studentService.GetStudentsAsync(statusFilter: "all", pageSize: 1000);
            var currentYear = DateTime.Now.Year;

            TotalActiveStudents = allStudents.Count(s => s.Status == "active");
            NewAdmissionsThisYear = allStudents.Count(s => s.CreatedAt.ToDateTime().Year == currentYear);
            WithdrawalsThisYear = allStudents.Count(s => s.Status == "inactive" && s.UpdatedAt.ToDateTime().Year == currentYear);
            GraduatedThisYear = allStudents.Count(s => s.Status == "graduated" || s.Grade?.Contains("Graduated") == true);

            // By grade
            var gradeGroups = allStudents
                .Where(s => !string.IsNullOrEmpty(s.Grade))
                .GroupBy(s => s.Grade)
                .OrderBy(g => g.Key);

            foreach (var group in gradeGroups)
            {
                EnrollmentByGrade.Add(new GradeEnrollment
                {
                    Grade = group.Key,
                    ActiveCount = group.Count(s => s.Status == "active"),
                    InactiveCount = group.Count(s => s.Status == "inactive"),
                    TotalCount = group.Count()
                });
            }

            // Enrollment history by year
            var yearGroups = allStudents
                .GroupBy(s => s.CreatedAt.ToDateTime().Year)
                .OrderBy(g => g.Key)
                .Take(5);

            foreach (var group in yearGroups)
            {
                EnrollmentHistory.Add(new EnrollmentTrend
                {
                    Year = group.Key,
                    NewStudents = group.Count(),
                    ActiveStudents = allStudents.Count(s => s.Status == "active" && s.CreatedAt.ToDateTime().Year <= group.Key),
                    TotalStudents = allStudents.Count(s => s.CreatedAt.ToDateTime().Year <= group.Key)
                });
            }
        }

        private async Task LoadDemographicsAsync()
        {
            var allStudents = await _studentService.GetStudentsAsync(statusFilter: "all", pageSize: 1000);

            // Age distribution
            var ageGroups = allStudents
                .GroupBy(s => s.Age)
                .OrderBy(g => g.Key)
                .Select(g => new AgeDistribution
                {
                    Age = g.Key,
                    Count = g.Count(),
                    Percentage = allStudents.Any() ? (g.Count() / (double)allStudents.Count()) * 100 : 0
                });

            AgeDistribution = ageGroups.ToList();

            // Grade distribution
            GradeDistribution = allStudents
                .Where(s => !string.IsNullOrEmpty(s.GradeLevel))
                .GroupBy(s => s.GradeLevel)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        private async Task LoadOperationalKPIsAsync()
        {
            var allStudents = await _studentService.GetStudentsAsync(statusFilter: "all", pageSize: 1000);

            AverageBalancePerStudent = allStudents.Any() ? allStudents.Average(s => s.CurrentBalance) : 0;
            AveragePaymentPerStudent = allStudents.Any() ? allStudents.Average(s => s.TotalPaid) : 0;

            StudentsWithFullPayment = allStudents.Count(s => s.PaymentStatus == "Paid");
            StudentsWithPartialPayment = allStudents.Count(s => s.PaymentStatus == "Partial");
            StudentsWithNoPayment = allStudents.Count(s => s.PaymentStatus == "Outstanding" || s.TotalPaid == 0);
        }

        private async Task LoadAtRiskStudentsAsync()
        {
            var allStudents = await _studentService.GetStudentsAsync(statusFilter: "all", pageSize: 1000);

            AtRiskStudents = allStudents
                .Where(s => s.CurrentBalance > 5000 && s.PaymentStatus != "Paid") // High balance threshold
                .Select(s => new AtRiskStudent
                {
                    StudentId = s.Id,
                    FullName = s.FullName,
                    Grade = s.Grade,
                    CurrentBalance = s.CurrentBalance,
                    TotalPaid = s.TotalPaid,
                    RiskScore = CalculateRiskScore(s),
                    RiskLevel = GetRiskLevel(CalculateRiskScore(s))
                })
                .OrderByDescending(s => s.RiskScore)
                .Take(20)
                .ToList();
        }

        private async Task LoadDiscountImpactAsync()
        {
            var allStudents = await _studentService.GetStudentsAsync(statusFilter: "all", pageSize: 1000);

            // Calculate potential discounts based on sibling order
            var studentsWithSiblings = allStudents.Where(s => s.SiblingOrder > 1).ToList();
            StudentsWithDiscounts = studentsWithSiblings.Count;

            // Estimate discount value (10% for 2nd child, 15% for 3rd+)
            foreach (var student in studentsWithSiblings)
            {
                var discountRate = student.SiblingOrder == 2 ? 0.10 : 0.15;
                // This is a simplified calculation - in reality you'd need fee structure data
                TotalDiscountsApplied += student.TotalPaid * discountRate;
            }
        }

        private double CalculateRiskScore(Student student)
        {
            double score = 0;

            // Balance factor (higher balance = higher risk)
            if (student.CurrentBalance > 10000) score += 40;
            else if (student.CurrentBalance > 5000) score += 30;
            else if (student.CurrentBalance > 2000) score += 20;
            else if (student.CurrentBalance > 0) score += 10;

            // Payment history factor
            if (student.TotalPaid == 0) score += 30;
            else if (student.TotalPaid < student.CurrentBalance) score += 15;

            // Status factor
            if (student.Status == "inactive") score += 20;

            // Age factor (older students closer to graduation may have higher fees)
            if (student.Age > 10) score += 5;

            return Math.Min(score, 100); // Cap at 100
        }

        private string GetRiskLevel(double score)
        {
            return score switch
            {
                >= 70 => "High",
                >= 40 => "Medium",
                _ => "Low"
            };
        }

        private static string DetermineCurrentTerm()
        {
            var now = DateTime.Now;
            var month = now.Month;
            var day = now.Day;

            if ((month == 8) || (month == 9) || (month == 10) || (month == 11 && day <= 19))
                return "Term 1";
            else if ((month == 11 && day >= 20) || month == 12 || month == 1 || month == 2 || (month == 3 && day <= 14))
                return "Term 2";
            else
                return "Term 3";
        }
    }

    // Data Models for Analytics
    public class GradeCollectionStats
    {
        public string Grade { get; set; } = string.Empty;
        public int StudentCount { get; set; }
        public double Collected { get; set; }
        public double Outstanding { get; set; }
        public double Expected { get; set; }
        public double CollectionRate { get; set; }
    }

    public class RevenueDataPoint
    {
        public string Month { get; set; } = string.Empty;
        public double Revenue { get; set; }
        public int NewStudents { get; set; }
    }

    public class YearComparison
    {
        public int Year { get; set; }
        public double TotalRevenue { get; set; }
        public int StudentCount { get; set; }
        public double PreviousYearRevenue { get; set; }
        public double GrowthRate { get; set; }
    }

    public class GradeEnrollment
    {
        public string Grade { get; set; } = string.Empty;
        public int ActiveCount { get; set; }
        public int InactiveCount { get; set; }
        public int TotalCount { get; set; }
    }

    public class EnrollmentTrend
    {
        public int Year { get; set; }
        public int NewStudents { get; set; }
        public int ActiveStudents { get; set; }
        public int TotalStudents { get; set; }
    }

    public class AgeDistribution
    {
        public int Age { get; set; }
        public int Count { get; set; }
        public double Percentage { get; set; }
    }

    public class AtRiskStudent
    {
        public string StudentId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Grade { get; set; } = string.Empty;
        public double CurrentBalance { get; set; }
        public double TotalPaid { get; set; }
        public double RiskScore { get; set; }
        public string RiskLevel { get; set; } = string.Empty;
    }
}
