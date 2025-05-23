﻿using System.Diagnostics;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis.DistributedRepository.Indexing;
using StackExchange.Redis.DistributedRepository.Models;
using StackExchange.Redis.DistributedRepository.Telemetry;
using static StackExchange.Redis.DistributedRepository.Extensions.RepositoryExtensions;

[assembly: InternalsVisibleTo("StackExchange.Redis.DistributedRepository.Banchmark")]
namespace StackExchange.Redis.DistributedRepository;

public class DistributedRepository<T> : RepositoryBase<T>, IDistributedRepository<T> where T : class
{
	protected static string _instanceId = Guid.NewGuid().ToString();

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

	protected readonly IDatabase _database;
	protected readonly ISubscriber _bus;
	protected readonly IRepositoryMetrics? _metrics;
	protected readonly ILogger<DistributedRepository<T>>? _logger;
	protected readonly IEnumerable<RedisIndexer<T>>? _indexers;
	protected RedisChannel _busChannel;
	public DistributedRepository(
		IConnectionMultiplexer connection,
		Func<T, string> keySelector,
		IRepositoryMetrics? metrics = null,
		ILogger<DistributedRepository<T>>? logger = null,
		IEnumerable<RedisIndexer<T>>? indexers = null,
		string? keyPrefix = null)
	{
		_globalPrefix = keyPrefix;
		_database = connection.GetDatabase();
		_bus = connection.GetSubscriber();
		if (_bus.Ping() == TimeSpan.Zero)
			throw new Exception("Redis bus is not available");
		_busChannel = RedisChannel.Literal(BaseKey);

		_bus.Subscribe(_busChannel, ItemUpdatedHandler);
		_metrics = metrics;
		_logger = logger;
		_indexers = indexers;
		KeySelector = keySelector;
	}

	#region add
	public virtual T Add(T item) => AddAsync(item).GetAwaiter().GetResult();

	public virtual async Task<T> AddAsync(T item)
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
			_bus.Publish(_busChannel, GenerateMessage(MessageType.Created, key));
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

	public virtual void AddRange(IEnumerable<T> range) =>
		AddRangeAsync(range).GetAwaiter().GetResult();

