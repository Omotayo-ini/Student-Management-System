using Google.Cloud.Firestore;
using StudentManagement.Models;
using System.Collections.Generic;
using System.Linq;

namespace StudentManagement.Services
{
    public class GradeService
    {
        private readonly FirestoreDb _db;
        private readonly CollectionReference _gradesRef;

        public GradeService(FirestoreDb db)
        {
            _db = db;
            _gradesRef = db.Collection("Grades");
        }

        public async Task<List<string>> GetAllGradesAsync()
        {
            var snapshot = await _gradesRef.GetSnapshotAsync();
            return snapshot.Documents
                .Select(d => d.GetValue<string>("Grade") ?? d.GetValue<string>("grade") ?? "")
                .Where(g => !string.IsNullOrEmpty(g))
                .OrderBy(g => g)
                .ToList();
        }

        public async Task<List<string>> GetGradeLevelsAsync()
        {
            var snapshot = await _gradesRef.GetSnapshotAsync();
            var grades = new List<string>();

            foreach (var doc in snapshot.Documents)
            {
                var grade = doc.GetValue<string>("Grade") ?? doc.GetValue<string>("grade");
                if (!string.IsNullOrEmpty(grade) && !grades.Contains(grade))
                    grades.Add(grade);
            }

            return grades.OrderBy(g => g).ToList();
        }

        public async Task<List<(string Id, string Grade)>> GetAllGradeEntriesAsync()
        {
            var snapshot = await _gradesRef.GetSnapshotAsync();
            return snapshot.Documents
                .Select(d => (d.Id, d.GetValue<string>("Grade") ?? d.GetValue<string>("grade") ?? string.Empty))
                .Where(x => !string.IsNullOrEmpty(x.Item2))
                .ToList();
        }

        public async Task<string> CreateGradeAsync(string grade)
        {
            var doc = await _gradesRef.AddAsync(new Dictionary<string, object>
            {
                { "Grade", grade }
            });
            return doc.Id;
        }

        public async Task DeleteGradeAsync(string id)
        {
            await _gradesRef.Document(id).DeleteAsync();
        }

        // Compute next grade preserving class suffix (e.g., "Grade 1A" -> "Grade 2A")
        public async Task<string> GetNextGradeAsync(string currentGrade)
        {
            if (string.IsNullOrWhiteSpace(currentGrade)) return string.Empty;

            var allGrades = await GetAllGradesAsync();
            // Normalize
            var g = currentGrade.Trim();

            // Early-years progression is explicit because KG2 advances to
            // Grade R, not KG3. Preserve a section suffix when one exists.
            if (g.Equals("creche", StringComparison.OrdinalIgnoreCase))
                return "KG1";

            var kgMatch = System.Text.RegularExpressions.Regex.Match(g, @"^(KG)\s*(\d+)([A-Za-z]*)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (kgMatch.Success)
            {
                var prefix = kgMatch.Groups[1].Value.ToUpper();
                var num = int.Parse(kgMatch.Groups[2].Value);
                var suffix = kgMatch.Groups[3].Value;
                var candidate = num == 2 ? $"Grade R{suffix}" : $"{prefix}{num + 1}{suffix}";
                var found = allGrades.FirstOrDefault(x => x.Equals(candidate, StringComparison.OrdinalIgnoreCase));
                return found ?? candidate;
            }

            // Handle Grade R / Ready -> Grade 1, including sections.
            if (System.Text.RegularExpressions.Regex.IsMatch(g, @"^(Grade\s*R|GR|Ready)\s*[A-Za-z]*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                var suffix = System.Text.RegularExpressions.Regex.Match(g, @"([A-Za-z])$").Groups[1].Value;
                var candidate = $"Grade 1{suffix}";
                return allGrades.FirstOrDefault(x => x.Equals(candidate, StringComparison.OrdinalIgnoreCase)) ?? candidate;
            }

            // Try Grade number pattern e.g., Grade 1A or 1A
            var gradeMatch = System.Text.RegularExpressions.Regex.Match(g, @"(?:Grade\s*)?(?:G\s*)?(?:gr\s*)?([0-9]+)\s*([A-Za-z]*)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (gradeMatch.Success)
            {
                var num = int.Parse(gradeMatch.Groups[1].Value);
                var suffix = gradeMatch.Groups[2].Value;
                var candidate = $"Grade {num + 1}{suffix}";
                var found = allGrades.FirstOrDefault(x => x.Equals(candidate, StringComparison.OrdinalIgnoreCase));
                if (found != null) return found;
                // Try without 'Grade' prefix
                var candidate2 = $"{num + 1}{suffix}";
                found = allGrades.FirstOrDefault(x => x.Equals(candidate2, StringComparison.OrdinalIgnoreCase));
                return found ?? candidate;
            }

            // Fallback: attempt to find next in sorted list based on index
            var idx = allGrades.FindIndex(x => x.Equals(g, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0 && idx + 1 < allGrades.Count)
            {
                return allGrades[idx + 1];
            }

            // No rule matched, return empty string to indicate unknown
            return string.Empty;
        }
    }
}
