using Google.Cloud.Firestore;
using System.Collections.Generic;
using System.Linq;

namespace StudentManagement.Models
{
    [FirestoreData]
    public class ContactInfo
    {
        [FirestoreProperty("email")]
        public string Email { get; set; } = string.Empty;

        [FirestoreProperty("phone")]
        public long Phone { get; set; }
    }

    [FirestoreData]
    public class Teacher
    {
        [FirestoreDocumentId]
        public string Id { get; set; } = string.Empty;

        [FirestoreProperty("name")]
        public string Name { get; set; } = string.Empty;

        [FirestoreProperty("contactInfo")]
        public ContactInfo ContactInfo { get; set; } = new();

        [FirestoreProperty("gradesHandled")]
        public object? GradesHandledRaw { get; set; }

        public List<string> GradesHandled
        {
            get
            {
                if (GradesHandledRaw is IEnumerable<object> list)
                    return list.Select(x => x?.ToString() ?? string.Empty).ToList();
                if (GradesHandledRaw is string s && !string.IsNullOrEmpty(s))
                    return new List<string> { s };
                return new List<string>();
            }
            set
            {
                GradesHandledRaw = value;
            }
        }

        [FirestoreProperty("isActive")]
        public bool IsActive { get; set; } = true;

        // Convenience properties (not stored separately in Firestore)
        public string Email => ContactInfo?.Email ?? string.Empty;

        public long Phone => ContactInfo?.Phone ?? 0;
    }
}
