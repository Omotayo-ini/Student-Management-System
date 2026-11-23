using StudentManagement.Models; // Add this
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace StudentManagement.Services
{
    public class FeeExtractionService
    {
        private readonly ILogger<FeeExtractionService> _logger;

        public FeeExtractionService(ILogger<FeeExtractionService> logger)
        {
            _logger = logger;
        }

        // REMOVE the nested ExtractedFeeRule class — use Models.ExtractedFeeRule instead

        public class ProspectusFeeData
        {
            public string AcademicYear { get; set; } = string.Empty;
            public List<ExtractedFeeRule> Rules { get; set; } = new();
        }

        public ProspectusFeeData ExtractFeesFromPdf(string filePath)
        {
            var result = new ProspectusFeeData();
            var allWords = new List<(string Text, double X, double Y)>();

            using (var document = PdfDocument.Open(filePath))
            {
                var firstPage = document.GetPage(1);
                var firstPageText = string.Join(" ", firstPage.Letters.Select(l => l.Value));
                var yearMatch = Regex.Match(firstPageText, @"ACADEMIC YEAR\s+(\d{4}/\d{4})");
                if (yearMatch.Success)
                {
                    result.AcademicYear = yearMatch.Groups[1].Value;
                }
                else
                {
                    result.AcademicYear = DateTime.Now.Year.ToString();
                }

                var page = document.GetPage(2);
                var words = page.GetWords();

                foreach (var word in words)
                {
                    allWords.Add((word.Text, word.BoundingBox.Left, word.BoundingBox.Bottom));
                }
            }

            var rowTolerance = 8.0;
            var rows = allWords
                .GroupBy(w => Math.Round(w.Y / rowTolerance) * rowTolerance)
                .OrderByDescending(g => g.Key)
                .ToList();

            var gradePatterns = new Dictionary<string, string>
            {
                { "Crèche to KG 2", "Creche" },
                { "School Ready", "Ready" },
                { "1st Grade", "Grade1" },
                { "2nd to 5th Grade", "Primary" },
                { "6th Grade", "Senior" }
            };

            var extractedRows = new List<(string GradeKey, List<double> Amounts)>();

            foreach (var row in rows)
            {
                var rowWords = row.OrderBy(w => w.X).Select(w => w.Text).ToList();
                var rowText = string.Join(" ", rowWords);

                string? gradeKey = null;
                foreach (var gp in gradePatterns)
                {
                    if (rowText.Contains(gp.Key, StringComparison.OrdinalIgnoreCase) ||
                        rowText.Contains(gp.Key.Replace("è", "e"), StringComparison.OrdinalIgnoreCase) ||
                        rowText.Contains("Creche", StringComparison.OrdinalIgnoreCase))
                    {
                        gradeKey = gp.Value;
                        break;
                    }
                }

                if (gradeKey != null)
                {
                    var amounts = Regex.Matches(rowText, @"R(\d+(?:,\d{3})*)")
                        .Cast<Match>()
                        .Select(m => double.Parse(m.Groups[1].Value.Replace(",", "")))
                        .ToList();

                    if (amounts.Count >= 5)
                    {
                        extractedRows.Add((gradeKey, amounts));
                    }
                }
            }

            foreach (var row in extractedRows)
            {
                var amounts = row.Amounts;
                var grade = row.GradeKey;

                result.Rules.Add(new ExtractedFeeRule
                {
                    GradeLevel = grade,
                    Description = $"{GetClassName(grade)} - Registration",
                    Amount = amounts[0],
                    Term = "once",
                    Category = "registration"
                });

                result.Rules.Add(new ExtractedFeeRule
                {
                    GradeLevel = grade,
                    Description = $"{GetClassName(grade)} - Books and learning aids",
                    Amount = amounts[1],
                    Term = "once",
                    Category = "books"
                });

                result.Rules.Add(new ExtractedFeeRule
                {
                    GradeLevel = grade,
                    Description = $"{GetClassName(grade)} - Term 1 Tuition",
                    Amount = amounts[2],
                    Term = "Term 1",
                    Category = "tuition"
                });

                result.Rules.Add(new ExtractedFeeRule
                {
                    GradeLevel = grade,
                    Description = $"{GetClassName(grade)} - Term 2 Tuition",
                    Amount = amounts[3],
                    Term = "Term 2",
                    Category = "tuition"
                });

                result.Rules.Add(new ExtractedFeeRule
                {
                    GradeLevel = grade,
                    Description = $"{GetClassName(grade)} - Term 3 Tuition",
                    Amount = amounts[4],
                    Term = "Term 3",
                    Category = "tuition"
                });
            }

            _logger.LogInformation("Extracted {RuleCount} fee rules for academic year {Year}",
                result.Rules.Count, result.AcademicYear);

            return result;
        }

        private string GetClassName(string gradeLevel)
        {
            return gradeLevel switch
            {
                "Creche" => "Crèche to KG 2",
                "Ready" => "School Ready",
                "Grade1" => "1st Grade",
                "Primary" => "2nd Grade to 5th Grade",
                "Senior" => "6th Grade",
                _ => gradeLevel
            };
        }
    }
}