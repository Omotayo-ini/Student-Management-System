// Models/ImportModels.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Google.Cloud.Firestore;

namespace StudentManagement.Models
{
    public class ImportRow
    {
        public int RowNumber { get; set; }

        public string FirstName { get; set; } = string.Empty;

        public string LastName { get; set; } = string.Empty;

        public string Phone { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string Address { get; set; } = string.Empty;

        public string GradeText { get; set; } = string.Empty;

        public string BirthDateText { get; set; } = string.Empty;

        public string TeacherName { get; set; } = string.Empty;

        public string RawData { get; set; } = string.Empty;
    }



    public class ImportError
    {
        public int Row { get; set; }

        public string Column { get; set; } = string.Empty;

        public string ErrorType { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public string Severity { get; set; } = string.Empty;

        public string RawValue { get; set; } = string.Empty;
    }

    public class ImportSettings
    {
        public bool SkipHeader { get; set; } = true;

        public string DateFormat { get; set; } = "yyyy-MM-dd";

        public bool ValidateEmail { get; set; } = true;

        public bool CheckDuplicates { get; set; } = true;

        public string DuplicateCheckField { get; set; } = "Email";
    }

    [FirestoreData]
    public class BulkImportJob
    {
        [FirestoreProperty]
        public string Id { get; set; } = string.Empty;
        
        [FirestoreProperty]
        public string FileName { get; set; } = string.Empty;
        
        [FirestoreProperty]
        public string ImportType { get; set; } = string.Empty;  // "Students", "Fees"
        
        [FirestoreProperty]
        public int TotalRecords { get; set; }
        
        [FirestoreProperty]
        public int SuccessCount { get; set; }
        
        [FirestoreProperty]
        public int FailedCount { get; set; }
        
        [FirestoreProperty]
        public List<string> Errors { get; set; } = new List<string>();
        
        [FirestoreProperty]
        public string Status { get; set; } = string.Empty;  // "Processing", "Completed", "Failed"
        
        [FirestoreProperty]
        public DateTime StartedAt { get; set; }
        
        [FirestoreProperty]
        public DateTime? CompletedAt { get; set; }
    }
}