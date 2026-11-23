using System.Collections.Generic;

namespace StudentManagement.Models
{
    public class FeeSummary
    {
        public double TotalExpected { get; set; }
        public double TotalCollected { get; set; }
        public double TotalOutstanding => TotalExpected - TotalCollected;
        public double CollectionRate => TotalExpected > 0 ? (TotalCollected / TotalExpected) * 100 : 0;
        public int TotalStudents { get; set; }
        public int PaidStudents { get; set; }
        public int PartialStudents { get; set; }
        public int OverdueStudents { get; set; }
    }

    public class StudentFeeSummary
    {
        public string StudentId { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public string Grade { get; set; } = string.Empty;
        public double ExpectedFees { get; set; }
        public double TotalPaid { get; set; }
        public double Balance => ExpectedFees - TotalPaid;
        public string Status => Balance <= 0 ? "paid" : (TotalPaid > 0 ? "partial" : "overdue");
        public List<FeeBreakdown> FeeBreakdowns { get; set; } = new();
    }

    public class FeeBreakdown
    {
        public string AcademicYear { get; set; } = string.Empty;
        public string Term { get; set; } = string.Empty;
        public double AmountDue { get; set; }
        public double AmountPaid { get; set; }
        public double Balance => AmountDue - AmountPaid;
    }
}