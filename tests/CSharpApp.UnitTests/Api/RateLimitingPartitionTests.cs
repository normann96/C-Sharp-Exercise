using System.Net;
using CSharpApp.Api.Extensions;
using Microsoft.AspNetCore.Http;

namespace CSharpApp.UnitTests.Api;

public class RateLimitingPartitionTests
{
    private static HttpContext RequestFrom(string? address)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = address is null ? null : IPAddress.Parse(address);
        return context;
    }

    [Fact]
    public void AnAddresslessRequest_SharesOneKey()
    {
        // Arrange: no transport reports an address over a unix socket
        var context = RequestFrom(null);

        // Act
        var key = RateLimitingExtensions.ClientPartition(context);

        // Assert
        Assert.Equal("unknown", key);
    }

    [Fact]
    public void IPv4_IsTheKeyItself()
    {
        // Arrange
        var context = RequestFrom("203.0.113.7");

        // Act
        var key = RateLimitingExtensions.ClientPartition(context);

        // Assert
        Assert.Equal("203.0.113.7", key);
    }

    [Fact]
    public void AMappedAddress_KeysTheSameAsThePlainOne()
    {
        // Arrange: a dual-stack listener reports the mapped form for the same caller
        var mapped = RequestFrom("::ffff:203.0.113.7");
        var plain = RequestFrom("203.0.113.7");

        // Act
        var mappedKey = RateLimitingExtensions.ClientPartition(mapped);

        // Assert
        Assert.Equal(RateLimitingExtensions.ClientPartition(plain), mappedKey);
    }

    [Fact]
    public void TwoAddressesInOneAllocation_ShareOneAllowance()
    {
        // Arrange: a routed IPv6 allocation is a /64, so these are one caller
        var first = RequestFrom("2001:db8:1:2::1");
        var second = RequestFrom("2001:db8:1:2:ffff:ffff:ffff:ffff");

        // Act
        var firstKey = RateLimitingExtensions.ClientPartition(first);

        // Assert
        Assert.Equal(RateLimitingExtensions.ClientPartition(second), firstKey);
        Assert.EndsWith("/64", firstKey);
    }

    [Fact]
    public void TwoAllocations_AreCountedApart()
    {
        // Arrange
        var first = RequestFrom("2001:db8:1:2::1");
        var second = RequestFrom("2001:db8:1:3::1");

        // Act
        var firstKey = RateLimitingExtensions.ClientPartition(first);

        // Assert
        Assert.NotEqual(RateLimitingExtensions.ClientPartition(second), firstKey);
    }
}
