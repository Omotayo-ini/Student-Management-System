using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using StudentManagement.Models;
using StudentManagement.Services;
using Google.Cloud.Firestore;

namespace StudentManagement.Pages.StudentRecords
{
    public class CreateModel : PageModel
    {
        private readonly StudentService _studentService;
        private readonly GradeService _gradeService;
        private readonly TeacherService _teacherService;
        private readonly StudentFeeService _studentFeeService;
        private readonly FirestoreDb _db;
        private readonly ILogger<CreateModel> _logger;

        public CreateModel(
            StudentService studentService,
            GradeService gradeService,
            TeacherService teacherService,
            StudentFeeService studentFeeService,
            FirestoreDb db,
            ILogger<CreateModel> logger)
        {
            _studentService = studentService;
            _gradeService = gradeService;
            _teacherService = teacherService;
            _studentFeeService = studentFeeService;
            _db = db;
            _logger = logger;
        }

        [BindProperty]
        public Student Student { get; set; } = new();

        [BindProperty]
        public DateTime BirthDateInput { get; set; } = DateTime.Now.AddYears(-10);

        public SelectList TeacherSelectList { get; set; } = new SelectList(new List<Teacher>());
        public List<string> AvailableGrades { get; set; } = new();
        public List<Teacher> AvailableTeachers { get; set; } = new();

        public async Task OnGetAsync()
        {
            AvailableGrades = await _gradeService.GetAllGradesAsync();
            AvailableTeachers = await _teacherService.GetAllTeachersAsync(); // ✅ FIXED
            await LoadTeachersAsync();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                AvailableGrades = await _gradeService.GetAllGradesAsync();
                AvailableTeachers = await _teacherService.GetAllTeachersAsync(); // ✅ FIXED
                await LoadTeachersAsync();
                return Page();
            }

            try
            {
                // Convert DateTime to Timestamp
                Student.BirthDate = Timestamp.FromDateTime(BirthDateInput.ToUniversalTime());
                Student.Age = CalculateAge(BirthDateInput);
                Student.StudentId = await GenerateStudentIdAsync();

                // ✅ FIXED: Removed EnrollmentDate line (not in Student model)
                // Or add EnrollmentDate to Student.cs if you need it

                Student.EnrollmentYear = DateTime.Now.Year;
                Student.CreatedAt = Timestamp.GetCurrentTimestamp();
                Student.UpdatedAt = Timestamp.GetCurrentTimestamp();
                Student.Status = "active";

                var id = await _studentService.CreateStudentAsync(Student);

                // Normalize the raw grade (e.g. "grade 1b", "KG1 a", "Creche a", "grade Ra")
                // down to the fee structure bucket name (e.g. "Grade1", "Creche", "Ready").
                // The specific class (1A/1B/1C etc.) is kept separately in Student.Grade for
                // display, while GradeLevel drives fee lookups.
                var rawGrade = Student.Grade ?? Student.GradeLevel ?? string.Empty;
                Student.GradeLevel = NormalizeGradeToFeeStructure(rawGrade);
                if (string.IsNullOrEmpty(Student.Grade))
                    Student.Grade = rawGrade;

                await _studentService.UpdateStudentAsync(id, Student);

                // Initialize fee records for the student for the current academic year
                var academicYear = DateTime.Now.Year.ToString();
                await _studentFeeService.InitializeStudentFeesAsync(id, academicYear);

                // New students always start owing Registration + Books + the current term's fee,
                // regardless of which term they join in. Look up the fee structure directly
                // (same pattern as StudentService.BulkImportAsync) rather than going through
                // GetOutstandingBalanceForTermAsync, which only adds Registration + Books in Term 1.
                var currentTerm = DetermineCurrentTerm();
                var feeDocId = $"{DateTime.Now.Year}_{Student.GradeLevel}";
                var feeDoc = await _db.Collection("feeStructures").Document(feeDocId).GetSnapshotAsync();

                double startingBalance = 0;
                if (feeDoc.Exists)
                {
                    var fee = feeDoc.ConvertTo<FeeStructure>();
                    var termFee = currentTerm switch
                    {
                        "Term 1" => fee.Term1Fee,
                        "Term 2" => fee.Term2Fee,
                        "Term 3" => fee.Term3Fee,
                        _ => fee.Term1Fee
                    };
                    startingBalance = fee.RegistrationFee + fee.BooksAndAids + termFee;
                    Student.CurrentFeeStructureId = fee.Id;
                }
                else
                {
                    _logger.LogWarning("Fee structure {FeeDocId} not found when creating student {StudentId}; starting balance set to 0", feeDocId, id);
                }

                Student.CurrentBalance = startingBalance;
                Student.PaymentStatus = startingBalance > 0 ? "Outstanding" : "Paid";

                // Mark this student as already "rolled over" for the term they're joining in.
                // Without this, RolloverTermBalancesAsync (which runs on the Student Records
                // page load) would see no LastTermRolloverKey, assume they were never charged
                // for the current term, and add the term fee a second time on top of the
                // starting balance we just calculated.
                Student.LastTermRolloverKey = $"{DateTime.Now.Year}_{currentTerm}";

                await _studentService.UpdateStudentAsync(id, Student);

                TempData["SuccessMessage"] = $"Student {Student.FullName} created successfully!";
                return RedirectToPage("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating student");
                ModelState.AddModelError("", "Failed to create student.");
                await LoadTeachersAsync();
                return Page();
            }
        }

