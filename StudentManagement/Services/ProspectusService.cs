using Google.Cloud.Firestore;
using StudentManagement.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Linq;

namespace StudentManagement.Services
{
    public class ProspectusService
    {
        private readonly FirestoreDb _db;
        private readonly CollectionReference _prospectusRef;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<ProspectusService> _logger;
        private readonly FeeExtractionService _feeExtractor;

        public ProspectusService(
            FirestoreDb db,
            IWebHostEnvironment environment,
            ILogger<ProspectusService> logger,
            FeeExtractionService feeExtractor)
        {
            _db = db;
            _prospectusRef = db.Collection("prospectus");
            _environment = environment;
            _logger = logger;
            _feeExtractor = feeExtractor;
        }
        public async Task<Prospectus> UploadProspectusAsync(IFormFile file, string academicYear, string uploadedBy)
        {
            // Save file to disk
            var uploadsFolder = Path.Combine(_environment.WebRootPath, "prospectus");
            Directory.CreateDirectory(uploadsFolder);

            var fileName = $"{academicYear}_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // EXTRACT FEES FROM PDF INSTEAD OF HARDCODING
            FeeExtractionService.ProspectusFeeData extractedFees;
            try
            {
                extractedFees = _feeExtractor.ExtractFeesFromPdf(filePath);
                _logger.LogInformation("Successfully extracted {Count} fee rules from PDF", extractedFees.Rules.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract fees from PDF, using empty rules");
                extractedFees = new FeeExtractionService.ProspectusFeeData
                {
                    AcademicYear = academicYear,
                    Rules = new List<ExtractedFeeRule>()
                };
            }

            // Add CHESS optional fee if not present
            if (!extractedFees.Rules.Any(r => r.Description.Contains("CHESS")))
            {
                extractedFees.Rules.Add(new ExtractedFeeRule
                {
                    GradeLevel = "ALL",
                    Description = "CHESS (6 months)",
                    Amount = 200,
                    Term = "once",
                    Category = "optional"
                });
            }

            // Save to Firestore
            var prospectus = new Prospectus
            {
                AcademicYear = extractedFees.AcademicYear,
                FileName = file.FileName,
                FileUrl = $"/prospectus/{fileName}",
                UploadedAt = Timestamp.GetCurrentTimestamp(),
                UploadedBy = uploadedBy,
                IsActive = true,
                ExtractedRules = extractedFees.Rules
            };

            var docRef = await _prospectusRef.AddAsync(prospectus);
            prospectus.Id = docRef.Id;

            _logger.LogInformation("Uploaded prospectus for {AcademicYear} with {RuleCount} rules",
                academicYear, extractedFees.Rules.Count);

            return prospectus;
        }


        public async Task<List<Prospectus>> GetProspectusesAsync(string? academicYear = null)
        {
            // Fetch all active prospectuses first (single filter = no composite index required)
            var snapshot = await _prospectusRef.WhereEqualTo("isActive", true).GetSnapshotAsync();

            var prospectuses = snapshot.Documents
                .Select(d => d.ConvertTo<Prospectus>())
                .ToList();

            // Filter by academic year in memory if specified
            if (!string.IsNullOrEmpty(academicYear))
            {
                prospectuses = prospectuses
                    .Where(p => p.AcademicYear == academicYear)
                    .ToList();
            }

            // Sort in memory. UploadedAt is a Firestore Timestamp; convert to DateTime for ordering.
            return prospectuses
                .OrderByDescending(p => p.UploadedAt.ToDateTime())
                .ToList();
        }

        public async Task<Prospectus?> GetProspectusAsync(string id)
        {
            var doc = await _prospectusRef.Document(id).GetSnapshotAsync();
            return doc.Exists ? doc.ConvertTo<Prospectus>() : null;
        }

        public async Task ApplyProspectusToFeeStructureAsync(string prospectusId)
        {
            var prospectus = await GetProspectusAsync(prospectusId);
            if (prospectus == null)
            {
                _logger.LogWarning("Prospectus {ProspectusId} not found", prospectusId);
                return;
            }

            var feeStructuresRef = _db.Collection("feeStructures");

            // Group rules by grade level and aggregate sums per category/term
            // Normalize rules: separate 1st Grade from 2nd-5th within Primary
            string DetermineKey(ExtractedFeeRule r)
            {
                if (!string.IsNullOrEmpty(r.Description) && r.Description.IndexOf("1st Grade", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Grade1"; // separate bucket for grade 1

                if (!string.IsNullOrEmpty(r.Description) && r.Description.IndexOf("2nd-5th", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Primary"; // keep Primary as 2nd-5th

                // fallback to existing grade level
                return r.GradeLevel;
            }

            var groupedRules = prospectus.ExtractedRules
                .Where(r => !string.Equals(r.GradeLevel, "ALL", StringComparison.OrdinalIgnoreCase))
                .GroupBy(r => DetermineKey(r))
                .ToList();

            foreach (var group in groupedRules)
            {
                var gradeLevel = group.Key;

                var registration = group.Where(r => string.Equals(r.Category, "registration", StringComparison.OrdinalIgnoreCase)).Sum(r => r.Amount);
                var books = group.Where(r => string.Equals(r.Category, "books", StringComparison.OrdinalIgnoreCase)).Sum(r => r.Amount);
                var term1 = group.Where(r => string.Equals(r.Category, "tuition", StringComparison.OrdinalIgnoreCase) && r.Term == "Term 1").Sum(r => r.Amount);
                var term2 = group.Where(r => string.Equals(r.Category, "tuition", StringComparison.OrdinalIgnoreCase) && r.Term == "Term 2").Sum(r => r.Amount);
                var term3 = group.Where(r => string.Equals(r.Category, "tuition", StringComparison.OrdinalIgnoreCase) && r.Term == "Term 3").Sum(r => r.Amount);

                var totalPerYear = registration + books + term1 + term2 + term3;

                var feeStructure = new FeeStructure
                {
                    Id = $"{prospectus.AcademicYear}_{gradeLevel}",
                    AcademicYear = prospectus.AcademicYear,
                    ClassName = GetClassName(gradeLevel),
                    GradeLevel = gradeLevel,
                    RegistrationFee = registration,
                    BooksAndAids = books,
                    Term1Fee = term1,
                    Term2Fee = term2,
                    Term3Fee = term3,
                    TotalPerYear = totalPerYear,
                    IncludesFuel = group.Any(r => r.Description != null && r.Description.IndexOf("fuel", StringComparison.OrdinalIgnoreCase) >= 0),
                    IsActive = true,
                    EffectiveDate = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                };

                var docRef = feeStructuresRef.Document(feeStructure.Id);
                await docRef.SetAsync(feeStructure, SetOptions.Overwrite);

                _logger.LogInformation(
                    "Created/Updated fee structure for {GradeLevel}: Registration={Reg}, Books={Books}, T1={T1}, T2={T2}, T3={T3}, Total={Total}",
                    gradeLevel, registration, books, term1, term2, term3, totalPerYear);
            }

            // After creating/updating fee structures, update students' currentBalance by adding expected amount for the current term
            try
            {
                // Update all students (including inactive) so prospectus application affects everyone; filter in-memory if needed
                var studentsSnap = await _db.Collection("students").GetSnapshotAsync();
                var currentTerm = DetermineCurrentTerm();

                foreach (var sdoc in studentsSnap.Documents)
                {
                    try
                    {
                        var student = sdoc.ConvertTo<Student>();

                        // Determine which prospectus grade bucket this student belongs to (e.g., Grade1, Primary, Senior, Creche, Ready)
                        var targetGradeBucket = MapStudentToProspectusGrade(student);
                        if (string.IsNullOrEmpty(targetGradeBucket))
                        {
                            _logger.LogDebug("Skipping student {StudentId} - could not map grade {Grade}", sdoc.Id, student.Grade ?? student.GradeLevel);
                            continue;
                        }

                        // Try to get the fee structure for this academic year and grade bucket
                        var feeDocId = $"{prospectus.AcademicYear}_{targetGradeBucket}";
                        var feeSnap = await _db.Collection("feeStructures").Document(feeDocId).GetSnapshotAsync();
                        FeeStructure? fee = null;
                        if (feeSnap.Exists)
                        {
                            fee = feeSnap.ConvertTo<FeeStructure>();
                        }
                        else
                        {
                            // Flexible fallback: query feeStructures for the academic year and attempt to match by gradeLevel/className/digits
                            var candidatesSnap = await _db.Collection("feeStructures").WhereEqualTo("isActive", true).GetSnapshotAsync();
                            var candidates = candidatesSnap.Documents.Select(d => {
                                var f = d.ConvertTo<FeeStructure>();
                                if (string.IsNullOrEmpty(f.Id)) f.Id = d.Id;
                                return f;
                            }).ToList();

                            // Try normalized grade match
                            var studentNorm = NormalizeBucket(targetGradeBucket);
                            fee = candidates.FirstOrDefault(f => !string.IsNullOrEmpty(f.GradeLevel) && NormalizeBucket(f.GradeLevel).Equals(studentNorm, StringComparison.OrdinalIgnoreCase)
                                || (!string.IsNullOrEmpty(f.ClassName) && f.ClassName.Equals(targetGradeBucket, StringComparison.OrdinalIgnoreCase)));

                            // numeric match fallback
                            if (fee == null)
                            {
                                var digits = ExtractDigits(targetGradeBucket);
                                if (!string.IsNullOrEmpty(digits))
                                {
                                    fee = candidates.FirstOrDefault(f => (!string.IsNullOrEmpty(f.GradeLevel) && f.GradeLevel.IndexOf(digits, StringComparison.OrdinalIgnoreCase) >= 0)
                                        || (!string.IsNullOrEmpty(f.ClassName) && f.ClassName.IndexOf(digits, StringComparison.OrdinalIgnoreCase) >= 0));
                                }
                            }

                            if (fee == null)
                            {
                                _logger.LogDebug("No fallback fee structure found for student {StudentId} grade {GradeBucket}", sdoc.Id, targetGradeBucket);
                                continue;
                            }
                        }

                        // Determine expected amount for the current term
                        double expected = currentTerm switch
                        {
                            "Term 1" => fee.Term1Fee + fee.RegistrationFee + fee.BooksAndAids,
                            "Term 2" => fee.Term2Fee,
                            "Term 3" => fee.Term3Fee,
                            _ => 0
                        };

                        if (expected <= 0) continue;

                        // Read existing balance robustly (could be stored as double, long, int)
                        double existingBalance = 0.0;
                        if (sdoc.TryGetValue<double>("currentBalance", out var cbDouble)) existingBalance = cbDouble;
                        else if (sdoc.TryGetValue<long>("currentBalance", out var cbLong)) existingBalance = Convert.ToDouble(cbLong);
                        else if (sdoc.TryGetValue<int>("currentBalance", out var cbInt)) existingBalance = Convert.ToDouble(cbInt);
                        else if (sdoc.TryGetValue<object>("currentBalance", out var cbObj) && cbObj != null)
                        {
                            try { existingBalance = Convert.ToDouble(cbObj); } catch { existingBalance = 0.0; }
                        }

                        var newBalance = existingBalance + expected;

                        var updates = new Dictionary<string, object>
                        {
                            { "currentBalance", newBalance },
                            { "currentFeeStructureId", fee.Id },
                            { "updatedAt", Timestamp.GetCurrentTimestamp() }
                        };

                        await _db.Collection("students").Document(sdoc.Id).UpdateAsync(updates);
                        _logger.LogInformation("Updated balance for student {StudentId}: +{Expected} (was {Old} -> now {New})", sdoc.Id, expected, existingBalance, newBalance);
                    }
                    catch (Exception exStudent)
                    {
                        _logger.LogWarning(exStudent, "Failed to update student balance for doc {DocId}", sdoc.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update student balances after applying prospectus {ProspectusId}", prospectusId);
            }

            _logger.LogInformation("Applied prospectus {ProspectusId} to fee structures", prospectusId);
        }


        private string MapStudentToProspectusGrade(Student student)
        {
            // Use GradeLevel if present, otherwise Grade
            var g = (student.GradeLevel ?? student.Grade ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(g)) return string.Empty;

            var lower = g.ToLowerInvariant();

            if (lower.Contains("crech") || lower.Contains("creche")) return "Creche";
            if (lower.Contains("ready") || lower.Contains("kg")) return "Ready";
            if (lower.Contains("primary")) return "Primary";
            if (lower.Contains("senior")) return "Senior";

            // Extract first digit sequence for numeric grade
            var digits = new string(g.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var num))
            {
                if (num == 1) return "Grade1";
                if (num >= 2 && num <= 5) return "Primary";
                if (num >= 6) return "Senior";
            }

            // Fallback: if grade contains '1' word
            if (lower.Contains("1st") || lower.Contains("1")) return "Grade1";

            return string.Empty;
        }

        private string DetermineCurrentTerm()
        {
            var now = DateTime.Now;
            var month = now.Month;
            var day = now.Day;

            if ((month == 8) || (month == 9) || (month == 10) || (month == 11 && day <= 19))
            {
                return "Term 1";
            }
            else if ((month == 11 && day >= 20) || month == 12 || month == 1 || month == 2 || (month == 3 && day <= 14))
            {
                return "Term 2";
            }
            else
            {
                return "Term 3";
            }
        }

        private string GetClassName(string gradeLevel)
        {
            return gradeLevel switch
            {
                "Creche" => "Cr�che to KG 2",
                "Ready" => "School Ready",
                "Grade1" => "1st Grade",
                "Primary" => "2nd Grade to 5th Grade",
                "Senior" => "6th Grade",
                _ => gradeLevel
            };
        }
        private static string NormalizeBucket(string gradeLevel)
        {
            if (string.IsNullOrEmpty(gradeLevel)) return string.Empty;

            var lower = gradeLevel.Trim().ToLowerInvariant();

            if (lower.Contains("crech") || lower.Contains("kg") || lower.Contains("kindergarten"))
                return "Creche";

            if (lower.Contains("ready"))
                return "Ready";

            if (lower.Contains("primary"))
                return "Primary";

            if (lower.Contains("senior"))
                return "Senior";

            var digits = new string(lower.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var num))
            {
                if (num == 1) return "Grade1";
                if (num >= 2 && num <= 5) return "Primary";
                if (num >= 6) return "Senior";
            }

            return gradeLevel.Trim();
        }

        private static string ExtractDigits(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return new string(input.Where(char.IsDigit).ToArray());
        }
    }
}