using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StudentManagement.Models;
using StudentManagement.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StudentManagement.Pages.StudentRecords
{
    public class IndexModel : PageModel
    {
        private readonly StudentService _studentService;
        private readonly GradeService _gradeService;
        private readonly TeacherService _teacherService;
        private readonly StudentFeeService _studentFeeService;
        private readonly CreditService _creditService;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(StudentService studentService, GradeService gradeService, TeacherService teacherService, StudentFeeService studentFeeService, CreditService creditService, ILogger<IndexModel> logger)
        {
            _studentService = studentService;
            _gradeService = gradeService;
            _teacherService = teacherService;
            _studentFeeService = studentFeeService;
            _creditService = creditService;
            _logger = logger;
        }

        public List<Student> Students { get; set; } = new();
        public List<string> AvailableGrades { get; set; } = new();
        public Dictionary<string, double> StudentCredits { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? GradeFilter { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? StatusFilter { get; set; } = "active";

        [BindProperty(SupportsGet = true)]
        public int CurrentPage { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public int? AgeMin { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? AgeMax { get; set; }

        public int TotalRecords { get; set; }
        public int PageSize { get; set; } = 10;
        public int TotalPages => (int)Math.Ceiling(TotalRecords / (double)PageSize);
        public int StartRecord => (CurrentPage - 1) * PageSize + 1;
        public int EndRecord => Math.Min(CurrentPage * PageSize, TotalRecords);

        public async Task OnGetAsync()
        {
            try
            {
                AvailableGrades = await _gradeService.GetAllGradesAsync();

                TotalRecords = await _studentService.GetStudentsCountAsync(GradeFilter, StatusFilter, SearchTerm, AgeMin, AgeMax);

                var studentsPage = await _studentService.GetStudentsAsync(
                    gradeFilter: GradeFilter,
                    statusFilter: StatusFilter,
                    searchTerm: SearchTerm,
                    pageSize: PageSize,
                    currentPage: CurrentPage,
                    ageMin: AgeMin,
                    ageMax: AgeMax);

                var teachers = await _teacherService.GetAllTeachersAsync();
                var teacherMap = teachers.ToDictionary(t => t.Id, t => t.Name);

                Students = studentsPage;

                foreach (var s in Students)
                {
                    if (!string.IsNullOrEmpty(s.ClassTeacherId) && teacherMap.ContainsKey(s.ClassTeacherId))
                        s.ClassTeacherName = teacherMap[s.ClassTeacherId];
                }

                // Load credit balances for display
                StudentCredits = new Dictionary<string, double>();
                foreach (var s in Students)
                {
                    try
                    {
                        var c = await _credit_service_safe_get(s.Id);
                        StudentCredits[s.Id] = c;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load credit for student {StudentId}", s.Id);
                        StudentCredits[s.Id] = 0;
                    }
                }

                // Balance is never recalculated from scratch here. CurrentBalance in Firestore is
                // the single source of truth: it is set once when a student is created/imported,
                // adjusted by TermFeeService when the term changes, and reduced when a
                // payment is recorded (PaymentService). This avoids the bug where a fully-paid
                // student (CurrentBalance == 0) was mistaken for an uninitialized one and had
                // their balance reset to the Term 1 fee on every page load.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading students");
                TempData["ErrorMessage"] = "Failed to load student records. Please try again.";
                Students = new List<Student>();
            }
        }

        // Helper to safely get credit balance without throwing DI or missing service issues
        private async Task<double> _credit_service_safe_get(string studentId)
        {
            if (_creditService == null) return 0;
            return await _creditService.GetCreditBalanceAsync(studentId);
        }

        private string DetermineCurrentTerm()
        {
            var now = DateTime.Now;
            var month = now.Month;
            var day = now.Day;

            if ((month == 8) || (month == 9) || (month == 10) || (month == 11 && day <= 19))
                return "Term 1";
            else if ((month == 11 && day >= 20) || month == 12 || month == 1 || month == 2 || (month == 3 && day <= 14))
                return "Term 2";
            else
                return "Term 3";
        }

        public async Task<IActionResult> OnPostDeleteAsync(string id)
        {
            try
            {
                await _studentService.DeleteStudentAsync(id);
                TempData["SuccessMessage"] = "Student deleted successfully.";
                _logger.LogInformation("Deleted student {StudentId}", id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting student {StudentId}", id);
                TempData["ErrorMessage"] = "Failed to delete student. Please try again.";
            }

            // Read filter values from the POST form (hidden inputs populated by the modal)
            var gradeFilter = Request.Form["gradeFilter"].FirstOrDefault();
            var statusFilter = Request.Form["statusFilter"].FirstOrDefault();
            var searchTerm = Request.Form["searchTerm"].FirstOrDefault();
            var ageMin = Request.Form["ageMin"].FirstOrDefault();
            var ageMax = Request.Form["ageMax"].FirstOrDefault();
            var currentPage = Request.Form["currentPage"].FirstOrDefault();

            var routeValues = new Microsoft.AspNetCore.Routing.RouteValueDictionary();
            if (!string.IsNullOrWhiteSpace(gradeFilter)) routeValues["GradeFilter"] = gradeFilter;
            if (!string.IsNullOrWhiteSpace(statusFilter)) routeValues["StatusFilter"] = statusFilter;
            if (!string.IsNullOrWhiteSpace(searchTerm)) routeValues["SearchTerm"] = searchTerm;
            if (!string.IsNullOrWhiteSpace(ageMin) && int.TryParse(ageMin, out var minAge)) routeValues["AgeMin"] = minAge;
            if (!string.IsNullOrWhiteSpace(ageMax) && int.TryParse(ageMax, out var maxAge)) routeValues["AgeMax"] = maxAge;
            if (!string.IsNullOrWhiteSpace(currentPage) && int.TryParse(currentPage, out var page)) routeValues["CurrentPage"] = page;

            return RedirectToPage("./Index", routeValues);
        }

        public async Task<IActionResult> OnPostUpdateBalanceAsync(string id, double newBalance)
        {
            try
            {
                if (string.IsNullOrEmpty(id))
                {
                    TempData["ErrorMessage"] = "Student ID is required.";
                    return RedirectToPage();
                }

                await _studentService.UpdateStudentBalanceAsync(id, newBalance);

                TempData["SuccessMessage"] = "Balance updated successfully.";
                _logger.LogInformation("Balance updated via UI for student {StudentId}: {NewBalance}", id, newBalance);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating balance for student {StudentId}", id);
                TempData["ErrorMessage"] = "Failed to update balance.";
            }

            // Read filters from POST form to preserve filter state after update
            var gradeFilter = Request.Form["gradeFilter"].FirstOrDefault();
            var statusFilter = Request.Form["statusFilter"].FirstOrDefault();
            var searchTerm = Request.Form["searchTerm"].FirstOrDefault();
            var ageMin = Request.Form["ageMin"].FirstOrDefault();
            var ageMax = Request.Form["ageMax"].FirstOrDefault();
            var currentPage = Request.Form["currentPage"].FirstOrDefault();

            var routeValues = new Microsoft.AspNetCore.Routing.RouteValueDictionary();
            if (!string.IsNullOrWhiteSpace(gradeFilter)) routeValues["GradeFilter"] = gradeFilter;
            if (!string.IsNullOrWhiteSpace(statusFilter)) routeValues["StatusFilter"] = statusFilter;
            if (!string.IsNullOrWhiteSpace(searchTerm)) routeValues["SearchTerm"] = searchTerm;
            if (!string.IsNullOrWhiteSpace(ageMin) && int.TryParse(ageMin, out var minAge)) routeValues["AgeMin"] = minAge;
            if (!string.IsNullOrWhiteSpace(ageMax) && int.TryParse(ageMax, out var maxAge)) routeValues["AgeMax"] = maxAge;
            if (!string.IsNullOrWhiteSpace(currentPage) && int.TryParse(currentPage, out var page)) routeValues["CurrentPage"] = page;

            return RedirectToPage("./Index", routeValues);
        }

        public async Task<IActionResult> OnPostToggleGradeRolloverAsync(string id)
        {
            await _studentService.ToggleAutoGradeRolloverAsync(id);
            return new OkResult();
        }

        public async Task<IActionResult> OnPostToggleAgeUpdateAsync(string id)
        {
            await _studentService.ToggleAutoAgeUpdateAsync(id);
            return new OkResult();
        }
    }
}