using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using StudentManagement.Models;

namespace StudentManagement.Services
{
    public class StudentService
    {
        private readonly FirestoreDb _db;
        private readonly CollectionReference _studentsRef;
        private readonly ILogger<StudentService> _logger;

        private readonly CreditService? _creditService;

        public StudentService(FirestoreDb db, ILogger<StudentService> logger, CreditService? creditService = null)
        {
            _db = db;
            _studentsRef = db.Collection("students");
            _logger = logger;
            _creditService = creditService;
        }

        public async Task RecalculateAllBalancesAsync(StudentFeeService feeService, string academicYear, string currentTerm)
        {
            var snapshot = await _studentsRef.WhereEqualTo("status", "active").GetSnapshotAsync();
            foreach (var doc in snapshot.Documents)
            {
                var student = doc.ConvertTo<Student>();
                try
                {
                    await feeService.InitializeStudentFeesAsync(doc.Id, academicYear);
                    var balance = await feeService.GetOutstandingBalanceForTermAsync(doc.Id, academicYear, currentTerm);
                    await _studentsRef.Document(doc.Id).UpdateAsync(new Dictionary<string, object>
                    {
                        { "currentBalance", balance },
                        { "paymentStatus", balance > 0 ? "Outstanding" : "Paid" },
                        { "updatedAt", Timestamp.GetCurrentTimestamp() }
                    });
                    _logger.LogInformation("Updated balance for {StudentId}: {Balance}", doc.Id, balance);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update balance for {StudentId}", doc.Id);
                }
            }
        }

        public async Task<List<Student>> GetStudentsAsync(
            string? gradeFilter = null,
            string? statusFilter = "active",
            string? searchTerm = null,
            int pageSize = 10,
            int currentPage = 1,
            int? ageMin = null,
            int? ageMax = null)
        {
            Query query = _studentsRef;

            // Apply Firestore-native filters first
            if (!string.IsNullOrEmpty(statusFilter) && statusFilter != "all")
            {
                query = query.WhereEqualTo("status", statusFilter);
            }
            if (!string.IsNullOrEmpty(gradeFilter))
            {
                query = query.WhereEqualTo("grade", gradeFilter);
            }

            // Fetch all matching documents from Firestore
            var snapshot = await query.GetSnapshotAsync();
            var studentsAll = snapshot.Documents.Select(d => d.ConvertTo<Student>()).ToList();

            // Apply age filters in-memory if provided
            if (ageMin.HasValue)
            {
                studentsAll = studentsAll.Where(s => s.Age >= ageMin.Value).ToList();
            }
            if (ageMax.HasValue)
            {
                studentsAll = studentsAll.Where(s => s.Age <= ageMax.Value).ToList();
            }

            // Apply in-memory search if provided
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                // Split search term into individual words for cross-field matching
                var searchWords = searchTerm.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                            .Select(w => w.ToLowerInvariant())
                                            .ToList();

                var filtered = studentsAll.Where(s =>
                {
                    var firstName = s.FirstName?.ToLowerInvariant() ?? "";
                    var lastName = s.LastName?.ToLowerInvariant() ?? "";
                    var fullName = $"{firstName} {lastName}";
                    var phone = s.ContactPhone?.ToLowerInvariant() ?? "";
                    var studentId = s.StudentId?.ToLowerInvariant() ?? "";

                    // Check if ANY search word matches ANY field (OR within words, AND across words)
                    return searchWords.All(word =>
                        firstName.Contains(word) ||
                        lastName.Contains(word) ||
                        fullName.Contains(word) ||
                        phone.Contains(word) ||
                        studentId.Contains(word));
                }).ToList();

                var ordered = filtered.OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ToList();
                var skip = Math.Max(0, (currentPage - 1) * pageSize);
                return ordered.Skip(skip).Take(pageSize).ToList();
            }

            // No search term: just paginate the full result set
            var orderedAll = studentsAll.OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ToList();
            var skipAll = Math.Max(0, (currentPage - 1) * pageSize);
            return orderedAll.Skip(skipAll).Take(pageSize).ToList();
        }

