using System;
using Google.Cloud.Firestore;

namespace StudentManagement.Models
{
    [FirestoreData]
    public class Payment
    {
        [FirestoreProperty]
        public string Id { get; set; } = string.Empty;

        [FirestoreProperty]
        public string StudentFeeId { get; set; } = string.Empty;

        [FirestoreProperty]
        public string FeeStructureId { get; set; } = string.Empty;

        [FirestoreProperty]
        public double Amount { get; set; }

        [FirestoreProperty]
        public double BalanceBefore { get; set; }

        [FirestoreProperty]
        public double BalanceAfter { get; set; }

        [FirestoreProperty]
        public DateTime Date { get; set; }

        [FirestoreProperty]
        public string Method { get; set; } = "Cash";

        [FirestoreProperty]
        public string ReferenceNumber { get; set; } = string.Empty;

        [FirestoreProperty]
        public string Term { get; set; } = string.Empty;

        [FirestoreProperty]
        public int AcademicYear { get; set; } = DateTime.Now.Year;

        [FirestoreProperty]
        public string Notes { get; set; } = string.Empty;

        [FirestoreProperty]
        public string RecordedBy { get; set; } = string.Empty;

        [FirestoreProperty]
        public DateTime RecordedAt { get; set; }
    }
}