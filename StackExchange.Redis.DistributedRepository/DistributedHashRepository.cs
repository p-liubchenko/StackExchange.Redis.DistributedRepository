using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using StackExchange.Redis.DistributedRepository.Models;
using StackExchange.Redis.Extensions.Core.Abstractions;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using static StackExchange.Redis.DistributedRepository.Extensions.RepositoryExtensions;
using static StackExchange.Redis.DistributedRepository.Extensions.BinarySerializer;

[assembly: InternalsVisibleTo("StackExchange.Redis.DistributedRepository.Banchmark")]
namespace StackExchange.Redis.DistributedRepository;

public class DistributedHashRepository<T> : RepositoryBase<T>, IDistributedCache where T : class
{
	protected static string InstanceId = Guid.NewGuid().ToString();

	/// <summary>
	/// Base key for the repository object tracker
	/// </summary>
	protected string BaseKeyTracker
	{
		get => $"{BaseKey}:tracker";
	}

	/// <summary>
	/// Base key for the repository distributed object lock
	/// </summary>
	protected string BaseKeyLock
	{
		get => $"{BaseKey}:lock";
	}

	/// <summary>
	/// Key selector for the repository's entity
	/// </summary>
	public readonly Func<T, string> KeySelector;

	private readonly IDatabase _database;
	private readonly IRedisDatabase _redisDatabase;
	private readonly ISubscriber _subscriber;
	private readonly IMemoryCache _memoryCache;

	public DistributedHashRepository(IRedisClient redis, IMemoryCache memoryCache, Func<T, string> keySelector)
	{
		_redisDatabase = redis.GetDefaultDatabase();
		_database = _redisDatabase.Database;
		_subscriber = _redisDatabase.Database.Multiplexer.GetSubscriber();
		_subscriber.Subscribe(BaseKey, ItemUpdatedHandler);
		_memoryCache = memoryCache;
		KeySelector = keySelector;
		Task.Run(RebakeAll).ConfigureAwait(true);
	}

	public T Add(T item)
	{
		string key = KeySelector.Invoke(item);
		string fqk = this.FQK(key);
		ITransaction? transaction = _database.CreateTransaction();
		transaction.HashSetAsync(BaseKey, key, JsonSerializer.Serialize(item));
		transaction.SetAddAsync(BaseKeyTracker, key);
		transaction.Execute();
		_subscriber.Publish(BaseKey, GenerateMessage(MessageType.Created, key));
		return item;
	}

	public async Task<T> AddAsync(T item)
	{
		string key = KeySelector.Invoke(item);
		string fqk = this.FQK(key);
		ITransaction? transaction = _database.CreateTransaction();
		await transaction.HashSetAsync(BaseKey, key, JsonSerializer.Serialize(item));
		await transaction.SetAddAsync(BaseKeyTracker, key);
		await transaction.ExecuteAsync();
		_subscriber.Publish(BaseKey, GenerateMessage(MessageType.Created, key));
		return item;
	}

	public T? Remove(T item)
	{
		string? key = KeySelector.Invoke(item);
		return Remove(key);
	}

	public async Task<T?> RemoveAsync(T item)
	{
		string? key = KeySelector.Invoke(item);
		return await RemoveAsync(key);
	}

	public T? Remove(string key)
	{
		var poped = Get(key);
		if (poped is null)
			return null;
		ITransaction transaction = _database.CreateTransaction();
		transaction.HashDeleteAsync(BaseKey, key);
		transaction.SetRemoveAsync(BaseKeyTracker, key);
		transaction.Execute();
		_memoryCache.Remove(this.FQK(key));
		_subscriber.Publish(BaseKey, GenerateMessage(MessageType.Deleted, key));
		return poped;
	}

	public async Task<T?> RemoveAsync(string key)
	{
		var poped = await GetAsync(key);
		if (poped is null)
			return null;
		ITransaction transaction = _database.CreateTransaction();
		transaction.HashDeleteAsync(BaseKey, key);
		transaction.SetRemoveAsync(BaseKeyTracker, key);
		await transaction.ExecuteAsync();
		_memoryCache.Remove(this.FQK(key));
		_subscriber.Publish(BaseKey, GenerateMessage(MessageType.Deleted, key));
		return poped;
	}

	public T? Get(string Key)
	{
		if (_memoryCache.TryGetValue(this.FQK(Key), out T? item))
		{
			return item;
		}

		if (_database.HashExists(BaseKey, Key))
		{
			string? value = _database.HashGet(BaseKey, Key);
			if (string.IsNullOrEmpty(value))
				return null;
			item = JsonSerializer.Deserialize<T?>(value);
			_memoryCache.Set(this.FQK(Key), item);
			return item;
		}

		return null;
	}

