using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;
using CsvHelper;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    public interface IBulkImportService
    {
        /// <summary>Reads a CSV, Excel (.xlsx), or JSON file into a common row shape. Column/property
        /// names are matched case-insensitively: QuestionNumber, Subject, Topic, DifficultyLevel,
        /// QuestionText, OptionA-D, CorrectOption, Explanation, SourceReference. Throws
        /// InvalidDataException with a human-readable message on structural problems (missing required
        /// columns, unreadable file) -- anything that means "this file itself is unusable," as opposed
        /// to a single bad row, which Validate handles instead.</summary>
        Task<List<ImportedQuestionRow>> ParseAsync(Stream fileStream, ImportFileFormat format);

        /// <summary>Parses a ZIP-bundled questions.json (same JSON schema as the standalone .json
        /// upload, plus optional per-field image filenames and an optional contentBlocks array --
        /// see spec section 19-20) and stages any referenced images from the ZIP's images/ folder
        /// via IFileStorageService, under the given staging subfolder (see
        /// BulkImportController.Preview for how that subfolder is named and later cleaned up). An
        /// image filename referenced by a row but missing from the ZIP, or one that fails validation
        /// (bad extension, too large), is recorded on that row's ImageErrors rather than failing the
        /// whole batch -- the row is simply invalid, same as a missing required text field (spec
        /// section 30: one bad question shouldn't block the other 999).</summary>
        Task<List<ImportedQuestionRow>> ParseZipAsync(string questionsJson, IReadOnlyDictionary<string, byte[]> images, string stagingSubfolder);

        /// <summary>Annotates each row's IsValid/Errors in place. A row is invalid if it's missing a
        /// required field, has an unparseable Difficulty/CorrectOption, or its QuestionNumber collides
        /// with another row in this same batch or with a question already in the target paper.</summary>
        void Validate(List<ImportedQuestionRow> rows, List<Question> existingPaperQuestions);
    }

    public class BulkImportService : IBulkImportService
    {
        private static readonly string[] RequiredHeaders =
        {
            "QuestionNumber", "Subject", "Topic", "QuestionText",
            "OptionA", "OptionB", "OptionC", "OptionD", "CorrectOption"
        };

        private readonly IFileStorageService _fileStorage;

        public BulkImportService(IFileStorageService fileStorage)
        {
            _fileStorage = fileStorage;
        }

        public async Task<List<ImportedQuestionRow>> ParseAsync(Stream fileStream, ImportFileFormat format)
        {
            return format switch
            {
                ImportFileFormat.Csv => await ParseCsvAsync(fileStream),
                ImportFileFormat.Excel => ParseExcel(fileStream),
                ImportFileFormat.Json => await ParseJsonAsync(fileStream),
                _ => throw new InvalidDataException("Unsupported file format.")
            };
        }

        private async Task<List<ImportedQuestionRow>> ParseCsvAsync(Stream fileStream)
        {
            using var reader = new StreamReader(fileStream);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

            if (!await csv.ReadAsync() || !csv.ReadHeader())
                throw new InvalidDataException("The CSV file appears to be empty.");

            CheckHeaders(csv.HeaderRecord ?? Array.Empty<string>());

            var rows = new List<ImportedQuestionRow>();
            var rowNumber = 0;
            while (await csv.ReadAsync())
            {
                rowNumber++;
                rows.Add(MapRow(rowNumber, header => csv.GetField(header) ?? string.Empty));
            }
            return rows;
        }

        private List<ImportedQuestionRow> ParseExcel(Stream fileStream)
        {
            using var workbook = new XLWorkbook(fileStream);
            var sheet = workbook.Worksheets.FirstOrDefault()
                ?? throw new InvalidDataException("The Excel file has no worksheets.");

            // RowsUsed() already skips fully-blank rows, so gaps in the data don't need special
            // handling here the way a fixed row-range scan would.
            var usedRows = sheet.RowsUsed().ToList();
            if (usedRows.Count == 0)
                throw new InvalidDataException("The Excel file appears to be empty.");

            var headerRow = usedRows[0];
            const int maxColumnsToScan = 30; // generous bound -- our expected header set has ~12 columns
            var columnIndexByHeader = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var col = 1; col <= maxColumnsToScan; col++)
            {
                var headerText = headerRow.Cell(col).GetString().Trim();
                if (string.IsNullOrEmpty(headerText)) continue;
                if (!columnIndexByHeader.ContainsKey(headerText))
                    columnIndexByHeader[headerText] = col;
            }
            CheckHeaders(columnIndexByHeader.Keys);

            var rows = new List<ImportedQuestionRow>();
            var rowNumber = 0;
            foreach (var excelRow in usedRows.Skip(1))
            {
                rowNumber++;
                rows.Add(MapRow(rowNumber, header =>
                    columnIndexByHeader.TryGetValue(header, out var col) ? excelRow.Cell(col).GetString().Trim() : string.Empty));
            }
            return rows;
        }

        private async Task<List<ImportedQuestionRow>> ParseJsonAsync(Stream fileStream)
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

            var rows = new List<ImportedQuestionRow>();
            for (var i = 0; i < parsed.Count; i++)
            {
                var j = parsed[i];
                rows.Add(MapRow(i + 1, header => header switch
                {
                    "QuestionNumber" => j.QuestionNumber?.ToString() ?? "",
                    "Subject" => j.Subject ?? "",
                    "Topic" => j.Topic ?? "",
                    "DifficultyLevel" => j.DifficultyLevel ?? "",
                    "QuestionText" => j.QuestionText ?? "",
                    "OptionA" => j.OptionA ?? "",
                    "OptionB" => j.OptionB ?? "",
                    "OptionC" => j.OptionC ?? "",
                    "OptionD" => j.OptionD ?? "",
                    "CorrectOption" => j.CorrectOption ?? "",
                    "Explanation" => j.Explanation ?? "",
                    "SourceReference" => j.SourceReference ?? "",
                    _ => ""
                }));
            }
            return rows;
        }

        // See IBulkImportService.ParseZipAsync's own doc comment for the overall contract. Reuses
        // MapRow (below) for every text field exactly like ParseJsonAsync does, then layers on
        // ContentBlocksJson and staged image URLs -- the two things only a ZIP upload can supply.
        public async Task<List<ImportedQuestionRow>> ParseZipAsync(string questionsJson, IReadOnlyDictionary<string, byte[]> images, string stagingSubfolder)
        {
            List<JsonRow>? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<List<JsonRow>>(questionsJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"questions.json doesn't look like valid JSON: {ex.Message}");
            }

            if (parsed == null || parsed.Count == 0)
                throw new InvalidDataException("questions.json contains no questions (expected a top-level array).");

            var rows = new List<ImportedQuestionRow>();
            for (var i = 0; i < parsed.Count; i++)
            {
                var j = parsed[i];
                var row = MapRow(i + 1, header => header switch
                {
                    "QuestionNumber" => j.QuestionNumber?.ToString() ?? "",
                    "Subject" => j.Subject ?? "",
                    "Topic" => j.Topic ?? "",
                    "DifficultyLevel" => j.DifficultyLevel ?? "",
                    "QuestionText" => j.QuestionText ?? "",
                    "OptionA" => j.OptionA ?? "",
                    "OptionB" => j.OptionB ?? "",
                    "OptionC" => j.OptionC ?? "",
                    "OptionD" => j.OptionD ?? "",
                    "CorrectOption" => j.CorrectOption ?? "",
                    "Explanation" => j.Explanation ?? "",
                    "SourceReference" => j.SourceReference ?? "",
                    _ => ""
                });

                if (j.ContentBlocks is { Count: > 0 })
                    row.ContentBlocksJson = JsonSerializer.Serialize(j.ContentBlocks);

                row.QuestionImageUrl = await TryStageImageAsync(row, "questionImage", j.QuestionImage, images, stagingSubfolder);
                row.OptionAImageUrl = await TryStageImageAsync(row, "optionAImage", j.OptionAImage, images, stagingSubfolder);
                row.OptionBImageUrl = await TryStageImageAsync(row, "optionBImage", j.OptionBImage, images, stagingSubfolder);
                row.OptionCImageUrl = await TryStageImageAsync(row, "optionCImage", j.OptionCImage, images, stagingSubfolder);
                row.OptionDImageUrl = await TryStageImageAsync(row, "optionDImage", j.OptionDImage, images, stagingSubfolder);
                row.ExplanationImageUrl = await TryStageImageAsync(row, "explanationImage", j.ExplanationImage, images, stagingSubfolder);

                rows.Add(row);
            }
            return rows;
        }

        // Looks up `fileName` in the ZIP's images/ folder and uploads it to the staging subfolder,
        // recording a row-level error (rather than throwing) on either a missing filename or a
        // validation failure -- see ParseZipAsync's own doc comment on why this can't just bubble up
        // and fail the whole batch.
        private async Task<string?> TryStageImageAsync(ImportedQuestionRow row, string fieldLabel, string? fileName, IReadOnlyDictionary<string, byte[]> images, string stagingSubfolder)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;

            if (!images.TryGetValue(fileName, out var bytes))
            {
                row.ImageErrors.Add($"Image \"{fileName}\" referenced by {fieldLabel} wasn't found in the ZIP's images/ folder.");
                return null;
            }

            try
            {
                using var ms = new MemoryStream(bytes);
                return await _fileStorage.SaveImageFromStreamAsync(ms, fileName, bytes.Length, stagingSubfolder);
            }
            catch (ArgumentException ex)
            {
                row.ImageErrors.Add($"{fieldLabel} (\"{fileName}\"): {ex.Message}");
                return null;
            }
        }

        // Shared row-building logic for all four formats (CSV/Excel/JSON/ZIP) -- `field` abstracts
        // over "get this named field's raw string value", however the underlying format actually
        // stores it.
        private static ImportedQuestionRow MapRow(int rowNumber, Func<string, string> field)
        {
            _ = int.TryParse(field("QuestionNumber"), out var questionNumber);
            return new ImportedQuestionRow
            {
                RowNumber = rowNumber,
                QuestionNumber = questionNumber,
                Subject = field("Subject").Trim(),
                Topic = field("Topic").Trim(),
                DifficultyLevel = field("DifficultyLevel").Trim(),
                QuestionText = field("QuestionText").Trim(),
                OptionA = field("OptionA").Trim(),
                OptionB = field("OptionB").Trim(),
                OptionC = field("OptionC").Trim(),
                OptionD = field("OptionD").Trim(),
                CorrectOption = field("CorrectOption").Trim(),
                Explanation = string.IsNullOrWhiteSpace(field("Explanation")) ? null : field("Explanation").Trim(),
                SourceReference = string.IsNullOrWhiteSpace(field("SourceReference")) ? null : field("SourceReference").Trim()
            };
        }

        private static void CheckHeaders(IEnumerable<string> actualHeaders)
        {
            var present = new HashSet<string>(actualHeaders, StringComparer.OrdinalIgnoreCase);
            var missing = RequiredHeaders.Where(h => !present.Contains(h)).ToList();
            if (missing.Count > 0)
                throw new InvalidDataException($"Missing required column(s): {string.Join(", ", missing)}. " +
                    $"Expected headers: {string.Join(", ", RequiredHeaders)} (Explanation and SourceReference are optional).");
        }

        public void Validate(List<ImportedQuestionRow> rows, List<Question> existingPaperQuestions)
        {
            var existingNumbers = new HashSet<int>(existingPaperQuestions.Select(q => q.QuestionNumber ?? -1));
            var seenInBatch = new Dictionary<int, int>(); // QuestionNumber -> first RowNumber that used it

            foreach (var row in rows)
            {
                // Start from any image-staging errors (ZIP upload only) rather than an empty list --
                // see ImportedQuestionRow.ImageErrors's own comment for why these live separately
                // from Errors and have to be re-seeded here on every validation pass.
                row.Errors = new List<string>(row.ImageErrors);

                if (row.QuestionNumber <= 0) row.Errors.Add("Question number is required and must be positive.");
                if (string.IsNullOrWhiteSpace(row.Subject)) row.Errors.Add("Subject is required.");
                if (string.IsNullOrWhiteSpace(row.Topic)) row.Errors.Add("Topic is required.");
                if (string.IsNullOrWhiteSpace(row.QuestionText)) row.Errors.Add("Question text is required.");
                if (string.IsNullOrWhiteSpace(row.OptionA)) row.Errors.Add("Option A is required.");
                if (string.IsNullOrWhiteSpace(row.OptionB)) row.Errors.Add("Option B is required.");
                if (string.IsNullOrWhiteSpace(row.OptionC)) row.Errors.Add("Option C is required.");
                if (string.IsNullOrWhiteSpace(row.OptionD)) row.Errors.Add("Option D is required.");

                if (string.IsNullOrWhiteSpace(row.DifficultyLevel))
                {
                    row.DifficultyLevel = "Medium"; // reasonable default for an ad-hoc spreadsheet -- absence isn't an error
                }
                else if (!Enum.TryParse<DifficultyLevel>(row.DifficultyLevel, ignoreCase: true, out _))
                {
                    row.Errors.Add($"'{row.DifficultyLevel}' isn't a valid difficulty (expected Easy, Medium, or Hard).");
                }

                if (string.IsNullOrWhiteSpace(row.CorrectOption))
                    row.Errors.Add("Correct option is required (A, B, C, or D).");
                else if (!Enum.TryParse<OptionLetter>(row.CorrectOption, ignoreCase: true, out _))
                    row.Errors.Add($"'{row.CorrectOption}' isn't a valid correct option (expected A, B, C, or D).");

                if (row.QuestionNumber > 0)
                {
                    if (existingNumbers.Contains(row.QuestionNumber))
                        row.Errors.Add($"Question number {row.QuestionNumber} already exists in this paper.");
                    else if (seenInBatch.TryGetValue(row.QuestionNumber, out var firstRow))
                        row.Errors.Add($"Question number {row.QuestionNumber} is duplicated in this file (also used by row {firstRow}).");
                    else
                        seenInBatch[row.QuestionNumber] = row.RowNumber;
                }

                // Re-validate ContentBlocksJson here too (not just at single-question Create/Update)
                // -- a ZIP's questions.json can hand-author a contentBlocks array with an invalid
                // "type" or empty content just as easily as a form field can, and this is the one
                // place that check runs for a bulk-imported row.
                if (!string.IsNullOrWhiteSpace(row.ContentBlocksJson))
                {
                    try
                    {
                        row.ContentBlocksJson = ContentBlocksJsonHelper.ValidateAndSerialize(row.ContentBlocksJson);
                    }
                    catch (ArgumentException ex)
                    {
                        row.Errors.Add($"Content blocks: {ex.Message}");
                    }
                }

                row.IsValid = row.Errors.Count == 0;
            }
        }

        // Matches JSON property names case-insensitively via JsonSerializerOptions above. The image
        // filename fields and ContentBlocks are only ever populated for a ZIP upload's
        // questions.json -- a plain .json upload leaves them null, which is indistinguishable from
        // "this question has no images/rich content" (exactly the backward-compatible behavior
        // spec section 19-20 asks for).
        private class JsonRow
        {
            public int? QuestionNumber { get; set; }
            public string? Subject { get; set; }
            public string? Topic { get; set; }
            public string? DifficultyLevel { get; set; }
            public string? QuestionText { get; set; }
            public string? OptionA { get; set; }
            public string? OptionB { get; set; }
            public string? OptionC { get; set; }
            public string? OptionD { get; set; }
            public string? CorrectOption { get; set; }
            public string? Explanation { get; set; }
            public string? SourceReference { get; set; }
            public string? QuestionImage { get; set; }
            public string? OptionAImage { get; set; }
            public string? OptionBImage { get; set; }
            public string? OptionCImage { get; set; }
            public string? OptionDImage { get; set; }
            public string? ExplanationImage { get; set; }
            public List<ContentBlockDto>? ContentBlocks { get; set; }
        }
    }
}
