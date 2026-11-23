// Models/Student.cs - UPDATED
using Google.Cloud.Firestore;
using System;
using System.ComponentModel.DataAnnotations;

namespace StudentManagement.Models
{
    [FirestoreData]
    public class Student
    {
        [FirestoreDocumentId]
        public string Id { get; set; } = string.Empty;

        [FirestoreProperty("studentId")]
        public string StudentId { get; set; } = string.Empty;

        [Required]
        [FirestoreProperty("firstName")]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        [FirestoreProperty("lastName")]
        public string LastName { get; set; } = string.Empty;

        public string FullName => $"{FirstName} {LastName}".Trim();

        [FirestoreProperty("birthDate")]
        public Timestamp BirthDate { get; set; }

        [FirestoreProperty("age")]
        public int Age { get; set; }

        [FirestoreProperty("contactPhone")]
        public string ContactPhone { get; set; } = string.Empty;

        [EmailAddress]
        [FirestoreProperty("contactEmail")]
        public string ContactEmail { get; set; } = string.Empty;

        [FirestoreProperty("grade")]
        public string Grade { get; set; } = string.Empty;

        [FirestoreProperty("className")]
        public string ClassName { get; set; } = string.Empty;

        [FirestoreProperty("gradeLevel")]
        public string GradeLevel { get; set; } = string.Empty;

        [FirestoreProperty("specificGrade")]
        public string SpecificGrade { get; set; } = string.Empty;

        [FirestoreProperty("classTeacherId")]
        public string ClassTeacherId { get; set; } = string.Empty;

        [FirestoreProperty("classTeacherName")]
        public string ClassTeacherName { get; set; } = string.Empty;

        [FirestoreProperty("enrollmentYear")]
        public int EnrollmentYear { get; set; }

        [FirestoreProperty("status")]
        public string Status { get; set; } = "active";

        [FirestoreProperty("siblingOrder")]
        public int SiblingOrder { get; set; } = 1;

        [FirestoreProperty("parentId")]
        public string ParentId { get; set; } = string.Empty;

        [FirestoreProperty("createdAt")]
        public Timestamp CreatedAt { get; set; }

        [FirestoreProperty("updatedAt")]
        public Timestamp UpdatedAt { get; set; }

        [FirestoreProperty("birthMonth")]
        public string BirthMonth { get; set; } = string.Empty;

        [FirestoreProperty("currentFeeStructureId")]
        public string CurrentFeeStructureId { get; set; } = string.Empty;

        [FirestoreProperty("currentBalance")]
        public double CurrentBalance { get; set; }

        [FirestoreProperty("creditBalance")]
        public double CreditBalance { get; set; }

        [FirestoreProperty("totalPaid")]
        public double TotalPaid { get; set; }

        [FirestoreProperty("paymentStatus")]
        public string PaymentStatus { get; set; } = "Outstanding";

        [FirestoreProperty("autoGradeRolloverEnabled")]
        public bool AutoGradeRolloverEnabled { get; set; } = true;

        [FirestoreProperty("autoAgeUpdateEnabled")]
        public bool AutoAgeUpdateEnabled { get; set; } = true;

        [FirestoreProperty("lastGradeRolloverDate")]
        public DateTime? LastGradeRolloverDate { get; set; }

        [FirestoreProperty("lastAgeUpdateDate")]
        public DateTime? LastAgeUpdateDate { get; set; }

        [FirestoreProperty("lastRolloverYear")]
        public string LastRolloverYear { get; set; } = string.Empty;

        [FirestoreProperty("lastTermRolloverKey")]
        public string LastTermRolloverKey { get; set; } = string.Empty;  // Tracks "{year}_{term}" already applied to CurrentBalance
    }
}