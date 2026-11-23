using Google.Cloud.Firestore;
using StudentManagement.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StudentManagement.Services
{
    public class TeacherService
    {
        private readonly FirestoreDb _db;
        private readonly CollectionReference _teachersRef;

        public TeacherService(FirestoreDb db)
        {
            _db = db;
            _teachersRef = db.Collection("teachers");
        }

        public async Task<List<Teacher>> GetAllTeachersAsync()
        {
            var snapshot = await _teachersRef.GetSnapshotAsync();
            var teachers = new List<Teacher>();

            foreach (var doc in snapshot.Documents)
            {
                var teacher = new Teacher
                {
                    Id = doc.Id,
                    Name = GetStringValue(doc, "name", "Unknown Teacher"),
                    // ✅ FIXED: populate nested ContactInfo
                    ContactInfo = new ContactInfo
                    {
                        Email = GetNestedString(doc, "contactInfo", "email"),
                        Phone = GetNestedLong(doc, "contactInfo", "phone")
                    },
                    GradesHandledRaw = doc.TryGetValue<object>("gradesHandled", out var ghObj) ? ghObj : null,
                    IsActive = GetBoolValue(doc, "isActive", true)
                };
                teachers.Add(teacher);
            }

            return teachers;
        }

        public async Task<List<Teacher>> GetActiveTeachersAsync()
        {
            return await GetAllTeachersAsync();
        }

        public async Task<List<Teacher>> GetTeachersByGradeAsync(string grade)
        {
            var all = await GetAllTeachersAsync();
            return all
                .Where(t => t.GradesHandled != null
                    && t.GradesHandled.Any(g => g.Equals(grade, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        public async Task<Teacher?> GetTeacherByIdAsync(string id)
        {
            var doc = await _teachersRef.Document(id).GetSnapshotAsync();
            if (!doc.Exists) return null;

            return new Teacher
            {
                Id = doc.Id,
                Name = GetStringValue(doc, "name", "Unknown Teacher"),
                ContactInfo = new ContactInfo
                {
                    Email = GetNestedString(doc, "contactInfo", "email"),
                    Phone = GetNestedLong(doc, "contactInfo", "phone")
                },
                GradesHandledRaw = doc.TryGetValue<object>("gradesHandled", out var ghObj2) ? ghObj2 : null,
                IsActive = GetBoolValue(doc, "isActive", true)
            };
        }

        public async Task<Teacher?> GetTeacherAsync(string id)
        {
            return await GetTeacherByIdAsync(id);
        }

        public async Task<string> CreateTeacherAsync(Teacher teacher)
        {
            teacher.IsActive = true;
            var docRef = await _teachersRef.AddAsync(teacher);
            return docRef.Id;
        }

        public async Task DeleteTeacherAsync(string id)
        {
            await _teachersRef.Document(id).DeleteAsync();
        }

        // Helper methods for nested fields
        private static string GetNestedString(DocumentSnapshot doc, string parent, string child)
        {
            try
            {
                // Try exact parent key first
                if (doc.TryGetValue<Dictionary<string, object>>(parent, out var parentDict))
                {
                    if (TryGetFromDictCaseInsensitive(parentDict, child, out var value) && value != null)
                        return value.ToString() ?? string.Empty;
                }

                // Case-insensitive parent key fallback
                var matchingParent = doc.ToDictionary().Keys.FirstOrDefault(k => k.Equals(parent, StringComparison.OrdinalIgnoreCase));
                if (matchingParent != null && doc.TryGetValue<Dictionary<string, object>>(matchingParent, out var parentDict2))
                {
                    if (TryGetFromDictCaseInsensitive(parentDict2, child, out var value2) && value2 != null)
                        return value2.ToString() ?? string.Empty;
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static long GetNestedLong(DocumentSnapshot doc, string parent, string child)
        {
            try
            {
                if (doc.TryGetValue<Dictionary<string, object>>(parent, out var parentDict))
                {
                    if (TryGetFromDictCaseInsensitive(parentDict, child, out var value) && value != null)
                    {
                        if (value is long l) return l;
                        if (value is int i) return i;
                        if (long.TryParse(value.ToString(), out var parsed)) return parsed;
                    }
                }

                var matchingParent = doc.ToDictionary().Keys.FirstOrDefault(k => k.Equals(parent, StringComparison.OrdinalIgnoreCase));
                if (matchingParent != null && doc.TryGetValue<Dictionary<string, object>>(matchingParent, out var parentDict2))
                {
                    if (TryGetFromDictCaseInsensitive(parentDict2, child, out var value2) && value2 != null)
                    {
                        if (value2 is long l2) return l2;
                        if (value2 is int i2) return i2;
                        if (long.TryParse(value2.ToString(), out var parsed2)) return parsed2;
                    }
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private static string GetStringValue(DocumentSnapshot doc, string fieldName, string defaultValue = "")
        {
            if (doc.TryGetValue<string>(fieldName, out var value))
                return value ?? defaultValue;

            var matchingField = doc.ToDictionary().Keys.FirstOrDefault(k => k.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
            if (matchingField != null && doc.TryGetValue<string>(matchingField, out var value2))
                return value2 ?? defaultValue;

            return defaultValue;
        }

        private static bool GetBoolValue(DocumentSnapshot doc, string fieldName, bool defaultValue = false)
        {
            if (doc.TryGetValue<bool>(fieldName, out var value))
                return value;

            var matchingField = doc.ToDictionary().Keys.FirstOrDefault(k => k.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
            if (matchingField != null && doc.TryGetValue<bool>(matchingField, out var value2))
                return value2;

            return defaultValue;
        }

        private static bool TryGetFromDictCaseInsensitive(Dictionary<string, object> dict, string key, out object? value)
        {
            if (dict.TryGetValue(key, out value)) return true;
            var match = dict.Keys.FirstOrDefault(k => k.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                value = dict[match];
                return true;
            }
            value = null;
            return false;
        }
    }
}
