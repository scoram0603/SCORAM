using System.Text;
using ClosedXML.Excel;
using ScoramAPI.Enums;

namespace ScoramAPI.Services
{
    /// <summary>Downloadable sample files for the Mock Test / Practice Template bulk upload. Every sample uses the
    /// exam name "Your Exam Name" on purpose: a sample file uploaded unchanged fails validation instead of
    /// quietly creating junk tests.</summary>
    public static class TestImportTemplates
    {
        private static readonly string[] MockHeaders =
        {
            "Title", "Exam", "TestType", "DurationMinutes", "NegativeMarking", "Language", "ScheduledAt", "EndAt", "Status", "MaxAttempts",
            "RandomOrder", "ShuffleOptions", "ResultRelease", "ResultDelayMinutes", "ResultReleaseAt", "Instructions",
            "Section", "SectionMinutes", "Ref", "QuestionText", "OptionA", "OptionB", "OptionC", "OptionD", "CorrectOption",
            "Explanation", "Subject", "Topic", "Difficulty"
        };

        private static readonly string[] PracticeHeaders =
        {
            "Title", "Description", "Exam", "Subject", "Topic", "YearFrom", "YearTo", "Difficulty", "QuestionCount",
            "DurationMinutes", "NegativeMarking", "RandomOrder", "Status",
            "Ref", "QuestionText", "OptionA", "OptionB", "OptionC", "OptionD", "CorrectOption", "Explanation",
            "QuestionSubject", "QuestionTopic", "QuestionDifficulty"
        };

        private static string[] Headers(TestImportKind kind) => kind == TestImportKind.MockTest ? MockHeaders : PracticeHeaders;

        private static List<string[]> SampleRows(TestImportKind kind)
        {
            var rows = new List<string[]>();
            string[] Row(params (string Col, string Val)[] vals)
            {
                var h = Headers(kind);
                var r = new string[h.Length];
                foreach (var (col, val) in vals) r[Array.IndexOf(h, col)] = val;
                return r.Select(x => x ?? string.Empty).ToArray();
            }

            if (kind == TestImportKind.MockTest)
            {
                // Sectioned test: Maths 25 min, Reasoning 20 min. Title/settings only on the first row; Section carries down.
                rows.Add(Row(("Title", "SSC CGL Mock 1 (SAMPLE)"), ("Exam", "Your Exam Name"), ("TestType", "FullMockTest"), ("NegativeMarking", "0.25"),
                    ("Language", "English"), ("ScheduledAt", "2026-11-05 10:00"), ("EndAt", "2026-11-12 23:59"), ("Status", "Draft"),
                    ("ResultRelease", "OnDate"), ("ResultReleaseAt", "2026-11-13 18:00"), ("Instructions", "Read every question carefully."),
                    ("Section", "Maths"), ("SectionMinutes", "25"), ("QuestionText", "What is 15% of 200?"),
                    ("OptionA", "20"), ("OptionB", "30"), ("OptionC", "40"), ("OptionD", "50"), ("CorrectOption", "B"),
                    ("Explanation", "15/100 x 200 = 30"), ("Subject", "Quantitative Aptitude"), ("Topic", "Percentage"), ("Difficulty", "Easy")));
                rows.Add(Row(("Title", "SSC CGL Mock 1 (SAMPLE)"), ("Section", "Maths"), ("QuestionText", "Reuse an existing Question Bank question: just its text")));
                rows.Add(Row(("Title", "SSC CGL Mock 1 (SAMPLE)"), ("Section", "Reasoning"), ("SectionMinutes", "20"), ("QuestionText", "Find the odd one out: Apple, Mango, Potato, Banana"),
                    ("OptionA", "Apple"), ("OptionB", "Mango"), ("OptionC", "Potato"), ("OptionD", "Banana"), ("CorrectOption", "C"),
                    ("Subject", "Reasoning"), ("Topic", "Odd One Out")));
                // A simple, unsectioned test.
                rows.Add(Row(("Title", "Daily Quiz 1 (SAMPLE)"), ("Exam", "Your Exam Name"), ("TestType", "DailyQuiz"), ("DurationMinutes", "10"), ("NegativeMarking", "0"),
                    ("Status", "Draft"), ("ResultRelease", "Instant"), ("QuestionText", "Capital of India?"),
                    ("OptionA", "Mumbai"), ("OptionB", "New Delhi"), ("OptionC", "Kolkata"), ("OptionD", "Chennai"), ("CorrectOption", "B"),
                    ("Subject", "General Knowledge"), ("Topic", "Polity")));
            }
            else
            {
                rows.Add(Row(("Title", "Percentage Drill (SAMPLE, filter-based)"), ("Description", "20 random percentage questions"), ("Exam", "Your Exam Name"),
                    ("Subject", "Quantitative Aptitude"), ("Topic", "Percentage"), ("YearFrom", "2018"), ("YearTo", "2025"), ("Difficulty", "Medium"),
                    ("QuestionCount", "20"), ("DurationMinutes", "20"), ("NegativeMarking", "0.25"), ("RandomOrder", "yes"), ("Status", "Draft")));
                rows.Add(Row(("Title", "Weekly GK Set (SAMPLE, fixed questions)"), ("Exam", "Your Exam Name"), ("DurationMinutes", "10"), ("NegativeMarking", "0"), ("Status", "Draft"),
                    ("QuestionText", "Capital of India?"), ("OptionA", "Mumbai"), ("OptionB", "New Delhi"), ("OptionC", "Kolkata"), ("OptionD", "Chennai"),
                    ("CorrectOption", "B"), ("QuestionSubject", "General Knowledge"), ("QuestionTopic", "Polity")));
                rows.Add(Row(("Title", "Weekly GK Set (SAMPLE, fixed questions)"), ("QuestionText", "Reuse an existing Question Bank question: just its text")));
            }
            return rows;
        }

