# MongoDB

You’re asking about IMongoDbContextFactory. This is typically used in applications that interact with MongoDB and follow the unit-of-work / repository pattern, similar to how IDbContextFactory<T> works in Entity Framework. It’s usually part of custom abstractions, because MongoDB’s official driver does not define IMongoDbContextFactory by default — it’s something you or a library might implement to manage IMongoDatabase instances.

I’ll explain how to set it up and use it properly step by step.

## 1. Define the MongoDB Context
```
public interface IMongoDbContext
{
    IMongoDatabase Database { get; }
}

public class MongoDbContext : IMongoDbContext
{
    public IMongoDatabase Database { get; }

    public MongoDbContext(string connectionString, string databaseName)
    {
        var client = new MongoClient(connectionString);
        Database = client.GetDatabase(databaseName);
    }
}
```

## 2. Define IMongoDbContextFactory
```
public interface IMongoDbContextFactory
{
    IMongoDbContext CreateDbContext();
}
```

## 3. Implement the Factory
```
public class MongoDbContextFactory : IMongoDbContextFactory
{
    private readonly IConfiguration _configuration;

    public MongoDbContextFactory(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public IMongoDbContext CreateDbContext()
    {
        var connectionString = _configuration.GetConnectionString("MongoDb");
        var databaseName = _configuration["MongoDbSettings:DatabaseName"];
        return new MongoDbContext(connectionString, databaseName);
    }
}
```

## 4. appsettings.json example:
```
{
  "ConnectionStrings": {
    "MongoDb": "mongodb://localhost:27017"
  },
  "MongoDbSettings": {
    "DatabaseName": "MyDatabase"
  }
}
```

## 5. Register in DI Container
```
builder.Services.AddSingleton<IMongoDbContextFactory, MongoDbContextFactory>();
```

**You can use Singleton because MongoClient is thread-safe. If you need scoped behavior, you can change it to Scoped.**

## 6. Using the Factory in a Repository or Service
```
public class UserRepository
{
    private readonly IMongoDbContext _context;

    public UserRepository(IMongoDbContextFactory dbContextFactory)
    {
        _context = dbContextFactory.CreateDbContext();
    }

    public async Task<List<User>> GetAllUsersAsync()
    {
        var collection = _context.Database.GetCollection<User>("Users");
        return await collection.Find(_ => true).ToListAsync();
    }
}
```

## 7. Benefits of Using a Factory
+ Decouples MongoDB configuration from repositories/services.
+ Supports multiple database instances dynamically.
+ Makes testing easier because you can mock IMongoDbContextFactory.

## 8. IDesignTimeDbContextFactory

Define a Design-Time Factory Interface (Optional)

```
public interface IDesignTimeMongoDbContextFactory<TContext> where TContext : IMongoDbContext
{
    TContext CreateDbContext(string? databaseName = null);
}
```

databaseName is optional; if null, default database from configuration is used.

This is especially useful for unit tests or runtime switching.

## 9. Implement the IDesignTimeDbContextFactory
```
public class DesignTimeMongoDbContextFactory : IDesignTimeMongoDbContextFactory<MongoDbContext>
{
    private readonly IConfiguration _configuration;

    public DesignTimeMongoDbContextFactory()
    {
        // Load appsettings.json manually (like EF Core does at design time)
        _configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();
    }

    public MongoDbContext CreateDbContext(string? databaseName = null)
    {
        var connectionString = _configuration.GetConnectionString("MongoDb");
        var dbName = databaseName ?? _configuration["MongoDbSettings:DatabaseName"];
        return new MongoDbContext(connectionString, dbName);
    }
}
```

## 10. Example MongoDbContext
```
public class MongoDbContext : IMongoDbContext
{
    public IMongoDatabase Database { get; }

    public MongoDbContext(string connectionString, string databaseName)
    {
        var client = new MongoClient(connectionString);
        Database = client.GetDatabase(databaseName);
    }
}
```


## 11. Using the Design-Time Factory

### a) For a Multi-Tenant Scenario
```
var factory = new DesignTimeMongoDbContextFactory();
var tenant1Context = factory.CreateDbContext("Tenant1Db");
var tenant2Context = factory.CreateDbContext("Tenant2Db");

var users1 = await tenant1Context.Database.GetCollection<User>("Users")
                                       .Find(_ => true).ToListAsync();

var users2 = await tenant2Context.Database.GetCollection<User>("Users")
                                       .Find(_ => true).ToListAsync();
```

### b) For Unit Testing
```
var factory = new DesignTimeMongoDbContextFactory();
var testContext = factory.CreateDbContext("TestDatabase");

await testContext.Database.DropCollectionAsync("Users"); // reset
```

## 12. Register in DI for Runtime Use
```
builder.Services.AddSingleton<IDesignTimeMongoDbContextFactory<MongoDbContext>, DesignTimeMongoDbContextFactory>();
builder.Services.AddScoped<IMongoDbContext>(sp =>
{
    var factory = sp.GetRequiredService<IDesignTimeMongoDbContextFactory<MongoDbContext>>();
    return factory.CreateDbContext(); // default database
});
```

Now all services or repositories can inject IMongoDbContext and optionally switch to another database using the factory.

### Advantages
+ Switch databases at runtime dynamically (multi-tenancy).
+ Unit tests can use a separate database easily.
+ Similar design-time pattern as EF Core — easy to understand and extend.
+ Keeps your repositories decoupled from database creation logic.
