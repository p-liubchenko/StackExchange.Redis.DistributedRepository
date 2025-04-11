namespace StackExchange.Redis.DistributedRepository.ConsoleTest.Models;

public class TestObjectModel
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public string Name { get; set; }
	public string Description { get; set; }
	public decimal DecVal { get; set; }
	public DateTime Created { get; set; } = DateTime.UtcNow;
	public TestEnum ObjType { get; set; } = TestEnum.None;
}

public enum TestEnum : short
{
	None = 0,
	One = 1,
	Two = 2,
	Three = 3,
	Four = 4,
	Five = 5
}