        public static byte[] Csv(TestImportKind kind)
        {
            static string Esc(string s) => s.Contains(',') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", Headers(kind)));
            foreach (var r in SampleRows(kind)) sb.AppendLine(string.Join(",", r.Select(Esc)));
            return new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        }

        public static byte[] Excel(TestImportKind kind)
        {
            using var wb = new XLWorkbook();
            var headers = Headers(kind);

            var sheet = wb.AddWorksheet("Tests");
            for (var c = 0; c < headers.Length; c++)
            {
                var cell = sheet.Cell(1, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.LightSteelBlue;
            }
            sheet.SheetView.FreezeRows(1);

            var example = wb.AddWorksheet("Example");
            for (var c = 0; c < headers.Length; c++)
            {
                var cell = example.Cell(1, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            }
            var rowNo = 2;
            foreach (var r in SampleRows(kind))
            {
                for (var c = 0; c < r.Length; c++) if (r[c].Length > 0) example.Cell(rowNo, c + 1).SetValue(r[c]);
                rowNo++;
            }
            example.Columns().Width = 20;

            var help = wb.AddWorksheet("Instructions");
            var lines = kind == TestImportKind.MockTest ? MockInstructions : PracticeInstructions;
            for (var i = 0; i < lines.Length; i++) help.Cell(i + 1, 1).Value = lines[i];
            help.Column(1).Width = 120;
            help.Cell(1, 1).Style.Font.Bold = true;

            // The sheet the admin fills is first; Example/Instructions are skipped by the importer.
            sheet.SetTabActive();
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        public static byte[] Json(TestImportKind kind)
        {
            var text = kind == TestImportKind.MockTest ? MockJson : PracticeJson;
            return new UTF8Encoding(false).GetBytes(text);
        }

        private static readonly string[] MockInstructions =
        {
            "MOCK TEST BULK UPLOAD - how to fill the 'Tests' sheet",
            "One row = one question. A test is all rows with the same Title. Settings (Exam, Duration...) only need to be written on the test's first row.",
            "Section: the section the question belongs to (e.g. Maths). Leave blank on later rows to stay in the same section. SectionMinutes: time for that section - give it for EVERY section or none.",
            "With sections that have times, the overall DurationMinutes is the sum of the sections (you can leave it blank).",
            "Exam must already exist (Manage Exams). Dates/times are written like 2026-11-05 10:00 (same as the date fields on the Create Mock Test form).",
            "ResultRelease: Instant | AfterDelay (+ResultDelayMinutes) | OnDate (+ResultReleaseAt) | AfterWindowEnds (when EndAt passes).",
            "NegativeMarking: 0.25, 1/4 or 25%. Status: Draft | Published | Archived (default Draft). TestType: FullMockTest | SubjectQuiz | TopicPractice | DailyQuiz.",
            "QUESTIONS - three ways: (1) full question: QuestionText + OptionA-D + CorrectOption + Subject (Topic/Explanation/Difficulty optional) - reused if it already exists in the Question Bank, otherwise saved as a test-only question;",
            "(2) existing question by text only: fill just QuestionText (no options); (3) existing question by id: fill just Ref.",
            "New questions made here do NOT appear in PYQ search - they are only used by tests. They become normal PYQs only if the same question is later uploaded as a PYQ.",
            "The 'Example' sheet shows filled rows. Do not rename the 'Tests' sheet's header row."
        };

        private static readonly string[] PracticeInstructions =
        {
            "PRACTICE TEMPLATE BULK UPLOAD - how to fill the 'Tests' sheet",
            "One template = all rows with the same Title. Settings only need to be written on its first row.",
            "FILTER-BASED template: fill only the settings (Subject/Topic/Exam/Years/Difficulty/QuestionCount...) - students get a fresh random set each attempt from the Question Bank.",
            "FIXED template: also add one row per question - then those exact questions are used every time (QuestionCount is ignored).",
            "Questions: full (QuestionText + OptionA-D + CorrectOption + QuestionSubject), or QuestionText only to reuse an existing Question Bank question, or Ref (question id).",
            "Subject, Topic and Exam (if given) must already exist. Negative marking default 0, DurationMinutes default 20.",
            "New questions created here are test-only: they never show in PYQ search."
        };

        private const string MockJson = @"{
  ""tests"": [
    {
      ""title"": ""SSC CGL Mock 1 (SAMPLE)"",
      ""exam"": ""Your Exam Name"",
      ""testType"": ""FullMockTest"",
      ""negativeMarking"": 0.25,
      ""language"": ""English"",
      ""scheduledAt"": ""2026-11-05 10:00"",
      ""endAt"": ""2026-11-12 23:59"",
      ""status"": ""Draft"",
      ""maxAttempts"": 1,
      ""randomOrder"": false,
      ""shuffleOptions"": false,
      ""resultRelease"": { ""mode"": ""OnDate"", ""releaseAt"": ""2026-11-13 18:00"" },
      ""instructions"": ""Read every question carefully."",
      ""sections"": [
        {
          ""name"": ""Maths"",
          ""durationMinutes"": 25,
          ""questions"": [
            {
              ""questionText"": ""What is 15% of 200?"",
              ""optionA"": ""20"", ""optionB"": ""30"", ""optionC"": ""40"", ""optionD"": ""50"",
              ""correctOption"": ""B"",
              ""explanation"": ""15/100 x 200 = 30"",
              ""subject"": ""Quantitative Aptitude"", ""topic"": ""Percentage"", ""difficulty"": ""Easy""
            },
            { ""questionText"": ""Reuse an existing Question Bank question: just its text"" },
            { ""ref"": ""00000000-0000-0000-0000-000000000000"" }
          ]
        },
        {
          ""name"": ""Reasoning"",
          ""durationMinutes"": 20,
          ""questions"": [
            {
              ""questionText"": ""Find the odd one out: Apple, Mango, Potato, Banana"",
              ""options"": [""Apple"", ""Mango"", ""Potato"", ""Banana""],
              ""correctOption"": ""C"",
              ""subject"": ""Reasoning"", ""topic"": ""Odd One Out""
            }
          ]
        }
      ]
    },
    {
      ""title"": ""Daily Quiz 1 (SAMPLE, no sections)"",
      ""exam"": ""Your Exam Name"",
      ""testType"": ""DailyQuiz"",
      ""durationMinutes"": 10,
      ""negativeMarking"": 0,
      ""resultRelease"": { ""mode"": ""Instant"" },
      ""questions"": [
        {
          ""questionText"": ""Capital of India?"",
          ""optionA"": ""Mumbai"", ""optionB"": ""New Delhi"", ""optionC"": ""Kolkata"", ""optionD"": ""Chennai"",
          ""correctOption"": ""B"", ""subject"": ""General Knowledge"", ""topic"": ""Polity""
        }
      ]
    }
  ]
}
";

        private const string PracticeJson = @"{
  ""tests"": [
    {
      ""title"": ""Percentage Drill (SAMPLE, filter-based)"",
      ""description"": ""20 random percentage questions"",
      ""exam"": ""Your Exam Name"",
      ""subject"": ""Quantitative Aptitude"",
      ""topic"": ""Percentage"",
      ""yearFrom"": 2018,
      ""yearTo"": 2025,
      ""difficulty"": ""Medium"",
      ""questionCount"": 20,
      ""durationMinutes"": 20,
      ""negativeMarking"": 0.25,
      ""randomOrder"": true,
      ""status"": ""Draft""
    },
    {
      ""title"": ""Weekly GK Set (SAMPLE, fixed questions)"",
      ""exam"": ""Your Exam Name"",
      ""durationMinutes"": 10,
      ""negativeMarking"": 0,
      ""status"": ""Draft"",
      ""questions"": [
        {
          ""questionText"": ""Capital of India?"",
          ""optionA"": ""Mumbai"", ""optionB"": ""New Delhi"", ""optionC"": ""Kolkata"", ""optionD"": ""Chennai"",
          ""correctOption"": ""B"", ""subject"": ""General Knowledge"", ""topic"": ""Polity""
        },
        { ""questionText"": ""Reuse an existing Question Bank question: just its text"" }
      ]
    }
  ]
}
";
    }
}
