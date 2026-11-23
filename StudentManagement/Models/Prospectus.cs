using Google.Cloud.Firestore;
using System.Collections.Generic;

namespace StudentManagement.Models
{
    [FirestoreData]
    public class Prospectus
    {
        [FirestoreDocumentId]
        public string Id { get; set; } = string.Empty;

        [FirestoreProperty("academicYear")]
        public string AcademicYear { get; set; } = string.Empty;

        [FirestoreProperty("fileName")]
        public string FileName { get; set; } = string.Empty;

        [FirestoreProperty("fileUrl")]
        public string FileUrl { get; set; } = string.Empty;

        [FirestoreProperty("uploadedAt")]
        public Timestamp UploadedAt { get; set; }

        [FirestoreProperty("uploadedBy")]
        public string UploadedBy { get; set; } = string.Empty;

        [FirestoreProperty("isActive")]
        public bool IsActive { get; set; } = true;

        [FirestoreProperty("extractedRules")]
        public List<ExtractedFeeRule> ExtractedRules { get; set; } = new();
    }

    [FirestoreData]
    public class ExtractedFeeRule
    {
        [FirestoreProperty("gradeLevel")]
        public string GradeLevel { get; set; } = string.Empty;

        [FirestoreProperty("description")]
        public string Description { get; set; } = string.Empty;

        [FirestoreProperty("amount")]
        public double Amount { get; set; }

        [FirestoreProperty("term")]
        public string Term { get; set; } = string.Empty;

        [FirestoreProperty("category")]
        public string Category { get; set; } = string.Empty; // "tuition", "misc", "lab"
    }
}