// Pages/Settings/Deployment.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace StudentManagement.Pages.Settings
{
    public class DeploymentModel : PageModel
    {
        [BindProperty]
        public string ServerType { get; set; } = "IIS";

        public void OnGet()
        {
        }

        public IActionResult OnPost()
        {
            // Save settings logic here
            TempData["SuccessMessage"] = "Settings saved successfully!";
            return RedirectToPage();
        }

        public IActionResult OnPostExport()
        {
            // Backup logic here
            TempData["SuccessMessage"] = "Backup created successfully!";
            return RedirectToPage();
        }
    }
}