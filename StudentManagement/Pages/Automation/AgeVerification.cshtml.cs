using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StudentManagement.Models;
using StudentManagement.Services;
using Google.Cloud.Firestore;

namespace StudentManagement.Pages.Automation
{
    public class AgeVerificationModel : PageModel
    {
        private readonly StudentService _studentService;
        private readonly ILogger<AgeVerificationModel> _logger;

        public AgeVerificationModel(StudentService studentService, ILogger<AgeVerificationModel> logger)
        {
            _studentService = studentService;
            _logger = logger;
        }

        public List<AgeUpdateItem> PendingUpdates { get; set; } = new();
        public int TotalPending => PendingUpdates.Count;

        public async Task OnGetAsync()
        {
            var students = await _studentService.GetStudentsByBirthdayMonthAsync(DateTime.Now.Month);

            PendingUpdates = students.Select(s => new AgeUpdateItem
            {
                StudentId = s.Id,
                DisplayId = s.StudentId,
                FullName = s.FullName,
                BirthDate = s.BirthDate.ToDateTime(), // Convert Timestamp to DateTime
                CurrentAge = s.Age,
                NewAge = CalculateAge(s.BirthDate.ToDateTime()) // Convert then calculate
            }).ToList();
        }

        public async Task<IActionResult> OnPostBulkUpdateAsync()
        {
            var students = await _studentService.GetStudentsByBirthdayMonthAsync(DateTime.Now.Month);

            foreach (var student in students)
            {
                var newAge = CalculateAge(student.BirthDate.ToDateTime());
                if (newAge != student.Age)
                {
                    student.Age = newAge;
                    student.UpdatedAt = Timestamp.GetCurrentTimestamp(); // Use Timestamp
                    await _studentService.UpdateStudentAsync(student.Id, student);
                }
            }

            TempData["SuccessMessage"] = $"Updated ages for {students.Count} students";
            return RedirectToPage();
        }

        private int CalculateAge(DateTime birthDate)
        {
            var today = DateTime.Today;
            var age = today.Year - birthDate.Year;
            if (birthDate.Date > today.AddYears(-age)) age--;
            return age;
        }
    }

    public class AgeUpdateItem
    {
        public string StudentId { get; set; } = "";
        public string DisplayId { get; set; } = "";
        public string FullName { get; set; } = "";
        public DateTime BirthDate { get; set; }
        public int CurrentAge { get; set; }
        public int NewAge { get; set; }
    }
}