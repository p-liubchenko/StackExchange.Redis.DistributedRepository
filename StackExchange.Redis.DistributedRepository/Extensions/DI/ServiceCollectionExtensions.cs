using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
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
			IRedisClient? redis = provider.GetRequiredService<IRedisClient>();
			IMemoryCache? memoryCache = provider.GetRequiredService<IMemoryCache>();
			return new DistributedRepository<T>(redis, memoryCache, keySelector);
		});
		return services;
	}

	public static IServiceCollection AddDistributedCache<T>(this IServiceCollection services, Func<T, string> keySelector) where T : class
	{
		services.AddScoped<IDistributedCache>((provider) =>
		{
			IRedisClient? redis = provider.GetRequiredService<IRedisClient>();
			IMemoryCache? memoryCache = provider.GetRequiredService<IMemoryCache>();
			return new DistributedRepository<T>(redis, memoryCache, keySelector);
		});
		return services;
	}
}
