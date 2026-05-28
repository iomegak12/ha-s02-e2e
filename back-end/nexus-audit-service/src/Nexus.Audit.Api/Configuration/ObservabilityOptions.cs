namespace Nexus.Audit.Api.Configuration;

/// <summary>Observability configuration. Bound from <c>Observability</c>.</summary>
public sealed class ObservabilityOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Observability";

    /// <summary>OTLP exporter settings.</summary>
    public OtlpOptions Otlp { get; init; } = new();

    /// <summary>Prometheus scrape-endpoint settings.</summary>
    public PrometheusOptions Prometheus { get; init; } = new();
}

/// <summary>OTLP exporter knobs.</summary>
public sealed class OtlpOptions
{
    /// <summary>Endpoint URL. Empty disables OTLP export.</summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>Transport protocol: <c>Grpc</c> or <c>HttpProtobuf</c>.</summary>
    public string Protocol { get; init; } = "Grpc";
}

/// <summary>Prometheus exposition endpoint knobs.</summary>
public sealed class PrometheusOptions
{
    /// <summary>Whether to expose <c>GET /metrics</c>.</summary>
    public bool Enabled { get; init; } = true;
}
