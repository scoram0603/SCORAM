using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;

namespace ScoramAPI.Services.Analytics
{
    /// <summary>
    /// SMART PROGRESS -- maps a (subject name, topic name) taken from a student's answer snapshots back
    /// onto the active Question Bank subject/topic ids, so a client can open the EXISTING Practice Test
    /// generator pre-filtered. A name that does not map to exactly one active subject/topic is never
    /// guessed: the subject-only (or no) target is returned instead. Loaded once per request (both
    /// tables are small admin-curated taxonomies).
    /// </summary>
    internal sealed class PracticeTargetResolver
    {
        private readonly Dictionary<string, List<Guid>> _subjects;
        private readonly Dictionary<string, List<Guid>> _topics; // key = subjectId|topicNameLower

        private PracticeTargetResolver(Dictionary<string, List<Guid>> subjects, Dictionary<string, List<Guid>> topics)
        {
            _subjects = subjects;
            _topics = topics;
        }

        public static async Task<PracticeTargetResolver> LoadAsync(ScoramDbContext db, CancellationToken ct)
        {
            var subjects = await db.QuestionBankSubjects.AsNoTracking()
                .Where(s => s.IsActive)
                .Select(s => new { s.Id, s.Name })
                .ToListAsync(ct);
            var topics = await db.QuestionBankTopics.AsNoTracking()
                .Where(t => t.IsActive)
                .Select(t => new { t.Id, t.SubjectId, t.Name })
                .ToListAsync(ct);

            var subjectMap = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
            foreach (var s in subjects)
            {
                var key = (s.Name ?? string.Empty).Trim().ToLowerInvariant();
                if (!subjectMap.TryGetValue(key, out var list)) subjectMap[key] = list = new List<Guid>();
                list.Add(s.Id);
            }

            var topicMap = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
            foreach (var t in topics)
            {
                var key = t.SubjectId.ToString() + "|" + (t.Name ?? string.Empty).Trim().ToLowerInvariant();
                if (!topicMap.TryGetValue(key, out var list)) topicMap[key] = list = new List<Guid>();
                list.Add(t.Id);
            }
            return new PracticeTargetResolver(subjectMap, topicMap);
        }

        public PracticeTargetDto? Resolve(string? subject, string? topic, Guid? examId)
        {
            if (Taxonomy.IsUnclassified(subject)) return null;
            if (!_subjects.TryGetValue(Taxonomy.SubjectKey(subject), out var subjectIds) || subjectIds.Count != 1) return null;

            var target = new PracticeTargetDto { SubjectId = subjectIds[0], ExamId = examId };
            if (!Taxonomy.IsUnclassified(topic)
                && _topics.TryGetValue(subjectIds[0].ToString() + "|" + Taxonomy.TopicName(topic).ToLowerInvariant(), out var topicIds)
                && topicIds.Count == 1)
            {
                target.TopicId = topicIds[0];
            }
            return target;
        }
    }
}
