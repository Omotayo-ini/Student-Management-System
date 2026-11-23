using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using OfficeOpenXml;
using Microsoft.Extensions.Logging;
using StudentManagement.Models;

namespace StudentManagement.Services
{
    public class ImportService
    {
        private readonly FirestoreDb _db;
        private readonly StudentService _studentService;
        private readonly ILogger<ImportService> _logger;

        public ImportService(FirestoreDb db, StudentService studentService, ILogger<ImportService> logger)
        {
            _db = db;
            _studentService = studentService;
            _logger = logger;
        }

        public async Task<List<ColumnMapping>> AnalyzeColumnsAsync(string filePath)
        {
            OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

            using var package = new ExcelPackage(new FileInfo(filePath));
            var worksheet = package.Workbook.Worksheets.FirstOrDefault();
            if (worksheet == null) return new List<ColumnMapping>();

            var mappings = new List<ColumnMapping>();
            var lastCol = worksheet.Dimension?.End.Column ?? 0;

            for (int col = 1; col <= lastCol; col++)
            {
                var header = worksheet.Cells[2, col].GetValue<string>()?.Trim() ?? $"Column{col}";
                mappings.Add(new ColumnMapping
                {
                    SourceColumn = header,
                    TargetField = MapHeaderToField(header),
                    IsRequired = IsRequiredField(header),
                    IsMapped = true
                });
            }

            return mappings;
        }

        private static string MapHeaderToField(string header)
        {
            var lower = header.ToLower().Replace(" ", "").Replace("_", "").Replace("-", "");

            return lower switch
            {
                var h when h.Contains("firstname") || h.Contains("first") => "firstName",
                var h when h.Contains("surname") || h.Contains("lastname") || h.Contains("last") => "lastName",
                var h when h.Contains("birthdate") || h.Contains("birth") || h.Contains("dob") => "birthDate",
                var h when h.Contains("phone") || h.Contains("contactphone") || h.Contains("phonenumber") => "contactPhone",
                var h when h.Contains("email") || h.Contains("emailaddress") => "contactEmail",
                var h when h.Contains("grade") => "grade",
                var h when h.Contains("teacher") || h.Contains("classteacher") => "classTeacherId",
                _ => lower
            };
        }

        private static bool IsRequiredField(string header)
        {
            var lower = header.ToLower();
            return lower.Contains("first") || lower.Contains("surname") || lower.Contains("grade");
        }

