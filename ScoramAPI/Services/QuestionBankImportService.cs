using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    public interface IQuestionBankImportService
    {
        /// <summary>Reads an Excel (.xlsx) or JSON file into a common row shape. Column/property names
        /// are matched case-insensitively: QuestionText, OptionA-D, CorrectOption, Explanation, Subject,
        /// Topic, SourceReference, ExamYears. Throws InvalidDataException with a human-readable message
        /// on structural problems (missing required columns, unreadable file).</summary>
        Task<List<QuestionBankImportRow>> ParseAsync(Stream fileStream, ImportFileFormat format);

        /// <summary>Annotates each row's IsValid/Errors/IsDuplicate in place. Checks required fields,
        /// a parseable CorrectOption, at least one valid Exam+Year pair, and duplicate detection
        /// (normalized question text) against both the database and the rest of this same batch.</summary>
        Task ValidateAsync(List<QuestionBankImportRow> rows, ScoramDbContext db);

        /// <summary>Lowercases, collapses whitespace, and strips basic punctuation -- see section 13 of
        /// the spec ("Who discovered Harappa?" and "Who discovered Harappa ?" must normalize equal).
        /// Shared by both bulk import and the single-question admin form's duplicate check.</summary>
        string NormalizeForDuplicateCheck(string questionText);
    }

    public class QuestionBankImportService : IQuestionBankImportService
    {
        private static readonly string[] RequiredHeaders =
        {
            "QuestionText", "OptionA", "OptionB", "OptionC", "OptionD", "CorrectOption",
            "Subject", "Topic", "ExamYears"
        };

        public async Task<List<QuestionBankImportRow>> ParseAsync(Stream fileStream, ImportFileFormat format)
        {
            return format switch
            {
                ImportFileFormat.Excel => ParseExcel(fileStream),
                ImportFileFormat.Json => await ParseJsonAsync(fileStream),
                _ => throw new InvalidDataException("Question Bank bulk import only supports Excel (.xlsx) or JSON.")
            };
        }

        private List<QuestionBankImportRow> ParseExcel(Stream fileStream)
        {
            using var workbook = new XLWorkbook(fileStream);
            var sheet = workbook.Worksheets.FirstOrDefault()
                ?? throw new InvalidDataException("The Excel file has no worksheets.");

            var usedRows = sheet.RowsUsed().ToList();
            if (usedRows.Count == 0)
                throw new InvalidDataException("The Excel file appears to be empty.");

            var headerRow = usedRows[0];
            const int maxColumnsToScan = 30; // expected header set has ~11 columns
            var columnIndexByHeader = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var col = 1; col <= maxColumnsToScan; col++)
            {
                var headerText = headerRow.Cell(col).GetString().Trim();
                if (string.IsNullOrEmpty(headerText)) continue;
                if (!columnIndexByHeader.ContainsKey(headerText))
                    columnIndexByHeader[headerText] = col;
            }
            CheckHeaders(columnIndexByHeader.Keys);

            var rows = new List<QuestionBankImportRow>();
            var rowNumber = 0;
            foreach (var excelRow in usedRows.Skip(1))
            {
                rowNumber++;
                rows.Add(MapRow(rowNumber, header =>
                    columnIndexByHeader.TryGetValue(header, out var col) ? excelRow.Cell(col).GetString().Trim() : string.Empty));
            }
            return rows;
        }

        private async Task<List<QuestionBankImportRow>> ParseJsonAsync(Stream fileStream)
        {
            List<JsonRow>? parsed;
            try
            {
                parsed = await JsonSerializer.DeserializeAsync<List<JsonRow>>(fileStream,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"That doesn't look like valid JSON: {ex.Message}");
            }

            if (parsed == null || parsed.Count == 0)
                throw new InvalidDataException("The JSON file contains no questions (expected a top-level array).");

            var rows = new List<QuestionBankImportRow>();
            for (var i = 0; i < parsed.Count; i++)
            {
                var j = parsed[i];

                // examYears can be given either as a structured array ([{examName,year}, ...], the
                // preferred JSON shape) or as the same "Name:Year; Name:Year" string the Excel template
                // uses -- accepting both means a JSON file hand-edited from the Excel template still works.
                string rawExamYears;
                if (j.ExamYears != null && j.ExamYears.Count > 0)
                {
                    rawExamYears = string.Join("; ", j.ExamYears.Select(e => $"{e.ExamName}:{e.Year}"));
                }
                else
                {
                    rawExamYears = j.ExamYearsRaw ?? string.Empty;
                }

                var row = new QuestionBankImportRow
                {
                    RowNumber = i + 1,
                    QuestionText = (j.QuestionText ?? "").Trim(),
                    OptionA = (j.OptionA ?? "").Trim(),
                    OptionB = (j.OptionB ?? "").Trim(),
                    OptionC = (j.OptionC ?? "").Trim(),
                    OptionD = (j.OptionD ?? "").Trim(),
                    CorrectOption = (j.CorrectOption ?? "").Trim(),
                    Explanation = string.IsNullOrWhiteSpace(j.Explanation) ? null : j.Explanation!.Trim(),
                    Subject = (j.Subject ?? "").Trim(),
                    Topic = (j.Topic ?? "").Trim(),
                    SourceReference = string.IsNullOrWhiteSpace(j.SourceReference) ? null : j.SourceReference!.Trim(),
                    RawExamYears = rawExamYears.Trim()
                };
                rows.Add(row);
            }
            return rows;
        }

        private static QuestionBankImportRow MapRow(int rowNumber, Func<string, string> field) => new QuestionBankImportRow
        {
            RowNumber = rowNumber,
            QuestionText = field("QuestionText"),
            OptionA = field("OptionA"),
            OptionB = field("OptionB"),
            OptionC = field("OptionC"),
            OptionD = field("OptionD"),
            CorrectOption = field("CorrectOption"),
            Explanation = string.IsNullOrWhiteSpace(field("Explanation")) ? null : field("Explanation"),
            Subject = field("Subject"),
            Topic = field("Topic"),
            SourceReference = string.IsNullOrWhiteSpace(field("SourceReference")) ? null : field("SourceReference"),
            RawExamYears = field("ExamYears")
        };

        private static void CheckHeaders(IEnumerable<string> actualHeaders)
        {
            var present = new HashSet<string>(actualHeaders, StringComparer.OrdinalIgnoreCase);
            var missing = RequiredHeaders.Where(h => !present.Contains(h)).ToList();
            if (missing.Count > 0)
                throw new InvalidDataException($"Missing required column(s): {string.Join(", ", missing)}. " +
                    $"Expected headers: {string.Join(", ", RequiredHeaders)} (Explanation and SourceReference are optional). " +
                    "Use the \"Download Excel Template\" button to get the exact format.");
        }

        public async Task ValidateAsync(List<QuestionBankImportRow> rows, ScoramDbContext db)
        {
            // Load every existing (NormalizedText -> QuestionId/snippet) pair once, rather than one
            // query per row -- fine up to a few hundred thousand questions; if the bank grows well
            // past that, this can become a per-row indexed lookup instead.
            var existing = await db.QuestionBankQuestions
                .Where(q => q.IsActive)
                .Select(q => new { q.Id, q.NormalizedQuestionText, q.QuestionText })
                .ToListAsync();
            var existingByNormalized = existing
                .GroupBy(q => q.NormalizedQuestionText)
                .ToDictionary(g => g.Key, g => g.First());

            var seenInBatch = new Dictionary<string, int>(); // normalized text -> first RowNumber that used it

            foreach (var row in rows)
            {
                row.Errors.Clear();
                row.IsDuplicate = false;
                row.DuplicateOfQuestionId = null;
                row.DuplicateOfQuestionTextSnippet = null;

                if (string.IsNullOrWhiteSpace(row.QuestionText)) row.Errors.Add("Question text is required.");
                if (string.IsNullOrWhiteSpace(row.OptionA)) row.Errors.Add("Option A is required.");
                if (string.IsNullOrWhiteSpace(row.OptionB)) row.Errors.Add("Option B is required.");
                if (string.IsNullOrWhiteSpace(row.OptionC)) row.Errors.Add("Option C is required.");
                if (string.IsNullOrWhiteSpace(row.OptionD)) row.Errors.Add("Option D is required.");
                if (string.IsNullOrWhiteSpace(row.Subject)) row.Errors.Add("Subject is required.");
                if (string.IsNullOrWhiteSpace(row.Topic)) row.Errors.Add("Topic is required.");

                if (string.IsNullOrWhiteSpace(row.CorrectOption))
                    row.Errors.Add("Correct option is required (A, B, C, or D).");
                else if (!Enum.TryParse<OptionLetter>(row.CorrectOption, ignoreCase: true, out _))
                    row.Errors.Add($"'{row.CorrectOption}' isn't a valid correct option (expected A, B, C, or D).");

                var (examYears, examYearErrors) = ParseExamYears(row.RawExamYears);
                row.ExamYears = examYears;
                foreach (var err in examYearErrors) row.Errors.Add(err);
                if (row.ExamYears.Count == 0 && examYearErrors.Count == 0)
                    row.Errors.Add("At least one Exam+Year is required, e.g. \"SSC CGL:2018; UP TGT:2022\".");

                if (!string.IsNullOrWhiteSpace(row.QuestionText))
                {
                    var normalized = NormalizeForDuplicateCheck(row.QuestionText);

                    if (existingByNormalized.TryGetValue(normalized, out var existingMatch))
                    {
                        row.IsDuplicate = true;
                        row.DuplicateOfQuestionId = existingMatch.Id;
                        row.DuplicateOfQuestionTextSnippet = Snippet(existingMatch.QuestionText);
                    }
                    else if (seenInBatch.TryGetValue(normalized, out var firstRowNumber))
                    {
                        row.IsDuplicate = true;
                        row.DuplicateOfQuestionTextSnippet = $"Row {firstRowNumber} in this same file";
                    }
                    else
                    {
                        seenInBatch[normalized] = row.RowNumber;
                    }
                }

                row.IsValid = row.Errors.Count == 0;
            }
        }

        // Parses "SSC CGL:2018; UP TGT:2022; SSC CHSL : 2020" into structured pairs. Tolerant of extra
        // whitespace around ':' and ';'. Returns per-pair errors (e.g. an unparseable year) separately
        // from the successfully-parsed pairs so a partially-bad list doesn't discard the good pairs.
        private static (List<QuestionBankExamYearInputDto> Pairs, List<string> Errors) ParseExamYears(string raw)
        {
            var pairs = new List<QuestionBankExamYearInputDto>();
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(raw)) return (pairs, errors);

            var entries = raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var entry in entries)
            {
                var parts = entry.Split(':', 2, StringSplitOptions.TrimEntries);
                if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]))
                {
                    errors.Add($"Couldn't read exam/year entry \"{entry}\" -- expected \"Exam Name:Year\".");
                    continue;
                }
                if (!int.TryParse(parts[1], out var year) || year < 1990 || year > DateTime.UtcNow.Year + 1)
                {
                    errors.Add($"\"{entry}\" has an invalid year.");
                    continue;
                }
                pairs.Add(new QuestionBankExamYearInputDto { ExamName = parts[0], Year = year });
            }

            return (pairs, errors);
        }

        public string NormalizeForDuplicateCheck(string questionText)
        {
            if (string.IsNullOrWhiteSpace(questionText)) return string.Empty;

            var sb = new StringBuilder(questionText.Length);
            var lastWasSpace = false;
            foreach (var ch in questionText.ToLowerInvariant())
            {
                if (char.IsWhiteSpace(ch))
                {
                    if (!lastWasSpace && sb.Length > 0) sb.Append(' ');
                    lastWasSpace = true;
                    continue;
                }

                // Strip basic ASCII punctuation that doesn't change the question's meaning
                // ("Harappa?" vs "Harappa"), but keep letters/digits from any script (Hindi included)
                // and keep the few punctuation marks (like '%') that can be meaningful in a question.
                if (".,;:!?\"'`()[]{}".IndexOf(ch) >= 0)
                {
                    lastWasSpace = false;
                    continue;
                }

                sb.Append(ch);
                lastWasSpace = false;
            }

            var result = sb.ToString().Trim();

            // The NormalizedQuestionText column is nvarchar(450) (the max length SQL Server allows
            // for an indexed nvarchar column, used here for fast duplicate lookups). Long
            // paragraph-style questions (e.g. "rearrange these sentences...") can exceed that after
            // normalization, which previously crashed the bulk-import commit with an unhandled
            // SqlException ("String or binary data would be truncated"). Truncate here instead so
            // duplicate-check and storage always agree on the same value.
            const int MaxNormalizedLength = 450;
            if (result.Length > MaxNormalizedLength)
                result = result.Substring(0, MaxNormalizedLength);

            return result;
        }

        private static string Snippet(string text) => text.Length > 140 ? text[..140] + "…" : text;

        // Matches JSON property names case-insensitively via JsonSerializerOptions above.
        private class JsonRow
        {
            public string? QuestionText { get; set; }
            public string? OptionA { get; set; }
            public string? OptionB { get; set; }
            public string? OptionC { get; set; }
            public string? OptionD { get; set; }
            public string? CorrectOption { get; set; }
            public string? Explanation { get; set; }
            public string? Subject { get; set; }
            public string? Topic { get; set; }
            public string? SourceReference { get; set; }

            // Accepts either shape -- see ParseJsonAsync above.
            public List<JsonExamYear>? ExamYears { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("examYearsRaw")]
            public string? ExamYearsRaw { get; set; }
        }

        private class JsonExamYear
        {
            public string ExamName { get; set; } = string.Empty;
            public int Year { get; set; }
        }
    }
}