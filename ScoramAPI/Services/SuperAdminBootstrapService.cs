using Microsoft.EntityFrameworkCore;
using ScoramAPI.Data;
using ScoramAPI.Enums;
using ScoramAPI.Models;

namespace ScoramAPI.Services
{
    // Bootstraps the very first SuperAdmin on a brand-new database. Replaces the old
    // ScoramDbContext HasData seed, which always created the same account with the same
    // password -- see that file's comment, and Migrations/20260916000000_RemoveDefaultSuperAdminSeed
    // for how an already-deployed database with the old seeded row is handled.
    //
    // Called once at startup (see Program.cs, right before app.Run()). No-ops immediately once any
    // SuperAdmin exists, so it's safe to call on every restart.
    public static class SuperAdminBootstrapService
    {
        public static async Task RunAsync(IServiceProvider services, ILogger logger)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ScoramDbContext>();

            if (await db.Admins.AnyAsync(a => a.Role == AdminRole.SuperAdmin))
                return;

            var email = Environment.GetEnvironmentVariable("SUPERADMIN_EMAIL");
            var password = Environment.GetEnvironmentVariable("SUPERADMIN_PASSWORD");

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                logger.LogWarning(
                    "No SuperAdmin exists yet, and SUPERADMIN_EMAIL/SUPERADMIN_PASSWORD aren't both " +
                    "set -- there is currently no way to log in as an admin. Set both environment " +
                    "variables and restart the app, or create the first SuperAdmin manually (insert " +
                    "an Admins row with a BCrypt-hashed password and Role='SuperAdmin').");
                return;
            }

            // A weak or trivially-guessable value here would just recreate the exact problem this
            // bootstrap flow exists to fix, so refuse rather than silently accepting one.
            if (password.Length < 12)
            {
                logger.LogWarning(
                    "SUPERADMIN_PASSWORD is shorter than 12 characters -- refusing to bootstrap the " +
                    "first SuperAdmin with a weak password. Set a stronger SUPERADMIN_PASSWORD and " +
                    "restart the app.");
                return;
            }

            db.Admins.Add(new Admin
            {
                FullName = "Super Admin",
                Email = email.Trim(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                Role = AdminRole.SuperAdmin,
                IsActive = true,
                MustChangePassword = true,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            logger.LogWarning(
                "Bootstrapped the first SuperAdmin ({Email}) from SUPERADMIN_EMAIL/SUPERADMIN_PASSWORD. " +
                "They'll be required to change their password on first login.", email.Trim());
        }
    }
}
