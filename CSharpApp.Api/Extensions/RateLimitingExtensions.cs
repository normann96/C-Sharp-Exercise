using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using CSharpApp.Core.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace CSharpApp.Api.Extensions;

public static class RateLimitingExtensions
{
    public const string PolicyName = "per-client";

    private const string RejectionTitle = "Too many requests.";
    private const string RejectionType = "https://tools.ietf.org/html/rfc6585#section-4";
    private const string UnknownClient = "unknown";

    public static IServiceCollection AddConfiguredRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(limiter =>
        {
            // Stated rather than inherited: the framework default is 503, and the contract this service
            // publishes is 429.
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.AddPolicy(PolicyName, context =>
            {
                var settings = context.RequestServices.GetRequiredService<IOptions<RateLimitingSettings>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(ClientPartition(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.PermitLimit,
                    Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                    // Queuing would make a caller who is already over budget wait instead of learning it now.
                    QueueLimit = 0,
                });
            });

            limiter.OnRejected = RejectAsync;
        });

        return services;
    }

    /// <summary>
    /// One allowance per caller rather than one for the whole service, so a single noisy client cannot spend
    /// everyone else's. Anything that rewrites the source address puts every caller in one partition: a reverse
    /// proxy, a load balancer, and the published port of this repository's own compose file. Such a deployment
    /// has to configure forwarded headers before this key distinguishes anybody.
    /// </summary>
    internal static string ClientPartition(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null)
        {
            return UnknownClient;
        }

        // A dual-stack listener reports "::ffff:127.0.0.1" where a v4-only listener reports "127.0.0.1";
        // without this the key silently changes shape with the listener configuration.
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6 ? NetworkPrefix(address) : address.ToString();
    }

    // A routed IPv6 allocation is a /64, so one address is not one caller. Counting per address would hand a
    // single caller 2^64 allowances and grow the partition table with every one it used.
    private static string NetworkPrefix(IPAddress address)
    {
        Span<byte> bytes = stackalloc byte[16];
        address.TryWriteBytes(bytes, out _);
        bytes[8..].Clear();
        return $"{new IPAddress(bytes)}/64";
    }

    private static async ValueTask RejectAsync(OnRejectedContext context, CancellationToken ct)
    {
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        // The same problem details contract as every other failure, so no client special-cases this one.
        var problemDetails = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        var written = await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context.HttpContext,
            ProblemDetails = new ProblemDetails
            {
                // Not in the framework's defaults table, unlike every other status this service emits.
                Type = RejectionType,
                Status = StatusCodes.Status429TooManyRequests,
                Title = RejectionTitle,
                Instance = context.HttpContext.Request.Path,
            },
        });

        if (!written)
        {
            // No writer accepts the caller's Accept header; the status still answers, with the title as plain text.
            response.ContentType = "text/plain; charset=utf-8";
            await response.WriteAsync(RejectionTitle, ct);
        }
    }
}
