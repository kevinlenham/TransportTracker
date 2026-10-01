using Microsoft.Extensions.Options;
using TransitRealtime;
using TransportTracker.Api.Tfnsw;

namespace TransportTracker.Api.Poller;

public interface IRealtimeFeed
{
    Task<FeedMessage> FetchAsync(CancellationToken ct);
}

public class TfnswRealtimeFeed(IHttpClientFactory httpFactory, IOptions<TfnswOptions> options) : IRealtimeFeed
{
    public async Task<FeedMessage> FetchAsync(CancellationToken ct)
    {
        var http = httpFactory.CreateClient(TfnswOptions.HttpClientName);
        var bytes = await http.GetByteArrayAsync(options.Value.RealtimeTripUpdatesPath, ct);
        return FeedMessage.Parser.ParseFrom(bytes);
    }
}
