// See https://aka.ms/new-console-template for more information

using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Configuration;

public record Makelaar(int Id, string Name);
public record Listing(Makelaar Makelaar, bool hasGarden);

public class Program
{
    public static async Task Main()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<Program>() 
            .Build();

        var secrets = config.GetSection("Funda").Get<SConfig>();
        await APIAnalyzer.Run(secrets);
    }
}
public static class APIAnalyzer
{
    private static readonly HttpClient client = new HttpClient();
    private static DateTime lastRequest = DateTime.MinValue;
    private const int RequestDelayinMilliSeconds = 600; //100 requests p/m
    //configuration 
    
    public static async Task Run(SConfig config)
    {

        var amsterdamListings = await fetchAllListings(config,"/amsterdam");
        var gardenListings = await fetchAllListings(config,"/amsterdam/tuin");

        DisplayTop10Listings("TOP 10 MAKELAARS IN AMSTERDAM", CountListings(amsterdamListings));
        DisplayTop10Listings("TOP 10 MAKELAARS IN AMSTERDAM WITH GARDEN", CountListings(gardenListings));
        
    }

    private static async Task<List<Listing>> fetchAllListings(SConfig config,string query)
    {
        var allListings = new List<Listing>();
        int page = 1;

        while (true)
        {
            var response = await FetchPage(config,query, page);
            if (response?.Objects == null || !response.Objects.Any()) break;

            allListings.AddRange(response.Objects.Select(
                obj => new Listing(new Makelaar(obj.MakelaarId, obj.MakelaarNaam),
                    query.Contains("tuin"))));

            if (page >= response.Paging.TotalPages) break;
            page++;
        }

        return allListings;
    }

    private static async Task<ApiResponse?> FetchPage(SConfig config, string query, int page)
    {
        await RateLimit();

        var url = $"{config.BASEURL}{config.APIKEY}/?type=koop&zo={query}&page={page}&pagesize=25";
        try
        {
            var json = await client.GetStringAsync(url);
           return JsonSerializer.Deserialize<ApiResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching page {page} with {ex.Message}");
            await Task.Delay(5000); //5 seconds before retry 
            return null;
        }
    }

    private static async Task RateLimit()
    {
        var timeSinceLastApiCall = (DateTime.Now - lastRequest).TotalMilliseconds;
        if (timeSinceLastApiCall < RequestDelayinMilliSeconds)
        {
            await Task.Delay((int)(RequestDelayinMilliSeconds - timeSinceLastApiCall));
        }

        lastRequest = DateTime.Now;
    }

    private static List<(string Name, int Count)> CountListings(List<Listing> listings)
    {
        var grouped = listings.GroupBy(listing => listing.Makelaar.Name);
        var counted = grouped.Select(grouping => (Name: grouping.Key, Count: grouping.Count()));
        var sorted = counted.OrderByDescending(x => x.Count);
        var top10 = sorted.Take(10).ToList();
        return top10;
    }

    private static void DisplayTop10Listings(string title, List<(string Name, int Count)> results)
    {
        Console.WriteLine($"\n{title}");
        Console.WriteLine(new string('=', title.Length));
        for (int i = 0; i < results.Count; i++)
        {
            Console.WriteLine($"{i + 1,2}. {results[i].Name,-50} -------> {results[i].Count} properties");
        }
    }
}

    public class ApiResponse
    {
        public List<PropertyObject> Objects { get; set; }
        public PagingInfo Paging { get; set; }
    }

    public class PropertyObject
    {
        public int MakelaarId { get; set; }
        public string MakelaarNaam { get; set; }
    }

    public class PagingInfo
    {
        public int TotalPages { get; set; }
    }

    public class SConfig
    {
        public string APIKEY { get; set; }
        public string BASEURL { get; set; }
    }
