// See https://aka.ms/new-console-template for more information

using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Configuration;

public record Makelaar(int Id, string Name);
public record Listing(Makelaar Makelaar, bool hasGarden);

public class Program
{
    //user secrets 
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
        var amsterdamListings = await FetchAllListings(config,"/amsterdam");
        var gardenListings = await FetchAllListings(config,"/amsterdam/tuin");

        DisplayTop10Listings("TOP 10 MAKELAARS IN AMSTERDAM", CountListings(amsterdamListings));
        DisplayTop10Listings("TOP 10 MAKELAARS IN AMSTERDAM WITH GARDEN", CountListings(gardenListings));
        
    }

    private static async Task<List<Listing>> FetchAllListings(SConfig config,string query)
    {
        var allListings = new List<Listing>();
        int page = 1;

        //processing all pages 
        while (true)
        {
            //fetching a single page of results from API
            var response = await FetchPage(config,query, page);
            //if API returns no objects or returns a null response
            if (response?.Objects == null || !response.Objects.Any()) break;

            //collect all makelaar object results from api and check if the query contains "tuin and add to list for results with gardens"
            allListings.AddRange(response.Objects.Select(
                obj => new Listing(new Makelaar(obj.MakelaarId, obj.MakelaarNaam),  
                    query.Contains("tuin"))));

            // if >= total page 25 
            if (page >= response.Paging.TotalPages) break;
            page++;
        }

        return allListings;
    }

    private static async Task<APIResponse?> FetchPage(SConfig config, string query, int page)
    {
        await APIRateLimit();

        var url = $"{config.BASEURL}{config.APIKEY}/?type=koop&zo={query}&page={page}&pagesize=25";
        try
        {
            var json = await client.GetStringAsync(url);
           return JsonSerializer.Deserialize<APIResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching page {page} with {ex.Message}");
            await Task.Delay(5000); //5 seconds before retry 
            return null;
        }
    }

    private static async Task APIRateLimit()
    {
        //checking for limits for 100 requests per minute for preventing 429 errors
        var timeSinceLastApiCall = (DateTime.Now - lastRequest).TotalMilliseconds;
        if (timeSinceLastApiCall < RequestDelayinMilliSeconds)
        {
            await Task.Delay((int)(RequestDelayinMilliSeconds - timeSinceLastApiCall));
        }

        lastRequest = DateTime.Now;
    }

    private static List<(string Name, int Count)> CountListings(List<Listing> listings)
    {
        //grouping by makelaar name
        var groupedByName = listings.GroupBy(listing => listing.Makelaar.Name);
        //(Name: Count) for each makelaar 
        var groupedCount = groupedByName.Select(grouping => (Name: grouping.Key, Count: grouping.Count()));
        //sorting by highest to lowest 
        var sorted = groupedCount.OrderByDescending(x => x.Count);
        //taking the first 10 from the list
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

    public class APIResponse
    {
        public List<APIObject> Objects { get; set; }
        public PagingInfo Paging { get; set; }
    }

    public class APIObject
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
