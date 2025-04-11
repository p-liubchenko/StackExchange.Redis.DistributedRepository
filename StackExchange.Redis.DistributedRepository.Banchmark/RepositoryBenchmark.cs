using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis.DistributedRepository.Extensions.DI;

namespace StackExchange.Redis.DistributedRepository.Banchmark;
public class RepositoryBenchmark
{
	IDistributedRepository<TestObjectModel> _dr1 = Start();
	IDistributedRepository<TestObjectModel> _dr2 = Start();

	TestObjectModel tom = new TestObjectModel()
	{
		Name = "Test",
		Description = "Test Description",
		DecVal = 1.1m
	};

	TestObjectModel tom2 = new TestObjectModel()
	{
		Name = "Test",
		Description = "Test Description",
		DecVal = 1.1m
	};

	TestObjectModel tom3 = new TestObjectModel()
	{
		Name = "Test",
		Description = "Test Description",
		DecVal = 1.1m
	};

	string _firstKey;
	string _firstKey2;

	public RepositoryBenchmark()
	{
		_firstKey = tom.Id.ToString();
		_firstKey2 = tom2.Id.ToString();
		_dr1.Purge();
		List<TestObjectModel> list = new List<TestObjectModel>();
		for (int i = 0; i < 100000; i++)
		{
			list.Add(new TestObjectModel()
			{
				Name = "pre",
				Description = "pre-pre",
				DecVal = 1.1m
			});
		}
		_dr1.AddRange(list);
	}

	[Benchmark]
	public string GetFromSame()
	{
		_dr1.Add(tom);
		return _dr1.Get(_firstKey)?.Name;
	}

	[Benchmark]
	public string GetSingle()
	{
		_dr1.Add(tom2);
		return _dr2.Get(_firstKey2)?.Name;
	}

	[Benchmark]
	public void GetAll()
	{
		var all = _dr2.Get();
	}

	//[Benchmark]
	//public string GetMessageInterpolation()
	//{
	//	//return DistributedHashRepository<object>.GenerateMessageInterpolation(MessageType, testResourceKey);
	//}

	public static IDistributedRepository<TestObjectModel> Start()
	{
		IConfiguration configuration = new ConfigurationBuilder()
			.AddUserSecrets<Program>().Build();

		ServiceCollection services = new ServiceCollection();
		services.AddMemoryCache();
		services.AddLogging();
		services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(configuration.GetConnectionString("redis")));

		services.AddDistributedRepository<TestObjectModel>((x) => x.Id.ToString());
		IServiceProvider serviceProvider = services.BuildServiceProvider();
		var repo = serviceProvider.GetRequiredService<IDistributedRepository<TestObjectModel>>();
		return repo;
	}
}

public class TestObjectModel
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public string? Name { get; set; }
	public string? Description { get; set; }
	public decimal DecVal { get; set; }
	public DateTime Created { get; set; } = DateTime.UtcNow;
}
