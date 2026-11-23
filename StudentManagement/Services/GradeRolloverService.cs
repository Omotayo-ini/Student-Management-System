using Google.Cloud.Firestore;
using Microsoft.Extensions.Logging;
using StudentManagement.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace StudentManagement.Services
{
    public class GradeRolloverService
    {
        private readonly FirestoreDb _db;
        private readonly GradeService _gradeService;
        private readonly TeacherService _teacherService;
        private readonly ILogger<GradeRolloverService> _logger;

        public GradeRolloverService(FirestoreDb db, GradeService gradeService, TeacherService teacherService, ILogger<GradeRolloverService> logger)
        {
            _db = db;
            _gradeService = gradeService;
            _teacherService = teacherService;
            _logger = logger;
        }

        /// <summary>
        /// Runs the annual rollover once it is due. The rollover is allowed to
        /// catch up after July 29 if the application was not running that day.
        /// </summary>
        public async Task CheckAndExecuteAutoRolloverAsync()
        {
            var today = DateTime.Today;
            var academicYear = today.Year;
            var rolloverDate = new DateTime(academicYear, 7, 29);

            if (today < rolloverDate)
            {
                _logger.LogInformation("Annual grade rollover is not due until {Date}", rolloverDate);
                return;
            }

            var newAcademicYear = academicYear.ToString();
            _logger.LogInformation("Checking annual grade rollover for academic year {Year}", newAcademicYear);
            await ExecuteRolloverAsync(newAcademicYear, autoRolloverOnly: true);
        }

        /// <summary>
        /// Preview what changes would happen in a rollover
        /// </summary>
        public async Task<RolloverPreview> PreviewRolloverAsync(string newAcademicYear)
        {
            var preview = new RolloverPreview { AcademicYear = newAcademicYear };
            var studentsSnapshot = await _db.Collection("students")
                .WhereEqualTo("status", "active")
                .GetSnapshotAsync();

            foreach (var doc in studentsSnapshot.Documents)
            {
                var student = doc.ConvertTo<Student>();
                if (!student.AutoGradeRolloverEnabled) continue;

                var change = await PreviewStudentRolloverAsync(student, newAcademicYear);
                if (change != null)
                    preview.Changes.Add(change);
            }

            return preview;
        }

        /// <summary>
        /// Execute rollover — can be auto (only students with flag) or manual (all approved)
        /// </summary>
        public async Task<RolloverResult> ExecuteRolloverAsync(string newAcademicYear, bool autoRolloverOnly = false, List<string>? approvedStudentIds = null)
        {
            var result = new RolloverResult { AcademicYear = newAcademicYear };
            var studentsSnapshot = await _db.Collection("students")
                .WhereEqualTo("status", "active")
                .GetSnapshotAsync();

            foreach (var doc in studentsSnapshot.Documents)
            {
                var student = doc.ConvertTo<Student>();

                // Skip if already rolled over this year
                if (student.LastRolloverYear == newAcademicYear) continue;

                // Auto mode: only process students with flag enabled
                if (autoRolloverOnly && !student.AutoGradeRolloverEnabled) continue;

                // Manual mode: only process approved students
                if (!autoRolloverOnly && approvedStudentIds != null && !approvedStudentIds.Contains(student.Id))
                    continue;

                var success = await RolloverStudentAsync(student, newAcademicYear);
                if (success)
                    result.SuccessCount++;
                else
                    result.FailedCount++;
            }

            _logger.LogInformation("Rollover completed for {Year}: {Success} succeeded, {Failed} failed",
                newAcademicYear, result.SuccessCount, result.FailedCount);

            return result;
        }

        private async Task<RolloverChange?> PreviewStudentRolloverAsync(Student student, string newAcademicYear)
        {
            // Grade 6 students: don't roll over in August, they graduate in October
            var isGrade6 = IsGrade6(student.Grade);
            if (isGrade6)
            {
                return new RolloverChange
                {
                    StudentId = student.Id,
                    StudentName = student.FullName,
                    CurrentGrade = student.Grade,
                    ProposedGrade = "[Graduate in October]",
                    ProposedGradeLevel = "Senior",
                    Action = RolloverAction.Skip,
                    Reason = "Grade 6 students graduate in October, not rolled over in August"
                };
            }

            var nextGrade = await _gradeService.GetNextGradeAsync(student.Grade);
            if (string.IsNullOrEmpty(nextGrade))
            {
                return new RolloverChange
                {
                    StudentId = student.Id,
                    StudentName = student.FullName,
                    CurrentGrade = student.Grade,
                    ProposedGrade = "[Unknown]",
                    Action = RolloverAction.Warning,
                    Reason = "Could not determine next grade"
                };
            }

            var nextGradeLevel = NormalizeGradeToFeeStructure(nextGrade);
            var newFeeDocId = $"{newAcademicYear}_{nextGradeLevel}";

            return new RolloverChange
            {
                StudentId = student.Id,
                StudentName = student.FullName,
                CurrentGrade = student.Grade,
                CurrentGradeLevel = student.GradeLevel,
                ProposedGrade = nextGrade,
                ProposedGradeLevel = nextGradeLevel,
                ProposedFeeStructureId = newFeeDocId,
                Action = student.AutoGradeRolloverEnabled ? RolloverAction.RollOver : RolloverAction.Skip,
                Reason = student.AutoGradeRolloverEnabled ? "Will be rolled over" : "Auto-rollover disabled for this student"
            };
        }

        private async Task<bool> RolloverStudentAsync(Student student, string newAcademicYear)
        {
            try
            {
                // Grade 6: don't roll over, they stay until October graduation
                if (IsGrade6(student.Grade))
                {
                    _logger.LogInformation("Skipping Grade 6 student {StudentName} - graduates in October", student.FullName);
                    return true; // Not a failure, just skipped
                }

                var nextGrade = await _gradeService.GetNextGradeAsync(student.Grade);
                if (string.IsNullOrEmpty(nextGrade))
                {
                    _logger.LogWarning("Could not determine next grade for {StudentName}", student.FullName);
                    return false;
                }

                var nextGradeLevel = NormalizeGradeToFeeStructure(nextGrade);
                var newFeeDocId = $"{newAcademicYear}_{nextGradeLevel}";

                // The fee structure is used only to update the student's academic
                // placement metadata. It must never be used to calculate or reset
                // the student's balance during an annual grade rollover.
                var feeDoc = await _db.Collection("feeStructures").Document(newFeeDocId).GetSnapshotAsync();
                string className = student.ClassName;

                if (feeDoc.Exists)
                {
                    var fee = feeDoc.ConvertTo<FeeStructure>();
                    className = fee.ClassName;
                }

                var updates = new Dictionary<string, object>
                {
                    { "grade", nextGrade },
                    { "gradeLevel", nextGradeLevel },
                    { "className", className },
                    { "currentFeeStructureId", newFeeDocId },
                    { "lastGradeRolloverDate", Timestamp.GetCurrentTimestamp() },
                    { "lastRolloverYear", newAcademicYear },
                    { "updatedAt", Timestamp.GetCurrentTimestamp() }
                };

                var teacher = await FindTeacherForGradeAsync(nextGrade);
                updates["classTeacherId"] = teacher?.Id ?? string.Empty;
                updates["classTeacherName"] = teacher?.Name ?? string.Empty;

                await _db.Collection("students").Document(student.Id).UpdateAsync(updates);

                _logger.LogInformation("Rolled over student {StudentName}: {OldGrade} -> {NewGrade}, Teacher: {Teacher}",
                    student.FullName, student.Grade, nextGrade, teacher?.Name ?? "Not assigned");

                return true;
            }

            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to roll over student {StudentId}", student.Id);
                return false;
            }
        }

        private async Task<Teacher?> FindTeacherForGradeAsync(string grade)
        {
            var teachers = await _teacherService.GetActiveTeachersAsync();
            return teachers
                .Where(t => t.IsActive)
                .FirstOrDefault(t => t.GradesHandled.Any(assignedGrade => GradesMatch(assignedGrade, grade)));
        }

        private static bool GradesMatch(string assignedGrade, string studentGrade)
        {
            var assigned = NormalizeGradeName(assignedGrade);
            var student = NormalizeGradeName(studentGrade);

            if (string.IsNullOrEmpty(assigned) || string.IsNullOrEmpty(student))
                return false;

            if (assigned == student)
                return true;

            return RemoveSection(assigned) == assigned &&
                   RemoveSection(student).Equals(assigned, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeGradeName(string grade)
        {
            return Regex.Replace(grade?.Trim().ToLowerInvariant() ?? string.Empty, @"\s+", "")
                .Replace("grade", string.Empty)
                .Replace("gr", string.Empty);
        }

        private static string RemoveSection(string grade)
        {
            return Regex.Replace(grade, @"[a-z]$", string.Empty);
        }

        /// <summary>
        /// Process class list upload to override auto-assigned grades and teachers
        /// </summary>
        public async Task<ClassListImportResult> ProcessClassListAsync(List<ClassListEntry> entries, string academicYear)
        {
            var result = new ClassListImportResult();
            var activeStudents = (await _db.Collection("students")
                .WhereEqualTo("status", "active")
                .GetSnapshotAsync())
                .Documents
                .Select(d => d.ConvertTo<Student>())
                .ToList();

            foreach (var entry in entries)
            {
                try
                {
                    var match = FindStudent(activeStudents, entry.FirstName, entry.Surname, entry.Birthdate);
                    var student = match.Student;
                    if (student == null)
                    {
                        result.NotFound.Add(entry);
                        result.ErrorCount++;
                        result.Errors.Add(match.Error ?? $"Student not found: {entry.FirstName} {entry.Surname}");
                        continue;
                    }

                    // Find teacher by name
                    var teacher = await FindTeacherAsync(entry.ClassTeacher);
                    var teacherId = teacher?.Id ?? "";
                    var teacherName = (teacher?.Name ?? entry.ClassTeacher).Trim();

                    if (!string.IsNullOrWhiteSpace(entry.ClassTeacher) && teacher == null)
                    {
                        result.Errors.Add($"Teacher not found for {student.FullName}: {entry.ClassTeacher}. The uploaded name was retained for review.");
                    }

                    // Check if grade differs from current
                    var gradeChanged = !student.Grade.Equals(entry.Grade, StringComparison.OrdinalIgnoreCase);
                    var teacherChanged = !student.ClassTeacherId.Equals(teacherId, StringComparison.OrdinalIgnoreCase) ||
                                         !student.ClassTeacherName.Equals(teacherName, StringComparison.OrdinalIgnoreCase);
                    var classChanged = !string.IsNullOrWhiteSpace(entry.ClassName) &&
                                       !student.ClassName.Equals(entry.ClassName, StringComparison.OrdinalIgnoreCase);

                    if (!gradeChanged && !teacherChanged && !classChanged)
                    {
                        result.Unchanged.Add(entry);
                        result.SkippedCount++;
                        continue;
                    }

                    // Normalize new grade
                    var newGradeLevel = NormalizeGradeToFeeStructure(entry.Grade);
                    var feeDocId = $"{academicYear}_{newGradeLevel}";

                    // Get fee structure for new grade
                    var feeDoc = await _db.Collection("feeStructures").Document(feeDocId).GetSnapshotAsync();
                    var updates = new Dictionary<string, object>
                    {
                        { "grade", entry.Grade },
                        { "gradeLevel", newGradeLevel },
                        { "currentFeeStructureId", feeDocId },
                        { "classTeacherId", teacherId },
                        { "classTeacherName", teacherName },
                        { "updatedAt", Timestamp.GetCurrentTimestamp() }
                    };

                    if (!string.IsNullOrWhiteSpace(entry.ClassName))
                        updates["className"] = entry.ClassName.Trim();

                    if (feeDoc.Exists)
                    {
                        var fee = feeDoc.ConvertTo<FeeStructure>();
                        if (string.IsNullOrWhiteSpace(entry.ClassName))
                            updates["className"] = fee.ClassName;
                    }

                    await _db.Collection("students").Document(student.Id).UpdateAsync(updates);

                    result.Updated.Add(new ClassListUpdate
                    {
                        StudentName = student.FullName,
                        OldGrade = student.Grade,
                        NewGrade = entry.Grade,
                        OldTeacher = student.ClassTeacherName,
                        NewTeacher = teacherName,
                        GradeChanged = gradeChanged,
                        TeacherChanged = teacherChanged
                    });
                    result.UpdatedCount++;

                    _logger.LogInformation("Class list updated student {StudentName}: Grade {OldGrade}->{NewGrade}, Teacher {OldTeacher}->{NewTeacher}",
                        student.FullName, student.Grade, entry.Grade, student.ClassTeacherName, teacherName);
                }
                catch (Exception ex)
                {
                    result.ErrorCount++;
                    _logger.LogError(ex, "Failed to process class list entry for {Name} {Surname}", entry.FirstName, entry.Surname);
                    result.Errors.Add($"Failed for {entry.FirstName} {entry.Surname}: {ex.Message}");
                }
            }

            return result;
        }

        private static (Student? Student, string? Error) FindStudent(
            List<Student> students,
            string firstName,
            string surname,
            DateTime? birthdate)
        {
            var normalizedFirstName = NormalizePersonName(firstName);
            var normalizedSurname = NormalizePersonName(surname);
            var matches = students
                .Where(s => NormalizePersonName(s.FirstName) == normalizedFirstName &&
                            NormalizePersonName(s.LastName) == normalizedSurname)
                .ToList();

            if (birthdate.HasValue && matches.Count > 1)
            {
                var date = birthdate.Value.Date;
                var datedMatches = matches.Where(s => s.BirthDate.ToDateTime().Date == date).ToList();
                if (datedMatches.Count == 1)
                    return (datedMatches[0], null);
                matches = datedMatches;
            }

            if (matches.Count == 1)
                return (matches[0], null);

            if (matches.Count > 1)
                return (null, $"Multiple active students matched: {firstName} {surname}. Include a matching birthdate.");

            return (null, $"Student not found: {firstName} {surname}");
        }

        private async Task<Teacher?> FindTeacherAsync(string teacherName)
        {
            if (string.IsNullOrWhiteSpace(teacherName))
                return null;

            var teachers = await _teacherService.GetAllTeachersAsync();
            var normalizedName = NormalizePersonName(teacherName);
            return teachers.FirstOrDefault(t => NormalizePersonName(t.Name) == normalizedName);
        }

        private static string NormalizePersonName(string value)
        {
            return Regex.Replace(value?.Trim().ToLowerInvariant() ?? string.Empty, @"[^a-z0-9]", string.Empty);
        }

        private static bool IsGrade6(string grade)
        {
            var g = grade?.Trim().ToLowerInvariant() ?? "";
            return g.Contains("grade 6") || g == "6th grade" || g == "6th" || g == "grade6" || g.Contains("senior");
        }

        private static string NormalizeGradeToFeeStructure(string grade)
        {
            if (string.IsNullOrWhiteSpace(grade))
                return string.Empty;

            var normalized = grade.Trim().ToLowerInvariant();

            if (normalized.Contains("creche") || normalized.Contains("crèche") ||
                normalized == "kg1" || normalized == "kg 1" || normalized == "kg2" || normalized == "kg 2")
                return "Creche";

            if (normalized.Contains("ready") || normalized.Contains("school ready"))
                return "Ready";

            if (normalized == "grade 1" || normalized == "grade1" || normalized == "1st grade" || normalized == "1st" ||
                normalized == "grade 1a" || normalized == "grade 1b" || normalized == "1 a" || normalized == "1 b")
                return "Grade1";

            if (normalized == "grade 2" || normalized == "grade2" || normalized == "2nd grade" ||
                normalized == "grade 3" || normalized == "grade3" || normalized == "3rd grade" ||
                normalized == "grade 4" || normalized == "grade4" || normalized == "4th grade" ||
                normalized == "grade 5" || normalized == "grade5" || normalized == "5th grade" ||
                normalized.Contains("grade 2") || normalized.Contains("grade 3") ||
                normalized.Contains("grade 4") || normalized.Contains("grade 5") ||
                normalized == "primary")
                return "Primary";

            if (normalized.Contains("grade 6") || normalized.Contains("senior") || normalized == "6th")
                return "Senior";

            return grade;
        }
    }

    // Result classes
    public class RolloverPreview
    {
        public string AcademicYear { get; set; } = string.Empty;
        public List<RolloverChange> Changes { get; set; } = new();
        public int TotalStudents => Changes.Count;
        public int WillRollover => Changes.Count(c => c.Action == RolloverAction.RollOver);
        public int WillSkip => Changes.Count(c => c.Action == RolloverAction.Skip);
        public int Warnings => Changes.Count(c => c.Action == RolloverAction.Warning);
    }

    public class RolloverChange
    {
        public string StudentId { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public string CurrentGrade { get; set; } = string.Empty;
        public string CurrentGradeLevel { get; set; } = string.Empty;
        public string ProposedGrade { get; set; } = string.Empty;
        public string ProposedGradeLevel { get; set; } = string.Empty;
        public string ProposedFeeStructureId { get; set; } = string.Empty;
        public RolloverAction Action { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public enum RolloverAction
    {
        RollOver,
        Skip,
        Warning
    }

    public class RolloverResult
    {
        public string AcademicYear { get; set; } = string.Empty;
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
    }

    // Class list models
    public class ClassListEntry
    {
        public string FirstName { get; set; } = string.Empty;
        public string Surname { get; set; } = string.Empty;
        public DateTime? Birthdate { get; set; }
        public string Grade { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public string ClassTeacher { get; set; } = string.Empty;
    }

    // UPDATED: ClassListImportResult now includes both list-based and count-based properties
    public class ClassListImportResult
    {
        // Count-based properties (used by ImportService.ImportStudentsFromClassListAsync)
        public int AddedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int SkippedCount { get; set; }
        public int ErrorCount { get; set; }
        public int TotalProcessed => AddedCount + UpdatedCount + SkippedCount + ErrorCount;

        // List-based properties (used by GradeRolloverService.ProcessClassListAsync)
        public List<ClassListUpdate> Updated { get; set; } = new();
        public List<ClassListEntry> Unchanged { get; set; } = new();
        public List<ClassListEntry> NotFound { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }

    public class ClassListUpdate
    {
        public string StudentName { get; set; } = string.Empty;
        public string OldGrade { get; set; } = string.Empty;
        public string NewGrade { get; set; } = string.Empty;
        public string OldTeacher { get; set; } = string.Empty;
        public string NewTeacher { get; set; } = string.Empty;
        public bool GradeChanged { get; set; }
        public bool TeacherChanged { get; set; }
    }
}