using REST_API.LocalCache;
using StackExchange.Redis;
using System.Text.Json;

namespace REST_API
{
    public class RedisService
    {
        private readonly IDatabase _db;
        private readonly ISubscriber _sub;

        public RedisService(IConnectionMultiplexer redis)
        {
            _db = redis.GetDatabase();
            _sub = redis.GetSubscriber();
        }

        public async Task SetAsync(string key, string value, TimeSpan? expiry = null)
        {
            await _db.StringSetAsync(key, value, expiry);
        }

        public async Task<string?> GetAsync(string key)
        {
            var value = await _db.StringGetAsync(key);
            return value.HasValue ? value.ToString() : null;
        }

        public async Task<bool> ExistsAsync(string key)
        {
            return await _db.KeyExistsAsync(key);
        }

        public async Task<bool> DeleteAsync(string key)
        {
            return await _db.KeyDeleteAsync(key);
        }

        public async Task Publish(string channel, string message)
        {
            await _sub.PublishAsync(channel, message);
        }

        public async Task Subscribe<T>(string channel, Action<T> handler)
        {
            await _sub.SubscribeAsync(channel, (ch, msg) =>
            {
                var data = JsonSerializer.Deserialize<T>(msg!);
                if (data != null)
                    handler(data);
            });
        }
    }

}
