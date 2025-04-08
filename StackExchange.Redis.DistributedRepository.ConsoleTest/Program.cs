using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis.DistributedRepository.ConsoleTest.Models;
using StackExchange.Redis.DistributedRepository.Extensions.DI;
using StackExchange.Redis.Extensions.System.Text.Json;

namespace StackExchange.Redis.DistributedRepository.ConsoleTest;

internal class Program
{
	static object _lock = new object();
	static void Main(string[] args)
	{
		var repo1 = Start();
		bool exit = false;

		Task.Run(() =>
		{
			while (!exit)
			{
				lock (_lock)
				{
					Console.SetCursorPosition(0, 0);
					Console.Clear();
					var all = repo1.Get();
					int counter = 1;
					foreach (var item in all)
					{
						Console.WriteLine($"{counter} Id: {item.Id} Name: {item.Name} Description: {item.Description} DecVal: {item.DecVal}");
						counter++;
					}
					Console.WriteLine(StringRepositoryMetrics.ToUserFriendlyString());
				}
				Thread.Sleep(1000); // Redraw every 0.5 sec
			}
		});

		while (!exit)
		{
			if (Console.KeyAvailable)
			{
				var key = Console.ReadKey(true); // true = do not show key in console

				lock (_lock)
				{
					switch (key.Key)
					{
						case ConsoleKey.Enter:
							var obj = new TestObjectModel()
							{
                                Name = Random.Shared.Next(0, 2) == 0 ? "tester" : "worker",
								Description = "Test Description",
								DecVal = 1.1m
							};
							repo1.Add(obj);
							break;
						case ConsoleKey.C:
							repo1.Purge();
							break;
						case ConsoleKey.W:
							var found = repo1.WhereAsync(x => x.Name == "worker");
							foreach (var item in found.Result)
							{
								Console.WriteLine($"Found: {item.Id} Name: {item.Name} Description: {item.Description} DecVal: {item.DecVal}");
							}
							break;
						case ConsoleKey.Escape:
							exit = true;
							break;
					}
				}
			}
			Thread.Sleep(50); // Reduce CPU usage
		}
	}

	public static IDistributedRepository<TestObjectModel> Start()
	{
		IConfiguration configuration = new ConfigurationBuilder()
			.AddUserSecrets<Program>().Build();

		ServiceCollection services = new ServiceCollection();
		services.AddMemoryCache();
		services.AddLogging();
		services.AddScoped<IRepositoryMetrics, StringRepositoryMetrics>();

		services.AddStackExchangeRedisExtensions<SystemTextJsonSerializer>(new Redis.Extensions.Core.Configuration.RedisConfiguration()
		{
			ConnectionString = configuration.GetConnectionString("redis"),
			Database = 0,
			KeyPrefix = "local:"
		});
		services.AddDistributedRepository<TestObjectModel>((x) => x.Id.ToString());
		services.AddIndexer<TestObjectModel>(x=>x.Name);
		IServiceProvider serviceProvider = services.BuildServiceProvider();
		var repo = serviceProvider.GetRequiredService<IDistributedRepository<TestObjectModel>>();
		return repo;
	}
}
