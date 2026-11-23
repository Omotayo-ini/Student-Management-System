using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using StudentManagement.Models;
using StudentManagement.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Cloud.Firestore;

namespace StudentManagement.Pages.Payments
{
    public class RecordModel : PageModel
    {
        private readonly PaymentService _paymentService;
        private readonly StudentService _studentService;
        private readonly ILogger<RecordModel> _logger;

        public RecordModel(
            PaymentService paymentService,
            StudentService studentService,
            ILogger<RecordModel> logger)
        {
            _paymentService = paymentService;
            _studentService = studentService;
            _logger = logger;
        }

        [BindProperty]
        public PaymentInput PaymentInput { get; set; } = new();

        public Dictionary<string, string> StudentMap { get; set; } = new Dictionary<string, string>();
        public Student? SelectedStudent { get; set; }
        public double CurrentBalance { get; set; }
        public List<Dictionary<string, object>> RecentPayments { get; set; } = new();
        public string CurrentTerm { get; set; } = string.Empty;

        public async Task OnGetAsync(string? studentId = null)
        {
            await LoadDataAsync(studentId);
        }

        // Lightweight endpoint for JS autocomplete to call via fetch
        public async Task<JsonResult> OnGetSearchAsync(string q)
        {
            try
            {
                var matches = await _studentService.SearchStudentsAsync(q, limit: 10);
                var result = matches.Select(m => new { display = m.Display, id = m.Id }).ToList();
                return new JsonResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Search failed for query {Query}", q);
                return new JsonResult(new List<object>());
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // Remove validation errors for optional fields not collected by the form.
            // These can be flagged by the model binder even without [Required] attributes
            // when the corresponding form fields are absent.
            ModelState.Remove("PaymentInput.FeeStructureId");
            ModelState.Remove("PaymentInput.ReferenceNumber");
            ModelState.Remove("PaymentInput.Notes");
            ModelState.Remove("PaymentInput.Method");

            _logger.LogInformation("OnPostAsync called. StudentFeeId='{StudentFeeId}', Amount={Amount}, Term='{Term}', AcademicYear={Year}",
                PaymentInput.StudentFeeId, PaymentInput.Amount, PaymentInput.Term, PaymentInput.AcademicYear);

            // CRITICAL FIX: Try to recover StudentFeeId from posted hidden field before validating ModelState
            if (string.IsNullOrEmpty(PaymentInput.StudentFeeId))
            {
                var hiddenId = Request.Form["SelectedStudentId"].ToString();
                if (!string.IsNullOrEmpty(hiddenId))
                {
                    PaymentInput.StudentFeeId = hiddenId;
                    _logger.LogInformation("Recovered StudentFeeId from SelectedStudentId: {Id}", hiddenId);
                }
            }

            if (string.IsNullOrEmpty(PaymentInput.StudentFeeId))
            {
                _logger.LogWarning("StudentFeeId is empty after recovery attempts");
                ModelState.AddModelError("PaymentInput.StudentFeeId", "Please select a student from the dropdown.");
                await LoadDataAsync(null);
                return Page();
            }

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("ModelState invalid: {Errors}",
                    string.Join(", ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
                await LoadDataAsync(PaymentInput.StudentFeeId);
                return Page();
            }

            try
            {
                var recordedBy = User.Identity?.Name ?? "System";

                // RecordPaymentAsync returns void and updates the student document internally
                await _paymentService.RecordPaymentAsync(PaymentInput, recordedBy);

                TempData["SuccessMessage"] = $"Payment of {PaymentInput.Amount:C} recorded successfully!";
                _logger.LogInformation("Payment recorded successfully for student {StudentId}", PaymentInput.StudentFeeId);

                return RedirectToPage("/StudentRecords/Details", new { id = PaymentInput.StudentFeeId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording payment for student {StudentId}", PaymentInput.StudentFeeId);
                ModelState.AddModelError("", $"Failed to record payment: {ex.Message}");
                await LoadDataAsync(PaymentInput.StudentFeeId);
                return Page();
            }
        }

        private async Task LoadDataAsync(string? selectedStudentId)
        {
            var students = await _studentService.GetStudentsAsync(statusFilter: "active");
            StudentMap = students.ToDictionary(s => $"{s.FullName} ({s.StudentId}) - {s.Grade}", s => s.Id);

            if (!string.IsNullOrEmpty(selectedStudentId))
            {
                SelectedStudent = await _studentService.GetStudentAsync(selectedStudentId);
                CurrentBalance = SelectedStudent?.CurrentBalance ?? 0;
            }

            var now = DateTime.Now;
            var month = now.Month;
            var day = now.Day;

            if ((month == 8) || (month == 9) || (month == 10) || (month == 11 && day <= 19))
                CurrentTerm = "Term 1";
            else if ((month == 11 && day >= 20) || month == 12 || month == 1 || month == 2 || (month == 3 && day <= 14))
                CurrentTerm = "Term 2";
            else
                CurrentTerm = "Term 3";

            PaymentInput.Term = CurrentTerm;
            PaymentInput.AcademicYear = DateTime.Now.Year;
            PaymentInput.Method = "Cash";
            PaymentInput.Date = DateTime.Now;

            if (!string.IsNullOrEmpty(selectedStudentId))
            {
                try
                {
                    RecentPayments = await _paymentService.GetStudentPaymentsAsync(selectedStudentId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not load recent payments for student {StudentId}", selectedStudentId);
                }
            }
        }
    }
}