using System;
using Google.Cloud.Firestore;

namespace StudentManagement.Models
{
    [FirestoreData]
    public class FeeStructure
    {
        [FirestoreProperty("id")]
        public string Id { get; set; } = string.Empty;

        [FirestoreProperty("className")]
        public string ClassName { get; set; } = string.Empty;

        [FirestoreProperty("gradeLevel")]
        public string GradeLevel { get; set; } = string.Empty;

        [FirestoreProperty("booksAndAids")]
        public double BooksAndAids { get; set; }

        [FirestoreProperty("term1Fee")]
        public double Term1Fee { get; set; }

        [FirestoreProperty("term2Fee")]
        public double Term2Fee { get; set; }

        [FirestoreProperty("term3Fee")]
        public double Term3Fee { get; set; }

        // ✅ CHANGED: Made writable so ProspectusService can assign to it
        [FirestoreProperty("totalPerYear")]
        public double TotalPerYear { get; set; }

        [FirestoreProperty("registrationFee")]
        public double RegistrationFee { get; set; }

        [FirestoreProperty("includesFuel")]
        public bool IncludesFuel { get; set; }

        // ✅ CHANGED: type from int to string
        [FirestoreProperty("academicYear")]
        public string AcademicYear { get; set; } = string.Empty;  // ✅ Changed from int to string

        [FirestoreProperty("term")]
        public string Term { get; set; } = string.Empty;

        [FirestoreProperty("autoAdjustOnRollover")]
        public bool AutoAdjustOnRollover { get; set; } = true;

        [FirestoreProperty("previousGradeFeeStructureId")]
        public string PreviousGradeFeeStructureId { get; set; } = string.Empty;

        [FirestoreProperty("effectiveDate")]
        public DateTime EffectiveDate { get; set; } = DateTime.UtcNow;

        [FirestoreProperty("isActive")]
        public bool IsActive { get; set; } = true;

        [FirestoreProperty("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}