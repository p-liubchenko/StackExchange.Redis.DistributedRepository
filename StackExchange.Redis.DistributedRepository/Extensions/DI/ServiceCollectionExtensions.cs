﻿using System.Linq.Expressions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis.Extensions.Core.Abstractions;

namespace StackExchange.Redis.DistributedRepository.Extensions.DI;
public static class ServiceCollectionExtensions
{
	/// <summary>
	/// Adds a distributed repository to the service collection
	/// </summary>
	/// <typeparam name="T">Entity type</typeparam>
	/// <param name="services"></param>
	/// <param name="keySelector"></param>
	/// <returns></returns>
	public static IServiceCollection AddDistributedRepository<T>(this IServiceCollection services, Func<T, string> keySelector) where T : class
	{
		services.AddScoped<IDistributedRepository<T>>((provider) =>
		{
			IRedisClient redis = provider.GetRequiredService<IRedisClient>();
			IMemoryCache memoryCache = provider.GetRequiredService<IMemoryCache>();
			IRepositoryMetrics? metrics = provider.GetService<IRepositoryMetrics>();
			ILogger<DistributedRepository<T>>? logger = provider.GetService<ILogger<DistributedRepository<T>>>();
			IEnumerable<RedisIndexer<T>>? indexers = provider.GetServices<RedisIndexer<T>>();
			return new DistributedRepository<T>(redis, keySelector, metrics, logger, indexers);
		});
		return services;
	}

	/// <summary>
	/// Adds a distributed repository with memory mirror to the service collection
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="services"></param>
	/// <param name="keySelector"></param>
	/// <returns></returns>
	public static IServiceCollection AddDistributedBackedRepository<T>(this IServiceCollection services, Func<T, string> keySelector) where T : class
	{
		services.AddScoped<IDistributedRepository<T>>((provider) =>
		{
			IRedisClient redis = provider.GetRequiredService<IRedisClient>();
			IMemoryCache memoryCache = provider.GetRequiredService<IMemoryCache>();
			IRepositoryMetrics? metrics = provider.GetService<IRepositoryMetrics>();
			ILogger<DistributedBackedRepository<T>>? logger = provider.GetService<ILogger<DistributedBackedRepository<T>>>();
			IEnumerable<RedisIndexer<T>>? indexers = provider.GetServices<RedisIndexer<T>>();
			return new DistributedBackedRepository<T>(redis, memoryCache, keySelector, metrics, logger, indexers);
		});
		return services;
	}

	public static IServiceCollection AddIndexer<T>(this IServiceCollection services, string indexName, Expression<Func<T, object>> expression)
	{
		services.AddSingleton<RedisIndexer<T>>((provider) =>
		{
			return new RedisIndexer<T>(indexName, expression);
		});
		return services;
	}

	public static IServiceCollection AddIndexer<T>(this IServiceCollection services, Expression<Func<T, object>> expression)
	{
		services.AddSingleton<RedisIndexer<T>>((provider) =>
		{
			return new RedisIndexer<T>(IndexingExtensions.ExtractPropertyName(expression), expression);
		});
		return services;
	}

	public static IServiceCollection AddDistributedCache<T>(this IServiceCollection services, Func<T, string> keySelector) where T : class
	{
		services.AddScoped<IDistributedCache>((provider) =>
		{
			IRedisClient redis = provider.GetRequiredService<IRedisClient>();
			IMemoryCache memoryCache = provider.GetRequiredService<IMemoryCache>();
			return new DistributedBackedRepository<T>(redis, memoryCache, keySelector);
		});
		return services;
	}
}
