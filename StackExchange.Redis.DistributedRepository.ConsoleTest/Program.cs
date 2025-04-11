using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis.DistributedRepository.ConsoleTest.Models;
using StackExchange.Redis.DistributedRepository.Extensions.DI;

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
								DecVal = 1.1m,
								ObjType = (TestEnum)Random.Shared.Next(0, 5),
							};
							repo1.Add(obj);
							break;
						case ConsoleKey.C:
							repo1.Purge();
							break;
						case ConsoleKey.W:
							var found = repo1.WhereAsync(x => x.Name == "worker" && x.Created.Year == 2025 && x.ObjType == TestEnum.None);
							foreach (var item in found.Result)
							{
								Console.WriteLine($"Found: {item.Id} Name: {item.Name} Description: {item.Description} DecVal: {item.DecVal} OnjType: {item.ObjType}");
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
		services.AddSingleton<IConnectionMultiplexer>((provider) =>
		{
			var config = configuration.GetConnectionString("redis");
			return ConnectionMultiplexer.Connect(config);
		});
		services.AddDistributedRepository<TestObjectModel>((x) => x.Id.ToString());
		services.AddIndexer<TestObjectModel>(x => x.Name);
		services.AddIndexer<TestObjectModel>(x => x.Created.Date.Year);
		services.AddIndexer<TestObjectModel>(x => x.Created.Date.Month);
		services.AddIndexer<TestObjectModel>(x => x.ObjType);
		IServiceProvider serviceProvider = services.BuildServiceProvider();
		var repo = serviceProvider.GetRequiredService<IDistributedRepository<TestObjectModel>>();
		return repo;
	}
}
