namespace Nexus.Branches.Api.Configuration;

public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    public OtlpOptions Otlp { get; init; } = new();
    public PrometheusOptions Prometheus { get; init; } = new();
}

public sealed class OtlpOptions
{
    public string Endpoint { get; init; } = string.Empty;
    public string Protocol { get; init; } = "Grpc";
}

public sealed class PrometheusOptions
{
    public bool Enabled { get; init; } = true;
}
