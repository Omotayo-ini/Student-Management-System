using Google.Cloud.Firestore;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication.Cookies;
using StudentManagement.Services;
using OfficeOpenXml;

// Set EPPlus license context
ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
        options.Cookie.Name = "EduManager.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// Configure Firestore
var firestoreSection = builder.Configuration.GetSection("Firestore");
var projectId = firestoreSection["ProjectId"];
var credentialsPath = firestoreSection["CredentialsPath"];

// Set environment variable for Google credentials
Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", credentialsPath);

if (FirebaseApp.DefaultInstance == null)
{
    FirebaseApp.Create(new AppOptions
    {
        Credential = GoogleCredential.FromFile(credentialsPath),
        ProjectId = projectId
    });
}

// Create Firestore client
builder.Services.AddSingleton(_ => FirestoreDb.Create(projectId));

// Register services (ensure StudentService is registered before ImportService)
builder.Services.AddScoped<StudentService>();
builder.Services.AddScoped<StudentFeeService>();
builder.Services.AddScoped<FeeCalculationService>();
builder.Services.AddScoped<GradeRolloverService>();
builder.Services.AddScoped<BackupService>();
builder.Services.AddScoped<ProspectusService>(); // New for PDF handling
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<CreditService>();
builder.Services.AddScoped<GradeService>();
builder.Services.AddSingleton<TeacherService>();
builder.Services.AddSingleton<ImportService>();
builder.Services.AddSingleton<FeeExtractionService>();
builder.Services.AddScoped<TermFeeService>();
builder.Services.AddScoped<StudentCreationService>();
builder.Services.AddScoped<VoiceCommandService>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();
app.MapControllers();
// Promote students before applying any new-term fees. The rollover service
// runs on or after July 29 and is safe to repeat because each student stores
// the academic year in which they were last promoted.
using (var scope = app.Services.CreateScope())
{
    var rolloverService = scope.ServiceProvider.GetRequiredService<GradeRolloverService>();
    await rolloverService.CheckAndExecuteAutoRolloverAsync();
}

// Apply term fees only after promotion. This handles all terms and is
// idempotent per student through LastTermRolloverKey.
using (var scope2 = app.Services.CreateScope())
{
    var termFeeService = scope2.ServiceProvider.GetRequiredService<TermFeeService>();
    await termFeeService.ApplyTermFeesIfDueAsync();
}

app.Run("http://localhost:5000");