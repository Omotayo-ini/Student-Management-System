using Google.Cloud.Firestore;
using StudentManagement.Models;

namespace StudentManagement.Services;

public sealed class StudentCreationService
{
    private readonly FirestoreDb _db;
    private readonly StudentService _studentService;
    private readonly StudentFeeService _studentFeeService;
    private readonly ILogger<StudentCreationService> _logger;

    public StudentCreationService(
        FirestoreDb db,
        StudentService studentService,
        StudentFeeService studentFeeService,
        ILogger<StudentCreationService> logger)
    {
        _db = db;
        _studentService = studentService;
        _studentFeeService = studentFeeService;
        _logger = logger;
    }

    public async Task<string> CreateAsync(Student student, CancellationToken cancellationToken = default)
    {
        var rawGrade = student.Grade ?? student.GradeLevel ?? string.Empty;
        student.GradeLevel = NormalizeGradeToFeeStructure(rawGrade);
        student.Grade ??= rawGrade;
        student.StudentId = $"STU-{DateTime.Now.Year}-{(await _studentService.GetStudentCountForYearAsync(DateTime.Now.Year) + 1):D3}";
        student.BirthDate = student.BirthDate == default
            ? Timestamp.GetCurrentTimestamp()
            : student.BirthDate;
        student.Age = CalculateAge(student.BirthDate.ToDateTime());
        student.EnrollmentYear = DateTime.Now.Year;
        student.Status = "active";
        student.CreatedAt = Timestamp.GetCurrentTimestamp();
        student.UpdatedAt = Timestamp.GetCurrentTimestamp();

        var id = await _studentService.CreateStudentAsync(student);
        await _studentService.UpdateStudentAsync(id, student);
        await _studentFeeService.InitializeStudentFeesAsync(id, DateTime.Now.Year.ToString());

        var term = DetermineCurrentTerm();
        var feeSnapshot = await _db.Collection("feeStructures")
            .Document($"{DateTime.Now.Year}_{student.GradeLevel}")
            .GetSnapshotAsync(cancellationToken: cancellationToken);

        var startingBalance = 0d;
        if (feeSnapshot.Exists)
        {
            var fee = feeSnapshot.ConvertTo<FeeStructure>();
            startingBalance = fee.RegistrationFee + fee.BooksAndAids + (term switch
            {
                "Term 1" => fee.Term1Fee,
                "Term 2" => fee.Term2Fee,
                _ => fee.Term3Fee
            });
            student.CurrentFeeStructureId = fee.Id;
        }
        else
        {
            _logger.LogWarning("Fee structure not found for grade {GradeLevel}", student.GradeLevel);
        }

        student.CurrentBalance = startingBalance;
        student.PaymentStatus = startingBalance > 0 ? "Outstanding" : "Paid";
        student.LastTermRolloverKey = $"{DateTime.Now.Year}_{term}";
        await _studentService.UpdateStudentAsync(id, student);
        return id;
    }

    private static int CalculateAge(DateTime birthDate)
    {
        var age = DateTime.Today.Year - birthDate.Year;
        if (birthDate.Date > DateTime.Today.AddYears(-age)) age--;
        return Math.Max(0, age);
    }

    private static string DetermineCurrentTerm()
    {
        var now = DateTime.Now;
        if (now.Month is >= 8 and <= 10 || now.Month == 11 && now.Day <= 19) return "Term 1";
        if (now.Month == 11 && now.Day >= 20 || now.Month is 12 or 1 or 2 || now.Month == 3 && now.Day <= 14) return "Term 2";
        return "Term 3";
    }

    private static string NormalizeGradeToFeeStructure(string grade)
    {
        if (string.IsNullOrWhiteSpace(grade)) return string.Empty;
        var value = grade.Trim().ToLowerInvariant();
        if (value.StartsWith("creche") || value.StartsWith("crèche") || value.StartsWith("kg1") || value.StartsWith("kg 1") || value.StartsWith("kg2") || value.StartsWith("kg 2")) return "Creche";
        if (value.StartsWith("grade r") || value.StartsWith("ready") || value == "r") return "Ready";
        if (value.StartsWith("grade 1") || value.StartsWith("1st")) return "Grade1";
        if (value.StartsWith("grade 2") || value.StartsWith("grade 3") || value.StartsWith("grade 4") || value.StartsWith("grade 5") || value == "primary") return "Primary";
        if (value.StartsWith("grade 6") || value.StartsWith("6th") || value.Contains("senior")) return "Senior";
        return grade.Trim();
    }
}
