using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StudentManagement.Models;
using StudentManagement.Services;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StudentManagement.Pages.StudentRecords
{
    public class DetailsModel : PageModel
    {
        private readonly StudentService _studentService;
        private readonly TeacherService _teacherService;
        private readonly StudentFeeService _feeService;
        private readonly ILogger<DetailsModel> _logger;

        public DetailsModel(
            StudentService studentService,
            TeacherService teacherService,
            StudentFeeService feeService,
            ILogger<DetailsModel> logger)
        {
            _studentService = studentService;
            _teacherService = teacherService;
            _feeService = feeService;
            _logger = logger;
        }

        public Student Student { get; set; } = new();
        public string? TeacherName { get; set; }
        public List<StudentFee> FeeRecords { get; set; } = new();

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

            // Load teacher name
            if (!string.IsNullOrEmpty(student.ClassTeacherId))
            {
                var teacher = await _teacherService.GetTeacherByIdAsync(student.ClassTeacherId); // ✅ FIXED
                TeacherName = teacher?.Name;
            }

            // Load fee records
            FeeRecords = await _feeService.GetStudentFeesAsync(id);

            return Page();
        }
    }
}