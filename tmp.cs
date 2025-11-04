using System.Text.Json;
using System.Text.Json.Serialization;
using GEGCRM.Gaming.PerformanceMonitoring.Models;
using OpenSearch.Client;

namespace GEGCRM.Gaming.PerformanceMonitoring.Services;

public sealed class ElasticSearchGalaxyApiServices(IOpenSearchClient client, string indexName)
{
    /// <summary>
    /// Asynchronously queries OpenSearch for API access logs based on multiple criteria and extracts the request body.
    /// </summary>
    /// <param name="queryStartTime">The start time for the query range. Only logs at or after this time will be included.</param>
    /// <returns>An enumerable collection of UpdateRatingRequest objects deserialized from the log's body.</returns>
    public async Task<IEnumerable<UpdateRatingRequest>> QueryAndExtractBodyAsync(DateTime queryStartTime)
    {
        var response = await client.SearchAsync<ApiAccessLog>(s => s
            .Index(indexName)
            .Size(5000)
            .Query(q => q
                .Bool(b => b
                    .Must(
                        // 1. Original condition: Service must be "Rating"
                        m => m.Match(mt => mt.Field("Service").Query("Rating")),

                        // 2. Original condition (duplicate removed): Endpoint must match the update path
                        m => m.Match(mt => mt.Field("Endpoint").Query("POST /api/rating/ratingUpdate")),

                        // 3. ADDED: Time range condition to filter logs after a specific time.
                        //    NOTE: Assumes your time field in OpenSearch is named "@timestamp". 
                        //    Please change "@timestamp" if your field has a different name (e.g., "Timestamp", "CreationDate").
                        m => m.DateRange(r => r
                            .Field("@timestamp")
                            .GreaterThanOrEquals(queryStartTime)),

                        // 4. ADDED: Condition for open:"Update", translated to a Match query.
                        m => m.Match(mt => mt.Field("open").Query("Update"))
                    )
                )
            )
            // Only retrieve the "Body" field from the source to improve performance
            .Source(so => so.Includes(f => f.Fields("Body")))
        );

        var results = new List<UpdateRatingRequest>();

        if (response.IsValid)
        {
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            };

            foreach (var log in response.Hits)
            {
                // Assuming log.Source.Body is a JSON string
                if (log.Source?.Body != null)
                {
                    try
                    {
                        var updateRequest = JsonSerializer.Deserialize<UpdateRatingRequest>(log.Source.Body, jsonOptions);
                        if (updateRequest != null)
                        {
                            results.Add(updateRequest);
                        }
                    }
                    catch (JsonException ex)
                    {
                        // Handle or log potential deserialization errors if the body format is incorrect
                        Console.WriteLine($"Failed to deserialize log body: {ex.Message}");
                    }
                }
            }
        }
        else
        {
            // It's good practice to log the error if the query was not valid
            Console.WriteLine($"OpenSearch query failed: {response.DebugInformation}");
        }

        return results;
    }
}

// NOTE: These are placeholder classes to make the example complete.
// You should use your actual class definitions.
namespace GEGCRM.Gaming.PerformanceMonitoring.Models
{
    public class ApiAccessLog
    {
        public string Service { get; set; }
        public string Endpoint { get; set; }
        public string Body { get; set; }
        public DateTime Timestamp { get; set; } // Or use `@timestamp` if that's the field name
        public string open { get; set; }
    }

    public class UpdateRatingRequest
    {
        // Properties of your UpdateRatingRequest object go here
    }
}