        public async Task<int> GetStudentsCountAsync(string? gradeFilter = null, string? statusFilter = "active", string? searchTerm = null, int? ageMin = null, int? ageMax = null)
        {
            Query query = _studentsRef;

            // Apply Firestore-native filters first
            if (!string.IsNullOrEmpty(statusFilter) && statusFilter != "all")
                query = query.WhereEqualTo("status", statusFilter);
            if (!string.IsNullOrEmpty(gradeFilter))
                query = query.WhereEqualTo("grade", gradeFilter);

            // Fetch all matching documents from Firestore
            var snap = await query.GetSnapshotAsync();
            var studentsAll = snap.Documents.Select(d => d.ConvertTo<Student>()).ToList();

            if (ageMin.HasValue)
                studentsAll = studentsAll.Where(s => s.Age >= ageMin.Value).ToList();
            if (ageMax.HasValue)
                studentsAll = studentsAll.Where(s => s.Age <= ageMax.Value).ToList();

            // Apply in-memory search if provided
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var searchWords = searchTerm.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                            .Select(w => w.ToLowerInvariant())
                                            .ToList();

                var filtered = studentsAll.Where(s =>
                {
                    var firstName = s.FirstName?.ToLowerInvariant() ?? "";
                    var lastName = s.LastName?.ToLowerInvariant() ?? "";
                    var fullName = $"{firstName} {lastName}";
                    var phone = s.ContactPhone?.ToLowerInvariant() ?? "";
                    var studentId = s.StudentId?.ToLowerInvariant() ?? "";

                    return searchWords.All(word =>
                        firstName.Contains(word) ||
                        lastName.Contains(word) ||
                        fullName.Contains(word) ||
                        phone.Contains(word) ||
                        studentId.Contains(word));
                }).ToList();

                return filtered.Count;
            }
            return studentsAll.Count;
        }

        public async Task<List<(string Display, string Id)>> SearchStudentsAsync(string q, int limit = 10)
        {
            if (string.IsNullOrWhiteSpace(q)) return new List<(string, string)>();
            var searchWords = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(w => w.Trim().ToLowerInvariant())
                                .ToList();

            // Fetch active students and perform lightweight filtering in-memory
            var snapshot = await _studentsRef.WhereEqualTo("status", "active").GetSnapshotAsync();
            var candidate = snapshot.Documents
                .Select(d => new { Doc = d, Student = d.ConvertTo<Student>() })
                .Select(x => new { Id = x.Doc.Id, S = x.Student })
                .ToList();

            var matches = candidate.Where(x =>
            {
                var first = (x.S.FirstName ?? string.Empty).ToLowerInvariant();
                var last = (x.S.LastName ?? string.Empty).ToLowerInvariant();
                var full = ($"{first} {last}").Trim();
                var sid = (x.S.StudentId ?? string.Empty).ToLowerInvariant();
                var grade = (x.S.Grade ?? string.Empty).ToLowerInvariant();

                // All search words must match one of the fields
                return searchWords.All(w =>
                    first.Contains(w) || last.Contains(w) || full.Contains(w) || sid.Contains(w) || grade.Contains(w));
            })
            .OrderBy(x => x.S.LastName)
            .ThenBy(x => x.S.FirstName)
            .Take(limit)
            .Select(x => ($"{x.S.FullName} ({x.S.StudentId}) - {x.S.Grade}", x.Id))
            .ToList();

            return matches;
        }

        public async Task UpdateStudentFieldsAsync(string id, Dictionary<string, object> updates)
        {
            if (updates == null || updates.Count == 0) return;
            updates["updatedAt"] = Timestamp.GetCurrentTimestamp();
            await _studentsRef.Document(id).UpdateAsync(updates);
            _logger.LogInformation("Patched student {StudentId} with fields: {Fields}", id, string.Join(',', updates.Keys));
        }

        // Public helper to infer grade level (P/M/S) from a grade string
        public string GetGradeLevelFromGrade(string grade)
        {
            return DetermineGradeLevel(grade);
        }

        public async Task<Student?> GetStudentAsync(string id)
        {
            var doc = await _studentsRef.Document(id).GetSnapshotAsync();
            return doc.Exists ? doc.ConvertTo<Student>() : null;
        }

        public async Task<string> CreateStudentAsync(Student student)
        {
            student.CreatedAt = Timestamp.GetCurrentTimestamp();
            student.UpdatedAt = Timestamp.GetCurrentTimestamp();
            var docRef = await _studentsRef.AddAsync(student);
            _logger.LogInformation("Created student {StudentId}", docRef.Id);
            return docRef.Id;
        }

        public async Task UpdateStudentAsync(string id, Student student)
        {
            student.UpdatedAt = Timestamp.GetCurrentTimestamp();
            await _studentsRef.Document(id).SetAsync(student, SetOptions.Overwrite);
            _logger.LogInformation("Updated student {StudentId}", id);
        }

        public async Task DeleteStudentAsync(string id)
        {
            var feesSnap = await _studentsRef.Document(id).Collection("fees").GetSnapshotAsync();
            foreach (var doc in feesSnap.Documents)
            {
                await _studentsRef.Document(id).Collection("fees").Document(doc.Id).DeleteAsync();
            }
            await _studentsRef.Document(id).DeleteAsync();
        }

        public async Task<int> GetStudentCountForYearAsync(int year)
        {
            var snapshot = await _studentsRef
                .WhereEqualTo("enrollmentYear", year)
                .GetSnapshotAsync();
            return snapshot.Count;
        }

        public async Task<List<Student>> GetStudentsByBirthdayMonthAsync(int month)
        {
            var snapshot = await _studentsRef
                .WhereEqualTo("status", "active")
                .GetSnapshotAsync();
            return snapshot.Documents
                .Select(d => d.ConvertTo<Student>())
                .Where(s => s.BirthDate.ToDateTime().Month == month)
                .ToList();
        }

        public async Task<BulkImportResult> BulkImportAsync(Stream fileStream, string fileName)
        {
            OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
            var result = new BulkImportResult
            {
                FileName = fileName,
                StartedAt = DateTime.UtcNow,
                TotalRecords = 0,
                SuccessCount = 0,
                FailedCount = 0,
                Errors = new List<string>(),
                Status = "Processing"
            };
            try
            {
                using var package = new ExcelPackage(fileStream);
                var worksheet = package.Workbook.Worksheets.FirstOrDefault();
                if (worksheet == null)
                {
                    result.Status = "Failed";
                    result.Errors.Add("No worksheet found in file.");
                    return result;
                }
                var lastRow = worksheet.Dimension?.End.Row ?? 0;
                result.TotalRecords = Math.Max(0, lastRow - 1);
                for (int row = 2; row <= lastRow; row++)
                {
                    try
                    {
                        var firstName = worksheet.Cells[row, 1].GetValue<string>()?.Trim() ?? string.Empty;
                        var lastName = worksheet.Cells[row, 2].GetValue<string>()?.Trim() ?? string.Empty;
                        var grade = worksheet.Cells[row, 3].GetValue<string>()?.Trim() ?? string.Empty;
                        var age = worksheet.Cells[row, 4].GetValue<int?>() ?? 0;
                        var birthMonth = worksheet.Cells[row, 5].GetValue<string>()?.Trim() ?? string.Empty;
                        var guardianName = worksheet.Cells[row, 6].GetValue<string>()?.Trim() ?? string.Empty;
                        var guardianPhone = worksheet.Cells[row, 7].GetValue<string>()?.Trim() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName))
                        {
                            result.FailedCount++;
                            result.Errors.Add($"Row {row}: Missing name.");
                            continue;
                        }
                        var student = new Student
                        {
                            StudentId = string.Empty,
                            FirstName = firstName,
                            LastName = lastName,
                            Grade = grade,
                            GradeLevel = string.Empty,
                            SpecificGrade = string.Empty,
                            Age = age,
                            BirthMonth = birthMonth,
                            ContactPhone = guardianPhone,
                            ContactEmail = string.Empty,
                            ParentId = string.Empty,
                            Status = "active",
                            SiblingOrder = 1,
                            CreatedAt = Timestamp.GetCurrentTimestamp(),
                            UpdatedAt = Timestamp.GetCurrentTimestamp()
                        };
                        var normalizedGrade = NormalizeGradeToFeeStructure(grade);
                        var feeDocId = $"{DateTime.Now.Year}_{normalizedGrade}";
                        var feeDoc = await _db.Collection("feeStructures").Document(feeDocId).GetSnapshotAsync();
                        if (feeDoc.Exists)
                        {
                            var fee = feeDoc.ConvertTo<FeeStructure>();
                            student.CurrentFeeStructureId = fee.Id;
                            student.GradeLevel = normalizedGrade;
                            student.ClassName = fee.ClassName;
                            var currentTerm = DetermineCurrentTerm();
                            student.CurrentBalance = CalculateNewStudentStartingBalance(fee, currentTerm);
                            student.PaymentStatus = "Outstanding";

                            // Prevent RolloverTermBalancesAsync from double-charging this student
                            // for the current term on the next Student Records page load.
                            student.LastTermRolloverKey = $"{DateTime.Now.Year}_{currentTerm}";
                        }
                        var docRef = await _studentsRef.AddAsync(student);
                        await _studentsRef.Document(docRef.Id).UpdateAsync(new Dictionary<string, object>
                        {
                            { "studentId", docRef.Id },
                            { "updatedAt", Timestamp.GetCurrentTimestamp() }
                        });
                        result.SuccessCount++;
                    }
                    catch (Exception exRow)
                    {
                        result.FailedCount++;
                        result.Errors.Add($"Row {row}: {exRow.Message}");
                    }
                }
                result.Status = "Completed";
                result.CompletedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                result.Status = "Failed";
                result.Errors.Add(ex.Message);
            }
            return result;
        }

        public async Task ToggleAutoGradeRolloverAsync(string studentId)
        {
            var docRef = _studentsRef.Document(studentId);
            var snap = await docRef.GetSnapshotAsync();
            if (!snap.Exists) return;
            var student = snap.ConvertTo<Student>();
            var newValue = !student.AutoGradeRolloverEnabled;
            await docRef.UpdateAsync(new Dictionary<string, object>
            {
                { "autoGradeRolloverEnabled", newValue },
                { "updatedAt", Timestamp.GetCurrentTimestamp() }
            });
            _logger.LogInformation("Toggled AutoGradeRollover for {StudentId} to {Value}", studentId, newValue);
        }

        public async Task ToggleAutoAgeUpdateAsync(string studentId)
        {
            var docRef = _studentsRef.Document(studentId);
            var snap = await docRef.GetSnapshotAsync();
            if (!snap.Exists) return;
            var student = snap.ConvertTo<Student>();
            var newValue = !student.AutoAgeUpdateEnabled;
            await docRef.UpdateAsync(new Dictionary<string, object>
            {
                { "autoAgeUpdateEnabled", newValue },
                { "updatedAt", DateTime.UtcNow }
            });
            _logger.LogInformation("Toggled AutoAgeUpdate for {StudentId} to {Value}", studentId, newValue);
        }

        // UPDATED: Enhanced ManualGradeRolloverAsync with fee calculation for new grade
        public async Task ManualGradeRolloverAsync(string studentId, string newGrade)
        {
            var studentDoc = _studentsRef.Document(studentId);
            var snapshot = await studentDoc.GetSnapshotAsync();
            if (!snapshot.Exists) return;

            var student = snapshot.ConvertTo<Student>();
            var currentGrade = student.Grade;

            // Only proceed if grade actually changes
            if (string.Equals(currentGrade?.Trim(), newGrade?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Student {StudentId} already in grade {Grade}, skipping rollover", studentId, newGrade);
                return;
            }

            // Determine academic year and term
            var now = DateTime.Now;
            var academicYear = now.Year.ToString();
            var currentTerm = DetermineCurrentTerm();

            // Get fee structure for new grade
            double term1Fee = 0, registrationFee = 0, booksFee = 0;

            try
            {
                var feeDocId = $"{academicYear}_{MapGradeToFeeStructureGrade(newGrade)}";
                var feeSnap = await _db.Collection("feeStructures").Document(feeDocId).GetSnapshotAsync();

                if (feeSnap.Exists)
                {
                    var fee = feeSnap.ConvertTo<FeeStructure>();
                    term1Fee = fee.Term1Fee;
                    registrationFee = fee.RegistrationFee;
                    booksFee = fee.BooksAndAids;
                    _logger.LogInformation("Found fee structure for {Grade}: T1={T1}, Reg={Reg}, Books={Books}",
                        newGrade, term1Fee, registrationFee, booksFee);
                }
                else
                {
                    _logger.LogWarning("No fee structure found for {Grade} in year {Year}, using zero fees", newGrade, academicYear);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load fee structure for {Grade}, using zero fees", newGrade);
            }

            // Calculate new balance: add Term 1 + Registration + Books for new academic year
            var balanceToAdd = term1Fee + registrationFee + booksFee;

            // Apply any existing creditBalance to reduce the added fees
            var creditBefore = student.CreditBalance;
            var appliedCredit = Math.Min(creditBefore, balanceToAdd);
            var remainingCredit = Math.Max(0, creditBefore - appliedCredit);
            var remainingToAdd = Math.Max(0, balanceToAdd - appliedCredit);

            var newBalance = student.CurrentBalance + remainingToAdd;

            var updates = new Dictionary<string, object>
            {
                { "grade", newGrade },
                { "gradeLevel", DetermineGradeLevel(newGrade) },
                { "currentBalance", newBalance },
                { "creditBalance", remainingCredit },
                { "currentFeeStructureId", $"{academicYear}_{MapGradeToFeeStructureGrade(newGrade)}" },
                { "updatedAt", Timestamp.GetCurrentTimestamp() }
            };

            await studentDoc.UpdateAsync(updates);
            _logger.LogInformation("Manual grade rollover for {StudentId} to {NewGrade}: added {Added} (appliedCredit={Applied}, remainingCredit={Remaining}) balance was {Old} -> now {New}",
                studentId, newGrade, balanceToAdd, appliedCredit, remainingCredit, student.CurrentBalance, newBalance);
        }

        public async Task UpdateStudentBalanceAsync(string id, double newBalance)
        {
            var docRef = _studentsRef.Document(id);
            string paymentStatus;
            if (newBalance <= 0)
            {
                paymentStatus = "Paid";
                newBalance = 0;
            }
            else
            {
                paymentStatus = "Outstanding";
            }
            var updates = new Dictionary<string, object>
            {
                { "currentBalance", newBalance },
                { "paymentStatus", paymentStatus },
                { "updatedAt", Timestamp.GetCurrentTimestamp() }
            };
            await docRef.UpdateAsync(updates);
            _logger.LogInformation("Updated balance for student {StudentId}: {Balance}, Status: {Status}", id, newBalance, paymentStatus);
        }

        /// <summary>
        /// Advances every active student's balance to the current calendar term.
        /// Adds the new term's fee on top of whatever balance remains (so unpaid
        /// previous-term amounts carry forward), and only runs once per term per
        /// student by checking LastTermRolloverKey against "{academicYear}_{term}".
        /// Intended to be called once when the page loads in a new term, or via a
        /// scheduled/manual admin action - never on every page view.
        /// </summary>
        public async Task RolloverTermBalancesAsync()
        {
            var currentTerm = DetermineCurrentTerm();
            var academicYear = DateTime.Now.Year;
            var rolloverKey = $"{academicYear}_{currentTerm}";

            var snapshot = await _studentsRef.WhereEqualTo("status", "active").GetSnapshotAsync();

            foreach (var doc in snapshot.Documents)
            {
                try
                {
                    var student = doc.ConvertTo<Student>();

                    // Already rolled over for this term - skip
                    if (student.LastTermRolloverKey == rolloverKey)
                        continue;

                    if (string.IsNullOrEmpty(student.CurrentFeeStructureId))
                        continue;

                    var feeDoc = await _db.Collection("feeStructures").Document(student.CurrentFeeStructureId).GetSnapshotAsync();
                    if (!feeDoc.Exists)
                        continue;

                    var fee = feeDoc.ConvertTo<FeeStructure>();
                    var newTermFee = CalculateTermStartingBalance(fee, currentTerm);
                    // Apply any existing credit to reduce the new term fee. Use CreditService if available
                    double appliedCredit = 0;
                    double remainingCredit = 0;
                    double remainingFee = newTermFee;

                    if (_creditService != null)
                    {
                        var (rf, leftover) = await _creditService.ApplyCreditAsync(doc.Id, newTermFee);
                        remainingFee = rf;
                        appliedCredit = newTermFee - rf;
                        remainingCredit = leftover;
                    }
                    else
                    {
                        var creditBefore = student.CreditBalance;
                        appliedCredit = Math.Min(creditBefore, newTermFee);
                        remainingCredit = Math.Max(0, creditBefore - appliedCredit);
                        remainingFee = Math.Max(0, newTermFee - appliedCredit);
                    }

                    // Carry forward any unpaid balance from the previous term and add the remaining fee
                    var updatedBalance = student.CurrentBalance + remainingFee;

                    var updates = new Dictionary<string, object>
                    {
                        { "currentBalance", updatedBalance },
                        { "paymentStatus", updatedBalance > 0 ? "Outstanding" : "Paid" },
                        { "lastTermRolloverKey", rolloverKey },
                        { "updatedAt", Timestamp.GetCurrentTimestamp() }
                    };

                    // Only persist creditBalance on the student document when not using separate credits collection
                    if (_creditService == null)
                    {
                        updates["creditBalance"] = remainingCredit;
                    }

                    await _studentsRef.Document(doc.Id).UpdateAsync(updates);

                    _logger.LogInformation(
                        "Rolled over student {StudentId} to {Term}: fee={NewFee}, appliedCredit={AppliedCredit}, remainingCredit={RemainingCredit}, balance now {Balance}",
                        doc.Id, currentTerm, newTermFee, appliedCredit, remainingCredit, updatedBalance);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to roll over balance for student {StudentId}", doc.Id);
                }
            }
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

        private static double CalculateTermStartingBalance(FeeStructure fee, string currentTerm)
        {
            if (fee == null) return 0;
            return currentTerm?.ToLowerInvariant() switch
            {
                "term 1" => fee.RegistrationFee + fee.BooksAndAids + fee.Term1Fee,
                "term 2" => fee.Term2Fee,
                "term 3" => fee.Term3Fee,
                _ => fee.TotalPerYear
            };
        }

        /// <summary>
        /// Calculates the starting balance for a BRAND NEW student joining the school.
        /// Unlike CalculateTermStartingBalance (used for term rollover on existing students,
        /// which only adds Registration + Books in Term 1), a new student always owes
        /// Registration + Books + that term's fee, no matter which term they join in -
        /// joining in Term 3 means RegistrationFee + BooksAndAids + Term3Fee.
        /// </summary>
        private static double CalculateNewStudentStartingBalance(FeeStructure fee, string currentTerm)
        {
            if (fee == null) return 0;
            var termFee = currentTerm?.ToLowerInvariant() switch
            {
                "term 1" => fee.Term1Fee,
                "term 2" => fee.Term2Fee,
                "term 3" => fee.Term3Fee,
                _ => fee.Term1Fee
            };
            return fee.RegistrationFee + fee.BooksAndAids + termFee;
        }

        public static string NormalizeGradeToFeeStructure(string grade)
        {
            if (string.IsNullOrWhiteSpace(grade))
                return string.Empty;

            var normalized = grade.Trim().ToLowerInvariant();

            // Creche / KG
            if (normalized.Contains("creche") || normalized.Contains("crèche") || normalized.Contains("kg"))
                return "Creche";

            // School Ready / Reception (ra, rb, rc, grade r)
            if (normalized.Contains("ready") || normalized.Contains("school ready") ||
                Regex.IsMatch(normalized, @"^r[a-z]?$") ||
                Regex.IsMatch(normalized, @"^grade\s*r[a-z]?$"))
                return "Ready";

            // Grade 1 variants (1, 1a, 1 b, 1st)
            if (Regex.IsMatch(normalized, @"^grade\s*1[a-z]?$") ||
                Regex.IsMatch(normalized, @"^1\s*[a-z]?$") ||
                Regex.IsMatch(normalized, @"^1(st|nd|rd|th)?\b"))
                return "Grade1";

            // Grades 2-5 primary
            if (Regex.IsMatch(normalized, @"^grade\s*[2-5][a-z]?$") ||
                Regex.IsMatch(normalized, @"^[2-5]\s*[a-z]?$") ||
                Regex.IsMatch(normalized, @"^[2-5](nd|rd|th)?\b") ||
                normalized == "primary")
                return "Primary";

            // Grade 6+ senior
            if (Regex.IsMatch(normalized, @"^grade\s*[6-9][a-z]?$") ||
                Regex.IsMatch(normalized, @"^[6-9]\s*[a-z]?$") ||
                Regex.IsMatch(normalized, @"^[6-9](th)?\b") ||
                normalized.Contains("senior"))
                return "Senior";

            return grade;
        }

        // NEW: Maps a human-readable grade string to the fee structure grade key
        public static string MapGradeToFeeStructureGrade(string grade)
        {
            var normalized = grade?.Trim().ToLowerInvariant() ?? "";

            if (normalized.Contains("creche") || normalized.Contains("kg")) return "Creche";

            // Ready / Reception variants: "ra", "rb", "r", "grade r", "rA" etc.
            if (normalized.Contains("ready") ||
                Regex.IsMatch(normalized, @"^r[a-z]?$") ||
                Regex.IsMatch(normalized, @"^grade\s*r[a-z]?$"))
                return "Ready";

            // Grade 1 variants: "1", "1a", "1 a", "grade 1", "grade 1a", "1st"
            if (Regex.IsMatch(normalized, @"^(grade\s*)?1(\s*[a-z])?$") ||
                normalized.StartsWith("1st"))
                return "Grade1";

            // Primary: grades 2-5 (allow lettered variants like "2a", "3 b", "grade 4c")
            if (Regex.IsMatch(normalized, @"^(grade\s*)?[2-5](\s*[a-z])?$"))
                return "Primary";

            // Senior: grades 6-9 (allow lettered variants)
            if (Regex.IsMatch(normalized, @"^(grade\s*)?[6-9](\s*[a-z])?$"))
                return "Senior";

            // Fallback: extract digits and map ranges
            var digits = new string(normalized.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var num))
            {
                if (num == 1) return "Grade1";
                if (num >= 2 && num <= 5) return "Primary";
                if (num >= 6) return "Senior";
            }

            return "Primary";
        }

        // NEW: Determines the grade level category (P/M/S) from a grade string
        private static string DetermineGradeLevel(string grade)
        {
            var normalized = grade?.Trim().ToLowerInvariant() ?? "";
            var feeStructureGrade = MapGradeToFeeStructureGrade(grade);

            return feeStructureGrade switch
            {
                "Creche" or "Ready" => "P",  // Primary level (early years)
                "Grade1" or "Primary" => "P", // Primary level
                "Senior" => "S",              // Senior level
                _ => "P"                      // Default to Primary
            };
        }
    }
}