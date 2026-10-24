using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ScoramAPI.Controllers;
using Xunit;

namespace ScoramAPI.Tests.Controllers
{
    // SEC-001 follow-up. The existing SubjectManagementAuthorizationTests prove the controller's
    // ManageSubjects gate, but only ASSERT that [Authorize] is present for the "unauthenticated" case.
    // These tests evaluate the controllers' real [Authorize(Roles=...)] metadata through ASP.NET Core's
    // actual authorization service, so the outcomes for an anonymous caller, a student, an admin and a
    // super admin are proven by the framework rather than by reading an attribute.
    //
    // Scope: the ROLE gate (who may reach the controller at all). The finer ManageSubjects gate is tested
    // in SubjectManagementAuthorizationTests / ExamTaxonomyAuthorizationTests.
    public class AdminSubjectAuthorizationPipelineTests
    {
        public static IEnumerable<object[]> Controllers() => new[]
        {
            new object[] { typeof(SubjectManagementController) },
            new object[] { typeof(ExamTaxonomyController) },
        };

        private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity()); // no authentication type = unauthenticated

        private static ClaimsPrincipal WithRole(string role) =>
            new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, authenticationType: "test"));

        private static async Task<bool> IsAllowedAsync(Type controller, ClaimsPrincipal user)
        {
            var services = new ServiceCollection().AddLogging().AddAuthorization().BuildServiceProvider();
            var authz = services.GetRequiredService<IAuthorizationService>();
            var provider = new DefaultAuthorizationPolicyProvider(services.GetRequiredService<IOptions<AuthorizationOptions>>());

            var authorizeData = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Cast<IAuthorizeData>().ToList();
            Assert.NotEmpty(authorizeData); // a controller with no [Authorize] would be anonymous -- fail loudly

            var policy = await AuthorizationPolicy.CombineAsync(provider, authorizeData);
            Assert.NotNull(policy);

            var result = await authz.AuthorizeAsync(user, resource: null, policy!.Requirements);
            return result.Succeeded;
        }

        [Theory]
        [MemberData(nameof(Controllers))]
        public async Task Unauthenticated_caller_is_rejected(Type controller) =>
            Assert.False(await IsAllowedAsync(controller, Anonymous()));

        [Theory]
        [MemberData(nameof(Controllers))]
        public async Task Student_role_is_rejected(Type controller) =>
            Assert.False(await IsAllowedAsync(controller, WithRole("Student")));

        [Theory]
        [MemberData(nameof(Controllers))]
        public async Task Admin_role_reaches_the_controller_where_the_ManageSubjects_gate_then_applies(Type controller) =>
            Assert.True(await IsAllowedAsync(controller, WithRole("Admin")));

        [Theory]
        [MemberData(nameof(Controllers))]
        public async Task SuperAdmin_role_reaches_the_controller(Type controller) =>
            Assert.True(await IsAllowedAsync(controller, WithRole("SuperAdmin")));

        [Theory]
        [MemberData(nameof(Controllers))]
        public void No_action_opts_out_of_authorization(Type controller)
        {
            Assert.Empty(controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
            var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>(inherit: true).Any());
            foreach (var m in actions)
                Assert.Empty(m.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true));
        }
    }
}
