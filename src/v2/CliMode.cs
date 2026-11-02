using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RogueCleanerV2
{
    // 无界面命令行模式: 供远程维护/批量部署脚本调用, 不弹 GUI。
    //   --scan                       只扫描, 写报告
    //   --clean-auto                 扫描后清理所有"可勾选清理"项(与界面「勾选可清理」同一判定: BulkSelectable),
    //                                走原有 CleanerEngine: 先备份、可按批次恢复; 只报告项和需要弹卸载器的项不动
    //   --restore <批次ID|latest>     按批次恢复
    //   --report <路径>               报告写到指定位置(默认写数据目录 reports\cli-*.json)
    // 退出码: 0 成功; 2 参数错误; 3 有清理/恢复项失败; 1 未预期异常
    internal static class CliMode
    {
        private static readonly string[] Modes = new string[] { "--scan", "--clean-auto", "--restore" };

        public static bool IsCliArg(string[] args)
        {
            foreach (string mode in Modes)
            {
                if (HasArg(args, mode)) return true;
            }
            return false;
        }

        public static int Run(string[] args, DataStore store)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string reportPath = ArgValue(args, "--report");
            try
            {
                if (HasArg(args, "--restore")) return Restore(args, store, reportPath ?? Path.Combine(store.Reports, "cli-restore-" + stamp + ".json"));
                bool clean = HasArg(args, "--clean-auto");
                return ScanAndMaybeClean(store, clean, reportPath ?? Path.Combine(store.Reports, (clean ? "cli-clean-" : "cli-scan-") + stamp + ".json"));
            }
            catch (Exception ex)
            {
                Logger.Error("命令行模式失败", ex);
                CleanerEngine.WriteJson(reportPath ?? Path.Combine(store.Reports, "cli-error-" + stamp + ".json"), new Dictionary<string, object>
                {
                    { "Mode", "error" },
                    { "Version", AppMeta.Version },
                    { "Error", ex.GetType().Name + ": " + ex.Message }
                });
                return 1;
            }
        }

        private static int ScanAndMaybeClean(DataStore store, bool clean, string reportPath)
        {
            ScannerEngine scanner = new ScannerEngine();
            List<Finding> findings = scanner.ScanAll(null);
            List<Finding> selectable = findings.Where(delegate(Finding f) { return f.BulkSelectable; }).ToList();
            List<Finding> notSelectable = findings.Where(delegate(Finding f) { return !f.BulkSelectable; }).ToList();

            CleanupBatch batch = null;
            if (clean && selectable.Count > 0)
            {
                foreach (Finding f in findings) f.Selected = false;
                foreach (Finding f in selectable) f.Selected = true;
                batch = new CleanerEngine(store).Clean(selectable);
            }

            List<object> handled = new List<object>();
            if (batch != null)
            {
                foreach (CleanupResult r in batch.Results)
                {
                    handled.Add(new Dictionary<string, object>
                    {
                        { "Title", r.Title }, { "Vendor", r.Vendor }, { "Category", r.Category },
                        { "Action", r.ActionKind }, { "Status", r.Status }, { "StatusText", ChineseDisplayText.CleanupStatus(r.Status) },
                        { "Message", r.Message }, { "Location", r.TechnicalLocation }
                    });
                }
            }
            int failed = batch == null ? 0 : batch.Results.Count(delegate(CleanupResult r) { return string.Equals(r.Status, "Failed", StringComparison.OrdinalIgnoreCase); });

            CleanerEngine.WriteJson(reportPath, new Dictionary<string, object>
            {
                { "Mode", clean ? "clean-auto" : "scan" },
                { "Version", AppMeta.Version },
                { "ScannedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                { "IsAdministrator", AdminUtil.IsAdministrator() },
                { "FindingCount", findings.Count },
                { "BatchId", batch == null ? null : batch.Id },
                { "Failed", failed },
                { "Cleanable", Summaries(selectable) },
                { "ReportOnly", Summaries(notSelectable) },
                { "Handled", handled },
                { "Warnings", scanner.Warnings }
            });
            return failed > 0 ? 3 : 0;
        }

        private static int Restore(string[] args, DataStore store, string reportPath)
        {
            string id = ArgValue(args, "--restore");
            if (string.IsNullOrWhiteSpace(id)) return 2;
            CleanerEngine engine = new CleanerEngine(store);
            List<CleanupBatch> batches = engine.LoadBatches();
            CleanupBatch batch = string.Equals(id, "latest", StringComparison.OrdinalIgnoreCase)
                ? batches.FirstOrDefault()
                : batches.FirstOrDefault(delegate(CleanupBatch b) { return string.Equals(b.Id, id, StringComparison.OrdinalIgnoreCase); });
            if (batch == null)
            {
                CleanerEngine.WriteJson(reportPath, new Dictionary<string, object> { { "Mode", "restore" }, { "Error", "找不到批次 " + id } });
                return 2;
            }
            RestoreBatchResult result = engine.RestoreBatch(batch);
            CleanerEngine.WriteJson(reportPath, new Dictionary<string, object>
            {
                { "Mode", "restore" }, { "Version", AppMeta.Version }, { "BatchId", batch.Id },
                { "Total", result.Total }, { "Succeeded", result.Succeeded }, { "Failed", result.Failed }, { "Messages", result.Messages }
            });
            return result.Failed > 0 ? 3 : 0;
        }

        private static List<object> Summaries(IEnumerable<Finding> findings)
        {
            List<object> list = new List<object>();
            foreach (Finding f in findings)
            {
                list.Add(new Dictionary<string, object>
                {
                    { "Id", f.Id }, { "Title", f.UserVisibleName }, { "Vendor", f.Vendor }, { "Category", f.Category },
                    { "Risk", f.Risk }, { "Action", f.ActionKind }, { "ActionText", f.ActionText }, { "Location", f.TechnicalLocation }
                });
            }
            return list;
        }

        private static bool HasArg(string[] args, string name)
        {
            return Array.Exists(args, delegate(string a) { return string.Equals(a, name, StringComparison.OrdinalIgnoreCase); });
        }

        private static string ArgValue(string[] args, string name)
        {
            int i = Array.FindIndex(args, delegate(string a) { return string.Equals(a, name, StringComparison.OrdinalIgnoreCase); });
            return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1] : null;
        }
    }
}
