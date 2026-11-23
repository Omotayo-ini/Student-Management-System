using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Logging;
using StudentManagement.Models;

namespace StudentManagement.Services
{
    public class PaymentService
    {
        private readonly FirestoreDb _db;
        private readonly ILogger<PaymentService> _logger;
        private readonly CreditService? _creditService;

        public PaymentService(FirestoreDb db, ILogger<PaymentService> logger, CreditService? creditService = null)
        {
            _db = db;
            _logger = logger;
            _creditService = creditService;
        }

        public async Task RecordPaymentAsync(PaymentInput input, string recordedBy)
        {
            var studentId = input.StudentFeeId;

            // Get current student data
            var studentRef = _db.Collection("students").Document(studentId);
            var studentDoc = await studentRef.GetSnapshotAsync();

            if (!studentDoc.Exists)
                throw new Exception($"Student not found: {studentId}");

            var student = studentDoc.ConvertTo<Student>();
            var balanceBefore = student.CurrentBalance;
            var totalPaidBefore = student.TotalPaid;
            // If CreditService exists, use it as the source of truth for credit balances
            var creditBefore = _creditService != null ? await _creditServiceSafeGet(student.Id) : student.CreditBalance;

            double newBalance = balanceBefore;
            double newCredit = creditBefore;

            // If payment covers current balance first
            if (input.Amount >= balanceBefore)
            {
                var overpay = input.Amount - balanceBefore;
                newBalance = 0; // current term balance cleared

                // Apply overpayment to credit balance (persist to credits collection if service available)
                newCredit = Math.Max(0, creditBefore + overpay);

                if (overpay > 0 && _creditService != null)
                {
                    // Record overpayment into credits collection
                    await _creditServiceAdd(studentId, overpay, $"Overpayment via {input.Method} on {DateTime.UtcNow:yyyy-MM-dd}");
                }
            }
            else
            {
                newBalance = balanceBefore - input.Amount;
            }

            var newTotalPaid = totalPaidBefore + input.Amount;

            string paymentStatus;
            if (newBalance <= 0)
                paymentStatus = "Paid";
            else if (newTotalPaid > 0)
                paymentStatus = "Partial";
            else
                paymentStatus = "Outstanding";

            // Create payment record in student's payments subcollection
            var paymentData = new Dictionary<string, object>
            {
                { "amount", input.Amount },
                { "balanceBefore", balanceBefore },
                { "balanceAfter", newBalance },
                { "creditBefore", creditBefore },
                { "creditAfter", newCredit },
                { "date", Timestamp.FromDateTime(input.Date.ToUniversalTime()) },
                { "method", input.Method },
                { "referenceNumber", input.ReferenceNumber ?? "" },
                { "term", input.Term },
                { "academicYear", input.AcademicYear },
                { "notes", input.Notes ?? "" },
                { "recordedBy", recordedBy },
                { "recordedAt", Timestamp.GetCurrentTimestamp() }
            };

            var paymentsRef = studentRef.Collection("payments");
            var paymentDoc = await paymentsRef.AddAsync(paymentData);

            // Update student main document
            var updates = new Dictionary<string, object>
            {
                { "currentBalance", newBalance },
                { "totalPaid", newTotalPaid },
                { "paymentStatus", paymentStatus },
                { "updatedAt", Timestamp.GetCurrentTimestamp() }
            };

            // Only persist creditBalance on the student document if CreditService is not used
            if (_creditServiceShouldPersistToStudent())
            {
                updates["creditBalance"] = newCredit;
            }

            await studentRef.UpdateAsync(updates);

            _logger.LogInformation(
                "Payment recorded for student {StudentId}: Amount={Amount}, BalanceBefore={Before}, BalanceAfter={After}, TotalPaid={TotalPaid}, Status={Status}, PaymentDoc={PaymentDoc}",
                studentId, input.Amount, balanceBefore, newBalance, newTotalPaid, paymentStatus, paymentDoc.Id);
        }

        // Helper wrappers to avoid DI cyclic or naming issues
        private async Task<double> _creditServiceSafeGet(string studentId)
        {
            if (_creditService == null) return 0;
            return await _creditService.GetCreditBalanceAsync(studentId);
        }

        private async Task _creditServiceAdd(string studentId, double amount, string description)
        {
            if (_creditService == null) return;
            await _creditService.AddCreditAsync(studentId, amount, description);
        }

        private bool _creditServiceShouldPersistToStudent()
        {
            // If a separate credits collection is used (CreditService present), avoid duplicating creditBalance on student
            return _creditService == null;
        }

        public async Task<List<Dictionary<string, object>>> GetStudentPaymentsAsync(string studentId)
        {
            var snapshot = await _db.Collection("students").Document(studentId)
                .Collection("payments")
                .OrderByDescending("date")
                .GetSnapshotAsync();

            return snapshot.Documents.Select(d =>
            {
                var dict = new Dictionary<string, object>();
                foreach (var field in d.ToDictionary())
                {
                    dict[field.Key] = field.Value;
                }
                return dict;
            }).ToList();
        }

        public async Task<List<Dictionary<string, object>>> GetRecentPaymentsAsync(int count = 50)
        {
            // Firestore doesn't support collection group queries across all students easily
            // For now, return empty or implement if needed
            return new List<Dictionary<string, object>>();
        }
    }
}