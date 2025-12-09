protected override void OnStart(string[] args)
{
    // 获取应用程序基目录（通常以 \ 结尾）
    string baseDir = AppDomain.CurrentDomain.BaseDirectory;

    // 设置工作目录
    Directory.SetCurrentDirectory(baseDir);

    // 或者，你可以不改变工作目录，而是拼接路径来读取文件
    string filePath = Path.Combine(baseDir, "myconfig.json");
    // var data = File.ReadAllText(filePath);
}