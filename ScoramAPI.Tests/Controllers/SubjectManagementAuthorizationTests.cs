using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Moq;
using ScoramAPI.Controllers;
using ScoramAPI.Data;
using ScoramAPI.DTOs;
using ScoramAPI.Enums;
using ScoramAPI.Models;
using ScoramAPI.Services;
using Xunit;

namespace ScoramAPI.Tests.Controllers
{
    // Regression tests for SEC-001: every Subject Management action must require the granular
    // ManageSubjects permission (Super Admin implicitly holds it), on top of the Admin/SuperAdmin role.
    //
    // These are controller-level unit tests (permission service + subject service are faked), so they
    // prove the controller's gate. A true end-to-end 401 for an unauthenticated caller needs the real
    // JWT pipeline; here that is covered by asserting the [Authorize] attribute is present and that
    // nothing opts out with [AllowAnonymous].
    public class SubjectManagementAuthorizationTests
    {
        private static readonly Guid AdminId = Guid.NewGuid();

        private static ClaimsPrincipal Admin(string role = "Admin") =>
            new(new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, AdminId.ToString()),
                new Claim(ClaimTypes.Role, role),
            }, "test"));

        // One entry per public action on the controller. The reflection test below fails if an action
        // is added to the controller without being listed here, so a new endpoint can't skip the check.
        public static IEnumerable<object[]> Actions() => new (string Name, Func<SubjectManagementController, Task<IActionResult>> Call)[]
        {
            ("List",            c => c.List(new SubjectListQueryDto(), default)),
            ("Get",             c => c.Get(Guid.NewGuid(), default)),
            ("Create",          c => c.Create(new SubjectCreateRequestDto(), default)),
            ("Update",          c => c.Update(Guid.NewGuid(), new SubjectUpdateRequestDto(), default)),
            ("SetActive",       c => c.SetActive(Guid.NewGuid(), new SubjectSetActiveRequestDto(), default)),
            ("PreviewMerge",    c => c.PreviewMerge(new SubjectMergePreviewRequestDto(), default)),
            ("Merge",           c => c.Merge(new SubjectMergeRequestDto(), default)),
            ("PreviewReassign", c => c.PreviewReassign(new SubjectReassignPreviewRequestDto(), default)),
            ("Reassign",        c => c.Reassign(new SubjectReassignRequestDto(), default)),
            ("PreviewDelete",   c => c.PreviewDelete(Guid.NewGuid(), default)),
            ("Delete",          c => c.Delete(Guid.NewGuid(), true, "name", default)),
        }.Select(a => new object[] { a.Name, a.Call });

        private static (SubjectManagementController Controller, Mock<ISubjectManagementService> Subjects, Mock<IAdminPermissionService> Perms)
            Build(bool hasPermission, string role = "Admin")
        {
            var subjects = new Mock<ISubjectManagementService>(MockBehavior.Loose);
            var perms = new Mock<IAdminPermissionService>();
            perms.Setup(p => p.HasPermissionAsync(It.IsAny<ClaimsPrincipal>(), AdminPermission.ManageSubjects))
                 .ReturnsAsync(hasPermission);

            var controller = new SubjectManagementController(subjects.Object, perms.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = Admin(role) }
                }
            };
            return (controller, subjects, perms);
        }

        [Theory]
        [MemberData(nameof(Actions))]
        public async Task Admin_without_ManageSubjects_is_forbidden_and_service_is_never_called(
            string name, Func<SubjectManagementController, Task<IActionResult>> call)
        {
            var (controller, subjects, perms) = Build(hasPermission: false);

            var result = await call(controller);

            Assert.IsType<ForbidResult>(result);
            perms.Verify(p => p.HasPermissionAsync(It.IsAny<ClaimsPrincipal>(), AdminPermission.ManageSubjects), Times.Once, name);
            subjects.VerifyNoOtherCalls(); // nothing was read or mutated
        }

        [Theory]
        [MemberData(nameof(Actions))]
        public async Task Admin_with_ManageSubjects_reaches_the_service(
            string name, Func<SubjectManagementController, Task<IActionResult>> call)
        {
            var (controller, subjects, _) = Build(hasPermission: true);

            var result = await call(controller);

            Assert.IsType<OkObjectResult>(result);
            Assert.NotEmpty(subjects.Invocations); // the action actually ran: " + name
        }

        [Fact]
        public async Task Service_errors_keep_their_status_code_for_an_authorized_admin()
        {
            var (controller, subjects, _) = Build(hasPermission: true);
            subjects.Setup(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new SubjectOpException(409, "SUBJECT_IN_USE", "in use"));

            var result = await controller.Delete(Guid.NewGuid(), true, "x", default);

            var obj = Assert.IsType<ObjectResult>(result);
            Assert.Equal(409, obj.StatusCode);
        }

        // ---- unauthenticated / role gate (attribute level) ---------------------------------------

        [Fact]
        public void Controller_requires_authentication_and_an_admin_role_and_has_no_anonymous_escape()
        {
            var type = typeof(SubjectManagementController);

            var authorize = type.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();
            Assert.Contains(authorize, a => a.Roles == "Admin,SuperAdmin");

            Assert.Empty(type.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
            foreach (var m in PublicActions())
                Assert.Empty(m.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
        }

        [Fact]
        public void Every_public_action_is_covered_by_the_permission_tests()
        {
            var covered = Actions().Select(a => (string)a[0]).ToHashSet();
            var actual = PublicActions().Select(m => m.Name).ToHashSet();
            Assert.True(actual.SetEquals(covered),
                $"Actions not covered: {string.Join(", ", actual.Except(covered))}; stale entries: {string.Join(", ", covered.Except(actual))}");
        }

        private static IEnumerable<MethodInfo> PublicActions() =>
            typeof(SubjectManagementController)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any());

        // ---- the permission service: Super Admin exception + grant lookup ------------------------

        private static ScoramDbContext NewDb() =>
            new(new DbContextOptionsBuilder<ScoramDbContext>()
                .UseInMemoryDatabase("perm-" + Guid.NewGuid()).Options);

        [Fact]
        public async Task SuperAdmin_holds_ManageSubjects_implicitly_without_a_grant()
        {
            using var db = NewDb();
            var svc = new AdminPermissionService(db);
            Assert.True(await svc.HasPermissionAsync(Admin("SuperAdmin"), AdminPermission.ManageSubjects));
        }

        [Fact]
        public async Task Admin_is_allowed_only_with_an_explicit_ManageSubjects_grant()
        {
            using var db = NewDb();
            var svc = new AdminPermissionService(db);
            var principal = Admin("Admin");

            Assert.False(await svc.HasPermissionAsync(principal, AdminPermission.ManageSubjects)); // no grants

            db.AdminPermissionGrants.Add(new AdminPermissionGrant { AdminId = AdminId, Permission = AdminPermission.ManageQuestionBank });
            await db.SaveChangesAsync();
            Assert.False(await svc.HasPermissionAsync(principal, AdminPermission.ManageSubjects)); // other permission only

            db.AdminPermissionGrants.Add(new AdminPermissionGrant { AdminId = AdminId, Permission = AdminPermission.ManageSubjects });
            await db.SaveChangesAsync();
            Assert.True(await svc.HasPermissionAsync(principal, AdminPermission.ManageSubjects));
        }
    }
}
