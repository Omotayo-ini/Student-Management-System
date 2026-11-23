using Google.Cloud.Firestore;
using System.Collections.Generic;

namespace StudentManagement.Models
{
    [FirestoreData]
    public class Credit
    {
        [FirestoreDocumentId]
        public string Id { get; set; } = string.Empty;

        [FirestoreProperty("studentId")]
        public string StudentId { get; set; } = string.Empty;

        [FirestoreProperty("creditBalance")]
        public double CreditBalance { get; set; }

        [FirestoreProperty("source")]
        public string Source { get; set; } = "Overpayment";

        [FirestoreProperty("lastUpdated")]
        public Timestamp LastUpdated { get; set; }

        [FirestoreProperty("transactionHistory")]
        public List<CreditTransaction> TransactionHistory { get; set; } = new();
    }

    [FirestoreData]
    public class CreditTransaction
    {
        [FirestoreProperty("amount")]
        public double Amount { get; set; }

        [FirestoreProperty("type")]
        public string Type { get; set; } = string.Empty; // "CreditAdded", "CreditApplied", "CreditCreated"

        [FirestoreProperty("timestamp")]
        public Timestamp Timestamp { get; set; }

        [FirestoreProperty("description")]
        public string Description { get; set; } = string.Empty;
    }
}
