using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ScoramAPI.Data;
using ScoramAPI.Models;
using ScoramAPI.Services;
using Xunit;

namespace ScoramAPI.Tests.Services
{
    public class SecurityStampServiceTests : IDisposable
    {
        private readonly ScoramDbContext _db;
        private readonly IDistributedCache _cache;
        private readonly SecurityStampService _service;

        public SecurityStampServiceTests()
        {
            var options = new DbContextOptionsBuilder<ScoramDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()) // fresh, isolated DB per test instance
                .Options;
            _db = new ScoramDbContext(options);

            // A real IDistributedCache (backed by IMemoryCache), not a mock -- this is the exact
            // class AddDistributedMemoryCache() registers in Program.cs when Redis isn't configured,
            // so these tests exercise the real cache-hit/miss code path, not a stand-in for it.
            _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

            _service = new SecurityStampService(_db, _cache);
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task IsValidAsync_ReturnsTrue_WhenTokenStampMatchesCurrentDbStamp()
        {
            var stamp = Guid.NewGuid();
            var user = new User { Id = Guid.NewGuid(), Username = "u1", Email = "u1@example.com", FullName = "U1", PasswordHash = "x", SecurityStamp = stamp };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            var result = await _service.IsValidAsync(user.Id, isAdmin: false, tokenStamp: stamp.ToString());

            Assert.True(result);
        }

        [Fact]
        public async Task IsValidAsync_ReturnsFalse_WhenTokenStampIsStale()
        {
            // The actual revocation mechanism: an access token issued before a password change/
            // logout carries the OLD stamp value, which no longer matches once SecurityStamp is
            // regenerated -- this is what makes that already-issued token stop working immediately.
            var originalStamp = Guid.NewGuid();
            var user = new User { Id = Guid.NewGuid(), Username = "u2", Email = "u2@example.com", FullName = "U2", PasswordHash = "x", SecurityStamp = originalStamp };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            // Simulate a password change: stamp regenerated, cache invalidated -- same two steps
            // AuthController.ChangePassword actually performs.
            user.SecurityStamp = Guid.NewGuid();
            await _db.SaveChangesAsync();
            _service.Invalidate(user.Id, isAdmin: false);

            var result = await _service.IsValidAsync(user.Id, isAdmin: false, tokenStamp: originalStamp.ToString());

            Assert.False(result);
        }

        [Fact]
        public async Task IsValidAsync_ReturnsFalse_ForNonExistentPrincipal()
        {
            // A token for a deleted/never-existed account -- must fail closed, not throw or default
            // to true.
            var result = await _service.IsValidAsync(Guid.NewGuid(), isAdmin: false, tokenStamp: Guid.NewGuid().ToString());

            Assert.False(result);
        }

        [Fact]
        public async Task IsValidAsync_DoesNotConfuseAdminAndStudentIdsThatCollide()
        {
            // isAdmin is part of the cache key/lookup, not just a filter on which table gets queried
            // -- a User.Id and an unrelated Admin.Id could theoretically be equal Guids (astronomically
            // unlikely in practice, but the isolation should hold regardless), and this must not let a
            // valid student stamp validate an admin token or vice versa.
            var sharedId = Guid.NewGuid();
            var studentStamp = Guid.NewGuid();
            var adminStamp = Guid.NewGuid();

            _db.Users.Add(new User { Id = sharedId, Username = "shared", Email = "shared@example.com", FullName = "Shared", PasswordHash = "x", SecurityStamp = studentStamp });
            _db.Admins.Add(new Admin { Id = sharedId, Email = "shared-admin@example.com", FullName = "Shared Admin", PasswordHash = "x", SecurityStamp = adminStamp });
            await _db.SaveChangesAsync();

            Assert.True(await _service.IsValidAsync(sharedId, isAdmin: false, tokenStamp: studentStamp.ToString()));
            Assert.False(await _service.IsValidAsync(sharedId, isAdmin: true, tokenStamp: studentStamp.ToString()));
            Assert.True(await _service.IsValidAsync(sharedId, isAdmin: true, tokenStamp: adminStamp.ToString()));
        }

        [Fact]
        public async Task IsValidAsync_UsesCachedValue_WithoutRequeryingDbOnSecondCall()
        {
            // Not directly observable from the public API alone, so this checks the documented
            // behavior indirectly: updating the DB stamp WITHOUT calling Invalidate() should still
            // validate against the now-stale CACHED value for a second call immediately after the
            // first -- proving the second call didn't silently re-hit the DB and pick up the change.
            var originalStamp = Guid.NewGuid();
            var user = new User { Id = Guid.NewGuid(), Username = "u3", Email = "u3@example.com", FullName = "U3", PasswordHash = "x", SecurityStamp = originalStamp };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            await _service.IsValidAsync(user.Id, isAdmin: false, tokenStamp: originalStamp.ToString()); // primes the cache

            user.SecurityStamp = Guid.NewGuid();
            await _db.SaveChangesAsync(); // DB changed, but Invalidate() deliberately NOT called

            var stillValid = await _service.IsValidAsync(user.Id, isAdmin: false, tokenStamp: originalStamp.ToString());

            Assert.True(stillValid); // cache TTL (60s) hasn't elapsed, so this reads the cached pre-change value
        }
    }
}
