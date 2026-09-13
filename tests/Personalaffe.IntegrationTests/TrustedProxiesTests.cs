using System.Net;
using Personalaffe.Api.Http;

namespace Personalaffe.IntegrationTests;

/// <summary>
/// Who may speak for the caller. <c>X-Forwarded-For</c> is a header any client
/// can write, so nothing is believed until an operator names the proxy that is
/// allowed to write it.
/// </summary>
public sealed class TrustedProxiesTests
{
    [Fact]
    public void Unset_means_nobody_and_the_instance_reads_the_socket()
    {
        Assert.False(TrustedProxies.FromVariable(null).Configured);
        Assert.False(TrustedProxies.FromVariable("   ").Configured);
    }

    [Fact]
    public void A_named_proxy_is_the_only_peer_whose_header_counts()
    {
        var trusted = TrustedProxies.FromVariable("10.0.0.7, 192.168.0.0/16");

        Assert.True(trusted.Configured);

        var options = trusted.Options();

        Assert.Equal([IPAddress.Parse("10.0.0.7")], options.KnownProxies);
        Assert.Single(options.KnownIPNetworks);

        // The framework's defaults are loopback, and leaving them in would widen
        // what the operator named to include anything on the machine.
        Assert.DoesNotContain(IPAddress.Loopback, options.KnownProxies);
    }

    [Fact]
    public void All_is_how_an_operator_says_the_proxy_is_the_only_thing_that_can_reach_this()
    {
        var options = TrustedProxies.FromVariable("all").Options();

        // Empty lists are how the middleware is told not to check the peer at
        // all. It is the right answer for a container on a network that is
        // renumbered on every start, and the wrong one for anything reachable
        // from elsewhere — which is why it has to be said out loud.
        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
    }

    [Fact]
    public void One_hop_and_two_headers_and_nothing_else()
    {
        var options = TrustedProxies.FromVariable("10.0.0.7").Options();

        Assert.Equal(1, options.ForwardLimit);
        Assert.Equal(
            Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
            | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
            options.ForwardedHeaders);
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("10.0.0.0/not-a-mask")]
    [InlineData("10.0.0.7, nonsense")]
    public void A_value_that_is_neither_an_address_nor_a_network_stops_the_start(string value)
    {
        var refusal = Assert.Throws<ArgumentException>(() => TrustedProxies.FromVariable(value));

        Assert.Contains(TrustedProxies.Variable, refusal.Message, StringComparison.Ordinal);
    }
}
