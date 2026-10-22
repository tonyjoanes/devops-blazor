using DevOpsBlazor.AzureDevOps.Auth;
using DevOpsBlazor.AzureDevOps.Clients;
using DevOpsBlazor.AzureDevOps.Http;
using DevOpsBlazor.AzureDevOps.Links;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace DevOpsBlazor.AzureDevOps;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAzureDevOps(this IServiceCollection services, IConfiguration section)
    {
        services.Configure<AzureDevOpsOptions>(section);
        services.AddSingleton<IAdoTokenProvider, PatTokenProvider>();
        services.AddSingleton<AdoCache>();
        services.AddSingleton<AdoLinks>();
        services.AddTransient<AdoHttpHandler>();

        services.AddHttpClient<AdoHttp>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptionsMonitor<AzureDevOpsOptions>>().CurrentValue;
                // Left unset when misconfigured; AdoHttp then reports the problem on first use instead of failing page construction.
                if (options.TryGetOrganizationUri(out var organization)) client.BaseAddress = organization;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("DevOpsBlazor/1.0");
            })
            // Added first, so it runs once per logical request, outside the retries below.
            .AddHttpMessageHandler<AdoHttpHandler>()
            .AddStandardResilienceHandler(o =>
            {
                // Retries 5xx/408/429 with backoff and honours Retry-After.
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(90);
            });

        services.AddTransient<IConnectionClient, ConnectionClient>();
        services.AddTransient<IProjectClient, ProjectClient>();
        services.AddTransient<IBuildClient, BuildClient>();
        services.AddTransient<IGitClient, GitClient>();
        services.AddTransient<IPolicyClient, PolicyClient>();
        return services;
    }
}
