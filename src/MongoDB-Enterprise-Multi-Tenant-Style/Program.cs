/*
dotnet add package Microsoft.Extensions.Configuration --version 8.0.0
dotnet add package Microsoft.Extensions.Configuration.Binder --version 8.0.0
dotnet add package Microsoft.Extensions.Configuration.FileExtensions --version 8.0.0
dotnet add package Microsoft.Extensions.Configuration.Json --version 8.0.0
dotnet add package Microsoft.Extensions.DependencyInjection --version 8.0.0
dotnet add package MongoDB.Driver
 */

using System.Collections.Concurrent;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

// ====================================================================================================
namespace MongoDB_Enterprise_Multi_Tenant_Style;
// ====================================================================================================
class Program
{
    static async Task Main(string[] args)
    {
        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        // => ConfigurationBuilder class    : Using Microsoft.Extensions.Configuration
        // => .SetBasePath method           : Using Microsoft.Extensions.Configuration.FileExtensions
        // => .AddJsonFile method           : Using Microsoft.Extensions.Configuration.Json
        // => .Build method                 : Using Microsoft.Extensions.Configuration.Abstractions

        // Setup DI
        var services = new ServiceCollection();
        // => ServiceCollection class       : Using Microsoft.Extensions.DependencyInjection.Abstractions

        services.AddSingleton<IConfiguration>(configuration);
        // => IConfiguration interface      : Using Microsoft.Extensions.Configuration.Abstractions

        services.AddSingleton<IMultiTenantMongoDbContextFactory, MultiTenantMongoDbContextFactory>();

        services.AddTransient<UserService>();
        // => .AddTransient method          : Using Microsoft.Extensions.DependencyInjection.Abstractions

        var serviceProvider = services.BuildServiceProvider();
        // => .BuildServiceProvider method  : Using Microsoft.Extensions.DependencyInjection

        var userService = serviceProvider.GetRequiredService<UserService>();
        // => .GetRequiredService method    : Using Microsoft.Extensions.DependencyInjection.Abstractions

        // Add users to different tenants
        await userService.AddUserAsync("Tenant1", new User { Name = "Alice", Email = "alice@tenant1.com" });
        await userService.AddUserAsync("Tenant2", new User { Name = "Bob", Email = "bob@tenant2.com" });

        // Read users per tenant
        var tenant1Users = await userService.GetUsersAsync("Tenant1");
        var tenant2Users = await userService.GetUsersAsync("Tenant2");

        Console.WriteLine("Tenant1 Users:");
        tenant1Users.ForEach(u => Console.WriteLine($"{u.Name} - {u.Email}"));

        Console.WriteLine("Tenant2 Users:");
        tenant2Users.ForEach(u => Console.WriteLine($"{u.Name} - {u.Email}"));
    }
}

// ====================================================================================================
// namespace MongoDB_Enterprise_Multi_Tenant_Style.Data;
// ====================================================================================================

public interface IMongoDbContext
{
    IMongoDatabase Database { get; }
}

public interface IDesignTimeMongoDbContextFactory<TContext> where TContext : IMongoDbContext
{
    TContext CreateDbContext(string? databaseName = null);
}


public interface IMultiTenantMongoDbContextFactory
{
    IMongoDbContext CreateDbContext(string tenantId);
}

public class MultiTenantMongoDbContextFactory : IMultiTenantMongoDbContextFactory
{
    private readonly IDictionary<string, TenantSettings> _tenantSettings;
    private readonly ConcurrentDictionary<string, MongoClient> _clientCache = new();

    public MultiTenantMongoDbContextFactory(IConfiguration configuration)
    {
        var tenants = configuration.GetSection("Tenants").Get<List<TenantSettings>>();
        // => .Get method                   : Using Microsoft.Extensions.Configuration.Binder
        if (tenants == null)
        {
            throw new InvalidOperationException("No tenants configured in appsettings.json.");
        }
        _tenantSettings = configuration.GetSection("Tenants").Get<List<TenantSettings>>()!.ToDictionary(t => t.TenantId, t => t);
        // => .Get method                   : Using Microsoft.Extensions.Configuration.Binder
    }

    // => IMongoDbContext interface         : Using MongoDB.Driver
    public IMongoDbContext CreateDbContext(string tenantId)
    {
        if (!_tenantSettings.TryGetValue(tenantId, out var settings))
        {
            throw new ArgumentException($"Tenant '{tenantId}' not found.");
        }

        var client = _clientCache.GetOrAdd(tenantId, id =>
        {
            var mongoSettings = MongoClientSettings.FromConnectionString(settings.ConnectionString);
            // => MongoClientSettings class : Using MongoDB.Driver
            mongoSettings.MaxConnectionPoolSize = settings.MaxPoolSize;
            mongoSettings.MinConnectionPoolSize = settings.MinPoolSize;
            mongoSettings.WaitQueueTimeout = TimeSpan.FromSeconds(settings.WaitQueueTimeoutSeconds);
            return new MongoClient(mongoSettings);
            // => MongoClient class         : Using MongoDB.Driver
        });

        return new MongoDbContext(client, settings.DatabaseName);
        // => MongoDbContext class          : Using MongoDB.Driver
    }
}

public class MongoDbContext : IMongoDbContext
{
    public IMongoDatabase Database { get; }

    public MongoDbContext(MongoClient client, string databaseName)
    {
        Database = client.GetDatabase(databaseName);
    }

    public MongoDbContext(string connectionString, string databaseName)
    {
        var client = new MongoClient(connectionString);
        Database = client.GetDatabase(databaseName);
    }
}

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
        if (connectionString == null)
        {
            throw new InvalidOperationException("No ConnectionStrings:MongoDb configured in appsettings.json.");
        }
        var dbName = databaseName ?? _configuration["MongoDbSettings:DatabaseName"];
        if (dbName == null)
        {
            throw new InvalidOperationException("No MongoDbSettings:DatabaseName configured in appsettings.json.");
        }
        return new MongoDbContext(connectionString, dbName);
    }
}

public class TenantSettings
{
    public string TenantId { get; set; } = default!;
    public string ConnectionString { get; set; } = default!;
    public string DatabaseName { get; set; } = default!;
    public int MaxPoolSize { get; set; } = 100;
    public int MinPoolSize { get; set; } = 0;
    public int WaitQueueSize { get; set; } = 500;
    public int WaitQueueTimeoutSeconds { get; set; } = 5;
}

// ====================================================================================================
// namespace MongoDB_Enterprise_Multi_Tenant_Style.Models;
// ====================================================================================================
public class User
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = default!;

    [BsonElement("Name")]
    public string Name { get; set; } = default!;

    [BsonElement("Email")]
    public string Email { get; set; } = default!;
}

// ====================================================================================================
// namespace MongoDB_Enterprise_Multi_Tenant_Style.Services;
// ====================================================================================================
public class UserService
{
    private readonly IMultiTenantMongoDbContextFactory _factory;

    public UserService(IMultiTenantMongoDbContextFactory factory)
    {
        _factory = factory;
    }

    public async Task<List<User>> GetUsersAsync(string tenantId)
    {
        var context = _factory.CreateDbContext(tenantId);
        var collection = context.Database.GetCollection<User>("Users");
        return await collection.Find(_ => true).ToListAsync();
    }

    public async Task AddUserAsync(string tenantId, User user)
    {
        var context = _factory.CreateDbContext(tenantId);
        var collection = context.Database.GetCollection<User>("Users");
        await collection.InsertOneAsync(user);
    }
}
