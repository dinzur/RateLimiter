using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// Represents a rate limit with a maximum number of requests allowed within a specified time window.
public class RateLimit
{
    public int MaxRequests { get; set; }
    public TimeSpan TimeWindow { get; set; }

    public RateLimit(int maxRequests, TimeSpan timeWindow)
    {
        MaxRequests = maxRequests;
        TimeWindow = timeWindow;
    }
}

/// Custom exception to be thrown when rate limits are exceeded.
public class RateLimitExceededException : Exception
{
    public RateLimitExceededException(string message) : base(message) { }
}

/// A thread-safe RateLimiter that ensures multiple rate limits are honored for a given action.
/// Supports dynamic reconfiguration of rate limits.
/// <typeparam name="TArg">The type of the argument passed to the action.</typeparam>
public class RateLimiter<TArg>
{
    private readonly Func<TArg, Task> _action;
    private readonly ConcurrentDictionary<RateLimit, LinkedList<DateTime>> _requestQueues;
    private readonly ReaderWriterLockSlim _rateLimitLock = new ReaderWriterLockSlim();

    private List<RateLimit> _rateLimits;


    /// Initializes a new instance of the RateLimiter class.
    /// <param name="action">The action to be rate-limited.</param>
    /// <param name="rateLimits">A collection of rate limits to enforce.</param>
    public RateLimiter(Func<TArg, Task> action, IEnumerable<RateLimit> rateLimits)
    {
        _action = action;
        _rateLimits = rateLimits.ToList();
        _requestQueues = new ConcurrentDictionary<RateLimit, LinkedList<DateTime>>();

        foreach (var rateLimit in _rateLimits)
        {
            _requestQueues[rateLimit] = new LinkedList<DateTime>();
        }
    }

    /// Executes the action, ensuring that all rate limits are honored.
    /// <param name="argument">The argument to pass to the action.</param>
    public async Task Perform(TArg argument)
    {
        foreach (var rateLimit in GetRateLimits())
        {
            await EnsureRateLimitAsync(rateLimit);
        }

        await _action(argument);

        var now = DateTime.UtcNow;
        foreach (var rateLimit in GetRateLimits())
        {
            _requestQueues[rateLimit].AddLast(now);
        }
    }

    /// Ensures that the specified rate limit is not exceeded using a sliding window approach.
    private async Task EnsureRateLimitAsync(RateLimit rateLimit)
    {
        var queue = _requestQueues[rateLimit];
        var now = DateTime.UtcNow;

        _rateLimitLock.EnterWriteLock(); // Acquire the write lock to modify the queue
        try
        {
            // Remove expired timestamps from the sliding window
            while (queue.Count > 0 && (now - queue.First.Value) > rateLimit.TimeWindow)
            {
                queue.RemoveFirst();
            }

            // Check if we need to wait (i.e., if the count exceeds the rate limit)
            if (queue.Count < rateLimit.MaxRequests)
            {
                return; // No need to delay if we're within the limit
            }
        }
        finally
        {
            _rateLimitLock.ExitWriteLock(); // Ensure the write lock is released
        }

        // If the queue is full, calculate how long to wait
        var delay = rateLimit.TimeWindow - (now - queue.First.Value);
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay); // Delay until the rate limit window allows another request
        }

        // After delay, recheck the queue
        _rateLimitLock.EnterWriteLock();
        try
        {
            if (queue.Count >= rateLimit.MaxRequests)
            {
                throw new RateLimitExceededException($"Rate limit of {rateLimit.MaxRequests} requests per {rateLimit.TimeWindow} exceeded.");
            }
        }
        finally
        {
            _rateLimitLock.ExitWriteLock();
        }
    }

    /// Dynamically updates the rate limits at runtime.
    /// <param name="newRateLimits">The new set of rate limits to enforce.</param>
    public void UpdateRateLimits(IEnumerable<RateLimit> newRateLimits)
    {
        _rateLimitLock.EnterWriteLock();
        try
        {
            // Clear existing queues for removed rate limits
            var newRateLimitsList = newRateLimits.ToList();

            foreach (var rateLimit in _rateLimits.Except(newRateLimitsList))
            {
                _requestQueues.TryRemove(rateLimit, out _);
            }

            // Add queues for new rate limits
            foreach (var rateLimit in newRateLimitsList.Except(_rateLimits))
            {
                _requestQueues[rateLimit] = new LinkedList<DateTime>();
            }

            // Update the rate limits list
            _rateLimits = newRateLimitsList;
        }
        finally
        {
            _rateLimitLock.ExitWriteLock();
        }
    }

    /// Retrieves the current list of rate limits in a thread-safe manner.
    private List<RateLimit> GetRateLimits()
    {
        _rateLimitLock.EnterReadLock();
        try
        {
            return _rateLimits.ToList();
        }
        finally
        {
            _rateLimitLock.ExitReadLock();
        }
    }
}
