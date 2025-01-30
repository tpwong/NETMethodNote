public class KafkaProducerConfig;

public static ProducerConfig GetConfig(string bootstrapServers = "localhost:9092")
{
    return new ProducerConfig
    {
        // Batch 設定
        BatchSize = 16384 * 2,        // 32 KB
        LingerMs = 50,                // 50ms
        CompressionType = CompressionType.Snappy,
    };
}
