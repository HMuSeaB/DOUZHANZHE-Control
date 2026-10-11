// SPDX-License-Identifier: GPL-3.0-only
//
// MiniOverlayForm — 轻量级桌面/游戏硬件监控置顶悬浮窗
// =======================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace Douzhanzhe.Shell;

public sealed class MiniOverlayForm : Form
{
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HT_CAPTION = 0x2;

    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    private readonly System.Windows.Forms.Timer _updateTimer;
    private readonly HttpClient _http;
    private readonly string _stateFilePath;

    // 遥测数据
    private int _cpuTemp = 0;
    private int _cpuUsage = 0;
    private double _cpuFreq = 0;
    private int _gpuTemp = 0;
    private double _gpuWatts = 0;
    private int _cpuFan = 0;
    private int _gpuFan = 0;
    private string _mode = "均衡模式";
    private bool _turboActive = false;
    private bool _fetching = false;
    private double _opacity = 1.0;

    public MiniOverlayForm(string configDir)
    {
        _stateFilePath = Path.Combine(configDir, "overlay-state.json");
        _http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(800) };

        // 窗口基础属性
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(270, 72);
        BackColor = Color.FromArgb(16, 20, 26);
        DoubleBuffered = true;

        RestorePosition();
        Opacity = _opacity;

