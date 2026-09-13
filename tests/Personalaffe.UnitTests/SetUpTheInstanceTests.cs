using Personalaffe.Application.Acts;
using Personalaffe.Application.Ports;
using Personalaffe.Domain;

namespace Personalaffe.UnitTests;

/// <summary>
/// The one-time setup, against substituted ports: what it refuses, in what
/// order, and what it does not spend before refusing.
/// </summary>
public sealed class SetUpTheInstanceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    private readonly StoredOwner _owners = new();
    private readonly CountingHasher _passwords = new();

    private SetUpTheInstance Act => new(_owners, _passwords, new Fixed(Noon));

    [Fact]
    public async Task An_unclaimed_instance_acquires_its_owner()
    {
        var owner = await Act.ExecuteAsync(
            " Owner@Example.com ", "correct horse battery staple", TestContext.Current.CancellationToken);

        Assert.Equal("Owner@Example.com", owner.Email);
        Assert.Equal("owner@example.com", owner.NormalizedEmail);
        Assert.Equal(Noon, owner.CreatedAt);
        Assert.Same(owner, _owners.Stored);
    }

    [Fact]
    public async Task The_password_is_stored_as_its_hash_and_not_as_itself()
    {
        var owner = await Act.ExecuteAsync(
            "owner@example.com", "correct horse battery staple", TestContext.Current.CancellationToken);

        Assert.DoesNotContain("correct horse", owner.PasswordHash, StringComparison.Ordinal);
        Assert.Equal(1, _passwords.Hashed);
    }

    [Fact]
    public async Task A_second_setup_is_a_conflict_and_not_a_refusal_about_the_caller()
    {
        await Act.ExecuteAsync("owner@example.com", "correct horse battery staple", TestContext.Current.CancellationToken);

        var refusal = await Assert.ThrowsAsync<Refusal>(() => Act.ExecuteAsync(
            "someone@example.com", "another long enough password", TestContext.Current.CancellationToken));

        Assert.Equal(RefusalCode.Conflict, refusal.Code);
        Assert.Equal("owner@example.com", _owners.Stored!.NormalizedEmail);
    }

    [Fact]
    public async Task Two_bad_fields_are_two_lines_in_one_refusal()
    {
        var refusal = await Assert.ThrowsAsync<Refusal>(
            () => Act.ExecuteAsync("not an address", "short", TestContext.Current.CancellationToken));

        Assert.Equal(RefusalCode.Validation, refusal.Code);

        var errors = Assert.IsType<Dictionary<string, string[]>>(refusal.Extensions["errors"], exactMatch: false);
        Assert.Equal(["email", "password"], errors.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_password_that_is_refused_is_never_hashed()
    {
        await Assert.ThrowsAsync<Refusal>(
            () => Act.ExecuteAsync("owner@example.com", "short", TestContext.Current.CancellationToken));

        // Argon2id is deliberately expensive; a field that is wrong on its face
        // must not be able to buy that with a request.
        Assert.Equal(0, _passwords.Hashed);
        Assert.Null(_owners.Stored);
    }

    private sealed class StoredOwner : IOwners
    {
        public Owner? Stored { get; private set; }

        public Task<bool> ExistsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Stored is not null);

        public Task<Owner?> FindAsync(CancellationToken cancellationToken) => Task.FromResult(Stored);

        public Task AddAsync(Owner owner, CancellationToken cancellationToken)
        {
            if (Stored is not null)
            {
                throw SetUpTheInstance.AlreadySetUp();
            }

            Stored = owner;
            return Task.CompletedTask;
        }

        public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CountingHasher : IPasswordHasher
    {
        public int Hashed { get; private set; }

        public Task<string> HashAsync(string password, CancellationToken cancellationToken)
        {
            Hashed++;
            return Task.FromResult($"$hashed${password.Length}");
        }

        public Task<bool> VerifyAsync(string encodedHash, string password, CancellationToken cancellationToken) =>
            Task.FromResult(encodedHash == $"$hashed${password.Length}");
    }

    private sealed class Fixed(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
