using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StudentManagement.Models;
using StudentManagement.Services;

namespace StudentManagement.Pages.DataImport
{
    public class IndexModel : PageModel
    {
        private readonly ImportService _importService;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<IndexModel> _logger;

        public IndexModel(ImportService importService, IWebHostEnvironment environment, ILogger<IndexModel> logger)
        {
            _importService = importService;
            _environment = environment;
            _logger = logger;
        }

        [BindProperty]
        public IFormFile UploadFile { get; set; } = null!;

        public List<ColumnMapping> ColumnMappings { get; set; } = new();
        public ImportResult? ImportResult { get; set; }
        public string? TempFilePath { get; set; }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostUploadAsync()
        {
            if (UploadFile == null || UploadFile.Length == 0)
            {
                ModelState.AddModelError("", "Please select a file");
                return Page();
            }

            try
            {
                var uploadsFolder = Path.Combine(Path.GetTempPath(), "StudentManagement_Imports");
                Directory.CreateDirectory(uploadsFolder);

                var fileName = $"{Guid.NewGuid()}{Path.GetExtension(UploadFile.FileName)}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await UploadFile.CopyToAsync(stream);
                }

                // Analyze columns
                ColumnMappings = await _importService.AnalyzeColumnsAsync(filePath);
                TempFilePath = filePath;

                // Store in TempData for the import step
                TempData["ImportFilePath"] = filePath;
                TempData["ImportFileName"] = UploadFile.FileName;

                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading file");
                ModelState.AddModelError("", $"Error processing file: {ex.Message}");
                return Page();
            }
        }

        public async Task<IActionResult> OnPostImportAsync(List<ColumnMapping> mappings)
        {
            var filePath = TempData["ImportFilePath"]?.ToString();
            var fileName = TempData["ImportFileName"]?.ToString() ?? "unknown";

            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
            {
                ModelState.AddModelError("", "File not found. Please upload again.");
                return Page();
            }

            try
            {
                ImportResult = await _importService.ExecuteImportAsync(filePath, mappings);

                // Cleanup temp file
                try { System.IO.File.Delete(filePath); } catch { }

                if (ImportResult.SuccessCount > 0)
                {
                    TempData["SuccessMessage"] = $"Successfully imported {ImportResult.SuccessCount} of {ImportResult.TotalRows} students!";
                }

                if (ImportResult.FailedCount > 0)
                {
                    TempData["WarningMessage"] = $"{ImportResult.FailedCount} rows failed to import.";
                }

                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing import");
                ModelState.AddModelError("", $"Import failed: {ex.Message}");

                // Cleanup
                try { System.IO.File.Delete(filePath); } catch { }

                return Page();
            }
        }
    }
}
