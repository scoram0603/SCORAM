using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ScoramAPI.Middleware
{
    // Marks an admin action as reachable even while Admin.MustChangePassword is true -- currently
    // only AdminAuthController.ChangePassword. Everything else stays blocked so a bootstrapped or
    // flagged SuperAdmin can't wander off and use the rest of the admin API on a password they were
    // explicitly required to change first (see SuperAdminBootstrapService and the
    // RemoveDefaultSuperAdminSeed migration for the two ways this flag gets set).
    [AttributeUsage(AttributeTargets.Method)]
    public class AllowWhilePasswordChangeRequiredAttribute : Attribute
    {
    }

    // Global filter (registered in Program.cs) -- runs for every controller action, but only does
    // anything for an authenticated admin/superadmin whose token carries mustChangePassword=true.
    // Reads the claim straight off the JWT rather than hitting the DB, same tradeoff as the Role
    // claim it sits next to (see TokenService.GenerateAdminToken's comment).
    public class MustChangePasswordFilter : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var user = context.HttpContext.User;
            var isAdmin = user.IsInRole("Admin") || user.IsInRole("SuperAdmin");
            var mustChange = user.FindFirst("mustChangePassword")?.Value == "True";

            if (isAdmin && mustChange)
            {
                var allowed = (context.ActionDescriptor as ControllerActionDescriptor)?
                    .MethodInfo
                    .GetCustomAttributes(typeof(AllowWhilePasswordChangeRequiredAttribute), inherit: true)
                    .Any() == true;

                if (!allowed)
                {
                    context.Result = new Microsoft.AspNetCore.Mvc.ObjectResult(new
                    {
                        message = "You must change your password before continuing.",
                        mustChangePassword = true
                    })
                    { StatusCode = StatusCodes.Status403Forbidden };
                    return;
                }
            }

            await next();
        }
    }
}
