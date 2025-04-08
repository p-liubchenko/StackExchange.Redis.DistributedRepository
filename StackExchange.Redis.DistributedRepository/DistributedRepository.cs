using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StackExchange.Redis.DistributedRepository.Models;
using StackExchange.Redis.DistributedRepository.Telemetry;
using StackExchange.Redis.Extensions.Core.Abstractions;
using static StackExchange.Redis.DistributedRepository.Extensions.BinarySerializer;
using static StackExchange.Redis.DistributedRepository.Extensions.RepositoryExtensions;

[assembly: InternalsVisibleTo("StackExchange.Redis.DistributedRepository.Banchmark")]
namespace StackExchange.Redis.DistributedRepository;

public class DistributedRepository<T> : RepositoryBase<T>, IDistributedCache, IDistributedRepository<T> where T : class
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
	/// Base key in memory full list
	/// </summary>
	protected string MemoryListKey
	{
		get => $"{BaseKey}:list";
	}

	/// <summary>
	/// Key selector for the repository's entity
	/// </summary>
	public readonly Func<T, string> KeySelector;

	private readonly IDatabase _database;
	private readonly IRedisDatabase _redisDatabase;
	private readonly ISubscriber _subscriber;
	private readonly IMemoryCache _memoryCache;
	private readonly IRepositoryMetrics? _metrics;
	private readonly ILogger<DistributedRepository<T>>? _logger;

	public DistributedRepository(
		IRedisClient redis,
		IMemoryCache memoryCache,
		Func<T, string> keySelector,
		IRepositoryMetrics? metrics = null,
		ILogger<DistributedRepository<T>>? logger = null)
	{
		_redisDatabase = redis.GetDefaultDatabase();
		_database = _redisDatabase.Database;
		_subscriber = _redisDatabase.Database.Multiplexer.GetSubscriber();
		_subscriber.Subscribe(BaseKey, ItemUpdatedHandler);
		_memoryCache = memoryCache;
		_metrics = metrics;
		_logger = logger;
		KeySelector = keySelector;
		Task.Run(RebakeAll).ConfigureAwait(true);
	}

	#region add
	public T Add(T item) => AddAsync(item).GetAwaiter().GetResult();

	public async Task<T> AddAsync(T item)
	{
		ArgumentNullException.ThrowIfNull(item);
		string key = KeySelector.Invoke(item);

		if (string.IsNullOrEmpty(key))
			throw new ArgumentNullException(nameof(item), "KeySelector cannot return null or empty string");

		using Activity? activity = ActivitySourceProvider.StartActivity("repo.add.redis", key: key);
		Stopwatch? sw = Stopwatch.StartNew();

		try
		{

			string fqk = this.FQK(key);
			T result = await AddRedis(key, item);
			MemoryAdd(item, fqk);
			_subscriber.Publish(BaseKey, GenerateMessage(MessageType.Created, key));
			return result;
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error adding item to repository");
			throw;
		}
		finally
		{
			sw.Stop();
			_metrics?.ObserveDuration("repo.add", sw.Elapsed);
		}
	}

	public void AddRange(IEnumerable<T> range) =>
		AddRangeAsync(range).GetAwaiter().GetResult();

	public async Task AddRangeAsync(IEnumerable<T> range)
	{
		await RedisAddRange(range);
		MemoryAddRange(range);
	}

	protected async Task RedisAddRange(IEnumerable<T> range)
	{
		using Activity? activity = ActivitySourceProvider.StartActivity("repo.add-range.redis");
		Stopwatch? sw = Stopwatch.StartNew();
		try
		{

			ITransaction transaction = _database.CreateTransaction();
			foreach (var item in range)
			{
				string key = KeySelector.Invoke(item);
				transaction.HashSetAsync(BaseKey, key, JsonSerializer.Serialize(item));
				transaction.SetAddAsync(BaseKeyTracker, key);
			}
			await transaction.ExecuteAsync();
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error adding items to repository");
			throw;
		}
		finally
		{
			_metrics?.ObserveDuration("repo.add-range.redis", sw.Elapsed);
		}
	}

	protected void MemoryAddRange(IEnumerable<T> range)
	{
		_memoryCache.TryGetValue(MemoryListKey, out List<T>? items);
		if (items is null)
		{
			items = new List<T>();
			_memoryCache.Set(MemoryListKey, items);
		}
		items.AddRange(range);
		foreach (var item in range)
		{
			string key = KeySelector.Invoke(item);
			_memoryCache.Set(this.FQK(key), item);
		}
	}

	protected async Task<T> AddRedis(string key, T item)
	{
		using Activity? activity = ActivitySourceProvider.StartActivity("repo.add.redis", key: key);
		Stopwatch? sw = Stopwatch.StartNew();
		try
		{

			ITransaction? transaction = _database.CreateTransaction();
			transaction.HashSetAsync(BaseKey, key, JsonSerializer.Serialize(item));
			transaction.SetAddAsync(BaseKeyTracker, key);
			await transaction.ExecuteAsync();
			return item;
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error adding item to repository");
			throw;
		}
		finally
		{
			_metrics?.ObserveDuration("repo.add.redis", sw.Elapsed);
		}
	}
	protected void MemoryAdd(T item, string fqk)
	{
		_memoryCache.TryGetValue(MemoryListKey, out List<T>? items);
		if (items is null)
		{
			items = new List<T>();
			_memoryCache.Set(MemoryListKey, items);
		}
		items.Add(item);
		_memoryCache.Set(fqk, item);

	}
	#endregion

	#region remove
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

	public T? Remove(string key) => RemoveAsync(key).GetAwaiter().GetResult();

	public async Task<T?> RemoveAsync(string key)
	{
		var poped = await GetAsync(key);
		if (poped is null)
			return null;
		await RemoveRedis(key);
		RemoveMemory(key);
		_subscriber.Publish(BaseKey, GenerateMessage(MessageType.Deleted, key));
		return poped;
	}

	protected async Task RemoveRedis(string key)
	{
		ITransaction transaction = _database.CreateTransaction();
		transaction.HashDeleteAsync(BaseKey, key);
		transaction.SetRemoveAsync(BaseKeyTracker, key);
		await transaction.ExecuteAsync();
	}
	protected void RemoveMemory(string key)
	{
		_memoryCache.TryGetValue(MemoryListKey, out List<T>? items);
		if (items is null)
			return;
		items.RemoveAll(x => KeySelector.Invoke(x) == key);

		_memoryCache.Remove(this.FQK(key));
	}
	#endregion

	/// <inheritdoc/>
	public T? Get(string Key)
	{
		using Activity? activity = ActivitySourceProvider.StartActivity("repo.get", key: Key);
		Stopwatch? sw = Stopwatch.StartNew();
		try
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
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error getting item from repository");
			throw;
		}
		finally
		{
			_metrics?.ObserveDuration("repo.get", sw.Elapsed);
			activity?.Stop();
		}

		return null;
	}

	/// <inheritdoc/>
	public T GetOrAdd(string key, Func<T> factory)
	{
		T? item = Get(key);
		if (item is not null)
			return item;
		item = factory();
		Add(item);
		return item;
	}

	/// <inheritdoc/>
	public async Task<T?> GetAsync(string Key)
	{
		using Activity? activity = ActivitySourceProvider.StartActivity("repo.get", key: Key);
		Stopwatch? sw = Stopwatch.StartNew();
		try
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
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error getting item from repository");
			throw;
		}
		finally
		{
			_metrics?.ObserveDuration("repo.get", sw.Elapsed);
			activity?.Stop();
		}

		return null;
	}

	/// <inheritdoc/>
	public async Task<T> GetOrAddAsync(string key, Func<Task<T>> factory)
	{
		T? item = await GetAsync(key);
		if (item is not null)
			return item;
		item = await factory();
		await AddAsync(item);
		return item;
	}

	/// <inheritdoc/>
	public async Task<IEnumerable<T>> GetAsync()
	{
		bool fetchedFromMemory = _memoryCache.TryGetValue(MemoryListKey, out List<T>? items);
		if (!fetchedFromMemory || items is null)
		{
			HashEntry[]? objects = await _database.HashGetAllAsync(BaseKey);

			IDictionary<string, T?> values = objects.ToDictionary(x => x.Name.ToString(), x => JsonSerializer.Deserialize<T>(x.Value));

			_memoryCache.Set(
				MemoryListKey,
				values.Values.ToList()
			);

			foreach (KeyValuePair<string, T?> item in values)
			{
				if (item.Value is null)
					continue;

				_memoryCache.Set(this.FQK(item.Key), item.Value);
			}
			return values.Where(x => x.Value is not null).Select(x => x.Value);
		}

		return items;
	}

	/// <inheritdoc/>
	public IEnumerable<T> Get()
	{
		bool fetchedFromMemory = _memoryCache.TryGetValue(MemoryListKey, out List<T>? items);
		if (!fetchedFromMemory || items is null)
		{
			HashEntry[]? objects = _database.HashGetAll(BaseKey);

			IDictionary<string, T?> values = objects.ToDictionary(x => x.Name.ToString(), x => JsonSerializer.Deserialize<T>(x.Value));

			_memoryCache.Set(
				MemoryListKey,
				values.Values.ToList()
			);

			foreach (KeyValuePair<string, T?> item in values)
			{
				if (item.Value is null)
					continue;

				_memoryCache.Set(this.FQK(item.Key), item.Value);
			}
			return values.Where(x => x.Value is not null).Select(x => x.Value);
		}

		return items;
	}

	/// <inheritdoc/>
	public async Task RebakeAll()
	{
		using Activity? activity = ActivitySourceProvider.StartActivity("repo.rebake");
		Stopwatch? sw = Stopwatch.StartNew();
		try
		{

			RedisValue[]? keys = _database.SetMembers(BaseKeyTracker);

			foreach (var item in keys)
			{
				MemoryAdd(await GetAsync(item), this.FQK(item.ToString()));
			}
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error rebaking all items in repository");
			throw;
		}
		finally
		{
			_metrics?.ObserveDuration("repo.rebake", sw.Elapsed);
			activity?.Stop();
		}
	}

	/// <inheritdoc/>
	public async Task Purge()
	{
		using Activity? activity = ActivitySourceProvider.StartActivity("repo.purge");
		Stopwatch? sw = Stopwatch.StartNew();
		try
		{
			ITransaction transaction = _database.CreateTransaction();
			transaction.KeyDeleteAsync(BaseKey);
			transaction.KeyDeleteAsync(BaseKeyTracker);
			await transaction.ExecuteAsync();
			await _subscriber.PublishAsync(BaseKey, GenerateMessage(MessageType.Purged, null));
			MemoryPurge();
			await RebakeAll();
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error purging all items in repository");
			throw;
		}
		finally
		{
			_metrics?.ObserveDuration("repo.purge", sw.Elapsed);
			activity?.Stop();
		}
	}

	public void MemoryPurge()
	{
		List<T>? items = _memoryCache.Get<List<T>>(MemoryListKey);
		if (items is null)
			return;
		foreach (var item in items)
		{
			_memoryCache.Remove(this.FQK(KeySelector.Invoke(item)));
		}

		_memoryCache.Set(
			MemoryListKey,
			new List<T>()
		);
	}

	/// <inheritdoc/>
	public async Task Rebuild()
	{
		using Activity? activity = ActivitySourceProvider.StartActivity("repo.rebuild");
		Stopwatch? sw = Stopwatch.StartNew();
		try
		{
			RedisValue[]? keys = _database.HashKeys(BaseKey);
			RedisValue[]? tracked = _database.SetMembers(BaseKeyTracker);

			IEnumerable<RedisValue>? toRemove = tracked.Except(keys);
			IEnumerable<RedisValue>? toAdd = keys.Except(tracked);

			ITransaction? transaction = _database.CreateTransaction();

			transaction.SetRemoveAsync(BaseKeyTracker, toRemove.ToArray());
			transaction.SetAddAsync(BaseKeyTracker, toAdd.ToArray());
			await transaction.ExecuteAsync();

			foreach (RedisValue key in keys)
			{
				MemoryAdd(await GetAsync(key), this.FQK(key.ToString()));
			}
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error rebaking all items in repository");
			throw;
		}
		finally
		{
			_metrics?.ObserveDuration("repo.rebuild", sw.Elapsed);
			activity?.Stop();
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
				MemoryAdd(item, this.FQK(message.item));
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
				RemoveMemory(message.item);
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
