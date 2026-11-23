using System;
using System.Collections.Generic;

namespace StudentManagement.Models
{
    public class BulkImportResult
    {
        public string FileName { get; set; } = string.Empty;
        public string ImportType { get; set; } = "Students";
        public int TotalRecords { get; set; }
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public string Status { get; set; } = "Processing";
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
    }
}