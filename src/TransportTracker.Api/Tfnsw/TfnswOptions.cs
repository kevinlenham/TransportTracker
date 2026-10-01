namespace TransportTracker.Api.Tfnsw;

public class TfnswOptions
{
    public const string SectionName = "Tfnsw";
    public const string HttpClientName = "tfnsw";

    public string BaseUrl { get; set; } = "https://api.transport.nsw.gov.au/";
    /// <summary>Kept server-side only: user-secrets locally, App Service config or Key Vault in Azure.</summary>
    public string ApiKey { get; set; } = "";
    public string StaticGtfsPath { get; set; } = "v1/gtfs/schedule/sydneytrains";
    public string RealtimeTripUpdatesPath { get; set; } = "v2/gtfs/realtime/sydneytrains";
}
