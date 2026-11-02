using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RogueCleanerV2
{
    // 无界面命令行模式: 供远程维护/批量部署脚本调用, 不弹 GUI。
    //   --scan                       只扫描, 写报告
    //   --clean-auto                 扫描后清理所有"可勾选清理"项(与界面「勾选可清理」同一判定: BulkSelectable),
    //                                走原有 CleanerEngine: 先备份、可按批次恢复; 只报告项和需要弹卸载器的项不动
    //   --clean-ads                  更严格: 只清"可勾选清理"里带弹窗/广告/推广/资讯/守护特征的项,
    //                                且永远保留办公通讯/打印类(微信、QQ、钉钉、打印机等), 给"一键清牛皮癣"用
    //   --restore <批次ID|latest>     按批次恢复
    //   --report <路径>               报告写到指定位置(默认写数据目录 reports\cli-*.json)
    //   --summary <路径>              另写一份中文纯文本摘要(给不方便解析 JSON 的调用方, 如 PowerShell 2.0)
    // 退出码: 0 成功; 2 参数错误; 3 有清理/恢复项失败; 1 未预期异常
    internal static class CliMode
    {
        private static string summaryPath;
        private static readonly string[] Modes = new string[] { "--scan", "--clean-auto", "--clean-ads", "--restore" };

        // 牛皮癣特征: 看名称、分类、命中证据、位置
        private static readonly Regex AdLike = new Regex("弹窗|广告|推广|资讯|迷你页|守护|热点|推荐|画报|新闻|Popup|News|MiniPage|AdPop|Notify|Promo", RegexOptions.IgnoreCase);
        // 做生意要用的: 命中了也不动
        private static readonly Regex KeepAlways = new Regex("微信|Weixin|WeChat|企业微信|WXWork|钉钉|DingTalk|QQ(?!浏览器|管家|Browser|PCMgr)|打印|Print|Epson|爱普生|Deli|得力|速达|SuperData", RegexOptions.IgnoreCase);

        private static bool IsAdLike(Finding f)
        {
            string name = (f.UserVisibleName ?? "") + " " + (f.TechnicalLocation ?? "");
            string all = name + " " + (f.Category ?? "") + " " + (f.Evidence ?? "");
            return AdLike.IsMatch(all) && !KeepAlways.IsMatch(name);
        }

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
            summaryPath = ArgValue(args, "--summary");
            try
            {
                if (HasArg(args, "--restore")) return Restore(args, store, reportPath ?? Path.Combine(store.Reports, "cli-restore-" + stamp + ".json"));
                bool adsOnly = HasArg(args, "--clean-ads");
                bool clean = adsOnly || HasArg(args, "--clean-auto");
                return ScanAndMaybeClean(store, clean, adsOnly, reportPath ?? Path.Combine(store.Reports, (clean ? "cli-clean-" : "cli-scan-") + stamp + ".json"));
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

        private static int ScanAndMaybeClean(DataStore store, bool clean, bool adsOnly, string reportPath)
        {
            ScannerEngine scanner = new ScannerEngine();
            List<Finding> findings = scanner.ScanAll(null);
            List<Finding> selectable = findings.Where(delegate(Finding f) { return f.BulkSelectable && (!adsOnly || IsAdLike(f)); }).ToList();
            List<Finding> notSelectable = findings.Where(delegate(Finding f) { return !selectable.Contains(f); }).ToList();

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
                { "Mode", adsOnly ? "clean-ads" : (clean ? "clean-auto" : "scan") },
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
            WriteSummary(summaryPath, clean, findings.Count, selectable, notSelectable, batch);
            return failed > 0 ? 3 : 0;
        }

        private static void WriteSummary(string path, bool clean, int total, List<Finding> selectable, List<Finding> notSelectable, CleanupBatch batch)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            List<string> lines = new List<string>();
            lines.Add("扫描到 " + total + " 项, 本次清理范围 " + selectable.Count + " 项, 保留 " + notSelectable.Count + " 项");
            if (clean)
            {
                if (batch == null) lines.Add("没有需要清理的项目");
                else
                {
                    lines.Add("本次处理(批次 " + batch.Id + ", 可恢复):");
                    foreach (CleanupResult r in batch.Results)
                    {
                        bool ok = string.Equals(r.Status, "Done", StringComparison.OrdinalIgnoreCase);
                        lines.Add((ok ? "  √ " : "  × ") + Label(r.Title, r.Vendor) + (ok ? "" : "  (" + ChineseDisplayText.CleanupStatus(r.Status) + ": " + r.Message + ")"));
                    }
                }
            }
            else
            {
                foreach (Finding f in selectable) lines.Add("  · " + Label(f.UserVisibleName, f.Vendor) + "  → " + f.ActionText);
            }
            if (notSelectable.Count > 0)
            {
                lines.Add("保留未动(非弹窗广告类 / 仅提示):");
                foreach (Finding f in notSelectable) lines.Add("  - " + Label(f.UserVisibleName, f.Vendor));
            }
            File.WriteAllLines(path, lines.ToArray(), new System.Text.UTF8Encoding(true));
        }

        private static string Label(string title, string vendor)
        {
            return string.IsNullOrWhiteSpace(vendor) ? title : title + " [" + vendor + "]";
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
