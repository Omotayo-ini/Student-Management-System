using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StudentManagement.Services;

namespace StudentManagement.Pages.Grades
{
    public class IndexModel : PageModel
    {
        private readonly GradeService _gradeService;

        public IndexModel(GradeService gradeService)
        {
            _gradeService = gradeService;
        }

        public List<string> Grades { get; set; } = new();

        public async Task OnGetAsync()
        {
            Grades = await _gradeService.GetAllGradesAsync();
        }

        public async Task<IActionResult> OnPostDeleteAsync(string grade)
        {
            if (string.IsNullOrWhiteSpace(grade))
            {
                TempData["ErrorMessage"] = "No grade specified to delete.";
                return RedirectToPage();
            }

            try
            {
                var entries = await _gradeService.GetAllGradeEntriesAsync();
                var match = entries.FirstOrDefault(e => e.Grade.Equals(grade, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(match.Id))
                {
                    await _gradeService.DeleteGradeAsync(match.Id);
                    TempData["SuccessMessage"] = $"Grade '{grade}' deleted.";
                }
                else
                {
                    TempData["ErrorMessage"] = $"Grade '{grade}' not found.";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Failed to delete grade. Please try again.";
            }

            return RedirectToPage();
        }
    }
}