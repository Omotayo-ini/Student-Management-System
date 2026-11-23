using Google.Cloud.Firestore;
using System.Collections.Generic;

namespace StudentManagement.Models
{
    [FirestoreData]
    public class StudentFee
    {
        [FirestoreDocumentId]
        public string Id { get; set; } = string.Empty; // "{academicYear}_{term}"

        [FirestoreProperty("academicYear")]
        public string AcademicYear { get; set; } = string.Empty;

        [FirestoreProperty("term")]
        public string Term { get; set; } = string.Empty; // "Term 1", "Term 2", "Term 3"

        [FirestoreProperty("termDates")]
        public string TermDates { get; set; } = string.Empty; // "August to November"

        [FirestoreProperty("amountDue")]
        public double AmountDue { get; set; }

        [FirestoreProperty("amountPaid")]
        public double AmountPaid { get; set; }

        public double Balance => AmountDue - AmountPaid;

        public string Status => Balance <= 0 ? "paid" : (AmountPaid > 0 ? "partial" : "pending");

        [FirestoreProperty("payments")]
        public List<PaymentEntry> Payments { get; set; } = new();

        [FirestoreProperty("lastUpdated")]
        public Timestamp LastUpdated { get; set; }
    }

    [FirestoreData]
    public class PaymentEntry
    {
        [FirestoreProperty("date")]
        public Timestamp Date { get; set; }

        [FirestoreProperty("amount")]
        public double Amount { get; set; }

        [FirestoreProperty("method")]
        public string Method { get; set; } = string.Empty; // "Bank Transfer", "Mpesa", "Eco-cash", "Merchant Till"

        [FirestoreProperty("reference")]
        public string Reference { get; set; } = string.Empty;

        [FirestoreProperty("recordedBy")]
        public string RecordedBy { get; set; } = string.Empty;
    }
}