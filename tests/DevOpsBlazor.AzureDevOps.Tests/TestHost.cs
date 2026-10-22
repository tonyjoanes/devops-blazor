using DevOpsBlazor.AzureDevOps.Auth;
using DevOpsBlazor.AzureDevOps.Http;
using Microsoft.Extensions.Options;
using RichardSzalay.MockHttp;

namespace DevOpsBlazor.AzureDevOps.Tests;

/// <summary>Builds real clients over a mocked HTTP layer, wired the same way as in the app (handler included).</summary>
internal sealed class TestHost
{
    public const string Org = "https://dev.azure.com/contoso/";

    public MockHttpMessageHandler Http { get; } = new();
    public AzureDevOpsOptions Options { get; } = new() { Organization = "https://dev.azure.com/contoso", Pat = "secret-pat" };
    public AdoCache Cache { get; } = new();

    public IOptionsMonitor<AzureDevOpsOptions> Monitor => new StaticMonitor<AzureDevOpsOptions>(Options);

    public AdoHttp CreateHttp(Uri? baseAddress = null)
    {
        var handler = new AdoHttpHandler(new PatTokenProvider(Monitor)) { InnerHandler = Http };
        var client = new HttpClient(handler) { BaseAddress = baseAddress ?? new Uri(Org) };
        return new AdoHttp(client);
    }

    public static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
