using System;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        // Define a sample action to be rate-limited
        async Task SampleAction(string input)
        {
            await Task.Delay(1);
            Console.WriteLine($"Action performed with input: {input} at {DateTime.UtcNow}");
        }

        // Create rate limits
        var rateLimits = new[]
        {
            new RateLimit(10, TimeSpan.FromSeconds(1)), // 10 requests per second
            new RateLimit(100, TimeSpan.FromMinutes(1)), // 100 requests per minute
            new RateLimit(1000, TimeSpan.FromDays(1)) // 1000 requests per day
        };

        // Create a RateLimiter instance
        var rateLimiter = new RateLimiter<string>(SampleAction, rateLimits);

        // Test the RateLimiter
        await rateLimiter.Perform("Test 1");
        await rateLimiter.Perform("Test 2");

        // Add more actions here as needed
    }
}
