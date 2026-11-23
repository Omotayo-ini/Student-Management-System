using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StudentManagement.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StudentManagement.Pages.Automation
{
    public class RolloverModel : PageModel
    {
        private readonly GradeRolloverService _rolloverService;
        private readonly ILogger<RolloverModel> _logger;

        public RolloverModel(GradeRolloverService rolloverService, ILogger<RolloverModel> logger)
        {
            _rolloverService = rolloverService;
            _logger = logger;
        }

        [BindProperty]
        public List<string> SelectedStudentIds { get; set; } = new();

        public RolloverPreview? Preview { get; set; }
        public RolloverResult? Result { get; set; }
        public string AcademicYear { get; set; } = DateTime.Now.Year.ToString();
        public bool IsPreview { get; set; } = true;

        public async Task OnGetAsync(bool preview = true)
        {
            IsPreview = preview;
            AcademicYear = DateTime.Now.Year.ToString();

            if (IsPreview)
            {
                Preview = await _rolloverService.PreviewRolloverAsync(AcademicYear);
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            AcademicYear = DateTime.Now.Year.ToString();

            if (SelectedStudentIds == null || !SelectedStudentIds.Any())
            {
                // Auto mode: roll over all with flag enabled
                Result = await _rolloverService.ExecuteRolloverAsync(AcademicYear, autoRolloverOnly: true);
            }
            else
            {
                // Manual mode: roll over selected students
                Result = await _rolloverService.ExecuteRolloverAsync(AcademicYear, autoRolloverOnly: false, approvedStudentIds: SelectedStudentIds);
            }

            TempData["SuccessMessage"] = $"Rollover completed! {Result.SuccessCount} students advanced, {Result.FailedCount} failed.";
            return RedirectToPage("Index");
        }
    }
}