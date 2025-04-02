using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis.Extensions.Core.Abstractions;

namespace StackExchange.Redis.DistributedRepository.Extensions.DI;
public static class ServiceCollectionExtensions
{
	public static IServiceCollection AddDistributedRepository<T>(this IServiceCollection services, Func<T, string> keySelector) where T : class
	{
		services.AddScoped<DistributedHashRepository<T>>((provider) =>
		{
			IRedisClient? redis = provider.GetRequiredService<IRedisClient>();
			IMemoryCache? memoryCache = provider.GetRequiredService<IMemoryCache>();
			return new DistributedHashRepository<T>(redis, memoryCache, keySelector);
		});
		return services;
	}

	public static IServiceCollection AddDistributedCache<T>(this IServiceCollection services, Func<T, string> keySelector) where T : class
	{
		services.AddScoped<IDistributedCache>((provider) =>
		{
			IRedisClient? redis = provider.GetRequiredService<IRedisClient>();
			IMemoryCache? memoryCache = provider.GetRequiredService<IMemoryCache>();
			return new DistributedHashRepository<T>(redis, memoryCache, keySelector);
		});
		return services;
	}
}
