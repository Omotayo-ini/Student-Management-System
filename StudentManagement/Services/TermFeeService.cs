using Google.Cloud.Firestore;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using StudentManagement.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace StudentManagement.Services
{
    /// <summary>
    /// Applies term fees automatically at the start of each term.
    /// Completely separate from grade rollover. Safe to run multiple times (idempotent).
    /// </summary>
    public class TermFeeService
    {
        private readonly FirestoreDb _db;
        private readonly ILogger<TermFeeService> _logger;
        private readonly CreditService? _creditService;

        public TermFeeService(FirestoreDb db, ILogger<TermFeeService> logger, CreditService? creditService = null)
        {
            _db = db;
            _logger = logger;
            _creditService = creditService;
        }

        /// <summary>
        /// Checks if we're in a new term and applies term fees if not yet applied.
        /// Call this at app startup. Safe to run daily — does nothing if already done.
        /// </summary>
        public async Task ApplyTermFeesIfDueAsync()
        {
            var now = DateTime.Now;
            var currentTerm = DetermineCurrentTerm(now);
            var academicYear = now.Year.ToString();
            var termKey = $"{academicYear}_{currentTerm}";

            // CRITICAL: Check if we've already applied this term's fees
            var settingsRef = _db.Collection("systemSettings").Document("termFeeTracking");
            var settingsSnap = await settingsRef.GetSnapshotAsync();

            if (settingsSnap.Exists &&
                settingsSnap.TryGetValue<string>("lastAppliedTerm", out var lastApplied) &&
                lastApplied == termKey)
            {
                _logger.LogInformation("Term fees already applied for {TermKey}. Skipping.", termKey);
                return; // ALREADY DONE — exit immediately, touch nothing
            }

            _logger.LogInformation("Applying term fees for {TermKey}...", termKey);

            var activeStudents = await _db.Collection("students")
                .WhereEqualTo("status", "active")
                .GetSnapshotAsync();

            var appliedCount = 0;
            var skippedCount = 0;

            foreach (var doc in activeStudents.Documents)
            {
                try
                {
                    var student = doc.ConvertTo<Student>();

                    // Per-student idempotency: skip if this student already has this term's fee
                    if (student.LastTermRolloverKey == termKey)
                    {
                        skippedCount++;
                        continue;
                    }

                    var feeAmount = await GetTermFeeForStudentAsync(student, academicYear, currentTerm);
                    if (feeAmount <= 0)
                    {
                        _logger.LogWarning("No fee found for student {StudentId} grade {Grade}", student.Id, student.Grade);
                        continue;
                    }
                    double finalFee = feeAmount;
                    double appliedCredit = 0;

                    if (_creditService != null)
                    {
                        var (remainingFee, leftoverCredit) = await _creditService.ApplyCreditAsync(student.Id, feeAmount);
                        appliedCredit = feeAmount - remainingFee;
                        finalFee = remainingFee;
                        // Note: ApplyCreditAsync updates credits document already
                    }

                    var newBalance = student.CurrentBalance + finalFee;

                    var updates = new Dictionary<string, object>
                    {
                        { "currentBalance", newBalance },
                        { "lastTermRolloverKey", termKey },
                        { "paymentStatus", newBalance > 0 ? "Outstanding" : "Paid" },
                        { "updatedAt", Timestamp.GetCurrentTimestamp() }
                    };

                    await doc.Reference.UpdateAsync(updates);
                    appliedCount++;

                    _logger.LogDebug("Applied {Term} fee of {Amount} to {StudentName} (appliedCredit: {Applied}, balance: {Old} → {New})",
                        currentTerm, feeAmount, student.FullName, appliedCredit, student.CurrentBalance, newBalance);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to apply term fee to student {StudentId}", doc.Id);
                }
            }

            // Mark this term as applied globally
            await settingsRef.SetAsync(new Dictionary<string, object>
            {
                { "lastAppliedTerm", termKey },
                { "lastAppliedAt", Timestamp.GetCurrentTimestamp() },
                { "studentsUpdated", appliedCount },
                { "studentsSkipped", skippedCount }
            }, SetOptions.MergeAll);

            _logger.LogInformation("Term fee application complete for {TermKey}: {Applied} updated, {Skipped} already done.",
                termKey, appliedCount, skippedCount);
        }

        /// <summary>
        /// Returns the fee amount for the given term based on student's grade.
        /// Term 1: Registration + Books + Term1Fee
        /// Term 2: Term2Fee only
        /// Term 3: Term3Fee only
        /// </summary>
        private async Task<double> GetTermFeeForStudentAsync(Student student, string academicYear, string term)
        {
            if (string.IsNullOrEmpty(student.Grade)) return 0;

            var feeDocId = $"{academicYear}_{MapGradeToFeeStructure(student.Grade)}";
            var feeSnap = await _db.Collection("feeStructures").Document(feeDocId).GetSnapshotAsync();

            if (!feeSnap.Exists) return 0;

            var fee = feeSnap.ConvertTo<FeeStructure>();

            return term switch
            {
                "Term 1" => fee.Term1Fee + fee.RegistrationFee + fee.BooksAndAids,
                "Term 2" => fee.Term2Fee,
                "Term 3" => fee.Term3Fee,
                _ => 0
            };
        }

        /// <summary>
        /// Determines current term based on YOUR school's exact calendar:
        /// Term 1: Aug 1 - Nov 19
        /// Term 2: Nov 20 - Mar 28
        /// Term 3: Mar 29 - Jun 25 (school closes Jun 25, reopens Aug)
        /// </summary>
        private static string DetermineCurrentTerm(DateTime date)
        {
            var month = date.Month;
            var day = date.Day;

            // Term 1: August 1 - November 19
            if ((month == 8) || (month == 9) || (month == 10) || (month == 11 && day <= 19))
                return "Term 1";

            // Term 2: November 20 - March 28
            if ((month == 11 && day >= 20) || month == 12 || month == 1 || month == 2 || (month == 3 && day <= 28))
                return "Term 2";

            // Term 3: March 29 - June 25
            return "Term 3";
        }

        private static string MapGradeToFeeStructure(string grade)
        {
            var normalized = grade?.Trim().ToLowerInvariant() ?? "";

            if (normalized.Contains("creche") || normalized.Contains("kg")) return "Creche";
            if (normalized.Contains("ready") || normalized == "ra" || normalized == "rb" || normalized.Contains("grade r")) return "Ready";
            if (normalized.Contains("grade 1") || normalized == "1st grade" || normalized == "1st" || normalized == "1c" || normalized == "1 c") return "Grade1";
            if (normalized.Contains("grade 2") || normalized.Contains("grade 3") || normalized.Contains("grade 4") || normalized.Contains("grade 5")) return "Primary";
            if (normalized.Contains("grade 6") || normalized.Contains("grade 7")) return "Senior";

            var digits = new string(normalized.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var num))
            {
                if (num == 1) return "Grade1";
                if (num >= 2 && num <= 5) return "Primary";
                if (num >= 6) return "Senior";
            }

            return "Primary";
        }
    }
}