	public async Task<T?> GetAsync(string Key)
	{
		if (_memoryCache.TryGetValue(this.FQK(Key), out T? item))
		{
			return item;
		}

		if (_database.HashExists(BaseKey, Key))
		{
			string? value = await _database.HashGetAsync(BaseKey, Key);
			if (string.IsNullOrEmpty(value))
				return null;
			item = JsonSerializer.Deserialize<T>(value);
			_memoryCache.Set(this.FQK(Key), item);
			return item;
		}

		return null;
	}

	public async Task<IEnumerable<T>> GetAsync()
	{
		RedisValue[]? keys = _database.HashKeys(BaseKey);
		List<T>? items = new List<T>();
		foreach (var key in keys)
		{
			items.Add(await GetAsync(key));
		}
		return items;
	}

	public IEnumerable<T> Get()
	{
		RedisValue[]? keys = _database.HashKeys(BaseKey);
		List<T>? items = new List<T>();
		foreach (var key in keys)
		{
			items.Add(Get(key));
		}
		return items;
	}

	protected async Task RebakeAll()
	{
		RedisValue[]? keys = _database.SetMembers(BaseKeyTracker);
		_memoryCache.Set(
			BaseKeyTracker,
			keys.Where(x => x.HasValue).Select(x => x.ToString())
		);
		foreach (var item in keys)
		{
			_memoryCache.Set(this.FQK(item.ToString()), Get(item));
		}
	}

	public async Task Purge()
	{
		ITransaction transaction = _database.CreateTransaction();
		await transaction.KeyDeleteAsync(BaseKey);
		await transaction.KeyDeleteAsync(BaseKeyTracker);
		await transaction.ExecuteAsync();
		await _subscriber.PublishAsync(BaseKey, GenerateMessage(MessageType.Purged, null));
		await RebakeAll();
	}

	protected async Task Rebuild()
	{
		RedisValue[]? keys = _database.HashKeys(BaseKey);
		RedisValue[]? tracked = _database.SetMembers(BaseKeyTracker);

		IEnumerable<RedisValue>? toRemove = tracked.Except(keys);
		IEnumerable<RedisValue>? toAdd = keys.Except(tracked);

		ITransaction? transaction = _database.CreateTransaction();

		await transaction.SetRemoveAsync(BaseKeyTracker, toRemove.ToArray());
		await transaction.SetAddAsync(BaseKeyTracker, toAdd.ToArray());
		await transaction.ExecuteAsync();

		_memoryCache.Set(
			BaseKeyTracker,
			keys.Where(x => x.HasValue).Select(x => x.ToString())
		);

		foreach (RedisValue key in keys)
		{
			_memoryCache.Set(this.FQK(key.ToString()), Get(key));
		}

	}

	protected virtual void ItemUpdatedHandler(RedisChannel channel, RedisValue value)
	{
		Message? message = JsonSerializer.Deserialize<Message>(value.ToString());

		if (message is null)
			return;
		if (message.i == InstanceId)
			return;

		switch (message.type)
		{
			case MessageType.Created:
				if (string.IsNullOrEmpty(message.item))
					return;
				T? item = Get(message.item);
				if (item is null) return;
				_memoryCache.Set(this.FQK(message.item), item);
				break;
			case MessageType.Updated:
				if (string.IsNullOrEmpty(message.item))
					return;
				T? item2 = Get(message.item);
				if (item2 is null) return;
				_memoryCache.Set(this.FQK(message.item), item2);
				break;
			case MessageType.Deleted:
				if (string.IsNullOrEmpty(message.item))
					return;
				_memoryCache.Remove(this.FQK(message.item));
				break;
			case MessageType.Purged:
				List<string>? keys = _memoryCache.Get<List<string>>(BaseKeyTracker);
				if (keys is null || !keys.Any()) return;
				foreach (var key in keys)
				{
					_memoryCache.Remove(key);
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
	
	#region IDistributedCache
	byte[]? IDistributedCache.Get(string key)
	{
		T? found = Get(key);
		if (found is null)
			return null;
		return Serialize(found);
	}
	async Task<byte[]?> IDistributedCache.GetAsync(string key, CancellationToken token)
	{
		T? found = await GetAsync(key);
		if (found is null)
			return null;
		return Serialize(found);
	}
	void IDistributedCache.Set(string key, byte[] value, DistributedCacheEntryOptions options)
	{
		Add(Deserialize<T>(value));
	}
	async Task IDistributedCache.SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token)
	{
		await AddAsync(Deserialize<T>(value));
	}
	void IDistributedCache.Refresh(string key) => throw new NotImplementedException();
	Task IDistributedCache.RefreshAsync(string key, CancellationToken token) => throw new NotImplementedException();
	void IDistributedCache.Remove(string key)
	{
		Remove(key);
	}
	async Task IDistributedCache.RemoveAsync(string key, CancellationToken token)
	{
		await RemoveAsync(key);
	}
	#endregion
}
