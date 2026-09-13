using Personalaffe.Application.Acts;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.UnitTests;

/// <summary>
/// Sign-in against substituted ports: one answer for every way of being wrong,
/// and the same work done on the way to each.
/// </summary>
public sealed class SignInTests
{
    private const string Secret = "correct horse battery staple";

    private static readonly DateTimeOffset Noon = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly OneOwner _owners = new();
    private readonly KeptSessions _sessions = new();
    private readonly CountingHasher _passwords = new();

    private SignIn Act => new(_owners, _sessions, _passwords, new Fixed(Noon));

    [Fact]
    public async Task The_owner_with_their_password_gets_a_session()
    {
        _owners.Stored = Owner.Claim("owner@example.com", CountingHasher.Encode(Secret), Noon);

        var issued = await Act.ExecuteAsync(
            "owner@example.com", Secret, "a-browser/1.0", TestContext.Current.CancellationToken);

        Assert.NotNull(issued);
        Assert.Same(_sessions.Added, issued.Value.Session);
        Assert.Equal(_owners.Stored.Id, issued.Value.Session.OwnerId);
    }

    [Theory]
    [InlineData("owner@example.com", "the wrong password")]
    [InlineData("somebody-else@example.com", Secret)]
    [InlineData("not an address at all", Secret)]
    [InlineData(null, null)]
    public async Task Everything_else_is_the_same_nothing(string? email, string? password)
    {
        _owners.Stored = Owner.Claim("owner@example.com", CountingHasher.Encode(Secret), Noon);

        Assert.Null(await Act.ExecuteAsync(
            email, password, null, TestContext.Current.CancellationToken));
        Assert.Null(_sessions.Added);
    }

    [Fact]
    public async Task An_instance_with_no_owner_refuses_the_same_way_and_costs_the_same()
    {
        Assert.Null(await Act.ExecuteAsync(
            "owner@example.com", Secret, null, TestContext.Current.CancellationToken));

        // The password is verified against something that is not a password, so
        // that "no owner" and "wrong password" do not differ by the sixty-odd
        // milliseconds an Argon2id takes.
        Assert.Equal(1, _passwords.Verified);
    }

    [Fact]
    public async Task An_address_that_is_not_the_owners_is_still_a_verification()
    {
        _owners.Stored = Owner.Claim("owner@example.com", CountingHasher.Encode(Secret), Noon);

        Assert.Null(await Act.ExecuteAsync(
            "somebody-else@example.com", Secret, null, TestContext.Current.CancellationToken));

        Assert.Equal(1, _passwords.Verified);
    }

    private sealed class OneOwner : IOwners
    {
        public Owner? Stored { get; set; }

        public Task<bool> ExistsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Stored is not null);

        public Task<Owner?> FindAsync(CancellationToken cancellationToken) => Task.FromResult(Stored);

        public Task AddAsync(Owner owner, CancellationToken cancellationToken)
        {
            Stored = owner;
            return Task.CompletedTask;
        }

        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class KeptSessions : IBrowserSessions
    {
        public BrowserSession? Added { get; private set; }

        public Task AddAsync(BrowserSession session, CancellationToken cancellationToken)
        {
            Added = session;
            return Task.CompletedTask;
        }

        public Task<BrowserSession?> AdmitAsync(
            byte[] secretHash, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<BrowserSession?>(null);

        public Task RevokeAsync(Guid id, Guid ownerId, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RevokeAllAsync(
            Guid ownerId, Guid? except, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class CountingHasher : IPasswordHasher
    {
        public int Verified { get; private set; }

        public static string Encode(string password) => $"$hashed${password}";

        public Task<string> HashAsync(string password, CancellationToken cancellationToken) =>
            Task.FromResult(Encode(password));

        public Task<bool> VerifyAsync(string encodedHash, string password, CancellationToken cancellationToken)
        {
            Verified++;
            return Task.FromResult(encodedHash == Encode(password));
        }
    }

    private sealed class Fixed(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
