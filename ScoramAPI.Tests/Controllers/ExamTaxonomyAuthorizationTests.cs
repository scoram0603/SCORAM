using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Moq;
using ScoramAPI.Controllers;
using ScoramAPI.Data;
using ScoramAPI.Enums;
using ScoramAPI.Models;
using ScoramAPI.Services;
using Xunit;

namespace ScoramAPI.Tests.Controllers
{
    // Same guarantee as SubjectManagementAuthorizationTests, for the sibling controller that manages the
    // per-exam subject/topic taxonomy. Every action must require ManageSubjects; the three question
    // classification actions ALSO accept ManageQuestionBank (documented in the controller) -- and nothing
    // else may.
    //
    // Actions are discovered by reflection and called with default arguments, so an endpoint added later is
    // covered automatically and cannot skip the gate.
    public class ExamTaxonomyAuthorizationTests
    {
        private static readonly Guid AdminId = Guid.NewGuid();

        // Actions that deliberately also accept ManageQuestionBank (ExamTaxonomyController.Run's
        // allowQuestionBankPermission: true).
        private static readonly HashSet<string> AlsoAllowsQuestionBank = new() { "Map", "Unmap", "QuestionClassifications" };

        private static ClaimsPrincipal Principal(string role) =>
            new(new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, AdminId.ToString()),
                new Claim(ClaimTypes.Role, role),
            }, "test"));

        private static IEnumerable<MethodInfo> Actions() =>
            typeof(ExamTaxonomyController)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any());

        public static IEnumerable<object[]> ActionNames() => Actions().Select(m => new object[] { m.Name });

        private static ScoramDbContext NewDb() =>
            new(new DbContextOptionsBuilder<ScoramDbContext>().UseInMemoryDatabase("taxonomy-authz-" + Guid.NewGuid()).Options);

        private static async Task<IActionResult> Invoke(ExamTaxonomyController controller, string actionName)
        {
            var method = Actions().Single(m => m.Name == actionName);
            var args = method.GetParameters().Select(p =>
                p.ParameterType == typeof(CancellationToken) ? (object)default(CancellationToken)
                : p.ParameterType == typeof(string) ? null
                : Activator.CreateInstance(p.ParameterType)).ToArray(); // value types -> default; DTOs -> empty instance
            return await (Task<IActionResult>)method.Invoke(controller, args)!;
        }

        private static async Task<(ExamTaxonomyController Controller, Mock<IExamTaxonomyService> Service, ScoramDbContext Db)>
            BuildAsync(string role, params AdminPermission[] grants)
        {
            var db = NewDb();
            foreach (var g in grants) db.AdminPermissionGrants.Add(new AdminPermissionGrant { AdminId = AdminId, Permission = g });
            await db.SaveChangesAsync();

            var service = new Mock<IExamTaxonomyService>(MockBehavior.Loose);
            var controller = new ExamTaxonomyController(service.Object, new AdminPermissionService(db))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = Principal(role) }
                }
            };
            return (controller, service, db);
        }

        [Theory]
        [MemberData(nameof(ActionNames))]
        public async Task Admin_without_any_grant_is_forbidden_and_the_service_is_never_called(string action)
        {
            var (controller, service, db) = await BuildAsync("Admin");
            using var _ = db;

            Assert.IsType<ForbidResult>(await Invoke(controller, action));
            service.VerifyNoOtherCalls();
        }

        [Theory]
        [MemberData(nameof(ActionNames))]
        public async Task Admin_with_ManageSubjects_reaches_the_service(string action)
        {
            var (controller, service, db) = await BuildAsync("Admin", AdminPermission.ManageSubjects);
            using var _ = db;

            Assert.IsType<OkObjectResult>(await Invoke(controller, action));
            Assert.NotEmpty(service.Invocations);
        }

        [Theory]
        [MemberData(nameof(ActionNames))]
        public async Task SuperAdmin_reaches_the_service_without_an_explicit_grant(string action)
        {
            var (controller, service, db) = await BuildAsync("SuperAdmin");
            using var _ = db;

            Assert.IsType<OkObjectResult>(await Invoke(controller, action));
            Assert.NotEmpty(service.Invocations);
        }

        [Theory]
        [MemberData(nameof(ActionNames))]
        public async Task ManageQuestionBank_alone_opens_only_the_documented_classification_actions(string action)
        {
            var (controller, service, db) = await BuildAsync("Admin", AdminPermission.ManageQuestionBank);
            using var _ = db;

            var result = await Invoke(controller, action);

            if (AlsoAllowsQuestionBank.Contains(action))
            {
                Assert.IsType<OkObjectResult>(result);
                Assert.NotEmpty(service.Invocations);
            }
            else
            {
                Assert.IsType<ForbidResult>(result);
                service.VerifyNoOtherCalls();
            }
        }

        [Fact]
        public void The_QuestionBank_exception_list_only_names_real_actions()
        {
            var actual = Actions().Select(m => m.Name).ToHashSet();
            Assert.True(AlsoAllowsQuestionBank.IsSubsetOf(actual),
                $"Stale entries: {string.Join(", ", AlsoAllowsQuestionBank.Except(actual))}");
        }
    }
}
