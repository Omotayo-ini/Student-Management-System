using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StudentManagement.Models;
using StudentManagement.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StudentManagement.Pages
{
    public class IndexModel : PageModel
    {
        private readonly FirestoreDb _db;
        private readonly StudentService _studentService;
        private readonly GradeRolloverService _gradeRolloverService;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(FirestoreDb db, StudentService studentService, GradeRolloverService gradeRolloverService, ILogger<IndexModel> logger)
        {
            _db = db;
            _studentService = studentService;
            _gradeRolloverService = gradeRolloverService;
            _logger = logger;
        }

        public int TotalStudents { get; set; }
        public int BirthdaysThisMonth { get; set; }

        public double TotalCollected { get; set; }
        public double TotalOutstanding { get; set; }
        public int CollectionRate { get; set; }

        // Analytics JSON payloads for charts
        public string FeeByGradeJson { get; set; } = "[]";
        public string OutstandingStudentsJson { get; set; } = "[]";
        public string RevenueMonthlyJson { get; set; } = "[]";
        public string EnrollmentStatsJson { get; set; } = "[]";
        public string AgeDistributionJson { get; set; } = "[]";
        public string CorrelationJson { get; set; } = "[]";
        public string PredictiveJson { get; set; } = "[]";

        public bool GradeRolloverActive { get; set; }
        public bool AgeUpdateActive { get; set; }

        public DateTime? ProspectusLastSync { get; set; }

        public BulkImportResult? LastImport { get; set; }

        public async Task OnGetAsync()
        {
            try
            {
                var activeStudents = await _studentService.GetStudentsAsync(
                    statusFilter: "active",
                    pageSize: 1000);

                TotalStudents = activeStudents.Count;
                BirthdaysThisMonth = activeStudents.Count(s => s.BirthDate.ToDateTime().Month == DateTime.Now.Month);

                GradeRolloverActive = activeStudents.Any(s => s.AutoGradeRolloverEnabled);
                AgeUpdateActive = activeStudents.Any(s => s.AutoAgeUpdateEnabled);

                TotalCollected = activeStudents.Sum(s => s.TotalPaid);
                TotalOutstanding = activeStudents.Sum(s => s.CurrentBalance);

                var totalExpected = TotalCollected + TotalOutstanding;
                CollectionRate = totalExpected > 0
                    ? (int)Math.Round((TotalCollected / totalExpected) * 100)
                    : 0;

                await LoadProspectusSyncInfoAsync();

                // Build analytics data structures
                BuildFeeByGrade(activeStudents);
                BuildOutstandingStudents(activeStudents);
                BuildRevenueMonthly(activeStudents);
                BuildEnrollmentStats(activeStudents);
                BuildAgeDistribution(activeStudents);
                BuildCorrelationAndPredictive(activeStudents);

                _logger.LogInformation(
                    "Dashboard loaded: {Students} students, Collected={Collected:C}, Outstanding={Outstanding:C}, Rate={Rate}%",
                    TotalStudents, TotalCollected, TotalOutstanding, CollectionRate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading dashboard data");
                TotalStudents = 0;
                BirthdaysThisMonth = 0;
                TotalCollected = 0;
                TotalOutstanding = 0;
                CollectionRate = 0;
                GradeRolloverActive = false;
                AgeUpdateActive = false;
            }
        }

        public async Task<IActionResult> OnPostRunAutomationAsync()
        {
            try
            {
                _logger.LogInformation("Manual automation run triggered from dashboard");

                var activeStudents = await _studentService.GetStudentsAsync(
                    statusFilter: "active",
                    pageSize: 1000);

                var academicYear = DateTime.Now.Year.ToString();
                var rolloverResult = await _gradeRolloverService.ExecuteRolloverAsync(
                    academicYear,
                    autoRolloverOnly: true);
                var rolloverCount = rolloverResult.SuccessCount;
                var ageUpdateCount = 0;

                foreach (var student in activeStudents)
                {
                    // Age Update
                    if (student.AutoAgeUpdateEnabled)
                    {
                        try
                        {
                            var newAge = CalculateAge(student.BirthDate.ToDateTime());
                            if (newAge != student.Age)
                            {
                                await _db.Collection("students").Document(student.Id).UpdateAsync(new Dictionary<string, object>
                                {
                                    { "age", newAge },
                                    { "updatedAt", Timestamp.GetCurrentTimestamp() }
                                });
                                ageUpdateCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to update age for student {StudentId}", student.Id);
                        }
                    }
                }

                TempData["SuccessMessage"] = $"Automation completed! {rolloverCount} students rolled over, {ageUpdateCount} ages updated.";
                _logger.LogInformation("Automation run completed: {RolloverCount} rollovers, {AgeCount} age updates", rolloverCount, ageUpdateCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running automation");
                TempData["ErrorMessage"] = "Automation failed. Please check the logs.";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostApplyTermFeesAsync()
        {
            try
            {
                using var scope = HttpContext.RequestServices.CreateScope();
                var termFeeService = scope.ServiceProvider.GetRequiredService<TermFeeService>();
                await termFeeService.ApplyTermFeesIfDueAsync();
                TempData["SuccessMessage"] = "Term fees applied successfully (if due).";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying term fees");
                TempData["ErrorMessage"] = "Failed to apply term fees.";
            }
            return RedirectToPage();
        }

        private static int CalculateAge(DateTime birthDate)
        {
            var today = DateTime.Today;
            var age = today.Year - birthDate.Year;
            if (birthDate.Date > today.AddYears(-age)) age--;
            return age;
        }

        // Analytics builders
        private void BuildFeeByGrade(List<Student> students)
        {
            var byGrade = students
                .GroupBy(s => s.Grade ?? "Unspecified")
                .Select(g => new { grade = g.Key, collected = g.Sum(x => x.TotalPaid), outstanding = g.Sum(x => x.CurrentBalance) })
                .OrderBy(x => x.grade)
                .ToList();

            FeeByGradeJson = System.Text.Json.JsonSerializer.Serialize(byGrade);
        }

        private void BuildOutstandingStudents(List<Student> students)
        {
            var outstanding = students
                .Where(s => s.CurrentBalance > 0)
                .Select(s => new { id = s.Id, name = s.FullName, grade = s.Grade, balance = s.CurrentBalance })
                .OrderByDescending(x => x.balance)
                .Take(100)
                .ToList();

            OutstandingStudentsJson = System.Text.Json.JsonSerializer.Serialize(outstanding);
        }

        private void BuildRevenueMonthly(List<Student> students)
        {
            // Build a mock monthly revenue based on payments stored per student (not implemented). Fall back to totalPaid distribution by month of updatedAt
            var months = Enumerable.Range(1, 12).Select(m => new { month = m, amount = 0.0 }).ToList();
            RevenueMonthlyJson = System.Text.Json.JsonSerializer.Serialize(months);
        }

        private void BuildEnrollmentStats(List<Student> students)
        {
            var total = students.Count;
            var newAdmissions = students.Count(s => s.EnrollmentYear == DateTime.Now.Year);
            var withdrawals = 0; // Not tracked here
            var transfers = 0; // Not tracked here

            var payload = new { total, newAdmissions, withdrawals, transfers };
            EnrollmentStatsJson = System.Text.Json.JsonSerializer.Serialize(payload);
        }

        private void BuildAgeDistribution(List<Student> students)
        {
            var buckets = students.GroupBy(s => s.Age).OrderBy(g => g.Key).Select(g => new { age = g.Key, count = g.Count() }).ToList();
            AgeDistributionJson = System.Text.Json.JsonSerializer.Serialize(buckets);
        }

        private void BuildCorrelationAndPredictive(List<Student> students)
        {
            // Placeholder for correlation/predictive results
            CorrelationJson = System.Text.Json.JsonSerializer.Serialize(new { message = "Not computed" });
            PredictiveJson = System.Text.Json.JsonSerializer.Serialize(new { message = "Not computed" });
        }

        private async Task LoadProspectusSyncInfoAsync()
        {
            try
            {
                var prospectusSnapshot = await _db.Collection("prospectuses")
                    .OrderByDescending("uploadedAt")
                    .Limit(1)
                    .GetSnapshotAsync();

                var doc = prospectusSnapshot.Documents.FirstOrDefault();
                if (doc != null && doc.Exists)
                {
                    if (doc.TryGetValue<Timestamp>("uploadedAt", out var uploadedAt))
                    {
                        ProspectusLastSync = uploadedAt.ToDateTime();
                    }
                    else if (doc.TryGetValue<Timestamp>("createdAt", out var createdAt))
                    {
                        ProspectusLastSync = createdAt.ToDateTime();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load prospectus sync info");
            }
        }
    }
}