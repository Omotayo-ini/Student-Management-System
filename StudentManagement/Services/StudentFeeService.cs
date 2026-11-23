using Google.Cloud.Firestore;
using StudentManagement.Models;

namespace StudentManagement.Services
{
    public class StudentFeeService
    {
        private readonly FirestoreDb _db;
        private readonly FeeCalculationService _feeCalculation;
        private readonly ILogger<StudentFeeService> _logger;

        public StudentFeeService(
            FirestoreDb db,
            FeeCalculationService feeCalculation,
            ILogger<StudentFeeService> logger)
        {
            _db = db;
            _feeCalculation = feeCalculation;
            _logger = logger;
        }

        public async Task<List<StudentFee>> GetStudentFeesAsync(string studentId)
        {
            var snapshot = await _db.Collection("students")
                .Document(studentId)
                .Collection("fees")
                .OrderByDescending("academicYear")
                .GetSnapshotAsync();

            return snapshot.Documents
                .Select(d => d.ConvertTo<StudentFee>())
                .OrderBy(f => f.Term)
                .ToList();
        }

        public async Task<StudentFee?> GetStudentFeeAsync(string studentId, string academicYear, string term)
        {
            var docId = $"{academicYear}_{term}";
            var doc = await _db.Collection("students")
                .Document(studentId)
                .Collection("fees")
                .Document(docId)
                .GetSnapshotAsync();

            return doc.Exists ? doc.ConvertTo<StudentFee>() : null;
        }

        public async Task InitializeStudentFeesAsync(string studentId, string academicYear)
        {
            var student = await _db.Collection("students").Document(studentId).GetSnapshotAsync();
            if (!student.Exists) return;

            var studentData = student.ConvertTo<Student>();

            // Initialize Term 1
            await CreateFeeRecordIfNotExistsAsync(studentId, academicYear, "Term 1", studentData.GradeLevel);

            // Initialize Term 2
            await CreateFeeRecordIfNotExistsAsync(studentId, academicYear, "Term 2", studentData.GradeLevel);

            // Initialize Term 3
            await CreateFeeRecordIfNotExistsAsync(studentId, academicYear, "Term 3", studentData.GradeLevel);

            _logger.LogInformation("Initialized fees for student {StudentId} for year {AcademicYear}",
                studentId, academicYear);
        }

        private async Task CreateFeeRecordIfNotExistsAsync(string studentId, string academicYear, string term, string gradeLevel)
        {
            var docId = $"{academicYear}_{term}";
            var feeRef = _db.Collection("students").Document(studentId).Collection("fees").Document(docId);

            var existing = await feeRef.GetSnapshotAsync();
            if (existing.Exists) return;

            // Ensure we have student data available (fetch if not provided)
            var studentSnap = await _db.Collection("students").Document(studentId).GetSnapshotAsync();
            var student = studentSnap.Exists ? studentSnap.ConvertTo<Student>() : null;
            var effectiveGradeLevel = string.IsNullOrEmpty(gradeLevel) ? (student?.Grade ?? string.Empty) : gradeLevel;
            var amountDue = await _feeCalculation.CalculateTermFeeAsync(academicYear, effectiveGradeLevel, term);

            var feeRecord = new StudentFee
            {
                Id = docId,
                AcademicYear = academicYear,
                Term = term,
                AmountDue = amountDue,
                AmountPaid = 0,
                Payments = new List<PaymentEntry>(),
                LastUpdated = Timestamp.GetCurrentTimestamp()
            };

            // Set termDates according to school schedule
            feeRecord.TermDates = term switch
            {
                "Term 1" => "Aug 1 - Nov 19",
                "Term 2" => "Nov 20 - Mar 14",
                "Term 3" => "Mar 15 - Jun 30",
                _ => string.Empty
            };

            await feeRef.SetAsync(feeRecord);
        }

        public async Task<double> GetOutstandingBalanceAsync(string studentId)
        {
            var fees = await GetStudentFeesAsync(studentId);
            return fees.Sum(f => f.Balance);
        }

        // Get outstanding balance for a specific academic year and term
        public async Task<double> GetOutstandingBalanceForTermAsync(string studentId, string academicYear, string term)
        {
            var fee = await GetStudentFeeAsync(studentId, academicYear, term);
            if (fee != null)
                return fee.Balance;

            // If fee doesn't exist, calculate expected amount due for the term
            var studentSnap = await _db.Collection("students").Document(studentId).GetSnapshotAsync();
            if (!studentSnap.Exists) return 0;
            var student = studentSnap.ConvertTo<Student>();
            var amountDue = await _feeCalculation.CalculateTermFeeAsync(academicYear, student.GradeLevel, term);
            return amountDue; // no payments yet
        }

        // Get outstanding balance up to and including the provided term of the academic year
        public async Task<double> GetOutstandingBalanceUpToTermAsync(string studentId, string academicYear, string term)
        {
            var termOrder = new List<string> { "Term 1", "Term 2", "Term 3" };
            var idx = termOrder.IndexOf(term);
            if (idx < 0) return await GetOutstandingBalanceAsync(studentId);

            var balances = 0.0;
            for (int i = 0; i <= idx; i++)
            {
                var t = termOrder[i];
                balances += await GetOutstandingBalanceForTermAsync(studentId, academicYear, t);
            }

            return balances;
        }
    }
}