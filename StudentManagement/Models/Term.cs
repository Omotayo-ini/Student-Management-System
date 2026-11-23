using Google.Cloud.Firestore;
using System;

namespace StudentManagement.Models
{
    [FirestoreData]
    public class Term
    {
        [FirestoreProperty]
        public string Id { get; set; } = string.Empty;

        [FirestoreProperty]
        public string Name { get; set; } = string.Empty;  // "Term 1", "Term 2", "Term 3"

        [FirestoreProperty]
        public int AcademicYear { get; set; }

        [FirestoreProperty]
        public DateTime StartDate { get; set; }

        [FirestoreProperty]
        public DateTime EndDate { get; set; }

        [FirestoreProperty]
        public bool IsCurrent { get; set; }  // ✅ Which term is active now
    }
}