        private async Task LoadTeachersAsync()
        {
            var teachers = await _teacherService.GetAllTeachersAsync();
            TeacherSelectList = new SelectList(teachers, "Id", "Name");
        }

        private int CalculateAge(DateTime birthDate)
        {
            var today = DateTime.Today;
            var age = today.Year - birthDate.Year;
            if (birthDate.Date > today.AddYears(-age)) age--;
            return age;
        }

        private string DetermineCurrentTerm()
        {
            var now = DateTime.Now;
            var month = now.Month;
            var day = now.Day;

            if ((month == 8) || (month == 9) || (month == 10) || (month == 11 && day <= 19))
            {
                return "Term 1";
            }
            else if ((month == 11 && day >= 20) || month == 12 || month == 1 || month == 2 || (month == 3 && day <= 14))
            {
                return "Term 2";
            }
            else
            {
                return "Term 3";
            }
        }

        private async Task<string> GenerateStudentIdAsync()
        {
            var year = DateTime.Now.Year;
            var count = await _studentService.GetStudentCountForYearAsync(year); // ✅ FIXED
            return $"STU-{year}-{(count + 1).ToString("D3")}";
        }

        /// <summary>
        /// Maps the raw grade/class label (e.g. "grade 1b", "KG1 a", "Creche a", "grade Ra")
        /// to the fee structure bucket name used as part of the Firestore document ID
        /// (e.g. "Grade1", "Creche", "Ready", "Primary", "Senior"). Mirrors
        /// StudentService.NormalizeGradeToFeeStructure so both files agree on the mapping.
        /// The trailing A/B/C class letter is intentionally ignored - all sections of the
        /// same grade share one fee structure.
        /// </summary>
        private static string NormalizeGradeToFeeStructure(string grade)
        {
            if (string.IsNullOrWhiteSpace(grade))
                return string.Empty;

            var normalized = grade.Trim().ToLowerInvariant();

            // Creche a/b/c, KG1 a/b, KG2 a/b
            if (normalized.StartsWith("creche") || normalized.StartsWith("crèche") ||
                normalized.StartsWith("kg1") || normalized.StartsWith("kg 1") ||
                normalized.StartsWith("kg2") || normalized.StartsWith("kg 2"))
                return "Creche";

            // grade Ra/Rb/Rc (School Ready)
            if (normalized.StartsWith("grade r") || normalized.StartsWith("r a") ||
                normalized.StartsWith("ready") || normalized == "r")
                return "Ready";

            // grade 1a/1b/1c
            if (normalized.StartsWith("grade 1") || normalized.StartsWith("1st") || normalized.StartsWith("1 "))
                return "Grade1";

            // grade 2a/2b/2c through grade 5a/5b/5c -> Primary
            if (normalized.StartsWith("grade 2") || normalized.StartsWith("grade 3") ||
                normalized.StartsWith("grade 4") || normalized.StartsWith("grade 5") ||
                normalized.StartsWith("2nd") || normalized.StartsWith("3rd") ||
                normalized.StartsWith("4th") || normalized.StartsWith("5th") ||
                normalized == "primary")
                return "Primary";

            // grade 6a/6b/6c -> Senior
            if (normalized.StartsWith("grade 6") || normalized.StartsWith("6th") || normalized.Contains("senior"))
                return "Senior";

            return grade.Trim();
        }
    }
}