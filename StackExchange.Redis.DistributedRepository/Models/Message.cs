using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StackExchange.Redis.DistributedRepository.Models;
public class Message
{
	public string i { get; set; }
	public MessageType type { get; set; }
	public string? item { get; set; }
}
