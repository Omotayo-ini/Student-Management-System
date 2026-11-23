using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StudentManagement.Models;
using StudentManagement.Services;

namespace StudentManagement.Pages.Teachers
{
    public class IndexModel : PageModel
    {
        private readonly TeacherService _teacherService;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(TeacherService teacherService, ILogger<IndexModel> logger)
        {
            _teacherService = teacherService;
            _logger = logger;
        }

        public List<Teacher> Teachers { get; set; } = new();

        public async Task OnGetAsync()
        {
            Teachers = await _teacherService.GetAllTeachersAsync();
        }

        public async Task<IActionResult> OnPostDeleteAsync(string id)
        {
            try
            {
                await _teacherService.DeleteTeacherAsync(id);
                TempData["SuccessMessage"] = "Teacher deleted.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting teacher {TeacherId}", id);
                TempData["ErrorMessage"] = "Failed to delete teacher.";
            }
            return RedirectToPage();
        }
    }
}