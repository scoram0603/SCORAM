using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;

namespace ScoramAPI.Services
{
    /// <summary>
    /// Turns an uploaded JSON / Excel / CSV file into a list of <see cref="TestImportTestDto"/> -- structure only,
    /// no database access and no judgement about whether the content is acceptable (TestBulkImportService
    /// validates). Spreadsheets are "flat": one row per question, with the test's settings repeated (or just
    /// written on the first row); JSON is nested (tests -> sections -> questions).
    /// </summary>
    public static class TestImportParser
    {
        public const int MaxTests = 50;
        public const int MaxQuestions = 5000;

        public static List<TestImportTestDto> Parse(Stream stream, ImportFileFormat format, TestImportKind kind, List<string> fileErrors)
        {
            try
            {
                var tests = format switch
                {
                    ImportFileFormat.Json => ParseJson(stream, kind, fileErrors),
                    ImportFileFormat.Excel => BuildFromRows(ReadExcel(stream), kind),
                    ImportFileFormat.Csv => BuildFromRows(ReadCsv(stream), kind),
                    _ => throw new InvalidOperationException("Unsupported file type.")
                };
                for (var i = 0; i < tests.Count; i++) tests[i].Index = i;
                return tests;
            }
            catch (JsonException ex)
            {
                fileErrors.Add($"The JSON file is not valid: {ex.Message}");
            }
            catch (Exception ex) when (ex is InvalidOperationException or CsvHelperException or IOException or InvalidDataException)
            {
                fileErrors.Add(ex.Message);
            }
            return new List<TestImportTestDto>();
        }

        // ------------------------------------------------------------------ JSON

        private static List<TestImportTestDto> ParseJson(Stream stream, TestImportKind kind, List<string> fileErrors)
        {
            using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            JsonElement array;
            if (root.ValueKind == JsonValueKind.Array) array = root;
            else if (root.ValueKind == JsonValueKind.Object && TryProp(root, out var t, "tests", "mockTests", "practiceTests", "templates") && t.ValueKind == JsonValueKind.Array) array = t;
            else if (root.ValueKind == JsonValueKind.Object && TryProp(root, out _, "title")) return new List<TestImportTestDto> { JsonTest(root, kind) };
            else
            {
                fileErrors.Add("Expected a JSON object like { \"tests\": [ ... ] } (or a plain array of tests).");
                return new List<TestImportTestDto>();
            }

            var list = new List<TestImportTestDto>();
            foreach (var el in array.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) { fileErrors.Add("Every entry in \"tests\" must be an object."); continue; }
                list.Add(JsonTest(el, kind));
            }
            return list;
        }

