namespace StackExchange.Redis.DistributedRepository.Models;
public class Message
{
	public string i { get; set; }
	public MessageType type { get; set; }
	public string? item { get; set; }
}
