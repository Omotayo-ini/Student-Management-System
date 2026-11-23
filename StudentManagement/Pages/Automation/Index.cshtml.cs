using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StudentManagement.Services;
using System;
using System.Threading.Tasks;

namespace StudentManagement.Pages.Automation
{
    public class IndexModel : PageModel
    {
        private readonly GradeRolloverService _rolloverService;
        private readonly StudentService _studentService;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(GradeRolloverService rolloverService, StudentService studentService, ILogger<IndexModel> logger)
        {
            _rolloverService = rolloverService;
            _studentService = studentService;
            _logger = logger;
        }

        public int TotalStudents { get; set; }
        public int AutoRolloverEnabled { get; set; }
        public int AutoRolloverDisabled { get; set; }
        public string CurrentTerm { get; set; } = string.Empty;
        public bool IsRolloverDay { get; set; }

        public async Task OnGetAsync()
        {
            var students = await _studentService.GetStudentsAsync(statusFilter: "active", pageSize: 1000);
            TotalStudents = students.Count;
            AutoRolloverEnabled = students.Count(s => s.AutoGradeRolloverEnabled);
            AutoRolloverDisabled = TotalStudents - AutoRolloverEnabled;

            var today = DateTime.Now;
            CurrentTerm = GetCurrentTerm();
            IsRolloverDay = today.Month == 8 && today.Day == 1;
        }

        private static string GetCurrentTerm()
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
    }
}