        private static TestImportTestDto JsonTest(JsonElement el, TestImportKind kind)
        {
            var t = new TestImportTestDto
            {
                Title = Str(el, "title", "name") ?? string.Empty,
                Description = Str(el, "description"),
                Exam = Str(el, "exam", "examName"),
                TestType = Str(el, "testType", "type"),
                DurationMinutes = Int(el, "durationMinutes", "duration", "timeMinutes"),
                NegativeMarking = Str(el, "negativeMarking", "negativeMarkingRatio", "negative"),
                Language = Str(el, "language", "medium"),
                ScheduledAt = Str(el, "scheduledAt", "startAt", "startsAt"),
                EndAt = Str(el, "endAt", "endsAt"),
                Status = Str(el, "status"),
                MaxAttempts = Int(el, "maxAttempts"),
                Instructions = Str(el, "instructions"),
                RandomOrder = Bool(el, "randomOrder", "isRandomOrder", "shuffleQuestions"),
                ShuffleOptions = Bool(el, "shuffleOptions", "isShuffleOptions"),
                Subject = Str(el, "subject"),
                Topic = Str(el, "topic"),
                YearFrom = Int(el, "yearFrom"),
                YearTo = Int(el, "yearTo"),
                Difficulty = Str(el, "difficulty"),
                QuestionCount = Int(el, "questionCount", "numberOfQuestions")
            };

            // Result release: either { "resultRelease": { "mode": "OnDate", "releaseAt": "..." } } or flat keys.
            if (TryProp(el, out var rr, "resultRelease", "result") && rr.ValueKind == JsonValueKind.Object)
            {
                t.ResultRelease = Str(rr, "mode", "type");
                t.ResultDelayMinutes = Int(rr, "delayMinutes", "afterMinutes");
                t.ResultReleaseAt = Str(rr, "releaseAt", "date", "at");
            }
            else
            {
                t.ResultRelease = Str(el, "resultRelease", "resultReleaseMode");
                t.ResultDelayMinutes = Int(el, "resultDelayMinutes");
                t.ResultReleaseAt = Str(el, "resultReleaseAt");
            }

            var rowNo = 0;
            if (TryProp(el, out var secs, "sections") && secs.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in secs.EnumerateArray())
                {
                    var name = Str(s, "name", "title") ?? string.Empty;
                    t.Sections.Add(new TestImportSectionDto { Name = name, DurationMinutes = Int(s, "durationMinutes", "duration", "minutes") });
                    if (TryProp(s, out var qs, "questions") && qs.ValueKind == JsonValueKind.Array)
                        foreach (var q in qs.EnumerateArray())
                            t.Questions.Add(JsonQuestion(q, ++rowNo, name));
                }
            }
            if (TryProp(el, out var top, "questions") && top.ValueKind == JsonValueKind.Array)
                foreach (var q in top.EnumerateArray())
                    t.Questions.Add(JsonQuestion(q, ++rowNo, null));
            return t;
        }

        private static TestImportQuestionDto JsonQuestion(JsonElement q, int rowNo, string? sectionName)
        {
            var dto = new TestImportQuestionDto { RowNumber = rowNo };
            if (q.ValueKind == JsonValueKind.String) { dto.QuestionText = q.GetString() ?? string.Empty; dto.Section = sectionName; return dto; }
            dto.Ref = Str(q, "ref", "id", "questionId", "questionBankQuestionId");
            dto.QuestionText = Str(q, "questionText", "question", "text") ?? string.Empty;
            dto.OptionA = Str(q, "optionA", "a") ?? string.Empty;
            dto.OptionB = Str(q, "optionB", "b") ?? string.Empty;
            dto.OptionC = Str(q, "optionC", "c") ?? string.Empty;
            dto.OptionD = Str(q, "optionD", "d") ?? string.Empty;
            if (TryProp(q, out var opts, "options") && opts.ValueKind == JsonValueKind.Array)
            {
                var o = opts.EnumerateArray().Select(x => Scalar(x) ?? string.Empty).ToList();
                if (o.Count > 0) dto.OptionA = o[0];
                if (o.Count > 1) dto.OptionB = o[1];
                if (o.Count > 2) dto.OptionC = o[2];
                if (o.Count > 3) dto.OptionD = o[3];
            }
            dto.CorrectOption = Str(q, "correctOption", "answer", "correct") ?? string.Empty;
            dto.Explanation = Str(q, "explanation", "solution");
            dto.Subject = Str(q, "subject");
            dto.Topic = Str(q, "topic");
            dto.Difficulty = Str(q, "difficulty");
            dto.Section = Str(q, "section") ?? sectionName;
            return dto;
        }

        private static bool TryProp(JsonElement el, out JsonElement value, params string[] names)
        {
            foreach (var p in el.EnumerateObject())
                foreach (var n in names)
                    if (string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase)) { value = p.Value; return true; }
            value = default;
            return false;
        }

        private static string? Scalar(JsonElement v) => v.ValueKind switch
        {
            JsonValueKind.String => v.GetString()?.Trim(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };

        private static string? Str(JsonElement el, params string[] names)
        {
            if (!TryProp(el, out var v, names)) return null;
            var s = Scalar(v);
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        private static int? Int(JsonElement el, params string[] names) => ToInt(Str(el, names));

        private static bool? Bool(JsonElement el, params string[] names) => ParseBool(Str(el, names));

        public static bool? ParseBool(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            switch (s.Trim().ToLowerInvariant())
            {
                case "true": case "yes": case "y": case "1": return true;
                case "false": case "no": case "n": case "0": return false;
                default: return null;
            }
        }

        // ------------------------------------------------------------------ flat files

        private sealed class FlatRow
        {
            public int RowNumber;
            public Dictionary<string, string> Cells = new();
            public string? Get(params string[] names)
            {
                foreach (var n in names)
                    if (Cells.TryGetValue(n, out var v) && !string.IsNullOrWhiteSpace(v)) return v.Trim();
                return null;
            }
        }

        private static string NormKey(string? header) =>
            new string((header ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

        private static List<FlatRow> ReadCsv(Stream stream)
        {
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            var cfg = new CsvConfiguration(CultureInfo.InvariantCulture) { BadDataFound = null, MissingFieldFound = null, HeaderValidated = null };
            using var csv = new CsvReader(reader, cfg);
            if (!csv.Read() || !csv.ReadHeader()) throw new InvalidOperationException("The CSV file is empty.");
            var headers = (csv.HeaderRecord ?? Array.Empty<string>()).Select(NormKey).ToArray();
            var rows = new List<FlatRow>();
            while (csv.Read())
            {
                var row = new FlatRow { RowNumber = csv.Parser.Row };
                for (var i = 0; i < headers.Length; i++)
                {
                    if (headers[i].Length == 0) continue;
                    var v = csv.GetField(i);
                    if (!string.IsNullOrWhiteSpace(v)) row.Cells[headers[i]] = v.Trim();
                }
                if (row.Cells.Count > 0) rows.Add(row);
            }
            return rows;
        }

        private static List<FlatRow> ReadExcel(Stream stream)
        {
            using var wb = new XLWorkbook(stream);
            var sheet = wb.Worksheets.FirstOrDefault(w => !new[] { "instructions", "readme", "example", "examples", "help" }.Contains(w.Name.Trim().ToLowerInvariant()))
                        ?? wb.Worksheets.FirstOrDefault()
                        ?? throw new InvalidOperationException("The Excel file has no sheets.");
            var used = sheet.RangeUsed();
            if (used == null) throw new InvalidOperationException("The Excel sheet is empty.");

            var headerRow = used.FirstRow().RowNumber();
            var firstCol = used.FirstColumn().ColumnNumber();
            var lastCol = used.LastColumn().ColumnNumber();
            var headers = new Dictionary<int, string>();
            for (var c = firstCol; c <= lastCol; c++)
            {
                var key = NormKey(sheet.Cell(headerRow, c).GetString());
                if (key.Length > 0) headers[c] = key;
            }

            var rows = new List<FlatRow>();
            for (var r = headerRow + 1; r <= used.LastRow().RowNumber(); r++)
            {
                var row = new FlatRow { RowNumber = r };
                foreach (var (c, key) in headers)
                {
                    var cell = sheet.Cell(r, c);
                    if (cell.IsEmpty()) continue;
                    var v = cell.DataType == XLDataType.DateTime
                        ? cell.GetDateTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                        : cell.GetString();
                    if (!string.IsNullOrWhiteSpace(v)) row.Cells[key] = v.Trim();
                }
                if (row.Cells.Count > 0) rows.Add(row);
            }
            return rows;
        }

        private static List<TestImportTestDto> BuildFromRows(List<FlatRow> rows, TestImportKind kind)
        {
            var tests = new List<TestImportTestDto>();
            var byTitle = new Dictionary<string, TestImportTestDto>(StringComparer.OrdinalIgnoreCase);
            var sectionMinutes = new Dictionary<TestImportTestDto, Dictionary<string, int?>>();
            string? lastTitle = null;
            var isMock = kind == TestImportKind.MockTest;

            foreach (var row in rows)
            {
                // A blank title continues the test above it, so the title only has to be typed once.
                var title = row.Get("title", "testtitle", "testname", "name") ?? lastTitle;
                if (title == null)
                {
                    // Orphan row (no title anywhere above): attach to a placeholder so it is reported, not dropped.
                    title = string.Empty;
                }
                lastTitle = title;

                if (!byTitle.TryGetValue(title, out var t))
                {
                    t = new TestImportTestDto { Title = title };
                    byTitle[title] = t;
                    tests.Add(t);
                    sectionMinutes[t] = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
                }

                t.Description ??= row.Get("description");
                t.Exam ??= row.Get("exam", "examname");
                t.TestType ??= row.Get("testtype", "type");
                t.DurationMinutes ??= ToInt(row.Get("durationminutes", "duration", "totalminutes"));
                t.NegativeMarking ??= row.Get("negativemarking", "negativemarkingratio", "negative");
                t.Language ??= row.Get("language", "medium");
                t.ScheduledAt ??= row.Get("scheduledat", "startat", "startsat", "scheduledatist");
                t.EndAt ??= row.Get("endat", "endsat", "endatist");
                t.Status ??= row.Get("status");
                t.MaxAttempts ??= ToInt(row.Get("maxattempts"));
                t.Instructions ??= row.Get("instructions");
                t.RandomOrder ??= ParseBool(row.Get("randomorder", "israndomorder", "shufflequestions"));
                t.ShuffleOptions ??= ParseBool(row.Get("shuffleoptions", "isshuffleoptions"));
                t.ResultRelease ??= row.Get("resultrelease", "resultreleasemode");
                t.ResultDelayMinutes ??= ToInt(row.Get("resultdelayminutes"));
                t.ResultReleaseAt ??= row.Get("resultreleaseat", "resultreleaseatist");

                if (!isMock)
                {
                    t.Subject ??= row.Get("subject");
                    t.Topic ??= row.Get("topic");
                    t.YearFrom ??= ToInt(row.Get("yearfrom"));
                    t.YearTo ??= ToInt(row.Get("yearto"));
                    t.Difficulty ??= row.Get("difficulty");
                    t.QuestionCount ??= ToInt(row.Get("questioncount", "numberofquestions"));
                }

                var hasQuestion = row.Get("questiontext", "question", "ref", "questionid", "questionref") != null;
                // A blank Section cell on a question row means "same section as the row above".
                var sectionName = row.Get("section", "sectionname") ?? (hasQuestion && t.Sections.Count > 0 ? t.Sections[^1].Name : null);
                if (sectionName != null)
                {
                    var mins = ToInt(row.Get("sectionminutes", "sectiontime", "sectionduration"));
                    if (!sectionMinutes[t].ContainsKey(sectionName))
                    {
                        sectionMinutes[t][sectionName] = mins;
                        t.Sections.Add(new TestImportSectionDto { Name = sectionName, DurationMinutes = mins });
                    }
                    else if (mins.HasValue && sectionMinutes[t][sectionName] == null)
                    {
                        sectionMinutes[t][sectionName] = mins;
                        t.Sections.First(s => string.Equals(s.Name, sectionName, StringComparison.OrdinalIgnoreCase)).DurationMinutes = mins;
                    }
                }

                var qText = row.Get("questiontext", "question");
                var qRef = row.Get("ref", "questionid", "questionref");
                if (qText != null || qRef != null)
                {
                    t.Questions.Add(new TestImportQuestionDto
                    {
                        RowNumber = row.RowNumber,
                        Ref = qRef,
                        QuestionText = qText ?? string.Empty,
                        OptionA = row.Get("optiona", "a") ?? string.Empty,
                        OptionB = row.Get("optionb", "b") ?? string.Empty,
                        OptionC = row.Get("optionc", "c") ?? string.Empty,
                        OptionD = row.Get("optiond", "d") ?? string.Empty,
                        CorrectOption = row.Get("correctoption", "answer", "correct") ?? string.Empty,
                        Explanation = row.Get("explanation", "solution"),
                        Subject = row.Get("questionsubject") ?? (isMock ? row.Get("subject") : null),
                        Topic = row.Get("questiontopic") ?? (isMock ? row.Get("topic") : null),
                        Difficulty = isMock ? row.Get("difficulty") : row.Get("questiondifficulty"),
                        Section = sectionName
                    });
                }
            }
            return tests;
        }

        private static int? ToInt(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return n;
            if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d == Math.Truncate(d)) return (int)d;
            return null;
        }
    }
}