        public async Task<ImportResult> ExecuteImportAsync(string filePath, List<ColumnMapping> mappings)
        {
            OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

            var result = new ImportResult
            {
                TotalRows = 0,
                SuccessCount = 0,
                FailedCount = 0,
                Errors = new List<string>(),
                ImportedStudents = new List<Student>()
            };

            try
            {
                using var package = new ExcelPackage(new FileInfo(filePath));
                var worksheet = package.Workbook.Worksheets.FirstOrDefault();
                if (worksheet == null)
                {
                    result.Errors.Add("No worksheet found in file.");
                    return result;
                }

                var lastRow = worksheet.Dimension?.End.Row ?? 0;
                var lastCol = worksheet.Dimension?.End.Column ?? 0;

                var dataStartRow = 3;
                result.TotalRows = Math.Max(0, lastRow - dataStartRow + 1);

                var columnMap = new Dictionary<string, int>();
                for (int col = 1; col <= lastCol; col++)
                {
                    var header = worksheet.Cells[2, col].GetValue<string>()?.Trim() ?? "";
                    var mappedField = MapHeaderToField(header);
                    if (!string.IsNullOrEmpty(mappedField))
                        columnMap[mappedField] = col;
                }

                for (int row = dataStartRow; row <= lastRow; row++)
                {
                    try
                    {
                        var firstName = columnMap.TryGetValue("firstName", out var firstNameCol)
                            ? worksheet.Cells[row, firstNameCol].GetValue<string>()?.Trim()
                            : null;

                        var lastName = columnMap.TryGetValue("lastName", out var lastNameCol)
                            ? worksheet.Cells[row, lastNameCol].GetValue<string>()?.Trim()
                            : null;

                        if (string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName))
                        {
                            result.TotalRows--;
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
                        {
                            result.FailedCount++;
                            result.Errors.Add($"Row {row}: Missing first name or surname.");
                            continue;
                        }

                        var student = new Student
                        {
                            FirstName = firstName,
                            LastName = lastName
                        };

                        var hasBirthDate = false;
                        if (columnMap.TryGetValue("birthDate", out var birthDateCol))
                        {
                            var birthObj = worksheet.Cells[row, birthDateCol].Value;
                            if (birthObj != null)
                            {
                                if (birthObj is DateTime dt)
                                {
                                    student.BirthDate = Timestamp.FromDateTime(dt.ToUniversalTime());
                                    student.Age = CalculateAge(dt);
                                    hasBirthDate = true;
                                }
                                else
                                {
                                    var birthDateValue = birthObj.ToString().Trim();
                                    if (!string.IsNullOrWhiteSpace(birthDateValue))
                                    {
                                        var formats = new[] { "yyyy/MM/dd", "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy", "yyyy/M/d" };
                                        if (DateTime.TryParseExact(birthDateValue, formats,
                                            System.Globalization.CultureInfo.InvariantCulture,
                                            System.Globalization.DateTimeStyles.None, out var parsedDate))
                                        {
                                            student.BirthDate = Timestamp.FromDateTime(parsedDate.ToUniversalTime());
                                            student.Age = CalculateAge(parsedDate);
                                            hasBirthDate = true;
                                        }
                                        else if (DateTime.TryParse(birthDateValue, out var fallbackDate))
                                        {
                                            student.BirthDate = Timestamp.FromDateTime(fallbackDate.ToUniversalTime());
                                            student.Age = CalculateAge(fallbackDate);
                                            hasBirthDate = true;
                                        }
                                    }
                                }
                            }
                        }

                        if (columnMap.TryGetValue("contactPhone", out var phoneCol))
                        {
                            var phoneValue = worksheet.Cells[row, phoneCol].GetValue<string>();
                            student.ContactPhone = phoneValue?.Trim() ?? "";
                        }

                        if (columnMap.TryGetValue("contactEmail", out var emailCol))
                        {
                            var emailValue = worksheet.Cells[row, emailCol].GetValue<string>();
                            student.ContactEmail = emailValue?.Trim() ?? "";
                        }

                        if (columnMap.TryGetValue("grade", out var gradeCol))
                        {
                            student.Grade = worksheet.Cells[row, gradeCol].GetValue<string>()?.Trim() ?? "";
                        }

                        if (columnMap.TryGetValue("classTeacherId", out var teacherCol))
                        {
                            // Normalize teacher name read from Excel: trim and replace non-breaking spaces
                            var teacherNameRaw = worksheet.Cells[row, teacherCol].GetValue<string>();
                            var teacherName = (teacherNameRaw ?? string.Empty).Replace('\u00A0', ' ').Trim();
                            if (!string.IsNullOrEmpty(teacherName))
                            {
                                var teacherSnapshot = await _db.Collection("teachers").GetSnapshotAsync();
                                string NormalizeName(string? s) => (s ?? string.Empty).Replace('\u00A0', ' ').Trim();

                                var teacherDoc = teacherSnapshot.Documents
                                    .FirstOrDefault(d =>
                                    {
                                        if (d.TryGetValue<string>("name", out var n1) && !string.IsNullOrEmpty(n1))
                                        {
                                            var n1n = NormalizeName(n1);
                                            if (n1n.Equals(teacherName, StringComparison.OrdinalIgnoreCase) || n1n.IndexOf(teacherName, StringComparison.OrdinalIgnoreCase) >= 0)
                                                return true;
                                        }
                                        if (d.TryGetValue<string>("Name", out var n2) && !string.IsNullOrEmpty(n2))
                                        {
                                            var n2n = NormalizeName(n2);
                                            if (n2n.Equals(teacherName, StringComparison.OrdinalIgnoreCase) || n2n.IndexOf(teacherName, StringComparison.OrdinalIgnoreCase) >= 0)
                                                return true;
                                        }
                                        return false;
                                    });

                                if (teacherDoc != null)
                                {
                                    student.ClassTeacherId = teacherDoc.Id;
                                    // try to capture canonical teacher name too
                                    student.ClassTeacherName = teacherDoc.TryGetValue<string>("name", out var canonicalTeacherName) ? NormalizeName(canonicalTeacherName) : teacherName;
                                }
                            }
                        }

                        student.StudentId = string.Empty;
                        student.GradeLevel = string.Empty;
                        student.SpecificGrade = string.Empty;
                        student.ParentId = string.Empty;
                        student.SiblingOrder = 1;
                        student.Status = "active";
                        student.EnrollmentYear = DateTime.Now.Year;
                        student.CreatedAt = Timestamp.GetCurrentTimestamp();
                        student.UpdatedAt = Timestamp.GetCurrentTimestamp();

                        var year = DateTime.Now.Year;
                        var count = await _studentService.GetStudentCountForYearAsync(year);
                        student.StudentId = $"STU-{year}-{(count + result.SuccessCount + 1).ToString("D3")}";

                        if (!string.IsNullOrEmpty(student.Grade))
                        {
                            try
                            {
                                var normalizedGrade = NormalizeGradeToFeeStructure(student.Grade);
                                _logger.LogInformation("Mapping Excel grade '{OriginalGrade}' -> '{NormalizedGrade}'", student.Grade, normalizedGrade);

                                var feeDocId = $"{year}_{normalizedGrade}";
                                var feeDoc = await _db.Collection("feeStructures").Document(feeDocId).GetSnapshotAsync();

                                if (feeDoc.Exists)
                                {
                                    var fee = feeDoc.ConvertTo<FeeStructure>();
                                    student.CurrentFeeStructureId = fee.Id;
                                    student.GradeLevel = normalizedGrade;
                                    student.ClassName = fee.ClassName;

                                    var currentTerm = GetCurrentTermByCalendar();
                                    student.CurrentBalance = CalculateNewStudentStartingBalance(fee, currentTerm);
                                    student.PaymentStatus = "Outstanding";
                                    student.LastTermRolloverKey = $"{year}_{currentTerm}";

                                    _logger.LogInformation("Linked student {StudentName} to fee structure {FeeId}: Balance={Balance}, Term={Term}",
                                        $"{student.FirstName} {student.LastName}", fee.Id, student.CurrentBalance, currentTerm);
                                }
                                else
                                {
                                    _logger.LogWarning("No fee structure found for document ID '{FeeDocId}' (grade: '{OriginalGrade}', normalized: '{NormalizedGrade}')", feeDocId, student.Grade, normalizedGrade);
                                }
                            }
                            catch (Exception feeEx)
                            {
                                _logger.LogWarning(feeEx, "Could not auto-link fee for grade {Grade}", student.Grade);
                            }
                        }

                        if (hasBirthDate)
                        {
                            try
                            {
                                var dupQuery = _db.Collection("students").WhereEqualTo("birthDate", student.BirthDate);
                                var dupSnap = await dupQuery.GetSnapshotAsync();
                                var matchingDoc = dupSnap.Documents.FirstOrDefault(d =>
                                {
                                    var hasFn = d.TryGetValue<string>("firstName", out var fn) && !string.IsNullOrEmpty(fn);
                                    var hasLn = d.TryGetValue<string>("lastName", out var ln) && !string.IsNullOrEmpty(ln);
                                    return hasFn && hasLn && fn.Equals(student.FirstName, StringComparison.OrdinalIgnoreCase) && ln.Equals(student.LastName, StringComparison.OrdinalIgnoreCase);
                                });

                                if (matchingDoc != null)
                                {
                                    var existingTeacherId = matchingDoc.TryGetValue<string>("classTeacherId", out var et) ? et : string.Empty;
                                    var existingGrade = matchingDoc.TryGetValue<string>("grade", out var eg) ? eg : string.Empty;

                                    // If same grade level but different specific grade (e.g., 2B -> 2C), update grade and teacher info
                                    var existingGradeLevel = ExtractGradeLevel(existingGrade);
                                    var incomingGradeLevel = ExtractGradeLevel(student.Grade);

                                    if (!string.IsNullOrEmpty(student.Grade) && existingGradeLevel == incomingGradeLevel && existingGrade != student.Grade)
                                    {
                                        var updates = new Dictionary<string, object>
                                        {
                                            { "grade", student.Grade },
                                            { "updatedAt", Timestamp.GetCurrentTimestamp() }
                                        };

                                        if (!string.IsNullOrEmpty(student.ClassTeacherId) && !student.ClassTeacherId.Equals(existingTeacherId, StringComparison.OrdinalIgnoreCase))
                                        {
                                            updates["classTeacherId"] = student.ClassTeacherId;
                                            updates["classTeacherName"] = student.ClassTeacherName ?? string.Empty;
                                        }

                                        await _db.Collection("students").Document(matchingDoc.Id).UpdateAsync(updates);
                                        result.SuccessCount++;
                                        _logger.LogInformation("Updated existing student {StudentName} (Doc {Doc}) grade to {Grade} and teacher to {Teacher} from import (Row {Row})", student.FullName, matchingDoc.Id, student.Grade, student.ClassTeacherId, row);
                                        continue;
                                    }

                                    if (!string.IsNullOrEmpty(student.ClassTeacherId) && !student.ClassTeacherId.Equals(existingTeacherId, StringComparison.OrdinalIgnoreCase))
                                    {
                                        await _db.Collection("students").Document(matchingDoc.Id).UpdateAsync(new Dictionary<string, object>
                                        {
                                            { "classTeacherId", student.ClassTeacherId },
                                            { "classTeacherName", student.ClassTeacherName ?? string.Empty },
                                            { "updatedAt", Timestamp.GetCurrentTimestamp() }
                                        });

                                        result.SuccessCount++;
                                        _logger.LogInformation("Reassigned student {StudentName} (Doc {Doc}) to teacher {Teacher} from import (Row {Row})", student.FullName, matchingDoc.Id, student.ClassTeacherId, row);
                                        continue;
                                    }

                                    result.FailedCount++;
                                    result.Errors.Add($"Row {row}: Duplicate student detected - skipped.");
                                    _logger.LogInformation("Skipped duplicate student {StudentName} (Row {Row})", student.FullName, row);
                                    continue;
                                }
                            }
                            catch (Exception dupEx)
                            {
                                _logger.LogWarning(dupEx, "Could not perform duplicate check for row {Row}", row);
                            }
                        }

                        var docRef = await _db.Collection("students").AddAsync(student);

                        var updateFields = new Dictionary<string, object>
                        {
                            { "studentId", docRef.Id },
                            { "updatedAt", Timestamp.GetCurrentTimestamp() }
                        };
                        if (!string.IsNullOrEmpty(student.LastTermRolloverKey))
                            updateFields["lastTermRolloverKey"] = student.LastTermRolloverKey;
                        await docRef.UpdateAsync(updateFields);

                        student.Id = docRef.Id;
                        result.ImportedStudents.Add(student);
                        result.SuccessCount++;

                        _logger.LogInformation("Imported student {StudentName} (Row {Row})", student.FullName, row);
                    }
                    catch (Exception ex)
                    {
                        result.FailedCount++;
                        result.Errors.Add($"Row {row}: {ex.Message}");
                        _logger.LogError(ex, "Error importing row {Row}", row);
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"File processing error: {ex.Message}");
                _logger.LogError(ex, "Error processing import file");
            }

            return result;
        }

        public async Task<ClassListImportResult> ImportStudentsFromClassListAsync(List<ClassListStudent> students, string targetGrade, string targetClassSection, string? targetTeacherName = null)
        {
            var result = new ClassListImportResult();
            var academicYear = DateTime.Now.Year.ToString();

            // Resolve teacher ID and name once upfront if provided
            string? targetTeacherId = null;
            string? resolvedTeacherName = targetTeacherName;

            if (!string.IsNullOrWhiteSpace(targetTeacherName))
            {
                try
                {
                    var teacherSnapshot = await _db.Collection("teachers").GetSnapshotAsync();
                    var teacherDoc = teacherSnapshot.Documents
                        .FirstOrDefault(d =>
                        {
                            var hasLower = d.TryGetValue<string>("name", out var n1) && !string.IsNullOrEmpty(n1) && n1.Equals(targetTeacherName, StringComparison.OrdinalIgnoreCase);
                            var hasUpper = d.TryGetValue<string>("Name", out var n2) && !string.IsNullOrEmpty(n2) && n2.Equals(targetTeacherName, StringComparison.OrdinalIgnoreCase);
                            return hasLower || hasUpper;
                        });

                    if (teacherDoc != null)
                    {
                        targetTeacherId = teacherDoc.Id;
                        resolvedTeacherName = teacherDoc.TryGetValue<string>("name", out var tn) ? tn : targetTeacherName;
                    }
                }
                catch (Exception tEx)
                {
                    _logger.LogWarning(tEx, "Could not resolve teacher '{Teacher}' while importing class list.", targetTeacherName);
                }
            }

            foreach (var student in students)
            {
                try
                {
                    var existingQuery = await _db.Collection("students")
                        .WhereEqualTo("firstName", student.FirstName)
                        .WhereEqualTo("lastName", student.LastName)
                        .GetSnapshotAsync();

                    var existingStudents = existingQuery.Documents
                        .Select(d => d.ConvertTo<Student>())
                        .Where(s => s.BirthDate.ToDateTime().Date == student.BirthDate.Date)
                        .ToList();

                    if (existingStudents.Any())
                    {
                        var existing = existingStudents.First();
                        var existingGradeLevel = ExtractGradeLevel(existing.Grade);
                        var targetGradeLevel = ExtractGradeLevel(targetGrade);

                        // Check if grade OR teacher needs updating
                        var needsGradeUpdate = existingGradeLevel == targetGradeLevel && existing.Grade != targetGrade;
                        var needsTeacherUpdate = !string.IsNullOrWhiteSpace(targetTeacherName) &&
                            (string.IsNullOrEmpty(existing.ClassTeacherId) ||
                             !existing.ClassTeacherId.Equals(targetTeacherId ?? "", StringComparison.OrdinalIgnoreCase) ||
                             !existing.ClassTeacherName.Equals(targetTeacherName, StringComparison.OrdinalIgnoreCase));

                        if (needsGradeUpdate || needsTeacherUpdate)
                        {
                            var updates = new Dictionary<string, object>
                            {
                                { "updatedAt", Timestamp.GetCurrentTimestamp() }
                            };

                            if (needsGradeUpdate)
                            {
                                updates["grade"] = targetGrade;
                            }

                            if (needsTeacherUpdate)
                            {
                                if (!string.IsNullOrEmpty(targetTeacherId))
                                {
                                    updates["classTeacherId"] = targetTeacherId;
                                }
                                if (!string.IsNullOrEmpty(resolvedTeacherName))
                                {
                                    updates["classTeacherName"] = resolvedTeacherName;
                                }
                            }

                            await _db.Collection("students").Document(existing.Id).UpdateAsync(updates);

                            if (needsGradeUpdate && needsTeacherUpdate)
                            {
                                result.UpdatedCount++;
                                _logger.LogInformation("Updated existing student {Name} {Surname}: grade {OldGrade}->{NewGrade}, teacher {OldTeacher}->{NewTeacher}",
                                    student.FirstName, student.LastName, existing.Grade, targetGrade, existing.ClassTeacherName ?? "(none)", resolvedTeacherName);
                            }
                            else if (needsGradeUpdate)
                            {
                                result.UpdatedCount++;
                                _logger.LogInformation("Updated existing student {Name} {Surname}: grade {OldGrade}->{NewGrade}",
                                    student.FirstName, student.LastName, existing.Grade, targetGrade);
                            }
                            else
                            {
                                result.UpdatedCount++;
                                _logger.LogInformation("Updated existing student {Name} {Surname}: teacher {OldTeacher}->{NewTeacher}",
                                    student.FirstName, student.LastName, existing.ClassTeacherName ?? "(none)", resolvedTeacherName);
                            }
                            continue;
                        }
                        else if (existingGradeLevel == targetGradeLevel)
                        {
                            result.SkippedCount++;
                            _logger.LogInformation("Skipped existing student {Name} {Surname} — already in {Grade} with correct teacher",
                                student.FirstName, student.LastName, existing.Grade);
                            continue;
                        }
                    }

                    // Create new student
                    var newStudent = new Student
                    {
                        FirstName = student.FirstName,
                        LastName = student.LastName,
                        Grade = targetGrade,
                        BirthDate = Timestamp.FromDateTime(student.BirthDate.ToUniversalTime()),
                        Age = CalculateAge(student.BirthDate),
                        ContactPhone = student.GuardianPhone,
                        ParentId = student.GuardianName,
                        Status = "active",
                        CreatedAt = Timestamp.GetCurrentTimestamp(),
                        UpdatedAt = Timestamp.GetCurrentTimestamp(),
                        AutoGradeRolloverEnabled = true,
                        AutoAgeUpdateEnabled = true
                    };

                    if (!string.IsNullOrEmpty(targetTeacherId))
                    {
                        newStudent.ClassTeacherId = targetTeacherId;
                    }
                    if (!string.IsNullOrEmpty(resolvedTeacherName))
                    {
                        newStudent.ClassTeacherName = resolvedTeacherName;
                    }

                    var feeDocId = $"{academicYear}_{MapGradeToFeeStructureGrade(targetGrade)}";
                    var feeSnap = await _db.Collection("feeStructures").Document(feeDocId).GetSnapshotAsync();
                    if (feeSnap.Exists)
                    {
                        var fee = feeSnap.ConvertTo<FeeStructure>();
                        newStudent.CurrentBalance = fee.Term1Fee + fee.RegistrationFee + fee.BooksAndAids;
                        newStudent.CurrentFeeStructureId = feeDocId;
                    }

                    await _db.Collection("students").AddAsync(newStudent);
                    result.AddedCount++;
                }
                catch (Exception ex)
                {
                    result.ErrorCount++;
                    _logger.LogError(ex, "Failed to import student {Name} {Surname}", student.FirstName, student.LastName);
                }
            }

            return result;
        }

        private static string NormalizeGradeToFeeStructure(string grade)
        {
            // Delegate to StudentService to reuse centralized grade normalization logic.
            // Try static call first for the centralized implementation; if that is not available,
            // fall back to a local conservative mapping to preserve previous behavior.
            try
            {
                return StudentService.NormalizeGradeToFeeStructure(grade);
            }
            catch
            {
                if (string.IsNullOrWhiteSpace(grade))
                    return string.Empty;

                var normalized = grade.Trim().ToLowerInvariant();

                if (normalized.Contains("creche") || normalized.Contains("crèche") || normalized.Contains("kg"))
                    return "Creche";

                if (normalized.Contains("ready") || normalized.Contains("school ready") ||
                    normalized == "ra" || normalized == "rb" || normalized.Contains("grade r"))
                    return "Ready";

                if (normalized == "grade 1" || normalized == "grade1" || normalized == "1st grade" || normalized == "1st" ||
                    normalized == "grade 1a" || normalized == "grade 1b" || normalized == "grade 1c" ||
                    normalized == "1 a" || normalized == "1 b" || normalized == "1 c")
                    return "Grade1";

                if (normalized == "grade 2" || normalized == "grade2" || normalized == "2nd grade" ||
                    normalized == "grade 3" || normalized == "grade3" || normalized == "3rd grade" ||
                    normalized == "grade 4" || normalized == "grade4" || normalized == "4th grade" ||
                    normalized == "grade 5" || normalized == "grade5" || normalized == "5th grade" ||
                    normalized == "grade 2a" || normalized == "grade 2b" ||
                    normalized == "grade 3a" || normalized == "grade 3b" ||
                    normalized == "grade 4a" || normalized == "grade 4b" ||
                    normalized == "grade 5a" || normalized == "grade 5b" ||
                    normalized == "2 a" || normalized == "2 b" ||
                    normalized == "3 a" || normalized == "3 b" ||
                    normalized == "4 a" || normalized == "4 b" ||
                    normalized == "5 a" || normalized == "5 b" ||
                    normalized == "primary")
                    return "Primary";

                if (normalized == "grade 6" || normalized == "grade6" || normalized == "6th grade" ||
                    normalized == "6th" || normalized == "grade 6a" || normalized == "grade 6b" ||
                    normalized == "6 a" || normalized == "6 b" || normalized.Contains("senior"))
                    return "Senior";

                return grade;
            }
        }

        private static string GetCurrentTermByCalendar()
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

        private static int CalculateAge(DateTime birthDate)
        {
            var today = DateTime.Today;
            var age = today.Year - birthDate.Year;
            if (birthDate.Date > today.AddYears(-age)) age--;
            return age;
        }

        private static string ExtractGradeLevel(string? grade)
        {
            if (string.IsNullOrWhiteSpace(grade)) return "";

            var normalized = grade.Trim().ToLowerInvariant();
            var digits = new string(normalized.Where(char.IsDigit).ToArray());

            if (int.TryParse(digits, out var num))
            {
                if (num >= 1 && num <= 6) return $"Grade{num}";
            }

            if (normalized.Contains("creche") || normalized.Contains("kg")) return "Creche";
            if (normalized.Contains("ready") || normalized == "ra" || normalized == "rb" || normalized.Contains("grade r")) return "Ready";

            return grade.Trim();
        }

        private static string MapGradeToFeeStructureGrade(string grade)
        {
            // Delegate to StudentService to keep mapping logic centralized and consistent.
            try
            {
                return StudentService.MapGradeToFeeStructureGrade(grade);
            }
            catch
            {
                // Fallback to original conservative mapping if StudentService method is unavailable or throws.
                var normalized = grade?.Trim().ToLowerInvariant() ?? "";

                if (normalized.Contains("creche") || normalized.Contains("kg")) return "Creche";
                if (normalized.Contains("ready")) return "Ready";
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

    public class ColumnMapping
    {
        public string SourceColumn { get; set; } = string.Empty;
        public string TargetField { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
        public bool IsMapped { get; set; } = true;
    }

    public class ImportResult
    {
        public int TotalRows { get; set; }
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public List<Student> ImportedStudents { get; set; } = new List<Student>();
        public double SuccessRate => TotalRows > 0 ? (SuccessCount / (double)TotalRows) * 100 : 0;
    }

    

    
    public class ClassListStudent
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public DateTime BirthDate { get; set; }
        public string GuardianName { get; set; } = string.Empty;
        public string GuardianPhone { get; set; } = string.Empty;
    }
}