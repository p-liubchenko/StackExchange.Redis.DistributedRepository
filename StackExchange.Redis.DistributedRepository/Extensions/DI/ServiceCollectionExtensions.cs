using Microsoft.Extensions.DependencyInjection;

namespace StackExchange.Redis.DistributedRepository.Extensions.DI;
public static class ServiceCollectionExtensions
{
	public static IServiceCollection AddDistributedRepository<T>(this IServiceCollection services, Func<T, string> keySelector) where T : class
	{
		services.AddSingleton<DistributedHashRepository<T>>();
		DistributedHashRepository<T>.KeySelector = keySelector;
		return services;
	}
}
