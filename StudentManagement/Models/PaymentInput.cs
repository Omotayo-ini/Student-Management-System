using System;
using System.ComponentModel.DataAnnotations;

namespace StudentManagement.Models
{
    public class PaymentInput
    {
        [Required]
        public string StudentFeeId { get; set; } = string.Empty;

        // Removed [Required] - not collected by the Record Payment form
        public string FeeStructureId { get; set; } = string.Empty;

        [Required]
        [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
        public double Amount { get; set; }

        public string Method { get; set; } = "Cash";

        public string ReferenceNumber { get; set; } = string.Empty;

        public string Term { get; set; } = string.Empty;

        public int AcademicYear { get; set; } = DateTime.Now.Year;

        public string Notes { get; set; } = string.Empty;

        public DateTime Date { get; set; } = DateTime.Now;
    }
}