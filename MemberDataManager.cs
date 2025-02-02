using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MemberDataCache
{
    public class MemberDataManager
    {
        // 使用 Dictionary 存储数据，引用类型，需要用 ref
        private Dictionary<long, string> _accountIdDict = new Dictionary<long, string>();
        private Dictionary<long, string> _memberIdDict = new Dictionary<long, string>();

        public MemberDataManager()
        {
            // 初始化数据
            RefreshData();
            StartAutoRefresh();
        }

        // 高并发读取，无需加锁
        public string GetCardTierByAccountId(long accountId)
        {
            var dict = _accountIdDict;
            dict.TryGetValue(accountId, out var cardTier);
            return cardTier;
        }

        public string GetCardTierByMemberId(long memberId)
        {
            var dict = _memberIdDict;
            dict.TryGetValue(memberId, out var cardTier);
            return cardTier;
        }

        // 使用 SafeReplace 方法进行原子替换和清理
        private void RefreshData()
        {
            // 创建新的字典，预先分配容量，减少扩容带来的性能开销
            var newAccountIdDict = new Dictionary<long, string>(capacity: 8000000);
            var newMemberIdDict = new Dictionary<long, string>(capacity: 8000000);

            // 从数据库加载数据（请替换为实际的数据库读取逻辑）
            using (var reader = GetDataReader())
            {
                while (reader.Read())
                {
                    long accountId = reader.GetInt64(0);
                    long memberId = reader.GetInt64(1);
                    string cardTier = reader.GetString(2);

                    // 字符串驻留，减少内存占用
                    cardTier = string.Intern(cardTier);

                    newAccountIdDict[accountId] = cardTier;
                    newMemberIdDict[memberId] = cardTier;
                }
            }

            // 原子替换并清理旧的字典
            SafeReplace(ref _accountIdDict, newAccountIdDict);
            SafeReplace(ref _memberIdDict, newMemberIdDict);
        }

        // 推荐清理流程
        public void SafeReplace(ref Dictionary<long, string> activeDict, Dictionary<long, string> newDict)
        {
            // 原子替换
            var old = Interlocked.Exchange(ref activeDict, newDict);

            // 主动断开引用
            old.Clear();      // 释放内部存储
            old.TrimExcess(); // 归还内存到操作系统
            old = null;       // 显式标记可回收

            // 触发紧急回收（谨慎使用）
            if (GC.GetTotalMemory(false) > (1L << 30)) // 1L << 30 表示 1 GB
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: false);
                GC.WaitForPendingFinalizers();
            }
        }

        // 启动自动刷新任务，每12小时刷新一次数据
        private void StartAutoRefresh()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromHours(12));
                    RefreshData();
                }
            });
        }

        // 模拟数据读取器，替换为实际的数据库读取逻辑
        private IDataReader GetDataReader()
        {
            // 请实现实际的数据读取逻辑，例如执行数据库查询并返回 IDataReader
            throw new NotImplementedException();
        }
    }

    // 示例 IDataReader 接口
    public interface IDataReader : IDisposable
    {
        bool Read();
        long GetInt64(int i);
        string GetString(int i);
    }

    // 示例使用
    class Program
    {
        static void Main(string[] args)
        {
            // 创建会员数据管理器
            var memberDataManager = new MemberDataManager();

            // 模拟高并发读取
            Parallel.For(0, 1000000, i =>
            {
                long accountIdToFind = i;
                string cardTier = memberDataManager.GetCardTierByAccountId(accountIdToFind);
                if (cardTier != null)
                {
                    // 处理查找到的 CardTier
                }
            });

            Console.WriteLine("并发读取完成。按任意键退出。");
            Console.ReadKey();
        }
    }
}
