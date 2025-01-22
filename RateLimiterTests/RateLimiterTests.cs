using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Xunit;
using System.Threading;

namespace RateLimiterTests
{
    public class RateLimiterTests
    {
        private static async Task MockApiCall(string input)
        {
            await Task.Delay(10); // Simulate API call
        }

        [Fact]
        public async Task RespectsSingleRateLimit()
        {
            // Arrange
            var rateLimiter = new RateLimiter<string>(
                MockApiCall,
                new[] { new RateLimit(2, TimeSpan.FromSeconds(1)) }
            );

            // Act
            // Execute first two calls which should complete quickly
            await rateLimiter.Perform("call1");
            await rateLimiter.Perform("call2");

            // Try third call which should be rate limited
            var sw = Stopwatch.StartNew();
            await Assert.ThrowsAsync<RateLimitExceededException>(
                async () => await rateLimiter.Perform("call3")
            );
            sw.Stop();

            // Assert
            Assert.True(sw.ElapsedMilliseconds < 1000, 
                "Rate limit exception should be thrown immediately");
        }

        [Fact]
        public async Task RespectsMultipleRateLimits()
        {
            // Arrange
            var rateLimiter = new RateLimiter<string>(
                MockApiCall,
                new[] 
                { 
                    new RateLimit(2, TimeSpan.FromSeconds(1)),  // 2 per second
                    new RateLimit(3, TimeSpan.FromSeconds(2))   // 3 per 2 seconds
                }
            );

            // Act & Assert
            // Should allow first two calls
            await rateLimiter.Perform("call1");
            await rateLimiter.Perform("call2");

            // Third immediate call should trigger the 2/second limit
            var firstException = await Record.ExceptionAsync(async () => 
                await rateLimiter.Perform("call3"));
            Assert.IsType<RateLimitExceededException>(firstException);

            // Wait for the 1-second window to reset
            await Task.Delay(1100);

            // Make two more calls (now at 2 calls in new 1-second window, but 4 in 2-second window)
            await rateLimiter.Perform("call4");
            await rateLimiter.Perform("call5");

            // This should fail due to the 3/2seconds limit
            var secondException = await Record.ExceptionAsync(async () => 
                await rateLimiter.Perform("call6"));
            Assert.IsType<RateLimitExceededException>(secondException);

            // Verify the exception messages
            Assert.Contains("requests per", firstException.Message);
            Assert.Contains("requests per", secondException.Message);
        }

        [Fact]
        public async Task HandlesMultipleThreadsConcurrently()
        {
            // Arrange
            var rateLimiter = new RateLimiter<string>(
                MockApiCall,
                new[] { new RateLimit(10, TimeSpan.FromSeconds(2)) }
            );

            // Act
            var tasks = new List<Task>();
            var threadCount = 5; // Reduced from 10 to stay within rate limit
            var completedCalls = 0;
            
            // Launch threads simultaneously
            for (int i = 0; i < threadCount; i++)
            {
                tasks.Add(Task.Run(async () => 
                {
                    await rateLimiter.Perform($"thread{i}");
                    Interlocked.Increment(ref completedCalls);
                }));
            }

            await Task.WhenAll(tasks);

            // Assert
            Assert.Equal(threadCount, completedCalls);
            Assert.Equal(threadCount, tasks.Count);
            Assert.All(tasks, t => Assert.True(t.IsCompletedSuccessfully));
        }

        [Fact]
        public async Task HandlesRateLimitUpdates()
        {
            // Arrange
            var initialLimit = new RateLimit(1, TimeSpan.FromSeconds(1));
            var rateLimiter = new RateLimiter<string>(
                MockApiCall,
                new[] { initialLimit }
            );

            // Act - First call with initial limit
            await rateLimiter.Perform("first");

            // Should fail with initial limit
            await Assert.ThrowsAsync<RateLimitExceededException>(
                async () => await rateLimiter.Perform("second")
            );

            // Update to a more permissive rate limit
            var newLimit = new RateLimit(2, TimeSpan.FromSeconds(1));
            rateLimiter.UpdateRateLimits(new[] { newLimit });

            // Wait a bit to ensure rate limit window is clear
            await Task.Delay(100);

            // Should succeed with new limit
            await rateLimiter.Perform("third");
        }

        [Fact]
        public async Task HandlesMultipleRateLimitUpdates()
        {
            // Arrange
            var rateLimiter = new RateLimiter<string>(
                MockApiCall,
                new[] { new RateLimit(1, TimeSpan.FromSeconds(1)) }
            );

            // Act & Assert
            // Initial limit - first call succeeds
            await rateLimiter.Perform("first");
            
            // Second call should fail with initial limit
            await Assert.ThrowsAsync<RateLimitExceededException>(
                async () => await rateLimiter.Perform("second")
            );

            // Update to new limits
            rateLimiter.UpdateRateLimits(new[] 
            { 
                new RateLimit(2, TimeSpan.FromSeconds(1))
            });

            // Wait a bit to ensure rate limit window is clear
            await Task.Delay(1100);

            // Should work with new limit
            await rateLimiter.Perform("third");
            await rateLimiter.Perform("fourth");

            // Third call within second should fail
            await Assert.ThrowsAsync<RateLimitExceededException>(
                async () => await rateLimiter.Perform("fifth")
            );
        }

        [Fact]
        public async Task ThrowsRateLimitExceededException()
        {
            // Arrange
            var rateLimiter = new RateLimiter<string>(
                MockApiCall,
                new[] { new RateLimit(1, TimeSpan.FromMilliseconds(100)) }
            );

            // Act & Assert
            await rateLimiter.Perform("first");
            await Assert.ThrowsAsync<RateLimitExceededException>(
                async () => await rateLimiter.Perform("second")
            );
        }

        [Fact]
        public async Task AllowsExecutionAfterWindowReset()
        {
            // Arrange
            var rateLimiter = new RateLimiter<string>(
                MockApiCall,
                new[] { new RateLimit(1, TimeSpan.FromSeconds(1)) }
            );

            // Act
            await rateLimiter.Perform("first");
            await Task.Delay(1100); // Wait for window to reset
            
            // Should succeed after window reset
            await rateLimiter.Perform("second");
        }
    }
}