	public virtual async Task AddRangeAsync(IEnumerable<T> range)
	{
		await RedisAddRange(range);
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
			IndexRangeRedis(ref transaction, range);
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

	protected async Task<T> AddRedis(string key, T item)
	{
		using Activity? activity = ActivitySourceProvider.StartActivity("repo.add.redis", key: key);
		Stopwatch? sw = Stopwatch.StartNew();
		try
		{
			ITransaction? transaction = _database.CreateTransaction();
			transaction.HashSetAsync(BaseKey, key, JsonSerializer.Serialize(item));
			transaction.SetAddAsync(BaseKeyTracker, key);
			IndexRedis(ref transaction, item, key);
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

	protected ITransaction IndexRedis(ref ITransaction transaction, T item, string itemKey)
	{
		if (_indexers is null || !_indexers.Any()) return transaction;
		foreach (var indexer in _indexers)
		{
			transaction.SetAddAsync($"{IndexBaseKey}:{indexer.Name}:{IndexKeyHelper.NormalizeValue(indexer.IndexSelector.Invoke(item))}", itemKey);
		}
		return transaction;
	}

	protected ITransaction IndexRangeRedis(ref ITransaction transaction, IEnumerable<T> items)
	{
		if (_indexers is null || !_indexers.Any()) return transaction;
		foreach (var item in items)
		{
			IndexRedis(ref transaction, item, KeySelector.Invoke(item));
		}

		return transaction;
	}

	protected async Task IndexRedis(string itemKey, T item)
	{
		if (_indexers is null || !_indexers.Any())
			return;
		using Activity? activity = ActivitySourceProvider.StartActivity("repo.index.redis");
		Stopwatch? sw = Stopwatch.StartNew();
		try
		{
			ITransaction transaction = _database.CreateTransaction();
			foreach (var indexer in _indexers)
			{
				transaction.SetAddAsync($"{IndexBaseKey}:{indexer.Name}:{indexer.IndexSelector.Invoke(item)?.ToString()}", itemKey);
			}
			await transaction.ExecuteAsync();
			return;
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error adding item to repository");
			throw;
		}
		finally
		{
			sw.Stop();
			_metrics?.ObserveDuration("repo.index.redis", sw.Elapsed);
		}
	}

	#endregion

	#region remove
	public virtual T? Remove(T item)
	{
		string? key = KeySelector.Invoke(item);
		return Remove(key);
	}

	public virtual async Task<T?> RemoveAsync(T item)
	{
		string? key = KeySelector.Invoke(item);
		return await RemoveAsync(key);
	}

	public virtual T? Remove(string key) => RemoveAsync(key).GetAwaiter().GetResult();

	public virtual async Task<T?> RemoveAsync(string key)
	{
		var poped = await GetAsync(key);
		if (poped is null)
			return null;
		await RemoveRedis(poped);
		_bus.Publish(_busChannel, GenerateMessage(MessageType.Deleted, key));
		return poped;
	}

	protected async Task RemoveRedis(T item)
	{
		string key = KeySelector.Invoke(item);
		ITransaction transaction = _database.CreateTransaction();
		transaction.HashDeleteAsync(BaseKey, key);
		transaction.SetRemoveAsync(BaseKeyTracker, key);
		foreach (string indexKey in GetIndexes(item))
		{
			transaction.SetRemoveAsync(indexKey, key);

		}
		await transaction.ExecuteAsync();
	}

	protected IEnumerable<string> GetIndexes(T item)
	{
		if (_indexers is null || !_indexers.Any())
			return Enumerable.Empty<string>();
		IEnumerable<string> indexKeys = _indexers.Select(index => IndexKey(index.Name, index.IndexSelector.Invoke(item)));
		return indexKeys;
	}

	#endregion

	/// <inheritdoc/>
	public virtual T? Get(string Key)
	{
		using Activity? activity = ActivitySourceProvider.StartActivity("repo.get", key: Key);
		Stopwatch? sw = Stopwatch.StartNew();
		try
		{
			string? value = _database.HashGet(BaseKey, Key);
			if (string.IsNullOrEmpty(value))
				return null;
			T? item = JsonSerializer.Deserialize<T?>(value);
			return item;
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
	public virtual T GetOrAdd(string key, Func<T> factory)
	{
		T? item = Get(key);
		if (item is not null)
			return item;
		item = factory();
		Add(item);
		return item;
	}

	/// <inheritdoc/>
	public virtual async Task<T?> GetAsync(string Key)
	{
		using Activity? activity = ActivitySourceProvider.StartActivity("repo.get", key: Key);
		Stopwatch? sw = Stopwatch.StartNew();
		try
		{
			string? value = await _database.HashGetAsync(BaseKey, Key);
			if (string.IsNullOrEmpty(value))
				return null;
			T? item = JsonSerializer.Deserialize<T>(value);
			return item;
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
	public virtual async Task<T> GetOrAddAsync(string key, Func<Task<T>> factory)
	{
		T? item = await GetAsync(key);
		if (item is not null)
			return item;
		item = await factory();
		await AddAsync(item);
		return item;
	}

	/// <inheritdoc/>
	public virtual async Task<IEnumerable<T>> GetAsync()
	{
		using Activity? activity = ActivitySourceProvider.StartActivity("repo.get");
		Stopwatch? sw = Stopwatch.StartNew();
		try
		{

			HashEntry[]? objects = await _database.HashGetAllAsync(BaseKey);

			IDictionary<string, T?> values = objects.ToDictionary(x => x.Name.ToString(), x => JsonSerializer.Deserialize<T>(x.Value));

			return values.Where(x => x.Value is not null).Select(x => x.Value);
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
	}

	/// <inheritdoc/>
	public virtual IEnumerable<T> Get()
	{
		using Activity? activity = ActivitySourceProvider.StartActivity("repo.get");
		Stopwatch? sw = Stopwatch.StartNew();
		try
		{
			HashEntry[]? objects = _database.HashGetAll(BaseKey);

			IDictionary<string, T?> values = objects.ToDictionary(x => x.Name.ToString(), x => JsonSerializer.Deserialize<T>(x.Value));

			return values.Where(x => x.Value is not null).Select(x => x.Value);
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
	}

	public virtual async Task<IEnumerable<T>> WhereAsync(Expression<Func<T, bool>> predicate)
	{
		using Activity? activity = ActivitySourceProvider.StartActivity("repo.where");
		Stopwatch? sw = Stopwatch.StartNew();
		try
		{

			IndexConditionExtractor<T>? extractor = new(
				_indexers?.ToDictionary(x => x.Name, x => x.Index) ?? []
				);
			extractor.Visit(predicate.Body);

			List<IndexedCondition>? indexConditions = extractor.IndexedMatches;

			if (indexConditions.Count == 0)
				return Get().Where(predicate.Compile());

			IEnumerable<string>? ids = null;
			foreach (var condition in indexConditions)
			{
				string? key = $"{IndexBaseKey}:{condition.IndexName}:{IndexKeyHelper.NormalizeValue(condition.Value)}";
				RedisValue[]? members = await _database.SetMembersAsync(key);
				IEnumerable<string>? currentIds = members.Select(m => m.ToString());

				ids = ids == null ? currentIds : ids.Intersect(currentIds);
			}

			if (ids is null)
				return Enumerable.Empty<T>();
			IEnumerable<T>? items = ids.Select(id => Get(id));

			return items.Where(x => x is not null).Where(predicate.Compile()).Select(x => x!);
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error searching items in repository");
			throw;
		}
		finally
		{
			_metrics?.ObserveDuration("repo.where", sw.Elapsed);
			activity?.Stop();
		}
	}

	/// <inheritdoc/>
	public virtual async Task Purge()
	{
		using Activity? activity = ActivitySourceProvider.StartActivity("repo.purge");
		Stopwatch? sw = Stopwatch.StartNew();
		try
		{
			ITransaction transaction = _database.CreateTransaction();
			transaction.KeyDeleteAsync(BaseKey);
			transaction.KeyDeleteAsync(BaseKeyTracker);
			PurgeIndex(ref transaction);
			await transaction.ExecuteAsync();
			await _bus.PublishAsync(_busChannel, GenerateMessage(MessageType.Purged, null));
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
	public virtual void PurgeIndex(ref ITransaction transaction)
	{
		// Delete all index values and their corresponding sets
		if (_indexers is null)
			return;
		string pattern = $"{IndexBaseKey}:*";

		RedisResult? result = _database.ScriptEvaluate($"return redis.call('keys', '{pattern}')");

		if (result is not null && result.Resp2Type == ResultType.Array)
		{
			foreach (RedisResult redisValue in (RedisResult[])result!)
			{
				if (redisValue is null) continue;
				string key = (string)redisValue!;
				transaction.KeyDeleteAsync(key);
			}
		}
	}
	/// <inheritdoc/>
	public virtual async Task Rebuild()
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

		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "Error rebuilding all items in repository");
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
		if (message.i == _instanceId)
			return;

		return;
	}

	internal string GenerateMessage(MessageType messageType, string? resourceKey)
	{
		return resourceKey is null
			? $"{{ \"i\":\"{_instanceId}\", \"type\":{(int)messageType}}}"
			: $"{{ \"i\":\"{_instanceId}\", \"type\":{(int)messageType},\"item\":\"{resourceKey}\"}}";
	}

	protected string IndexKey(string name, object value) =>
		$"{IndexBaseKey}:{name}:{IndexKeyHelper.NormalizeValue(value)}";
}
