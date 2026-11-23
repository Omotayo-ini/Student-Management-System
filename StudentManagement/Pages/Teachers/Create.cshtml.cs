using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StudentManagement.Models;
using StudentManagement.Services;

namespace StudentManagement.Pages.Teachers
{
    public class CreateModel : PageModel
    {
        private readonly TeacherService _teacherService;
        private readonly ILogger<CreateModel> _logger;

        public CreateModel(TeacherService teacherService, ILogger<CreateModel> logger)
        {
            _teacherService = teacherService;
            _logger = logger;
        }

        [BindProperty]
        public Teacher Teacher { get; set; } = new();

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            try
            {
                Teacher.IsActive = true;
                var id = await _teacherService.CreateTeacherAsync(Teacher);
                TempData["SuccessMessage"] = $"Teacher {Teacher.Name} added successfully!";
                return RedirectToPage("/StudentRecords/Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating teacher");
                ModelState.AddModelError("", "Failed to add teacher.");
                return Page();
            }
        }
    }
}