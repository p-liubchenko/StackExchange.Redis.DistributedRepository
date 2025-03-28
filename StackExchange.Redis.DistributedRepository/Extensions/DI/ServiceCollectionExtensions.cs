using Microsoft.Extensions.DependencyInjection;

namespace StackExchange.Redis.DistributedRepository.Extensions.DI;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDistributedRepository<T>(IServiceCollection services)
    {
        services.AddSingleton<DistributedSetRepository<T>>();
        return services;
    }
}
