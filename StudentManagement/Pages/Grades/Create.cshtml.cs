using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StudentManagement.Services;

namespace StudentManagement.Pages.Grades
{
    public class CreateModel : PageModel
    {
        private readonly GradeService _gradeService;

        public CreateModel(GradeService gradeService)
        {
            _gradeService = gradeService;
        }

        [BindProperty]
        public string GradeName { get; set; } = string.Empty;

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrWhiteSpace(GradeName)) return Page();
            await _gradeService.CreateGradeAsync(GradeName.Trim());
            return RedirectToPage("Index");
        }
    }
}