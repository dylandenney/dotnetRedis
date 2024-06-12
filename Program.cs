﻿using System;
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

                    // Key used to check if data is already processed
                    var key = "example_data_key";
                    var exists = await db.StringGetAsync(key); // Check if key exists in Redis

                    if (exists.IsNullOrEmpty)
                    {
                        // Validate against the database to ensure no duplicate entries
                        var checkCmd = new NpgsqlCommand("SELECT COUNT(*) FROM my_table WHERE data = @data", conn);
                        checkCmd.Parameters.AddWithValue("data", "example_data");
                        var count = (long)await checkCmd.ExecuteScalarAsync();

                        if (count == 0)
                        {
                            // Set the key in Redis with a TTL of 30 seconds to mark data as processed
                            await db.StringSetAsync(key, "example_data", TimeSpan.FromSeconds(30));

                            // Insert new data into the PostgreSQL database
                            var insertCmd = new NpgsqlCommand("INSERT INTO my_table (data) VALUES (@data)", conn);
                            insertCmd.Parameters.AddWithValue("data", "example_data");
                            await insertCmd.ExecuteNonQueryAsync();
                            Console.WriteLine("Data written to database");
                        }
                        else
                        {
                            // Log that the data already exists in the database
                            Console.WriteLine("Duplicate data detected in the database, skipping write.");
                        }
                    }
                    else
                    {
                        // Log that the data already exists in Redis
                        Console.WriteLine("Duplicate data detected in Redis, skipping write.");
                    }
                }
                else
                {
                    // Log that the lock could not be acquired
                    Console.WriteLine("Could not acquire lock");
                }
            }
            catch (Exception ex)
            {
                // Log any errors that occur during processing
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

