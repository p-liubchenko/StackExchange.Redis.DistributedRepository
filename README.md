# StackExchange.Redis.DistributedRepository

A lightweight, memory-optimized distributed repository built on top of [StackExchange.Redis](https://www.nuget.org/packages/StackExchange.Redis), leveraging Redis hashes for structured data and IMemoryCache for local caching and change notifications.

> ⚠️ **Note:** Due to NuGet's reserved prefix policy, this library's NuGet package ID may be different than the namespace.

---

## 🚀 Features

- Distributed repository pattern using Redis hashes
- Local caching with `IMemoryCache`
- Real-time cache updates via Redis pub/sub
- Auto-generated keys with customizable logic
- Extensible and easy-to-integrate with DI

---

## 📦 Installation

Install via NuGet:

```bash
dotnet add package PL.StackExchange.Redis.DistributedRepository

```
## 🧠 Usage
1. Register the repository in DI

```cs
builder.Services.AddMemoryCache();
builder.Services.AddStackExchangeRedisExtensions<NewtonsoftSerializer>(redisConfig);
builder.Services.AddDistributedRepository<MyEntity>(x => x.Id);
```
2. Use the repository

```cs
public class MyService
{
    private readonly DistributedHashRepository<MyEntity> _repository;

    public MyService(DistributedHashRepository<MyEntity> repository)
    {
        _repository = repository;
    }

    public void DoStuff()
    {
        var entity = new MyEntity { Id = "abc", Name = "Test" };
        _repository.Add(entity);

        var retrieved = _repository.Get("abc");

        var all = _repository.GetAll();
    }
}
```

## 🧩 Under the Hood

- Data is stored as a Redis hash under a key derived from the type name.
- Each entity is stored using a key from your provided KeySelector.
- Changes (add/update/delete) are published via Redis Pub/Sub to keep all nodes in sync.
- Local memory cache is updated accordingly.

## 💡 Design Notes

- You can override ItemUpdated for advanced synchronization handling.

- InternalsVisibleTo("StackExchange.Redis.DistributedRepository.Banchmark") is set for benchmark testing.

- Includes a fallback ItemUpdatedLegacy for compatibility.

## 📄 License

## 🙏 Acknowledgements
Built on top of:
- [StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis)
- [Microsoft.Extensions.Caching.Memory](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.caching.memory)

## 📬 Feedback & Contributions
Pull requests and feedback are welcome!