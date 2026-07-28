using System.Collections.Concurrent;
using EnglishMasterAI.Web.Configuration;
using Microsoft.Extensions.Options;

namespace EnglishMasterAI.Web.Application;

public sealed class AiPracticeLimiter(IOptions<SecurityOptions> options)
{
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _requests = new();
    private readonly int _permitLimit = Math.Clamp(options.Value.AiRequestsPerMinute, 2, 60);

    public bool TryAcquire(string userId, DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        var queue = _requests.GetOrAdd(userId, _ => new Queue<DateTimeOffset>());
        lock (queue)
        {
            while (queue.TryPeek(out var oldest)
                   && timestamp - oldest >= TimeSpan.FromMinutes(1))
            {
                queue.Dequeue();
            }

            if (queue.Count >= _permitLimit)
            {
                return false;
            }

            queue.Enqueue(timestamp);
            return true;
        }
    }
}
