using System.ComponentModel.DataAnnotations;

namespace StudentManagement.Models;

public sealed class VoiceCommandRequest
{
    [Required]
    [MinLength(2)]
    public string Transcript { get; set; } = string.Empty;

    public bool Confirm { get; set; }
}

public enum VoiceIntentType
{
    Unknown,
    CreateStudent,
    ExportAnalyticsPdf
}

public sealed class VoiceCommandResponse
{
    public VoiceIntentType Intent { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public bool RequiresConfirmation { get; init; }
    public IReadOnlyList<string> MissingFields { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>();
    public string? StudentId { get; init; }
    public string? ClientAction { get; init; }
}

public sealed class StudentVoiceParameters
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Grade { get; set; }
    public DateTime? BirthDate { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
}
