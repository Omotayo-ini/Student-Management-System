using Google.Cloud.Firestore;

namespace StudentManagement.Models
{
    [FirestoreData]
    public class MiscellaneousFee
    {
        [FirestoreDocumentId]
        public string Id { get; set; } = string.Empty;

        [FirestoreProperty("name")]
        public string Name { get; set; } = string.Empty;

        [FirestoreProperty("amount")]
        public double Amount { get; set; }

        [FirestoreProperty("billingFrequency")]
        public string BillingFrequency { get; set; } = "year";

        [FirestoreProperty("description")]
        public string Description { get; set; } = string.Empty;

        [FirestoreProperty("isMandatory")]
        public bool IsMandatory { get; set; }

        [FirestoreProperty("isActive")]
        public bool IsActive { get; set; } = true;
    }
}