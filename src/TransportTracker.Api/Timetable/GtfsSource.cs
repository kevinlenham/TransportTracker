using Microsoft.Extensions.Options;
using TransportTracker.Api.Tfnsw;

namespace TransportTracker.Api.Timetable;

/// <summary>A downloaded static GTFS zip. The stream is seekable and owned by the caller.</summary>
public record GtfsDownload(Stream Zip, DateTimeOffset? LastModified) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Zip.DisposeAsync();
}

public interface IGtfsSource
{
    Task<GtfsDownload> DownloadAsync(CancellationToken ct);
}

public class TfnswGtfsSource(IHttpClientFactory httpFactory, IOptions<TfnswOptions> options) : IGtfsSource
{
    public async Task<GtfsDownload> DownloadAsync(CancellationToken ct)
    {
        var http = httpFactory.CreateClient(TfnswOptions.HttpClientName);
        using var response = await http.GetAsync(options.Value.StaticGtfsPath, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        // The zip is ~25 MB, so it goes to a temp file rather than memory. The file is deleted on close.
        var file = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite,
            FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            await response.Content.CopyToAsync(file, ct);
            file.Position = 0;
            return new GtfsDownload(file, response.Content.Headers.LastModified);
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }
}
