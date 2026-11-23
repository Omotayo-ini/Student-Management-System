using Google.Cloud.Firestore;
using StudentManagement.Models;
using System.Linq;
using System.Text.RegularExpressions;

namespace StudentManagement.Services
{
    public class FeeCalculationService
    {
        private readonly FirestoreDb _db;
        private readonly ILogger<FeeCalculationService> _logger;

        public FeeCalculationService(FirestoreDb db, ILogger<FeeCalculationService> logger)
        {
            _db = db;
            _logger = logger;
        }

        private static string NormalizeGradeLevel(string gradeLevel)
        {
            if (string.IsNullOrEmpty(gradeLevel)) return string.Empty;
            var cleaned = gradeLevel.Trim().ToLowerInvariant().Replace("grade", "").Replace(" ", "").Trim();
            return $"Grade{cleaned}";
        }

        public async Task<double> CalculateTermFeeAsync(string academicYear, string gradeLevel, string term, int siblingOrder = 1)
        {
            // Normalize grade level to match Firestore document IDs e.g. "grade 6" -> "Grade6"
            var normalizedGrade = NormalizeGradeLevel(gradeLevel);

            // Try direct document lookup first using normalized grade
            var attemptedDocId = $"{academicYear}_{normalizedGrade}";
            var feeStructure = await _db.Collection("feeStructures").Document(attemptedDocId).GetSnapshotAsync();

            FeeStructure? fee = null;

            if (feeStructure.Exists)
            {
                fee = feeStructure.ConvertTo<FeeStructure>();
            }
            else
            {
                _logger.LogWarning("Fee structure document {DocId} not found, attempting flexible lookup", attemptedDocId);

                // Flexible fallback: search active fee structures for matching academic year and grade/class name
                var snapshot = await _db.Collection("feeStructures").WhereEqualTo("isActive", true).GetSnapshotAsync();
                var candidates = snapshot.Documents.Select(d => {
                    var f = d.ConvertTo<FeeStructure>();
                    if (string.IsNullOrEmpty(f.Id)) f.Id = d.Id; // fallback to document id
                    return f;
                }).ToList();

                // Filter by academic year where possible, but allow candidates with empty stored academicYear
                if (!string.IsNullOrEmpty(academicYear))
                {
                    candidates = candidates.Where(f => string.IsNullOrEmpty(f.AcademicYear) || f.AcademicYear.Equals(academicYear, System.StringComparison.OrdinalIgnoreCase)).ToList();
                }

                var lookup = (gradeLevel ?? string.Empty).Trim();
                var lookupNorm = lookup.ToLowerInvariant();
                var digits = new string(lookup.Where(char.IsDigit).ToArray());

                // Try exact normalized match on GradeLevel or ClassName
                var match = candidates.FirstOrDefault(f => (!string.IsNullOrEmpty(f.GradeLevel) && NormalizeGradeLevel(f.GradeLevel).Equals(NormalizeGradeLevel(gradeLevel), System.StringComparison.OrdinalIgnoreCase))
                                                         || (!string.IsNullOrEmpty(f.ClassName) && f.ClassName.Equals(lookup, System.StringComparison.OrdinalIgnoreCase)));

                // Try numeric grade match if lookup contains digits (e.g., "grade 6" -> "6")
                if (match == null && !string.IsNullOrEmpty(digits))
                {
                    match = candidates.FirstOrDefault(f => ((!string.IsNullOrEmpty(f.GradeLevel) && f.GradeLevel.IndexOf(digits, System.StringComparison.OrdinalIgnoreCase) >= 0)
                                                        || (!string.IsNullOrEmpty(f.ClassName) && f.ClassName.IndexOf(digits, System.StringComparison.OrdinalIgnoreCase) >= 0)));
                }

                // Try substring match on GradeLevel or ClassName (use normalized compare for GradeLevel)
                if (match == null && !string.IsNullOrEmpty(lookup))
                {
                    match = candidates.FirstOrDefault(f => ((!string.IsNullOrEmpty(f.GradeLevel) && NormalizeGradeLevel(f.GradeLevel).ToLowerInvariant().Contains(lookupNorm))
                                                        || (!string.IsNullOrEmpty(f.ClassName) && f.ClassName.ToLowerInvariant().Contains(lookupNorm))));
                }

                // As a final fallback, try matching against parts of the document id (e.g. "2026_Senior", "2025-2026_Primary_G6")
                if (match == null)
                {
                    foreach (var candidate in candidates)
                    {
                        var idParts = candidate.Id.Split(new[] { '_', '-' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim().ToLowerInvariant()).ToList();
                        // check direct part equality or contains digits match or normalized grade match
                        if (idParts.Any(p => p.Equals(lookupNorm) || (!string.IsNullOrEmpty(digits) && p.Contains(digits)))
                            || (!string.IsNullOrEmpty(candidate.GradeLevel) && NormalizeGradeLevel(candidate.GradeLevel).Equals(NormalizeGradeLevel(gradeLevel), System.StringComparison.OrdinalIgnoreCase)))
                        {
                            match = candidate;
                            break;
                        }
                    }
                }

                if (match != null)
                {
                    fee = match;
                    _logger.LogInformation("Fallback matched fee structure {FeeId} (grade/class: {Match}) for academicYear {Year}", fee.Id, lookup, academicYear);
                }
                else
                {
                    _logger.LogWarning("No fee structure found for academicYear={AcademicYear} grade/class='{GradeLevel}'", academicYear, gradeLevel);
                    return 0;
                }
            }

            if (fee == null) return 0;

            double baseFee = term switch
            {
                "Term 1" => fee.Term1Fee,
                "Term 2" => fee.Term2Fee,
                "Term 3" => fee.Term3Fee,
                _ => 0
            };

            // Add registration and books only for Term 1 (once per year)
            if (term == "Term 1")
            {
                baseFee += fee.RegistrationFee + fee.BooksAndAids;
            }

            // Apply sibling discount on tuition only (not registration/books)
            double discount = siblingOrder switch
            {
                2 => 0.10, // 10% off for 2nd child
                >= 3 => 0.15, // 15% off for 3rd+ child
                _ => 0
            };

            if (discount > 0 && term != "Term 1") // Don't double-discount registration
            {
                var tuitionOnly = term switch
                {
                    "Term 1" => fee.Term1Fee,
                    "Term 2" => fee.Term2Fee,
                    "Term 3" => fee.Term3Fee,
                    _ => 0
                };

                var discountAmount = tuitionOnly * discount;
                baseFee -= discountAmount;
            }

            return baseFee;
        }

        public async Task<double> CalculateTotalAnnualFeeAsync(string academicYear, string gradeLevel, int siblingOrder = 1)
        {
            var term1 = await CalculateTermFeeAsync(academicYear, gradeLevel, "Term 1", siblingOrder);
            var term2 = await CalculateTermFeeAsync(academicYear, gradeLevel, "Term 2", siblingOrder);
            var term3 = await CalculateTermFeeAsync(academicYear, gradeLevel, "Term 3", siblingOrder);
            return term1 + term2 + term3;
        }

        // Keep existing methods...
        public async Task<List<FeeStructure>> GetFeeRulesAsync(string academicYear)
        {
            // Fetch active fee structures and filter in memory to avoid strict equality issues
            var snapshot = await _db.Collection("feeStructures")
                .WhereEqualTo("isActive", true)
                .GetSnapshotAsync();

            var feeStructures = snapshot.Documents.Select(d => d.ConvertTo<FeeStructure>()).ToList();

            if (!string.IsNullOrEmpty(academicYear))
            {
                // Flexible matching: match if stored academicYear contains requested value or vice-versa
                feeStructures = feeStructures
                    .Where(f => !string.IsNullOrEmpty(f.AcademicYear) &&
                                (f.AcademicYear.IndexOf(academicYear, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 academicYear.IndexOf(f.AcademicYear, StringComparison.OrdinalIgnoreCase) >= 0))
                    .ToList();
            }

            return feeStructures;
        }

        public async Task SaveFeeRuleAsync(FeeStructure rule)
        {
            if (string.IsNullOrEmpty(rule.Id))
            {
                rule.Id = $"{rule.AcademicYear}_{rule.GradeLevel}";
            }

            rule.EffectiveDate = DateTime.UtcNow;
            await _db.Collection("feeStructures").Document(rule.Id).SetAsync(rule, SetOptions.Overwrite);
        }

        public async Task<List<MiscellaneousFee>> GetMiscellaneousFeesAsync()
        {
            var snapshot = await _db.Collection("miscellaneousFees")
                .WhereEqualTo("isActive", true)
                .GetSnapshotAsync();

            return snapshot.Documents.Select(d => d.ConvertTo<MiscellaneousFee>()).ToList();
        }

        public async Task SaveMiscellaneousFeeAsync(MiscellaneousFee fee)
        {
            if (string.IsNullOrEmpty(fee.Id))
            {
                await _db.Collection("miscellaneousFees").AddAsync(fee);
            }
            else
            {
                await _db.Collection("miscellaneousFees").Document(fee.Id).SetAsync(fee, SetOptions.Overwrite);
            }
        }

        public async Task DeleteMiscellaneousFeeAsync(string id)
        {
            await _db.Collection("miscellaneousFees").Document(id).UpdateAsync(new Dictionary<string, object>
            {
                { "isActive", false },
                { "updatedAt", DateTime.UtcNow }
            });
        }
    }
}