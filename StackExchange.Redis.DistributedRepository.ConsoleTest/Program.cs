using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis.DistributedRepository.ConsoleTest.Models;
using StackExchange.Redis.DistributedRepository.Extensions.DI;
using StackExchange.Redis.Extensions.System.Text.Json;

namespace StackExchange.Redis.DistributedRepository.ConsoleTest;

internal class Program
{
	static void Main(string[] args)
	{
		var repo1 = Start();
		bool exit = false;

		while(!exit)
		{
			Console.Clear();
			var all = repo1.GetAll();
			foreach (var item in all)
			{
				Console.WriteLine($"Id: {item.Id} Name: {item.Name} Description: {item.Description} DecVal: {item.DecVal}");
			}
			var key = Console.ReadKey();

			switch (key.Key)
			{
				case ConsoleKey.Enter:
					var obj = new TestObjectModel()
					{
						Name = "Test",
						Description = "Test Description",
						DecVal = 1.1m
					};
					repo1.Add(obj);
					break;
				case ConsoleKey.Escape:
					exit = true;
					break;
			}
		}
	}

	public static DistributedHashRepository<TestObjectModel> Start()
	{
		IConfiguration configuration = new ConfigurationBuilder()
			.AddUserSecrets<Program>().Build();

		ServiceCollection services = new ServiceCollection();
		services.AddMemoryCache();
		services.AddLogging();

		services.AddStackExchangeRedisExtensions<SystemTextJsonSerializer>(new Redis.Extensions.Core.Configuration.RedisConfiguration()
		{
			ConnectionString = configuration.GetConnectionString("redis"),
			Database = 0,
			KeyPrefix = "TestPrefix"
		});
		services.AddDistributedRepository<TestObjectModel>((x)=>x.Id.ToString());
		IServiceProvider serviceProvider = services.BuildServiceProvider();
		var repo = serviceProvider.GetRequiredService<DistributedHashRepository<TestObjectModel>>();
		return repo;
	}
}
