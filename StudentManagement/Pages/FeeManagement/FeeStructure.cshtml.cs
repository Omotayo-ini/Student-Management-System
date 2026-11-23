// Pages/FeeManagement/FeeStructure.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StudentManagement.Models;
using StudentManagement.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StudentManagement.Pages.FeeManagement
{
    public class FeeStructureModel : PageModel
    {
        private readonly FeeCalculationService _feeService;
        private readonly ILogger<FeeStructureModel> _logger;

        public FeeStructureModel(FeeCalculationService feeService, ILogger<FeeStructureModel> logger)
        {
            _feeService = feeService;
            _logger = logger;
        }

        // Use Model directly, not ViewModel
        public List<FeeStructure> GradeFeeStructures { get; set; } = new();
        public List<MiscellaneousFee> MiscellaneousFees { get; set; } = new();
        public string CurrentAcademicYear { get; set; } = DateTime.Now.Year.ToString(); // "2026"
        public List<Term> Terms { get; set; } = new List<Term>();

        [BindProperty]
        public FeeStructureInput NewFeeStructure { get; set; } = new();

        [BindProperty]
        public MiscellaneousFeeInput NewMiscFee { get; set; } = new();

        public async Task OnGetAsync(string? academicYear = null)
        {
            CurrentAcademicYear = academicYear ?? DateTime.Now.Year.ToString();
            // Populate terms (hardcoded for now)
            Terms = new List<Term>
            {
                new Term { Name = "Term 1", AcademicYear = DateTime.Now.Year, IsCurrent = true },
                new Term { Name = "Term 2", AcademicYear = DateTime.Now.Year, IsCurrent = false },
                new Term { Name = "Term 3", AcademicYear = DateTime.Now.Year, IsCurrent = false }
            };

            await LoadFeeDataAsync();
        }

        // NEW: Handler to update an existing fee structure
        public async Task<IActionResult> OnPostUpdateFeeAsync(
            string id,
            string academicYear,
            string gradeLevel,
            string className,
            double registrationFee,
            double booksAndAids,
            double term1Fee,
            double term2Fee,
            double term3Fee,
            bool includesFuel,
            bool autoAdjustOnRollover)
        {
            try
            {
                var totalPerYear = registrationFee + booksAndAids + term1Fee + term2Fee + term3Fee;

                var feeStructure = new FeeStructure
                {
                    Id = id,
                    AcademicYear = academicYear,
                    GradeLevel = gradeLevel,
                    ClassName = className,
                    RegistrationFee = registrationFee,
                    BooksAndAids = booksAndAids,
                    Term1Fee = term1Fee,
                    Term2Fee = term2Fee,
                    Term3Fee = term3Fee,
                    TotalPerYear = totalPerYear,
                    IncludesFuel = includesFuel,
                    AutoAdjustOnRollover = autoAdjustOnRollover,
                    IsActive = true,
                    EffectiveDate = DateTime.UtcNow
                };

                await _feeService.SaveFeeRuleAsync(feeStructure);

                _logger.LogInformation("Updated fee structure {Id} for {GradeLevel}: Reg={Reg}, Books={Books}, T1={T1}, T2={T2}, T3={T3}, Total={Total}",
                    id, gradeLevel, registrationFee, booksAndAids, term1Fee, term2Fee, term3Fee, totalPerYear);

                TempData["SuccessMessage"] = $"Fee structure for {className} updated successfully.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating fee structure {Id}", id);
                TempData["ErrorMessage"] = $"Failed to update fee structure: {ex.Message}";
            }

            return RedirectToPage(new { academicYear });
        }

        // Keep your existing OnPostUpdateFeeStructureAsync for adding new fee structures
        public async Task<IActionResult> OnPostUpdateFeeStructureAsync()
        {
            if (!ModelState.IsValid)
            {
                await LoadFeeDataAsync();
                return Page();
            }

            try
            {
                var feeStructure = new FeeStructure
                {
                    AcademicYear = CurrentAcademicYear,
                    ClassName = GetClassName(NewFeeStructure.GradeLevel),
                    GradeLevel = NewFeeStructure.GradeLevel,
                    RegistrationFee = NewFeeStructure.RegistrationFee,
                    BooksAndAids = NewFeeStructure.BooksAndAids,
                    Term1Fee = NewFeeStructure.Term1Fee,
                    Term2Fee = NewFeeStructure.Term2Fee,
                    Term3Fee = NewFeeStructure.Term3Fee,
                    IncludesFuel = NewFeeStructure.IncludesFuel,
                    TotalPerYear = NewFeeStructure.RegistrationFee + NewFeeStructure.BooksAndAids +
                                  NewFeeStructure.Term1Fee + NewFeeStructure.Term2Fee + NewFeeStructure.Term3Fee,
                    IsActive = true,
                    EffectiveDate = DateTime.UtcNow
                };

                await _feeService.SaveFeeRuleAsync(feeStructure);
                TempData["SuccessMessage"] = "Fee structure updated successfully.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving fee structure");
                TempData["ErrorMessage"] = "Failed to update fee structure.";
            }

            return RedirectToPage(new { academicYear = CurrentAcademicYear });
        }

        private async Task LoadFeeDataAsync()
        {
            GradeFeeStructures = await _feeService.GetFeeRulesAsync(CurrentAcademicYear);
            _logger.LogInformation("Loaded {Count} fee structures for academic year {Year}", GradeFeeStructures.Count, CurrentAcademicYear);
            foreach (var fs in GradeFeeStructures)
            {
                _logger.LogInformation("FeeStructure {Grade} => Reg={Reg}, Books={Books}, T1={T1}, T2={T2}, T3={T3}, Total={Total}",
                    fs.GradeLevel, fs.RegistrationFee, fs.BooksAndAids, fs.Term1Fee, fs.Term2Fee, fs.Term3Fee, fs.TotalPerYear);
            }
            EnsureAllGradeLevels();
            MiscellaneousFees = await _feeService.GetMiscellaneousFeesAsync();
        }

        private void EnsureAllGradeLevels()
        {
            var requiredLevels = new[]
            {
                ("Creche", "Crèche to KG 2"),
                ("Ready", "School Ready"),
                ("Grade1", "1st Grade"),
                ("Primary", "2nd Grade to 5th Grade"),
                ("Senior", "6th Grade")
            };

            foreach (var (level, name) in requiredLevels)
            {
                if (!GradeFeeStructures.Any(g => g.GradeLevel == level))
                {
                    GradeFeeStructures.Add(new FeeStructure
                    {
                        GradeLevel = level,
                        ClassName = name,
                        RegistrationFee = 0,
                        BooksAndAids = 0,
                        Term1Fee = 0,
                        Term2Fee = 0,
                        Term3Fee = 0,
                        TotalPerYear = 0,
                        IncludesFuel = false,
                        IsActive = true
                    });
                }
            }
            GradeFeeStructures = GradeFeeStructures.OrderBy(g => g.GradeLevel).ToList();
        }

        private string GetClassName(string level)
        {
            return level switch
            {
                "Creche" => "Crèche to KG 2",
                "Ready" => "School Ready",
                "Grade1" => "1st Grade",
                "Primary" => "2nd Grade to 5th Grade",
                "Senior" => "6th Grade",
                _ => level
            };
        }
    }

    public class FeeStructureInput
    {
        public string GradeLevel { get; set; } = string.Empty;
        public double RegistrationFee { get; set; }
        public double BooksAndAids { get; set; }
        public double Term1Fee { get; set; }
        public double Term2Fee { get; set; }
        public double Term3Fee { get; set; }
        public bool IncludesFuel { get; set; }
    }

    public class MiscellaneousFeeInput
    {
        public string Name { get; set; } = string.Empty;
        public double Amount { get; set; }
        public string BillingFrequency { get; set; } = "year";
        public string Description { get; set; } = string.Empty;
        public bool IsMandatory { get; set; }
    }
}