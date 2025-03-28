namespace StackExchange.Redis.DistributedRepository;

/// <summary>
/// The type of message that was sent
/// </summary>
public enum MessageType
{
	Created,
	Updated,
	Deleted,
	Purged
}
