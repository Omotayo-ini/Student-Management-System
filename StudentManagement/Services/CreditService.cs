using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Logging;
using StudentManagement.Models;

namespace StudentManagement.Services
{
    public class CreditService
    {
        private readonly FirestoreDb _db;
        private readonly CollectionReference _creditsRef;
        private readonly ILogger<CreditService> _logger;

        public CreditService(FirestoreDb db, ILogger<CreditService> logger)
        {
            _db = db;
            _creditsRef = db.Collection("credits");
            _logger = logger;
        }

        public async Task<double> GetCreditBalanceAsync(string studentId)
        {
            var query = await _creditsRef.WhereEqualTo("studentId", studentId).Limit(1).GetSnapshotAsync();
            var doc = query.Documents.FirstOrDefault();
            if (doc != null && doc.Exists)
            {
                var credit = doc.ConvertTo<Credit>();
                return credit.CreditBalance;
            }
            return 0;
        }

        public async Task AddCreditAsync(string studentId, double amount, string description = "Overpayment")
        {
            if (amount <= 0) return;

            var query = await _creditsRef.WhereEqualTo("studentId", studentId).Limit(1).GetSnapshotAsync();
            var doc = query.Documents.FirstOrDefault();

            if (doc != null && doc.Exists)
            {
                var credit = doc.ConvertTo<Credit>();
                var newBalance = credit.CreditBalance + amount;

                var transaction = new CreditTransaction
                {
                    Amount = amount,
                    Type = "CreditAdded",
                    Timestamp = Timestamp.GetCurrentTimestamp(),
                    Description = description
                };

                var updates = new Dictionary<string, object>
                {
                    { "creditBalance", newBalance },
                    { "lastUpdated", Timestamp.GetCurrentTimestamp() },
                    { "transactionHistory", FieldValue.ArrayUnion(transaction) }
                };

                await doc.Reference.UpdateAsync(updates);
                _logger.LogInformation("Added credit of {Amount} to student {StudentId}. New balance: {Balance}", amount, studentId, newBalance);
            }
            else
            {
                var credit = new Credit
                {
                    StudentId = studentId,
                    CreditBalance = amount,
                    Source = "Overpayment",
                    LastUpdated = Timestamp.GetCurrentTimestamp(),
                    TransactionHistory = new List<CreditTransaction>
                    {
                        new CreditTransaction
                        {
                            Amount = amount,
                            Type = "CreditCreated",
                            Timestamp = Timestamp.GetCurrentTimestamp(),
                            Description = description
                        }
                    }
                };

                await _creditsRef.AddAsync(credit);
                _logger.LogInformation("Created new credit record for student {StudentId} with balance {Amount}", studentId, amount);
            }
        }

        public async Task<(double remainingFee, double leftoverCredit)> ApplyCreditAsync(string studentId, double feeAmount)
        {
            if (feeAmount <= 0) return (0, await GetCreditBalanceAsync(studentId));

            var query = await _creditsRef.WhereEqualTo("studentId", studentId).Limit(1).GetSnapshotAsync();
            var doc = query.Documents.FirstOrDefault();

            if (doc == null || !doc.Exists)
            {
                _logger.LogInformation("No credit found for student {StudentId}. Full fee of {Fee} applies.", studentId, feeAmount);
                return (feeAmount, 0);
            }

            var credit = doc.ConvertTo<Credit>();
            var creditBalance = credit.CreditBalance;

            if (creditBalance <= 0)
            {
                return (feeAmount, 0);
            }

            double remainingFee;
            double leftoverCredit;

            if (creditBalance >= feeAmount)
            {
                remainingFee = 0;
                leftoverCredit = creditBalance - feeAmount;
                _logger.LogInformation("Credit fully covered fee for student {StudentId}. Fee: {Fee}, Remaining credit: {Remaining}", studentId, feeAmount, leftoverCredit);
            }
            else
            {
                remainingFee = feeAmount - creditBalance;
                leftoverCredit = 0;
                _logger.LogInformation("Credit partially covered fee for student {StudentId}. Applied: {Applied}, Remaining fee: {Remaining}", studentId, creditBalance, remainingFee);
            }

            var transaction = new CreditTransaction
            {
                Amount = creditBalance - leftoverCredit,
                Type = "CreditApplied",
                Timestamp = Timestamp.GetCurrentTimestamp(),
                Description = $"Applied {creditBalance - leftoverCredit} to fee {feeAmount}"
            };

            var updates = new Dictionary<string, object>
            {
                { "creditBalance", leftoverCredit },
                { "lastUpdated", Timestamp.GetCurrentTimestamp() },
                { "transactionHistory", FieldValue.ArrayUnion(transaction) }
            };

            await doc.Reference.UpdateAsync(updates);
            return (remainingFee, leftoverCredit);
        }

        public async Task DeductCreditAsync(string studentId, double amount, string reason)
        {
            if (amount <= 0) return;

            var query = await _creditsRef.WhereEqualTo("studentId", studentId).Limit(1).GetSnapshotAsync();
            var doc = query.Documents.FirstOrDefault();

            if (doc == null || !doc.Exists) return;

            var credit = doc.ConvertTo<Credit>();
            var newBalance = Math.Max(0, credit.CreditBalance - amount);

            var transaction = new CreditTransaction
            {
                Amount = amount,
                Type = "CreditDeducted",
                Timestamp = Timestamp.GetCurrentTimestamp(),
                Description = reason
            };

            var updates = new Dictionary<string, object>
            {
                { "creditBalance", newBalance },
                { "lastUpdated", Timestamp.GetCurrentTimestamp() },
                { "transactionHistory", FieldValue.ArrayUnion(transaction) }
            };

            await doc.Reference.UpdateAsync(updates);
            _logger.LogInformation("Deducted {Amount} credit from student {StudentId}. New balance: {Balance}", amount, studentId, newBalance);
        }
    }
}