        // 鼠标拖拽支持
        MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
                SavePosition();
            }
        };

        // 右键或双击隐藏
        DoubleClick += (s, e) => Hide();
        var menu = new ContextMenuStrip();
        var opacityMenu = new ToolStripMenuItem("调节透明度");
        opacityMenu.DropDownItems.Add("100% (不透明)", null, (s, e) => SetOpacity(1.0));
        opacityMenu.DropDownItems.Add("85% (微透)", null, (s, e) => SetOpacity(0.85));
        opacityMenu.DropDownItems.Add("70% (半透)", null, (s, e) => SetOpacity(0.70));
        opacityMenu.DropDownItems.Add("50% (高透)", null, (s, e) => SetOpacity(0.50));
        menu.Items.Add(opacityMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("隐藏悬浮窗 (Ctrl+Shift+O 呼出)", null, (s, e) => Hide());
        ContextMenuStrip = menu;

        // 定时轮询
        _updateTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _updateTimer.Tick += async (s, e) => await FetchTelemetryAsync();
        _updateTimer.Start();
    }

    private void SetOpacity(double op)
    {
        _opacity = op;
        Opacity = op;
        SavePosition();
    }

    private void RestorePosition()
    {
        try
        {
            if (File.Exists(_stateFilePath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(_stateFilePath));
                var root = doc.RootElement;
                if (root.TryGetProperty("opacity", out var opProp))
                {
                    _opacity = Math.Clamp(opProp.GetDouble(), 0.3, 1.0);
                }
                if (root.TryGetProperty("x", out var xProp) && root.TryGetProperty("y", out var yProp))
                {
                    var x = xProp.GetInt32();
                    var y = yProp.GetInt32();
                    var screen = Screen.FromPoint(new Point(x, y));
                    if (screen.WorkingArea.Contains(new Point(x, y)))
                    {
                        Location = new Point(x, y);
                        return;
                    }
                }
            }
        }
        catch { }

        // 默认右上角贴边
        var primary = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(primary.Right - Width - 24, primary.Top + 32);
    }

    private void SavePosition()
    {
        try
        {
            var dir = Path.GetDirectoryName(_stateFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(new { x = Location.X, y = Location.Y, opacity = _opacity });
            File.WriteAllText(_stateFilePath, json);
        }
        catch { }
    }

    private async System.Threading.Tasks.Task FetchTelemetryAsync()
    {
        if (_fetching || !Visible) return;
        _fetching = true;
        try
        {
            foreach (var baseUrl in new[] { "http://127.0.0.1:3100", "http://127.0.0.1:3101" })
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/telemetry");
                    req.Headers.Add("Origin", baseUrl);
                    var resp = await _http.SendAsync(req);
                    if (resp.IsSuccessStatusCode)
                    {
                        var stream = await resp.Content.ReadAsStreamAsync();
                        using var doc = await JsonDocument.ParseAsync(stream);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("cpuTemp", out var ct)) _cpuTemp = ct.GetInt32();
                        if (root.TryGetProperty("cpuUsage", out var cu)) _cpuUsage = cu.GetInt32();
                        if (root.TryGetProperty("cpuFreq", out var cfq)) _cpuFreq = cfq.GetDouble();
                        if (root.TryGetProperty("gpuTemp", out var gt)) _gpuTemp = gt.GetInt32();
                        if (root.TryGetProperty("gpuPowerDrawW", out var gw)) _gpuWatts = gw.GetDouble();
                        if (root.TryGetProperty("fanLargeRpm", out var cf)) _cpuFan = cf.GetInt32();
                        if (root.TryGetProperty("fanSmallRpm", out var gf)) _gpuFan = gf.GetInt32();
                        if (root.TryGetProperty("thermalMode", out var tm))
                        {
                            _mode = tm.GetInt32() switch
                            {
                                0 => "均衡模式",
                                1 => "野兽模式",
                                2 => "安静模式",
                                3 => "斗战模式",
                                _ => "自定义模式"
                            };
                        }
                        break;
                    }
                }
                catch { /* fallback next */ }
            }

            // 检查 Turbo 状态
            foreach (var baseUrl in new[] { "http://127.0.0.1:3100", "http://127.0.0.1:3101" })
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/fan/turbo");
                    req.Headers.Add("Origin", baseUrl);
                    var tResp = await _http.SendAsync(req);
                    if (tResp.IsSuccessStatusCode)
                    {
                        var stream = await tResp.Content.ReadAsStreamAsync();
                        using var doc = await JsonDocument.ParseAsync(stream);
                        if (doc.RootElement.TryGetProperty("enabled", out var en))
                            _turboActive = en.GetBoolean();
                        break;
                    }
                }
                catch { }
            }

            Invalidate();
        }
        finally
        {
            _fetching = false;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);

        // 外框微光
        using (var borderPen = new Pen(Color.FromArgb(48, 54, 61), 1))
        {
            g.DrawRectangle(borderPen, rect);
        }

        using var fontBold = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var fontNormal = new Font("Segoe UI", 9.0f, FontStyle.Regular);
        using var fontSmall = new Font("Segoe UI", 8.0f, FontStyle.Regular);

        using var brushMuted = new SolidBrush(Color.FromArgb(139, 148, 158));
        using var brushWhite = new SolidBrush(Color.FromArgb(240, 246, 252));
        using var brushCpu = new SolidBrush(Color.FromArgb(88, 166, 255));
        using var brushGpu = new SolidBrush(Color.FromArgb(126, 231, 135));
        using var brushTurbo = new SolidBrush(Color.FromArgb(0, 229, 255));
        using var brushWarn = new SolidBrush(Color.FromArgb(248, 81, 73));

        // Line 1: CPU 与 GPU
        var cpuBrush = _cpuTemp >= 88 ? brushWarn : brushCpu;
        g.DrawString("CPU", fontBold, cpuBrush, 12, 10);
        g.DrawString($"{_cpuTemp}°C", fontBold, brushWhite, 46, 10);
        string cpuInfo = _cpuFreq > 0 ? $"{_cpuUsage}% {_cpuFreq:F1}G" : $"{_cpuUsage}%";
        g.DrawString(cpuInfo, fontSmall, brushMuted, 84, 12);

        g.DrawString("|", fontNormal, brushMuted, 134, 10);

        var gpuBrush = _gpuTemp >= 85 ? brushWarn : brushGpu;
        g.DrawString("GPU", fontBold, gpuBrush, 146, 10);
        g.DrawString($"{_gpuTemp}°C", fontBold, brushWhite, 182, 10);
        g.DrawString($"{_gpuWatts:F0}W", fontSmall, brushMuted, 222, 12);

        // 分割线
        using (var linePen = new Pen(Color.FromArgb(33, 38, 45), 1))
        {
            g.DrawLine(linePen, 10, 36, Width - 10, 36);
        }

        // Line 2: 风扇与模式状态
        g.DrawString("🌀", fontSmall, brushMuted, 10, 44);
        g.DrawString($"{_cpuFan}/{_gpuFan} RPM", fontNormal, brushWhite, 28, 44);

        if (_turboActive)
        {
            g.DrawString("⚡ TURBO 强冷", fontBold, brushTurbo, 162, 44);
        }
        else
        {
            g.DrawString(_mode, fontSmall, brushMuted, 180, 46);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _updateTimer?.Dispose();
            _http?.Dispose();
        }
        base.Dispose(disposing);
    }
}
