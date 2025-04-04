﻿using System.Text.Json;
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
					var all = repo1.GetAll();
					foreach (var item in all)
					{
						Console.WriteLine($"Id: {item.Id} Name: {item.Name} Description: {item.Description} DecVal: {item.DecVal}");
					}
				}
				Thread.Sleep(500); // Redraw every 0.5 sec
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
			Thread.Sleep(50); // Reduce CPU usage
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
		services.AddDistributedRepository<TestObjectModel>((x) => x.Id.ToString());
		IServiceProvider serviceProvider = services.BuildServiceProvider();
		var repo = serviceProvider.GetRequiredService<DistributedHashRepository<TestObjectModel>>();
		return repo;
	}
}
