using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StudentManagement.Models;
using StudentManagement.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StudentManagement.Pages.Prospectus
{
    public class IndexModel : PageModel
    {
        private readonly ProspectusService _prospectusService;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(ProspectusService prospectusService, ILogger<IndexModel> logger)
        {
            _prospectusService = prospectusService;
            _logger = logger;
        }

        public List<Models.Prospectus> Prospectuses { get; set; } = new();

        [BindProperty]
        public IFormFile? PdfFile { get; set; }

        [BindProperty]
        public string AcademicYear { get; set; } = string.Empty;

        public async Task OnGetAsync()
        {
            try
            {
                Prospectuses = await _prospectusService.GetProspectusesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading prospectuses");
                Prospectuses = new List<Models.Prospectus>();
                TempData["ErrorMessage"] = "Unable to load prospectus data. Please try again later.";
            }
        }

        public async Task<IActionResult> OnPostUploadAsync()
        {
            if (PdfFile == null || PdfFile.Length == 0 || string.IsNullOrEmpty(AcademicYear))
            {
                ModelState.AddModelError("", "Please select a file and academic year.");
                await OnGetAsync();
                return Page();
            }

            try
            {
                var uploadedBy = User.Identity?.Name ?? "System";
                await _prospectusService.UploadProspectusAsync(PdfFile, AcademicYear, uploadedBy);
                TempData["SuccessMessage"] = "Prospectus uploaded and processed successfully.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading prospectus");
                TempData["ErrorMessage"] = "Failed to upload prospectus.";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostApplyAsync(string id)
        {
            try
            {
                await _prospectusService.ApplyProspectusToFeeStructureAsync(id);
                TempData["SuccessMessage"] = "Fee structure updated from prospectus.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying prospectus");
                TempData["ErrorMessage"] = "Failed to apply prospectus.";
            }

            return RedirectToPage();
        }
    }
}