using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using StudentManagement.Models;
using StudentManagement.Services;
using Google.Cloud.Firestore;

namespace StudentManagement.Pages.StudentRecords
{
    public class EditModel : PageModel
    {
        private readonly StudentService _studentService;
        private readonly TeacherService _teacherService;
        private readonly ILogger<EditModel> _logger;

        public EditModel(
            StudentService studentService,
            TeacherService teacherService,
            ILogger<EditModel> logger)
        {
            _studentService = studentService;
            _teacherService = teacherService;
            _logger = logger;
        }

        [BindProperty]
        public Student Student { get; set; } = new();

        [BindProperty]
        public DateTime BirthDateInput { get; set; }

        public SelectList TeacherSelectList { get; set; } = new SelectList(new List<Teacher>());

        public async Task<IActionResult> OnGetAsync(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            var student = await _studentService.GetStudentAsync(id);
            if (student == null)
            {
                return NotFound();
            }

            Student = student;
            BirthDateInput = Student.BirthDate.ToDateTime();
            await LoadTeachersAsync();
            await LoadGradesAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                await LoadTeachersAsync();
                await LoadGradesAsync();
                return Page();
            }

            try
            {
                var existingStudent = await _studentService.GetStudentAsync(Student.Id);
                if (existingStudent == null)
                {
                    return NotFound();
                }

                // Build partial updates to avoid overwriting fields not present in the form
                var updates = new Dictionary<string, object>();

                if (existingStudent.FirstName != Student.FirstName)
                    updates["firstName"] = Student.FirstName ?? string.Empty;
                if (existingStudent.LastName != Student.LastName)
                    updates["lastName"] = Student.LastName ?? string.Empty;

                var birthTimestamp = Timestamp.FromDateTime(BirthDateInput.ToUniversalTime());
                if (existingStudent.BirthDate != birthTimestamp)
                {
                    updates["birthDate"] = birthTimestamp;
                    updates["age"] = CalculateAge(BirthDateInput);
                }

                if (existingStudent.ContactPhone != Student.ContactPhone)
                    updates["contactPhone"] = Student.ContactPhone ?? string.Empty;
                if (existingStudent.ContactEmail != Student.ContactEmail)
                    updates["contactEmail"] = Student.ContactEmail ?? string.Empty;
                if (existingStudent.Grade != Student.Grade)
                {
                    updates["grade"] = Student.Grade ?? string.Empty;
                    // Infer gradeLevel from selected grade and persist it
                    var gradeLevel = _studentService.GetGradeLevelFromGrade(Student.Grade ?? string.Empty);
                    updates["gradeLevel"] = gradeLevel;
                }
                if (existingStudent.ClassTeacherId != Student.ClassTeacherId)
                    updates["classTeacherId"] = Student.ClassTeacherId ?? string.Empty;
                if (existingStudent.Status != Student.Status)
                    updates["status"] = Student.Status ?? string.Empty;

                if (updates.Any())
                {
                    await _studentService.UpdateStudentFieldsAsync(Student.Id, updates);
                }

                TempData["SuccessMessage"] = $"Student {Student.FullName} updated successfully!";
                return RedirectToPage("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating student");
                ModelState.AddModelError("", "Failed to update student.");
                await LoadTeachersAsync();
                return Page();
            }
        }

        private async Task LoadTeachersAsync()
        {
            var teachers = await _teacherService.GetAllTeachersAsync();
            TeacherSelectList = new SelectList(teachers, "Id", "Name");
        }
        private async Task LoadGradesAsync()
        {
            try
            {
                var gradeService = (GradeService?)HttpContext.RequestServices.GetService(typeof(GradeService));
                if (gradeService != null)
                {
                    var grades = await gradeService.GetAllGradesAsync();
                    ViewData["AvailableGrades"] = grades;
                }
                else
                {
                    ViewData["AvailableGrades"] = new List<string>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading grades");
                ViewData["AvailableGrades"] = new List<string>();
            }
        }

        private int CalculateAge(DateTime birthDate)
        {
            var today = DateTime.Today;
            var age = today.Year - birthDate.Year;
            if (birthDate.Date > today.AddYears(-age)) age--;
            return age;
        }
    }
}