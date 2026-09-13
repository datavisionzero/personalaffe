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
    private readonly SpentCodes _codes = new();
    private readonly CountingHasher _passwords = new();

    private SignIn Act => new(_owners, _sessions, _codes, _passwords, new Fixed(Noon));

    [Fact]
    public async Task The_owner_with_their_password_gets_a_session()
    {
        _owners.Stored = Owner.Claim("owner@example.com", CountingHasher.Encode(Secret), Noon);

        var issued = await Act.ExecuteAsync(
            "owner@example.com", Secret, null, "a-browser/1.0", TestContext.Current.CancellationToken);

        Assert.Equal(SignInOutcome.SignedIn, issued.Outcome);
        Assert.Same(_sessions.Added, issued.Session);
        Assert.Equal(_owners.Stored.Id, issued.Session!.OwnerId);
    }

    [Theory]
    [InlineData("owner@example.com", "the wrong password")]
    [InlineData("somebody-else@example.com", Secret)]
    [InlineData("not an address at all", Secret)]
    [InlineData(null, null)]
    public async Task Everything_else_is_the_same_nothing(string? email, string? password)
    {
        _owners.Stored = Owner.Claim("owner@example.com", CountingHasher.Encode(Secret), Noon);

        var refused = await Act.ExecuteAsync(
            email, password, null, null, TestContext.Current.CancellationToken);

        Assert.Equal(SignInOutcome.Wrong, refused.Outcome);
        Assert.Null(_sessions.Added);
    }

    [Fact]
    public async Task An_instance_with_no_owner_refuses_the_same_way_and_costs_the_same()
    {
        var refused = await Act.ExecuteAsync(
            "owner@example.com", Secret, null, null, TestContext.Current.CancellationToken);

        Assert.Equal(SignInOutcome.Wrong, refused.Outcome);

        // The password is verified against something that is not a password, so
        // that "no owner" and "wrong password" do not differ by the sixty-odd
        // milliseconds an Argon2id takes.
        Assert.Equal(1, _passwords.Verified);
    }

    [Fact]
    public async Task An_address_that_is_not_the_owners_is_still_a_verification()
    {
        _owners.Stored = Owner.Claim("owner@example.com", CountingHasher.Encode(Secret), Noon);

        var refused = await Act.ExecuteAsync(
            "somebody-else@example.com", Secret, null, null, TestContext.Current.CancellationToken);

        Assert.Equal(SignInOutcome.Wrong, refused.Outcome);
        Assert.Equal(1, _passwords.Verified);
    }

    [Fact]
    public async Task An_enrolled_authenticator_is_asked_for_after_the_password()
    {
        var enrolled = Enrolled();

        var asked = await Act.ExecuteAsync(
            "owner@example.com", Secret, null, null, TestContext.Current.CancellationToken);

        Assert.Equal(SignInOutcome.SecondFactorRequired, asked.Outcome);
        Assert.Null(_sessions.Added);

        var signedIn = await Act.ExecuteAsync(
            "owner@example.com",
            Secret,
            Totp.CodeFor(enrolled, Totp.StepAt(Noon)),
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(SignInOutcome.SignedIn, signedIn.Outcome);
    }

    [Fact]
    public async Task A_code_that_has_been_used_does_not_work_a_second_time()
    {
        var enrolled = Enrolled();
        var code = Totp.CodeFor(enrolled, Totp.StepAt(Noon));

        var first = await Act.ExecuteAsync(
            "owner@example.com", Secret, code, null, TestContext.Current.CancellationToken);
        Assert.Equal(SignInOutcome.SignedIn, first.Outcome);

        // Thirty seconds is long enough for somebody who read it over a
        // shoulder to type it in.
        var again = await Act.ExecuteAsync(
            "owner@example.com", Secret, code, null, TestContext.Current.CancellationToken);
        Assert.Equal(SignInOutcome.Wrong, again.Outcome);
    }

    [Fact]
    public async Task A_recovery_code_is_the_other_thing_that_field_takes()
    {
        Enrolled();
        _codes.Unspent.Add(RecoveryCode.Hash("ABCDE-FGHJK"));

        var signedIn = await Act.ExecuteAsync(
            "owner@example.com", Secret, "abcde-fghjk", null, TestContext.Current.CancellationToken);
        Assert.Equal(SignInOutcome.SignedIn, signedIn.Outcome);

        // Once, and once only.
        var again = await Act.ExecuteAsync(
            "owner@example.com", Secret, "ABCDE-FGHJK", null, TestContext.Current.CancellationToken);
        Assert.Equal(SignInOutcome.Wrong, again.Outcome);
    }

    private string Enrolled()
    {
        var owner = Owner.Claim("owner@example.com", CountingHasher.Encode(Secret), Noon);
        var secret = Totp.GenerateSecret();

        owner.OfferSecondFactor(secret, Noon);
        owner.ConfirmSecondFactor(Totp.StepAt(Noon) - 1, Noon);

        _owners.Stored = owner;
        return secret;
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

        public Task<IReadOnlyList<BrowserSession>> ListAsync(
            Guid ownerId, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BrowserSession>>([]);

        public Task RevokeAsync(Guid id, Guid ownerId, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task RevokeAllAsync(
            Guid ownerId, Guid? except, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class SpentCodes : IRecoveryCodes
    {
        public List<byte[]> Unspent { get; } = [];

        public Task ReplaceAsync(
            Guid ownerId, IReadOnlyList<RecoveryCode> codes, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<int> RemainingAsync(Guid ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Unspent.Count);

        public Task<bool> ConsumeAsync(
            Guid ownerId, byte[] codeHash, DateTimeOffset at, CancellationToken cancellationToken)
        {
            var held = Unspent.FirstOrDefault(candidate => candidate.SequenceEqual(codeHash));

            return Task.FromResult(held is not null && Unspent.Remove(held));
        }

        public Task ClearAsync(Guid ownerId, CancellationToken cancellationToken)
        {
            Unspent.Clear();
            return Task.CompletedTask;
        }
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
