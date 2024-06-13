using System;
using System.Threading.Tasks;
using Npgsql;
using StackExchange.Redis;

class Program
{
    static async Task Main(string[] args)
    {
        var redisHost = Environment.GetEnvironmentVariable("REDIS_HOST") ?? string.Empty;
        var redisPort = Environment.GetEnvironmentVariable("REDIS_PORT") ?? string.Empty;
        var redisPassword = Environment.GetEnvironmentVariable("REDIS_PASSWORD") ?? string.Empty;
        var pgHost = Environment.GetEnvironmentVariable("PG_HOST") ?? string.Empty;
        var pgDb = Environment.GetEnvironmentVariable("PG_DB") ?? string.Empty;
        var pgUser = Environment.GetEnvironmentVariable("PG_USER") ?? string.Empty;
        var pgPassword = Environment.GetEnvironmentVariable("PG_PASSWORD") ?? string.Empty;

        var redisOptions = new ConfigurationOptions
        {
            EndPoints = { $"{redisHost}:{redisPort}" },
            Password = redisPassword,
            Ssl = false
        };

        var redis = await ConnectionMultiplexer.ConnectAsync(redisOptions);
        var db = redis.GetDatabase();
        var streamName = "order_stream";
        var consumerGroup = "order_group";
        var consumerName = Guid.NewGuid().ToString();

        // Create the consumer group if it doesn't exist
        try
        {
            db.StreamCreateConsumerGroup(streamName, consumerGroup, "$");
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            Console.WriteLine("Consumer group already exists, skipping creation.");
        }

        var pgConnectionString = $"Host={pgHost};Username={pgUser};Password={pgPassword};Database={pgDb}";

        await using var pgConnection = new NpgsqlConnection(pgConnectionString);
        await pgConnection.OpenAsync();

        // Producing orders to the stream
        for (int i = 0; i < 10; i++) // Adjust the number of orders for testing
        {
            var orderNumber = $"ORD-{new Random().Next(1, 10000):D5}";
            var itemName = $"Item {new Random().Next(1, 100)}";
            var quantity = new Random().Next(1, 50);
            
            // Add order to stream
            await db.StreamAddAsync(streamName, new NameValueEntry[]
            {
                new NameValueEntry("order_number", orderNumber),
                new NameValueEntry("item_name", itemName),
                new NameValueEntry("quantity", quantity.ToString())
            });

            Console.WriteLine($"Produced: {orderNumber}");
        }

        // Consuming orders from the stream with locking
        while (true)
        {
            try
            {
                var entries = db.StreamReadGroup(streamName, consumerGroup, consumerName, ">", count: 10);

                foreach (var entry in entries)
                {
                    var orderNumber = (string)entry.Values.FirstOrDefault(x => x.Name == "order_number").Value;
                    var itemName = (string)entry.Values.FirstOrDefault(x => x.Name == "item_name").Value;
                    var quantity = int.Parse((string)entry.Values.FirstOrDefault(x => x.Name == "quantity").Value);

                    var lockKey = $"order_lock:{orderNumber}";
                    var lockToken = Guid.NewGuid().ToString();

                    if (await db.LockTakeAsync(lockKey, lockToken, TimeSpan.FromMinutes(1)))
                    {
                        try
                        {
                            var insertCommand = new NpgsqlCommand(
                                "INSERT INTO simple_data (id, order_number, item_name, quantity) VALUES (@id, @order_number, @item_name, @quantity)",
                                pgConnection
                            );

                            insertCommand.Parameters.AddWithValue("id", Guid.NewGuid());
                            insertCommand.Parameters.AddWithValue("order_number", orderNumber);
                            insertCommand.Parameters.AddWithValue("item_name", itemName);
                            insertCommand.Parameters.AddWithValue("quantity", quantity);

                            await insertCommand.ExecuteNonQueryAsync();
                            Console.WriteLine($"Inserted into PostgreSQL: {orderNumber}, {itemName}, {quantity}");

                            // Acknowledge message processing
                            db.StreamAcknowledge(streamName, consumerGroup, entry.Id);
                            Console.WriteLine($"Acknowledged message: {entry.Id}");
                        }
                        finally
                        {
                            // Release the lock
                            await db.LockReleaseAsync(lockKey, lockToken);
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Could not acquire lock for order: {orderNumber}. Skipping.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            await Task.Delay(1000);
        }
    }
}

