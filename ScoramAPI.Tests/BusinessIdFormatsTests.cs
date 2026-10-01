using ScoramAPI.Models;
using ScoramAPI.Services;
using Xunit;

namespace ScoramAPI.Tests
{
    // Covers the pure rules: organization-code mapping (spec §3/§10), formats (§3-§7), backend format
    // validation (§19) and the entity-type parser used by the SuperAdmin change endpoint.
    // Database behaviour (backfill, concurrency, no-reuse) is covered by Scripts/BusinessIdsVerify.sql
    // and the manual checklist in BUSINESS_IDS_REPORT.md.
    public class OrganizationCodeTests
    {
        [Theory]
        [InlineData(null, "SSC CGL", "SSC")]
        [InlineData(null, "SSC CHSL", "SSC")]
        [InlineData(null, "RRB NTPC UG", "RRB")]
        [InlineData(null, "RRB Group D", "RRB")]
        [InlineData(null, "UPSSSC PET", "UP")]
        [InlineData(null, "UPPRPB Constable", "UPPR")]
        [InlineData(null, "UP Police SI", "UP")]
        [InlineData(null, "Bihar Police Constable", "BP")]
        [InlineData(null, "BPSC TRE", "BPSC")]
        [InlineData(null, "BSSC Inter Level", "BSSC")]
        [InlineData(null, "IBPS PO", "IBPS")]
        [InlineData(null, "SBI Clerk", "SBI")]
        [InlineData(null, "Railway NTPC", "RRB")]
        [InlineData("SSC", "Some Random Name", "SSC")]          // assigned Organization wins over the name
        [InlineData("RRB", "SSC CGL", "RRB")]
        [InlineData("SSC - Staff Selection Commission", "CGL", "SSC")]
        [InlineData("Bihar Police", "Constable", "BP")]
        [InlineData("Kerala PSC", "LDC", "KERALAPSC")]          // unmapped org -> its own letters
        [InlineData("Some Very Long Organization Name", "x", "SOMEVERYLO")]
        [InlineData(null, "Mystery Exam", "GEN")]
        [InlineData(null, "", "GEN")]
        [InlineData(null, null, "GEN")]
        [InlineData(null, "SSCGD", "GEN")]                      // never a blind substring of the name
        [InlineData(null, "Sscgd SSC", "GEN")]                  // org token buried mid-name is not used
        public void Resolves_organization_code(string? org, string? exam, string expected) =>
            Assert.Equal(expected, ExamOrganizationCodes.Resolve(org, exam));
    }

    public class FormatTests
    {
        [Theory]
        [InlineData(BusinessIdEntityType.Exam, 1, "SSC", "EXMSSC001")]
        [InlineData(BusinessIdEntityType.Exam, 1234, "SSC", "EXMSSC1234")]
        [InlineData(BusinessIdEntityType.Subject, 4, null, "SUB004")]
        [InlineData(BusinessIdEntityType.Subject, 1000, null, "SUB1000")]
        [InlineData(BusinessIdEntityType.Test, 1, null, "TST0001")]
        [InlineData(BusinessIdEntityType.MockTest, 12, null, "MCK0012")]
        [InlineData(BusinessIdEntityType.Admin, 3, null, "ADM0003")]
        public void Formats_ids(BusinessIdEntityType type, int number, string? org, string expected) =>
            Assert.Equal(expected, BusinessIdFormats.Format(type, number, org));

