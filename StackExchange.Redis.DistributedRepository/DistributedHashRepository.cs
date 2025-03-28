using Microsoft.Extensions.Caching.Memory;
using StackExchange.Redis.DistributedRepository.Models;
using StackExchange.Redis.Extensions.Core.Abstractions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
[assembly: InternalsVisibleTo("StackExchange.Redis.DistributedRepository.Banchmark")]
namespace StackExchange.Redis.DistributedRepository;

public class DistributedHashRepository<T> where T : class
{
	protected static string InstanceId = Guid.NewGuid().ToString(); 
	private string _baseKey;
	public string BaseKey
	{
		get
		{
			_baseKey ??= $"dsr:{typeof(T).Name}";
			return _baseKey;
		}
	}

	public static Func<T, string> KeySelector;

	private readonly IDatabase _database;
	private readonly IRedisDatabase _redisDatabase;
	private readonly ISubscriber _subscriber;
	private readonly IMemoryCache _memoryCache;

	public DistributedHashRepository(IRedisClient redis, IMemoryCache memoryCache)
	{
		_redisDatabase = redis.GetDefaultDatabase();
		_database = _redisDatabase.Database;
		_subscriber = _redisDatabase.Database.Multiplexer.GetSubscriber();
		_subscriber.Subscribe(BaseKey, ItemUpdatedLegacy);
		//subscribe to the channel
		//_redisDatabase.SubscribeAsync<Message>(BaseKey, ItemUpdated).Wait();
		_memoryCache = memoryCache;
		//_subscriber.Subscribe(BaseKey, ItemUpdated);
	}

	public T Add(T item)
	{
		string key = KeySelector.Invoke(item);
		string fqk = FQK(key);
		_database.HashSet(BaseKey, key, JsonSerializer.Serialize(item));
		_subscriber.Publish(BaseKey, GenerateMessage(MessageType.Created, key));
		//_database.PublishAsync(BaseKey, GenerateMessage(MessageType.Created, key)).Wait();
		return item;
	}

	public T Remove(T item)
	{
		var key = KeySelector.Invoke(item);
		_database.HashSet(FQK(key), key, JsonSerializer.Serialize(item));
		_subscriber.Publish(BaseKey, GenerateMessage(MessageType.Deleted, key));
		//_database.PublishAsync(BaseKey, GenerateMessage(MessageType.Deleted, key)).Wait();
		return item;
	}

	public T Get(string Key)
	{
		if (_memoryCache.TryGetValue(FQK(Key), out T item))
		{
			return item;
		}

		if (_database.HashExists(BaseKey,Key))
		{
			item = JsonSerializer.Deserialize<T>(_database.HashGet(BaseKey, Key));
			_memoryCache.Set(FQK(Key), item);
			return item;
		}

		return default;
	}
	public IEnumerable<T> GetAll()
	{
		var keys = _database.HashKeys(BaseKey);
		var items = new List<T>();
		foreach (var key in keys)
		{
			items.Add(Get(key));
		}
		return items;
	}
	private string FQK(string key) => $"{BaseKey}:{key}";
	private string FQK(T item) => $"{BaseKey}:{KeySelector.Invoke(item)}";

	protected virtual void ItemUpdatedLegacy(RedisChannel channel, RedisValue value)
	{
		 Message? message = JsonSerializer.Deserialize<Message>(value.ToString());

		if (message is null)
			return;
		if (message.i == InstanceId)
			return;

		switch (message.type)
		{
			case MessageType.Created:
				T item = Get(message.item);
				_memoryCache.Set(FQK(message.item), item);
				break;
			case MessageType.Updated:
				T item2 = Get(message.item);
				_memoryCache.Set(FQK(message.item), item2);
				break;
			case MessageType.Deleted:
				_memoryCache.Remove(FQK(message.item));
				break;
			case MessageType.Purged:
				//memory cache delete all items where key starts with
				MemoryCache? cacheItems = _memoryCache as MemoryCache;
				if (cacheItems is not null)
				{
					foreach (var cacheItem in cacheItems.Keys)
					{
						if (cacheItem?.ToString()?.StartsWith(BaseKey) ?? false)
						{
							_memoryCache.Remove(cacheItem);
						}
					}
				}
				break;
			default:
				break;
		}
	}

	protected virtual async Task ItemUpdated(Message? message)
	{
		if (message is null)
			return;
		if (message.i == InstanceId)
			return;

		switch (message.type)
		{
			case MessageType.Created:
				T item = Get(message.item);
				_memoryCache.Set(FQK(message.item), item);
				break;
			case MessageType.Updated:
				T item2 = Get(message.item);
				_memoryCache.Set(FQK(message.item), item2);
				break;
			case MessageType.Deleted:
				_memoryCache.Remove(FQK(message.item));
				break;
			case MessageType.Purged:
				//memory cache delete all items where key starts with
				MemoryCache? cacheItems = _memoryCache as MemoryCache;
				if (cacheItems is not null)
				{
					foreach (var cacheItem in cacheItems.Keys)
					{
						if (cacheItem?.ToString()?.StartsWith(BaseKey) ?? false)
						{
							_memoryCache.Remove(cacheItem);
						}
					}
				}
				break;
			default:
				break;
		}
	}

	internal string GenerateMessage(MessageType messageType, string? resourceKey)
	{
		return resourceKey is null
			? $"{{ \"i\":\"{InstanceId}\", \"type\":{(int)messageType}}}"
			: $"{{ \"i\":\"{InstanceId}\", \"type\":{(int)messageType},\"item\":\"{resourceKey}\"}}";
	}
}
