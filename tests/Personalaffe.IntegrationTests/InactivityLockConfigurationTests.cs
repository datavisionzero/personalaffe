using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Personalaffe.Domain;
using Personalaffe.Infrastructure.Security;

namespace Personalaffe.IntegrationTests;

/// <summary>The owner-only configuration half of the browser inactivity lock.</summary>
[Collection(nameof(PostgresCollection))]
public sealed class InactivityLockConfigurationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_new_instance_starts_unlocked_with_a_five_minute_suggestion()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        var security = await client.GetFromJsonAsync<JsonNode>(
            "/api/security", TestContext.Current.CancellationToken);

        Assert.False(security!["inactivity_lock_enabled"]!.GetValue<bool>());
        Assert.Equal(Owner.DefaultInactivityLockMinutes, security["inactivity_minutes"]!.GetValue<int>());
        Assert.DoesNotContain("pin", security.ToJsonString(), StringComparison.OrdinalIgnoreCase);

        await using var context = AnInstance.ContextFor(instance.ConnectionString);
        var owner = await context.Owners.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(owner.InactivityLockPinHash);
        Assert.Equal(0, owner.InactivityLockVersion);
    }

    [Fact]
    public async Task A_leading_zero_pin_is_slowly_hashed_and_never_returned_or_logged()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);
        const string pin = "0012";

        using var changed = await PutAsync(client, new
        {
            enabled = true,
            pin,
            inactivity_minutes = 23,
            current_password = AnOwner.Secret,
        });

        Assert.True(
            changed.StatusCode == HttpStatusCode.NoContent,
            await changed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(pin, await changed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var security = await client.GetFromJsonAsync<JsonNode>(
            "/api/security", TestContext.Current.CancellationToken);
        Assert.True(security!["inactivity_lock_enabled"]!.GetValue<bool>());
        Assert.Equal(23, security["inactivity_minutes"]!.GetValue<int>());
        Assert.DoesNotContain("pin", security.ToJsonString(), StringComparison.OrdinalIgnoreCase);

        await using var context = AnInstance.ContextFor(instance.ConnectionString);
        var owner = await context.Owners.SingleAsync(TestContext.Current.CancellationToken);
        Assert.NotEqual(pin, owner.InactivityLockPinHash);
        Assert.True(await new Argon2idPasswordHasher().VerifyAsync(
            owner.InactivityLockPinHash!, pin, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(instance.Logged, line => line.Contains(pin, StringComparison.Ordinal));
        Assert.DoesNotContain(instance.Logged, line => line.Contains(AnOwner.Secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Duration_pin_change_and_disable_each_move_the_configuration_version()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        using (var enabled = await PutAsync(client, new
        {
            enabled = true,
            pin = "0123",
            inactivity_minutes = 5,
            current_password = AnOwner.Secret,
        }))
        {
            Assert.Equal(HttpStatusCode.NoContent, enabled.StatusCode);
        }

        string firstHash;
        await using (var context = AnInstance.ContextFor(instance.ConnectionString))
        {
            var owner = await context.Owners.SingleAsync(TestContext.Current.CancellationToken);
            firstHash = owner.InactivityLockPinHash!;
            Assert.Equal(1, owner.InactivityLockVersion);
        }

        using (var duration = await PutAsync(client, new
        {
            enabled = true,
            inactivity_minutes = 90,
            current_password = AnOwner.Secret,
        }))
        {
            Assert.Equal(HttpStatusCode.NoContent, duration.StatusCode);
        }

        await using (var context = AnInstance.ContextFor(instance.ConnectionString))
        {
            var owner = await context.Owners.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(firstHash, owner.InactivityLockPinHash);
            Assert.Equal(90, owner.InactivityLockMinutes);
            Assert.Equal(2, owner.InactivityLockVersion);
        }

        using (var pin = await PutAsync(client, new
        {
            enabled = true,
            pin = "987654",
            inactivity_minutes = 90,
            current_password = AnOwner.Secret,
        }))
        {
            Assert.Equal(HttpStatusCode.NoContent, pin.StatusCode);
        }

        using (var disabled = await PutAsync(client, new
        {
            enabled = false,
            current_password = AnOwner.Secret,
        }))
        {
            Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);
        }

        await using (var context = AnInstance.ContextFor(instance.ConnectionString))
        {
            var owner = await context.Owners.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Null(owner.InactivityLockPinHash);
            Assert.Equal(4, owner.InactivityLockVersion);
        }
    }

    [Theory]
    [InlineData("123", 5)]
    [InlineData("1234567", 5)]
    [InlineData("１２３４", 5)]
    [InlineData("12a4", 5)]
    [InlineData("1234", 0)]
    [InlineData("1234", 1441)]
    public async Task Invalid_activation_is_atomic(string pin, int minutes)
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        using var refused = await PutAsync(client, new
        {
            enabled = true,
            pin,
            inactivity_minutes = minutes,
            current_password = AnOwner.Secret,
        });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        await using var context = AnInstance.ContextFor(instance.ConnectionString);
        var owner = await context.Owners.SingleAsync(TestContext.Current.CancellationToken);
        Assert.False(owner.InactivityLockEnabled);
        Assert.Equal(Owner.DefaultInactivityLockMinutes, owner.InactivityLockMinutes);
        Assert.Equal(0, owner.InactivityLockVersion);
    }

    [Fact]
    public async Task Activation_requires_a_pin_a_duration_and_the_current_password()
    {
        await using var instance = await AnInstance.StartedAsync(postgres);
        using var client = await AnOwner.SignedInAsync(instance, TestContext.Current.CancellationToken);

        foreach (var body in new object[]
        {
            new { enabled = true, inactivity_minutes = (int?)5, current_password = AnOwner.Secret },
            new { enabled = true, pin = "1234", inactivity_minutes = (int?)null, current_password = AnOwner.Secret },
            new { enabled = true, pin = "1234", inactivity_minutes = (int?)5, current_password = "wrong" },
        })
        {
            using var refused = await PutAsync(client, body);
            Assert.True(
                refused.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden,
                $"Unexpected status {refused.StatusCode}.");
        }

        await using var context = AnInstance.ContextFor(instance.ConnectionString);
        var owner = await context.Owners.SingleAsync(TestContext.Current.CancellationToken);
        Assert.False(owner.InactivityLockEnabled);
        Assert.Equal(0, owner.InactivityLockVersion);
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, object body) =>
        client.PutAsJsonAsync(
            "/api/security/inactivity-lock", body, TestContext.Current.CancellationToken);
}
