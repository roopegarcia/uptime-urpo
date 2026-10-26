using Npgsql;

namespace UptimeUrpo.Api.Persistence;

public sealed class DatabaseOptions
{
    public string ConnectionString { get; set; } = "";
    public int RetentionDays { get; set; } = 90;
    public int RetentionBatchSize { get; set; } = 500;

    public bool IsValid()
    {
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(ConnectionString);
            return !string.IsNullOrWhiteSpace(connection.Host)
                && !string.IsNullOrWhiteSpace(connection.Database)
                && !string.IsNullOrWhiteSpace(connection.Username)
                && RetentionDays is >= 30 and <= 365
                && RetentionBatchSize is >= 1 and <= 5000;
        }
        catch (ArgumentException) { return false; }
    }
}

public sealed class PersistenceHealth
{
    private int _ready;
    public bool IsReady => Volatile.Read(ref _ready) == 1;
    public void SetReady(bool ready) => Volatile.Write(ref _ready, ready ? 1 : 0);
}
