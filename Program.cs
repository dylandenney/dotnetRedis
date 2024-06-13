using System;
using System.Threading.Tasks;
using Npgsql; // For PostgreSQL connection
using StackExchange.Redis; // For Redis connection and operations

class Program
{
    static async Task Main(string[] args)
    {
        // Reading environment variables for configuration
        var redisHost = Environment.GetEnvironmentVariable("REDIS_HOST") ?? string.Empty;
        var redisPort = Environment.GetEnvironmentVariable("REDIS_PORT") ?? string.Empty;
        var redisPassword = Environment.GetEnvironmentVariable("REDIS_PASSWORD") ?? string.Empty;
        var pgHost = Environment.GetEnvironmentVariable("PG_HOST") ?? string.Empty;
        var pgDb = Environment.GetEnvironmentVariable("PG_DB") ?? string.Empty;
        var pgUser = Environment.GetEnvironmentVariable("PG_USER") ?? string.Empty;
        var pgPassword = Environment.GetEnvironmentVariable("PG_PASSWORD") ?? string.Empty;
        var lockName = "my_lock"; // Lock name used in Redis to control access

        // Logging connection details for debugging
        Console.WriteLine($"Connecting to Redis at {redisHost}:{redisPort} with provided password.");
        Console.WriteLine($"Connecting to PostgreSQL at {pgHost}.");

        // Configure Redis connection settings
        var options = new ConfigurationOptions
        {
            EndPoints = { $"{redisHost}:{redisPort}" },
            Password = redisPassword,
            Ssl = false // Set to true if your Redis server uses SSL
        };

        // Connect to Redis
        var redis = ConnectionMultiplexer.Connect(options);
        var db = redis.GetDatabase(); // Access the default database

        // Connection string for PostgreSQL
        var connectionString = $"Host={pgHost};Username={pgUser};Password={pgPassword};Database={pgDb}";

        // Open PostgreSQL connection
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        // Initialize random number generator
        var random = new Random();

        // Infinite loop to keep the application running and processing tasks
        while (true)
        {
            // Key used for locking mechanism in Redis
            var lockKey = $"lock_{lockName}";
            var lockToken = Guid.NewGuid().ToString(); // Unique token for the lock
            var lockTimeout = TimeSpan.FromSeconds(10); // Lock timeout

            try
            {
                // Lua script to acquire the lock in Redis
                var script = @"
                    if redis.call('setnx', KEYS[1], ARGV[1]) == 1 then
                        redis.call('pexpire', KEYS[1], ARGV[2])
                        return true
                    else
                        return false
                    end";

                // Execute the script to acquire the lock
                var acquired = (bool)(await db.ScriptEvaluateAsync(script, new RedisKey[] { lockKey }, new RedisValue[] { lockToken, lockTimeout.TotalMilliseconds }));

                if (acquired)
                {
                    Console.WriteLine("Lock acquired");

                    // Generate a unique GUID for each entry
                    var uniqueGuid = Guid.NewGuid();

                    // Generate random data
                    var randomValue = random.Next(1, 10000); // Generate a random number between 1 and 10000
                    var data = $"Random data {randomValue} at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}";

                    // Insert new data into the PostgreSQL database
                    var insertCmd = new NpgsqlCommand("INSERT INTO my_table (guid, data) VALUES (@guid, @data)", conn);
                    insertCmd.Parameters.Add(new NpgsqlParameter("guid", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = uniqueGuid });
                    insertCmd.Parameters.AddWithValue("data", data);
                    await insertCmd.ExecuteNonQueryAsync();

                    // Set the key in Redis with a TTL of 30 seconds to mark data as processed
                    var redisKey = $"data_{uniqueGuid}";
                    await db.StringSetAsync(redisKey, data, TimeSpan.FromSeconds(30));

                    Console.WriteLine("Data written to database: " + data);
                }
                else
                {
                    Console.WriteLine("Could not acquire lock");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                // Lua script to release the lock in Redis
                var scriptRelease = @"
                    if redis.call('get', KEYS[1]) == ARGV[1] then
                        return redis.call('del', KEYS[1])
                    else
                        return 0
                    end";

                // Execute the script to release the lock
                await db.ScriptEvaluateAsync(scriptRelease, new RedisKey[] { lockKey }, new RedisValue[] { lockToken });
                Console.WriteLine("Lock released");
            }

            // Wait for 1 second before the next iteration to avoid tight loops
            await Task.Delay(1000);
        }
    }
}

