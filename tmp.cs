// 建立連接
using var connection = new NpgsqlConnection(yourConnectionString);
await connection.OpenAsync();

try {
    // 嘗試執行一個插入操作到現有表（使用一個不重要的測試表）
    // 如果成功，說明連接到了可寫節點
    await connection.ExecuteAsync(@"
        INSERT INTO test_table (name, created_at) 
        VALUES ('connection_test', now()) 
        RETURNING id;
    ");
    
    // 獲取服務器信息
    var serverInfo = await connection.QueryFirstAsync<dynamic>(@"
        SELECT 
            inet_server_addr() AS server_ip,
            inet_server_port() AS server_port,
            pg_is_in_recovery() AS is_replica
    ");
    
    Console.WriteLine($"插入測試成功! 連接到可寫節點: {serverInfo.server_ip}:{serverInfo.server_port}");
    Console.WriteLine($"是否為副本: {serverInfo.is_replica} (應該為false)");
} 
catch (Exception ex) {
    if (ex.Message.Contains("read-only") || ex.Message.Contains("readonly")) {
        Console.WriteLine("錯誤: 連接到了只讀數據庫實例");
        
        // 獲取只讀節點的信息
        try {
            var serverInfo = await connection.QueryFirstAsync<dynamic>(@"
                SELECT 
                    inet_server_addr() AS server_ip,
                    inet_server_port() AS server_port,
                    pg_is_in_recovery() AS is_replica
            ");
            Console.WriteLine($"只讀節點IP: {serverInfo.server_ip}:{serverInfo.server_port}");
            Console.WriteLine($"確認為副本: {serverInfo.is_replica} (應該為true)");
        }
        catch {
            Console.WriteLine("無法獲取服務器信息");
        }
    }
    else {
        Console.WriteLine($"其他錯誤: {ex.Message}");
    }
}