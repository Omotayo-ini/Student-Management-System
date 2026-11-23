using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace StudentManagement.Pages.Account;

[AllowAnonymous]
public sealed class LoginModel : PageModel
{
    private readonly IConfiguration _configuration;

    public LoginModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public FirebaseWebOptions Firebase { get; private set; } = new();

    public void OnGet()
    {
        Firebase = new FirebaseWebOptions
        {
            ApiKey = _configuration["FirebaseWeb:ApiKey"] ?? string.Empty,
            AuthDomain = _configuration["FirebaseWeb:AuthDomain"] ?? string.Empty,
            ProjectId = _configuration["FirebaseWeb:ProjectId"] ?? string.Empty,
            StorageBucket = _configuration["FirebaseWeb:StorageBucket"] ?? string.Empty,
            MessagingSenderId = _configuration["FirebaseWeb:MessagingSenderId"] ?? string.Empty,
            AppId = _configuration["FirebaseWeb:AppId"] ?? string.Empty,
            MeasurementId = _configuration["FirebaseWeb:MeasurementId"] ?? string.Empty
        };
    }
}

public sealed class FirebaseWebOptions
{
    public string ApiKey { get; init; } = string.Empty;
    public string AuthDomain { get; init; } = string.Empty;
    public string ProjectId { get; init; } = string.Empty;
    public string StorageBucket { get; init; } = string.Empty;
    public string MessagingSenderId { get; init; } = string.Empty;
    public string AppId { get; init; } = string.Empty;
    public string MeasurementId { get; init; } = string.Empty;
}
