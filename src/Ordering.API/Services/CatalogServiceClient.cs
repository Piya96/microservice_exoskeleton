using System.Net.Http.Json;

namespace Ordering.API.Services;

public record CatalogProductResponse(int Id, string Name, decimal Price, int Stock);

/// <summary>
/// The one synchronous, cross-service HTTP call in this whole skeleton --
/// used only as a cache-miss fallback when CatalogProjection has never
/// heard of a product yet (see OrderingDb.cs). This is precisely the call
/// pattern the field guide's Section 03 argues against for internal
/// service-to-service traffic, kept here on purpose as the fallback path
/// so the resilience policies below (Section 05's exact retry + circuit
/// breaker shape) have a real reason to exist in this repo, not just a
/// paragraph about them. See the README for why this wasn't deleted in
/// favor of "reject the order if the cache is cold" -- a cold cache is the
/// normal state for a just-added product, not an error condition.
/// </summary>
public class CatalogServiceClient(HttpClient httpClient)
{
    public async Task<CatalogProductResponse?> GetProductAsync(int productId, CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync($"/products/{productId}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CatalogProductResponse>(cancellationToken: ct);
    }
}
