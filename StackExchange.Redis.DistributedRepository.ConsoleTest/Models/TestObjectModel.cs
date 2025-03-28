namespace StackExchange.Redis.DistributedRepository.ConsoleTest.Models;

public class TestObjectModel
{
	public Guid Id { get; set; } = Guid.NewGuid();
	public string Name { get; set; }
	public string Description { get; set; }
	public decimal DecVal { get; set; }
	public DateTime Created { get; set; } = DateTime.UtcNow;
}
