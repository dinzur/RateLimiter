# RateLimiter assignment

It's purpose is to be initialized with some Func> and multiple(!) rate limits.

It is then called like so: Task Perform(TArg argument) to perform the action.

It guarantees that ALL rate limits it holds are honored, and delays execution until it can honor the rate limits if needed.

The RateLimiter IS expected to be called from multiple threads at the same time - I have accommodated that.

For example, the passed function might be a call to some external API, and it may receive the rate limits: 10 per second, 100 per minute, 1000 per day

There are two approaches to Rate Limiting. Let's imagine a single RateLimit of 10 per day. The approaches are:

1. Sliding window - Where the RateLimiter ensures that no more than 10 were executed in the last 24 hrs, even if the call is made in the middle of day

2. Absolute - Where the RateLimiter ensures that no more than 10 were executed THIS day, that is, since 00:00.

I chose to implement the sliding window approach, and explain why (pros / cons), and then implement the RateLimiter.

## Sliding Window Pros:
* More precise control over actual rate
* Better suited for true API rate limiting since it prevents bursts
* More predictable behavior for the consumer

## Sliding Window Cons:
* More complex to implement correctly
* Higher memory usage (need to track timestamps)
* More CPU intensive
