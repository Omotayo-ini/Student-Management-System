using System.Globalization;
using System.Text.RegularExpressions;
using Google.Cloud.Firestore;
using StudentManagement.Models;

namespace StudentManagement.Services;

public sealed class VoiceCommandService
{
    private static readonly Regex NamePattern = new(@"(?:student\s+)?(?:named|called|name\s+is)\s+(?<name>[a-z][a-z'\-]*(?:\s+[a-z][a-z'\-]*){1,2})(?=\s+(?:in|from|born|birth|date|phone|telephone|email|,|$))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GradePattern = new(@"(?:grade|class)\s+(?<grade>[a-z0-9]+(?:\s+[a-z])?)(?=\s+(?:born|birth|date|phone|telephone|email|,|$))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PhonePattern = new(@"(?:phone|telephone|cell(?:phone)?)\s*(?:is|number)?\s*(?<phone>[+\d][\d\s()\-]{6,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EmailPattern = new(@"\b(?<email>[a-z0-9._%+\-]+@[a-z0-9.\-]+\.[a-z]{2,})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BirthDatePattern = new(@"(?:born|birth\s*date|date\s*of\s*birth)\s+(?:on\s+)?(?<date>\d{1,2}[/-]\d{1,2}[/-]\d{2,4}|[a-z]+\s+\d{1,2},?\s+\d{4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly StudentCreationService _studentCreationService;

    public VoiceCommandService(StudentCreationService studentCreationService)
    {
        _studentCreationService = studentCreationService;
    }

    public async Task<VoiceCommandResponse> ExecuteAsync(VoiceCommandRequest request, CancellationToken cancellationToken = default)
    {
        var transcript = request.Transcript.Trim();
        var normalized = transcript.ToLowerInvariant();

        if (normalized.Contains("export") && (normalized.Contains("analytics") || normalized.Contains("report")) && normalized.Contains("pdf"))
        {
            return new VoiceCommandResponse
            {
                Intent = VoiceIntentType.ExportAnalyticsPdf,
                Status = "client_action",
                Message = "Open the Analytics page and start the PDF export.",
                ClientAction = "export-analytics-pdf",
                Parameters = new Dictionary<string, string> { ["url"] = "/Analytics" }
            };
        }

        if (normalized.Contains("create") && (normalized.Contains("student") || normalized.Contains("learner") || normalized.Contains("pupil")))
        {
            var parameters = ParseStudentParameters(transcript);
            var missing = GetMissingFields(parameters);
            if (missing.Count > 0)
            {
                return new VoiceCommandResponse
                {
                    Intent = VoiceIntentType.CreateStudent,
                    Status = "needs_clarification",
                    Message = "Provide the student's first name, last name, grade, and birth date.",
                    MissingFields = missing,
                    Parameters = ToParameterMap(parameters)
                };
            }

            if (!request.Confirm)
            {
                return new VoiceCommandResponse
                {
                    Intent = VoiceIntentType.CreateStudent,
                    Status = "confirmation_required",
                    RequiresConfirmation = true,
                    Message = $"Create student {parameters.FirstName} {parameters.LastName} in {parameters.Grade}?",
                    Parameters = ToParameterMap(parameters)
                };
            }

            var student = new Student
            {
                FirstName = parameters.FirstName!,
                LastName = parameters.LastName!,
                Grade = parameters.Grade!,
                BirthDate = Timestamp.FromDateTime(parameters.BirthDate!.Value.ToUniversalTime()),
                ContactPhone = parameters.Phone ?? string.Empty,
                ContactEmail = parameters.Email ?? string.Empty
            };

            var id = await _studentCreationService.CreateAsync(student, cancellationToken);
            return new VoiceCommandResponse
            {
                Intent = VoiceIntentType.CreateStudent,
                Status = "executed",
                Message = $"Student {student.FullName} was created successfully.",
                StudentId = id,
                Parameters = ToParameterMap(parameters)
            };
        }

        return new VoiceCommandResponse
        {
            Intent = VoiceIntentType.Unknown,
            Status = "unsupported",
            Message = "I can create student records or start an analytics PDF export."
        };
    }

    private static StudentVoiceParameters ParseStudentParameters(string transcript)
    {
        var result = new StudentVoiceParameters();
        var nameMatch = NamePattern.Match(transcript);
        if (nameMatch.Success)
        {
            var parts = nameMatch.Groups["name"].Value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 2 && (parts[^1].Equals("in", StringComparison.OrdinalIgnoreCase) || parts[^1].Equals("from", StringComparison.OrdinalIgnoreCase)))
            {
                parts = parts[..^1];
            }
            result.FirstName = parts.FirstOrDefault();
            result.LastName = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : null;
        }

        var gradeMatch = GradePattern.Match(transcript);
        if (gradeMatch.Success) result.Grade = gradeMatch.Groups["grade"].Value.Trim().TrimEnd('.', ',');
        var phoneMatch = PhonePattern.Match(transcript);
        if (phoneMatch.Success) result.Phone = phoneMatch.Groups["phone"].Value.Trim();
        var emailMatch = EmailPattern.Match(transcript);
        if (emailMatch.Success) result.Email = emailMatch.Groups["email"].Value.Trim();
        var dateMatch = BirthDatePattern.Match(transcript);
        if (dateMatch.Success && DateTime.TryParse(dateMatch.Groups["date"].Value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var birthDate)) result.BirthDate = birthDate.Date;
        return result;
    }

    private static List<string> GetMissingFields(StudentVoiceParameters parameters)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(parameters.FirstName)) missing.Add("firstName");
        if (string.IsNullOrWhiteSpace(parameters.LastName)) missing.Add("lastName");
        if (string.IsNullOrWhiteSpace(parameters.Grade)) missing.Add("grade");
        if (!parameters.BirthDate.HasValue) missing.Add("birthDate");
        return missing;
    }

    private static IReadOnlyDictionary<string, string> ToParameterMap(StudentVoiceParameters parameters) =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["firstName"] = parameters.FirstName ?? string.Empty,
            ["lastName"] = parameters.LastName ?? string.Empty,
            ["grade"] = parameters.Grade ?? string.Empty,
            ["birthDate"] = parameters.BirthDate?.ToString("yyyy-MM-dd") ?? string.Empty,
            ["phone"] = parameters.Phone ?? string.Empty,
            ["email"] = parameters.Email ?? string.Empty
        };
}