        [Theory]
        [InlineData(BusinessIdEntityType.Exam, "EXMSSC001", true)]
        [InlineData(BusinessIdEntityType.Exam, "exmssc001", true)]
        [InlineData(BusinessIdEntityType.Exam, "EXMUP001", true)]
        [InlineData(BusinessIdEntityType.Exam, "EXMBPSC010", true)]
        [InlineData(BusinessIdEntityType.Exam, "EXMSSC2001", true)]
        [InlineData(BusinessIdEntityType.Exam, "EXM001", false)]
        [InlineData(BusinessIdEntityType.Exam, "EXMSSC01", false)]
        [InlineData(BusinessIdEntityType.Exam, "EXMSS1C001", false)]
        [InlineData(BusinessIdEntityType.Exam, "EXMSSC000", false)]
        [InlineData(BusinessIdEntityType.Exam, "SUB001", false)]
        [InlineData(BusinessIdEntityType.Exam, "EXMSSC001; DROP TABLE", false)]
        [InlineData(BusinessIdEntityType.Exam, "EXMABCDEFGHIJK001", false)]
        [InlineData(BusinessIdEntityType.Subject, "SUB001", true)]
        [InlineData(BusinessIdEntityType.Subject, "SUB0001", true)]
        [InlineData(BusinessIdEntityType.Subject, "SUB01", false)]
        [InlineData(BusinessIdEntityType.Subject, "TST0001", false)]
        [InlineData(BusinessIdEntityType.Test, "TST0001", true)]
        [InlineData(BusinessIdEntityType.Test, "TST001", false)]
        [InlineData(BusinessIdEntityType.Test, "SUB0001", false)]
        [InlineData(BusinessIdEntityType.MockTest, "MCK0001", true)]
        [InlineData(BusinessIdEntityType.MockTest, "MCK001", false)]
        [InlineData(BusinessIdEntityType.MockTest, "TST0001", false)]
        [InlineData(BusinessIdEntityType.Admin, "ADM0001", true)]
        [InlineData(BusinessIdEntityType.Admin, " adm0002 ", true)]
        [InlineData(BusinessIdEntityType.Admin, "ADM001", false)]
        [InlineData(BusinessIdEntityType.Admin, "ADM0000", false)]
        [InlineData(BusinessIdEntityType.Admin, "ADMJOHN", false)]
        [InlineData(BusinessIdEntityType.Admin, "", false)]
        public void Validates_ids_per_entity_type(BusinessIdEntityType type, string id, bool valid) =>
            Assert.Equal(valid, BusinessIdFormats.IsValid(type, id));

        [Fact]
        public void Parses_counter_key_and_number()
        {
            Assert.True(BusinessIdFormats.TryParse(BusinessIdEntityType.Exam, "EXMSSC010", out var key, out var n));
            Assert.Equal("EXM:SSC", key);
            Assert.Equal(10, n);

            Assert.True(BusinessIdFormats.TryParse(BusinessIdEntityType.Admin, "ADM0007", out key, out n));
            Assert.Equal("ADM", key);
            Assert.Equal(7, n);
        }

        [Fact]
        public void Every_generated_id_and_example_round_trips()
        {
            foreach (var type in Enum.GetValues<BusinessIdEntityType>())
            {
                var id = BusinessIdFormats.Format(type, 42, type == BusinessIdEntityType.Exam ? "IBPS" : null);
                Assert.True(BusinessIdFormats.IsValid(type, id), $"{type} {id}");
                Assert.True(BusinessIdFormats.IsValid(type, BusinessIdFormats.ExampleFor(type)), $"example {type}");
            }
        }

        [Theory]
        [InlineData("exam", BusinessIdEntityType.Exam)]
        [InlineData("Subjects", BusinessIdEntityType.Subject)]
        [InlineData("practice-tests", BusinessIdEntityType.Test)]
        [InlineData("mock-tests", BusinessIdEntityType.MockTest)]
        [InlineData("mocktest", BusinessIdEntityType.MockTest)]
        [InlineData("admins", BusinessIdEntityType.Admin)]
        public void Parses_entity_type(string raw, BusinessIdEntityType expected)
        {
            Assert.True(BusinessIdFormats.TryParseEntityType(raw, out var type));
            Assert.Equal(expected, type);
        }

        [Fact]
        public void Rejects_unknown_entity_type() =>
            Assert.False(BusinessIdFormats.TryParseEntityType("users", out _));
    }
}
