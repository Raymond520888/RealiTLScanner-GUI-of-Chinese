using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace RealiTLScannerGUI
{
    public class ScanResult
    {
        public string IP { get; set; } = "";
        public string Origin { get; set; } = "";
        public string TLS { get; set; } = "";
        public string ALPN { get; set; } = "";
        public string Curve { get; set; } = "";
        public string CertLength { get; set; } = "";
        public string CertSignature { get; set; } = "";
        public string CertPublicKey { get; set; } = "";
        public string CertDomain { get; set; } = "";
        public string CertIssuer { get; set; } = "";
        public string GeoCode { get; set; } = "";
    }

    public partial class MainWindow : Window
    {
        private Process? _scanProcess;
        private bool _isScanning;
        private readonly ObservableCollection<ScanResult> _results = new();
        private readonly StringBuilder _logBuilder = new();
        private DateTime _startTime;
        private DispatcherTimer? _elapsedTimer;
        private string _exePath = "";

        private const string GeoDbUrl = "https://github.com/Loyalsoldier/geoip/releases/latest/download/Country.mmdb";
        private const string GeoDbFileName = "Country.mmdb";
        private const int GeoUpdateIntervalDays = 7;
        private static readonly HttpClient _httpClient = new();

        public MainWindow()
        {
            InitializeComponent();
            ResultsGrid.ItemsSource = _results;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(appDir, "Core.exe"),
                Path.Combine(appDir, "..", "Core.exe"),
                Path.Combine(appDir, "..", "..", "Core.exe"),
                Path.Combine(appDir, "..", "..", "..", "Core.exe"),
                Path.Combine(appDir, "..", "..", "..", "..", "Core.exe"),
            };

            foreach (var candidate in candidates)
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                {
                    _exePath = fullPath;
                    break;
                }
            }

            if (string.IsNullOrEmpty(_exePath))
            {
                AppendLog("[警告] 未找到 Core.exe！请将其放置在本应用程序所在目录或父目录中。\n", isWarning: true);
            }
            else
            {
                AppendLog($"[信息] 扫描器已找到：{_exePath}\n");
            }

            UpdatePlaceholder();
            _ = CheckAndDownloadGeoDbAsync();
        }

        private async Task CheckAndDownloadGeoDbAsync()
        {
            try
            {
                string scannerDir = !string.IsNullOrEmpty(_exePath)
                    ? Path.GetDirectoryName(_exePath) ?? AppDomain.CurrentDomain.BaseDirectory
                    : AppDomain.CurrentDomain.BaseDirectory;

                string geoDbPath = Path.Combine(scannerDir, GeoDbFileName);

                bool needsDownload = false;
                if (!File.Exists(geoDbPath))
                {
                    AppendLog("[地理IP] 未找到 Country.mmdb，开始下载...\n", isWarning: true);
                    AppendLog("[地理IP] 提示：如果下载缓慢或失败，请开启代理（如 Clash、V2RayN 等）并确保网络畅通,更新完必须退出代理软件。\n", isWarning: true);
                    needsDownload = true;
                }
                else
                {
                    var fileAge = DateTime.Now - File.GetLastWriteTime(geoDbPath);
                    if (fileAge.TotalDays > GeoUpdateIntervalDays)
                    {
                        AppendLog($"[地理IP] Country.mmdb 已 {fileAge.Days} 天未更新，正在更新...\n", isWarning: true);
                        AppendLog("[地理IP] 提示：更新失败时请检查网络或开启代理。\n", isWarning: true);
                        needsDownload = true;
                    }
                    else
                    {
                        AppendLog($"[地理IP] Country.mmdb 已是最新（{fileAge.Days} 天前更新）\n");
                    }
                }

                if (needsDownload)
                {
                    SetStatus("正在下载 GeoIP 数据库...", Brushes.Orange);
                    BtnStart.IsEnabled = false;

                    string tempPath = geoDbPath + ".tmp";

                    await Task.Run(async () =>
                    {
                        using var response = await _httpClient.GetAsync(GeoDbUrl, HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();

                        var totalBytes = response.Content.Headers.ContentLength ?? -1;

                        using var contentStream = await response.Content.ReadAsStreamAsync();
                        using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                        var buffer = new byte[8192];
                        long downloaded = 0;
                        int bytesRead;
                        DateTime lastUpdate = DateTime.MinValue;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            downloaded += bytesRead;

                            if ((DateTime.Now - lastUpdate).TotalMilliseconds > 500)
                            {
                                lastUpdate = DateTime.Now;
                                var mb = downloaded / 1024.0 / 1024.0;
                                var totalMb = totalBytes > 0 ? totalBytes / 1024.0 / 1024.0 : -1;

                                await Dispatcher.InvokeAsync(() => SetStatus(
                                    totalMb > 0
                                        ? $"正在下载 GeoIP 数据库... {mb:F1}/{totalMb:F1} MB"
                                        : $"正在下载 GeoIP 数据库... {mb:F1} MB",
                                    Brushes.Orange));
                            }
                        }
                    });

                    if (File.Exists(geoDbPath))
                        File.Delete(geoDbPath);
                    File.Move(tempPath, geoDbPath);

                    var sizeMb = new FileInfo(geoDbPath).Length / 1024.0 / 1024.0;
                    AppendLog($"[地理IP] ✓ Country.mmdb 下载成功（{sizeMb:F1} MB）\n", isFeasible: true);

                    SetStatus("就绪", new SolidColorBrush((Color)FindResource("TextSecondary")));
                    BtnStart.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                string errorMsg = ex.Message;
                string suggestion = "";

                if (ex is HttpRequestException httpEx)
                {
                    if (httpEx.StatusCode.HasValue && (int)httpEx.StatusCode.Value >= 400)
                    {
                        suggestion = "服务器返回错误，请稍后重试或检查网络代理设置。";
                    }
                    else
                    {
                        suggestion = "网络连接失败，请确认网络可达，并尝试开启代理（如 Clash、V2RayN 等），更新完必须退出代理软件。";
                    }
                }
                else if (ex is TaskCanceledException || ex is TimeoutException)
                {
                    suggestion = "下载超时，可能是网络速度慢或代理未开启，请尝试使用代理。";
                }
                else
                {
                    suggestion = "请检查网络并尝试开启代理，也可手动下载后放置到扫描器目录。";
                }

                AppendLog($"[地理IP] ⚠ 下载 Country.mmdb 失败：{errorMsg}\n", isError: true);
                AppendLog($"[地理IP] 建议：{suggestion}\n", isWarning: true);
                AppendLog($"[地理IP] 您也可以手动从以下地址下载，并放入扫描器所在目录：\n", isWarning: true);
                AppendLog($"[地理IP] {GeoDbUrl}\n", isWarning: true);
                AppendLog($"[地理IP] 文件名必须为 {GeoDbFileName}\n", isWarning: true);
                AppendLog($"[地理IP] 或者，如果您已有 Country.mmdb 文件，请直接复制到扫描器所在目录，程序将自动使用（跳过下载）。\n", isWarning: true);

                SetStatus("就绪（GeoIP 不可用）", new SolidColorBrush((Color)FindResource("AccentOrange")));
                BtnStart.IsEnabled = true;
            }
        }

        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            UpdatePlaceholder();
        }

        private void UpdatePlaceholder()
        {
            if (TxtTarget == null || BtnBrowse == null) return;

            if (ModeAddr.IsChecked == true)
            {
                TxtTarget.Tag = "输入 IP、CIDR 或域名（例如 1.1.1.1、10.0.0.0/24、example.com）";
                TxtTarget.Text = "";
                BtnBrowse.Visibility = Visibility.Collapsed;
                TxtTarget.IsReadOnly = false;
            }
            else if (ModeFile.IsChecked == true)
            {
                TxtTarget.Tag = "选择包含 IP/CIDR/域名的输入文件...";
                TxtTarget.Text = "";
                BtnBrowse.Visibility = Visibility.Visible;
                TxtTarget.IsReadOnly = true;
            }
            else if (ModeUrl.IsChecked == true)
            {
                TxtTarget.Tag = "输入要爬取域名的 URL（例如 https://launchpad.net/ubuntu/+archivemirrors）";
                TxtTarget.Text = "";
                BtnBrowse.Visibility = Visibility.Collapsed;
                TxtTarget.IsReadOnly = false;
            }
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "选择输入文件",
                Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                TxtTarget.Text = dlg.FileName;
            }
        }

        private void BtnOutputBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title = "选择输出文件",
                Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
                FileName = TxtOutput.Text
            };
            if (dlg.ShowDialog() == true)
            {
                TxtOutput.Text = dlg.FileName;
            }
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (_isScanning) return;

            if (string.IsNullOrEmpty(_exePath))
            {
                MessageBox.Show(
                    "未找到 Core.exe！\n\n请将扫描器可执行文件放置在本应用程序所在目录或父目录中。",
                    "扫描器未找到",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var target = TxtTarget.Text.Trim();
            if (string.IsNullOrEmpty(target))
            {
                MessageBox.Show("请指定要扫描的目标。", "缺少目标",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var args = new StringBuilder();

            if (ModeAddr.IsChecked == true)
                args.Append($"-addr \"{target}\" ");
            else if (ModeFile.IsChecked == true)
                args.Append($"-in \"{target}\" ");
            else if (ModeUrl.IsChecked == true)
                args.Append($"-url \"{target}\" ");

            if (int.TryParse(TxtPort.Text, out var port) && port != 443)
                args.Append($"-port {port} ");

            args.Append("-thread 128 ");

            if (int.TryParse(TxtTimeout.Text, out var timeout) && timeout != 10)
                args.Append($"-timeout {timeout} ");

            var outFile = TxtOutput.Text.Trim();
            if (!string.IsNullOrEmpty(outFile) && outFile != "out.csv")
                args.Append($"-out \"{outFile}\" ");

            if (ChkVerbose.IsChecked == true)
                args.Append("-v ");

            if (ChkIpv6.IsChecked == true)
                args.Append("-46 ");

            StartScan(args.ToString().Trim());
        }

        private void StartScan(string arguments)
        {
            _logBuilder.Clear();
            TxtLog.Text = "";
            TxtLog.Inlines.Clear();
            _results.Clear();
            UpdateResultCount();

            _isScanning = true;
            BtnStart.IsEnabled = false;
            BtnStop.IsEnabled = true;
            SetStatus("扫描中...", Brushes.Orange);

            AppendLog($"[命令] Core.exe {arguments}\n", isCommand: true);
            AppendLog($"[信息] 开始扫描...\n");

            _startTime = DateTime.Now;
            StartElapsedTimer();

            var startInfo = new ProcessStartInfo
            {
                FileName = _exePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(_exePath) ?? "",
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            try
            {
                _scanProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                _scanProcess.OutputDataReceived += Process_OutputDataReceived;
                _scanProcess.ErrorDataReceived += Process_ErrorDataReceived;
                _scanProcess.Exited += Process_Exited;
                _scanProcess.Start();
                _scanProcess.BeginOutputReadLine();
                _scanProcess.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                AppendLog($"[错误] 启动扫描器失败：{ex.Message}\n", isError: true);
                ScanFinished();
            }
        }

        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            Dispatcher.BeginInvoke(() =>
            {
                var line = e.Data;
                bool isFeasible = line.Contains("feasible=true");
                AppendLog(line + "\n", isFeasible: isFeasible);

                if (isFeasible)
                {
                    TryParseResult(line);
                }
            });
        }

        private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            Dispatcher.BeginInvoke(() =>
            {
                AppendLog(e.Data + "\n", isError: true);
            });
        }

        private void Process_Exited(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                AppendLog("\n[信息] 扫描进程已退出。\n");
                ScanFinished();
                LoadResultsFromCsv();
            });
        }

        private void LoadResultsFromCsv()
        {
            var outFile = TxtOutput.Text.Trim();
            if (string.IsNullOrEmpty(outFile)) outFile = "out.csv";

            string csvPath;
            if (Path.IsPathFullyQualified(outFile))
            {
                csvPath = outFile;
            }
            else
            {
                csvPath = Path.Combine(Path.GetDirectoryName(_exePath) ?? "", outFile);
            }

            if (!File.Exists(csvPath))
            {
                AppendLog($"[信息] 未找到输出文件：{csvPath}\n");
                return;
            }

            try
            {
                var lines = File.ReadAllLines(csvPath);
                int loadedCount = 0;
                for (int i = 1; i < lines.Length; i++)
                {
                    var parsed = ParseCsvLine(lines[i]);
                    if (parsed != null)
                    {
                        ScanResult? existing = null;
                        foreach (var r in _results)
                        {
                            if (r.IP == parsed.IP && r.CertDomain == parsed.CertDomain)
                            {
                                existing = r;
                                break;
                            }
                        }

                        if (existing != null)
                        {
                            existing.Origin = parsed.Origin;
                            existing.TLS = parsed.TLS;
                            existing.ALPN = parsed.ALPN;
                            existing.Curve = parsed.Curve;
                            existing.CertLength = parsed.CertLength;
                            existing.CertSignature = parsed.CertSignature;
                            existing.CertPublicKey = parsed.CertPublicKey;
                            existing.CertDomain = parsed.CertDomain;
                            existing.CertIssuer = parsed.CertIssuer;
                            existing.GeoCode = parsed.GeoCode;
                        }
                        else
                        {
                            _results.Add(parsed);
                            loadedCount++;
                        }
                    }
                }

                if (loadedCount > 0)
                {
                    AppendLog($"[信息] 从 {csvPath} 加载了 {loadedCount} 条结果\n");
                }

                UpdateResultCount();
            }
            catch (Exception ex)
            {
                AppendLog($"[警告] 读取或处理 CSV 失败：{ex.Message}\n", isWarning: true);
            }
        }

        private ScanResult? ParseCsvLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;

            var parts = SplitCsvLine(line);
            
            if (parts.Count >= 11)
            {
                return new ScanResult
                {
                    IP = parts[0],
                    Origin = parts[1],
                    TLS = parts[2],
                    ALPN = parts[3],
                    Curve = parts[4],
                    CertLength = parts[5],
                    CertSignature = parts[6],
                    CertPublicKey = parts[7],
                    CertDomain = parts[8],
                    CertIssuer = parts[9],
                    GeoCode = parts[10]
                };
            }
            else if (parts.Count >= 5)
            {
                return new ScanResult
                {
                    IP = parts[0],
                    Origin = parts[1],
                    CertDomain = parts[2],
                    CertIssuer = parts[3],
                    GeoCode = parts[4]
                };
            }
            
            return null;
        }

        private System.Collections.Generic.List<string> SplitCsvLine(string line)
        {
            var result = new System.Collections.Generic.List<string>();
            bool inQuotes = false;
            var current = new StringBuilder();

            foreach (char c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString());
            return result;
        }

        private void TryParseResult(string logLine)
        {
            try
            {
                string ip = ExtractField(logLine, "ip=");
                string origin = ExtractField(logLine, "origin=");
                string certDomain = ExtractField(logLine, "cert-domain=");
                string certIssuer = ExtractField(logLine, "cert-issuer=");
                string geo = ExtractField(logLine, "geo=");

                if (!string.IsNullOrEmpty(ip))
                {
                    _results.Add(new ScanResult
                    {
                        IP = ip,
                        Origin = origin,
                        CertDomain = certDomain,
                        CertIssuer = certIssuer,
                        GeoCode = geo
                    });
                    UpdateResultCount();
                }
            }
            catch
            {
            }
        }

        private string ExtractField(string text, string key)
        {
            int idx = text.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return "";

            int start = idx + key.Length;
            if (start >= text.Length) return "";

            if (text[start] == '"')
            {
                int end = text.IndexOf('"', start + 1);
                return end > start ? text.Substring(start + 1, end - start - 1) : "";
            }
            else
            {
                int end = text.IndexOf(' ', start);
                return end > start ? text.Substring(start, end - start) : text.Substring(start);
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            StopScan();
        }

        private void StopScan()
        {
            if (_scanProcess != null && !_scanProcess.HasExited)
            {
                try
                {
                    _scanProcess.Kill(entireProcessTree: true);
                    AppendLog("[信息] 用户停止了扫描。\n", isWarning: true);
                }
                catch (Exception ex)
                {
                    AppendLog($"[错误] 停止失败：{ex.Message}\n", isError: true);
                }
            }
            ScanFinished();
        }

        private void ScanFinished()
        {
            _isScanning = false;
            BtnStart.IsEnabled = true;
            BtnStop.IsEnabled = false;
            StopElapsedTimer();

            if (_results.Count > 0)
            {
                SetStatus($"完成 — 找到 {_results.Count} 个结果",
                    new SolidColorBrush((Color)FindResource("AccentGreen")));
            }
            else
            {
                SetStatus("完成 — 没有可行结果",
                    new SolidColorBrush((Color)FindResource("TextSecondary")));
            }
        }

        private void ResultTab_Changed(object sender, RoutedEventArgs e)
        {
            if (LogPanel == null || ResultsPanel == null) return;

            if (TabLog.IsChecked == true)
            {
                LogPanel.Visibility = Visibility.Visible;
                ResultsPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                LogPanel.Visibility = Visibility.Collapsed;
                ResultsPanel.Visibility = Visibility.Visible;
            }
        }

        private void AppendLog(string text, bool isError = false, bool isWarning = false,
            bool isFeasible = false, bool isCommand = false)
        {
            _logBuilder.Append(text);

            var run = new System.Windows.Documents.Run(text);
            if (isError)
                run.Foreground = new SolidColorBrush((Color)FindResource("AccentRed"));
            else if (isWarning)
                run.Foreground = new SolidColorBrush((Color)FindResource("AccentOrange"));
            else if (isFeasible)
                run.Foreground = new SolidColorBrush((Color)FindResource("AccentGreen"));
            else if (isCommand)
                run.Foreground = new SolidColorBrush((Color)FindResource("AccentPurple"));
            else
                run.Foreground = (SolidColorBrush)FindResource("TextSecondaryBrush");

            TxtLog.Inlines.Add(run);
            LogScrollViewer.ScrollToBottom();
        }

        private void SetStatus(string text, Brush dotColor)
        {
            TxtStatus.Text = text;
            StatusDot.Fill = dotColor;
        }

        private void UpdateResultCount()
        {
            TxtResultCount.Text = _results.Count > 0 ? $"{_results.Count} 个结果" : "";
        }

        private void StartElapsedTimer()
        {
            _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _elapsedTimer.Tick += (s, e) =>
            {
                var elapsed = DateTime.Now - _startTime;
                TxtElapsed.Text = $"⏱ {elapsed:hh\\:mm\\:ss}";
            };
            _elapsedTimer.Start();
        }

        private void StopElapsedTimer()
        {
            _elapsedTimer?.Stop();
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (_results.Count == 0)
            {
                MessageBox.Show("没有可导出的结果。", "导出", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title = "导出结果为 CSV",
                Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
                FileName = "scan_results.csv"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("IP,ORIGIN,TLS,ALPN,CURVE,CERT_LENGTH,CERT_SIGNATURE,CERT_PUBLICKEY,CERT_DOMAIN,CERT_ISSUER,GEO_CODE");
                    foreach (var r in _results)
                    {
                        sb.AppendLine($"{r.IP},{r.Origin},{r.TLS},{r.ALPN},{r.Curve},{r.CertLength},\"{r.CertSignature}\",\"{r.CertPublicKey}\",{r.CertDomain},\"{r.CertIssuer}\",{r.GeoCode}");
                    }
                    File.WriteAllText(dlg.FileName, sb.ToString());
                    MessageBox.Show($"已导出 {_results.Count} 条结果到：\n{dlg.FileName}", "导出完成",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出失败：{ex.Message}", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_isScanning)
            {
                var result = MessageBox.Show(
                    "扫描仍在运行，是否停止并关闭？",
                    "扫描进行中",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }

                StopScan();
            }
            base.OnClosing(e);
        }

        private void CopyIP_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsGrid.SelectedItem is ScanResult result)
            {
                Clipboard.SetText(result.IP);
            }
        }

        private void CopyDomain_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsGrid.SelectedItem is ScanResult result)
            {
                Clipboard.SetText(result.CertDomain);
            }
        }

        private void CopyRow_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsGrid.SelectedItems.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var item in ResultsGrid.SelectedItems)
                {
                    if (item is ScanResult r)
                    {
                        sb.AppendLine($"{r.IP}\t{r.Origin}\t{r.CertDomain}\t{r.CertIssuer}\t{r.GeoCode}");
                    }
                }
                Clipboard.SetText(sb.ToString().TrimEnd());
            }
        }

        private void CopyAll_Click(object sender, RoutedEventArgs e)
        {
            if (_results.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine("IP\tORIGIN\tCERT_DOMAIN\tCERT_ISSUER\tGEO_CODE");
            foreach (var r in _results)
            {
                sb.AppendLine($"{r.IP}\t{r.Origin}\t{r.CertDomain}\t{r.CertIssuer}\t{r.GeoCode}");
            }
            Clipboard.SetText(sb.ToString().TrimEnd());
        }
    }
}