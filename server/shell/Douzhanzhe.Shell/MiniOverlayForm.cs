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
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

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
    private string _mode = "办公均衡";
    private bool _turboActive = false;
    private bool _fetching = false;
    private double _opacity = 1.0;

    public MiniOverlayForm(string configDir)
    {
        _stateFilePath = Path.Combine(configDir, "overlay-state.json");
        _http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(800) };

        // 窗口基础属性：现代科技风悬浮挂件
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(286, 78);
        BackColor = Color.FromArgb(16, 21, 30);
        DoubleBuffered = true;

        ApplyRoundedRegion();

        RestorePosition();
        Opacity = _opacity;

        // 鼠标左键按住拖动
        MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
                SavePosition();
            }
        };

        // 双击隐藏，右键现代暗黑科技菜单
        DoubleClick += (s, e) => Hide();
        var menu = new ContextMenuStrip
        {
            Renderer = new ModernDarkMenuRenderer(),
            ShowImageMargin = false,
            Font = new Font("Microsoft YaHei UI", 9.0f, FontStyle.Regular),
            Padding = new Padding(4, 4, 4, 4)
        };
        var opacityMenu = new ToolStripMenuItem("🌓 调节透明度");
        opacityMenu.DropDown.Renderer = new ModernDarkMenuRenderer();
        if (opacityMenu.DropDown is ToolStripDropDownMenu opDrop) opDrop.ShowImageMargin = false;
        opacityMenu.DropDown.Font = new Font("Microsoft YaHei UI", 9.0f, FontStyle.Regular);
        opacityMenu.DropDownItems.Add("100% (不透明)", null, (s, e) => SetOpacity(1.0));
        opacityMenu.DropDownItems.Add("85% (微透)", null, (s, e) => SetOpacity(0.85));
        opacityMenu.DropDownItems.Add("70% (半透)", null, (s, e) => SetOpacity(0.70));
        opacityMenu.DropDownItems.Add("50% (高透)", null, (s, e) => SetOpacity(0.50));
        menu.Items.Add(opacityMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("📊 隐藏挂件 (Ctrl+Shift+O 呼出)", null, (s, e) => Hide());
        ContextMenuStrip = menu;

        // 定时轮询
        _updateTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _updateTimer.Tick += async (s, e) => await FetchTelemetryAsync();
        _updateTimer.Start();
    }

    private void ApplyRoundedRegion()
    {
        try
        {
            var rgn = CreateRoundRectRgn(0, 0, Width + 1, Height + 1, 14, 14);
            if (rgn != IntPtr.Zero)
            {
                Region = Region.FromHrgn(rgn);
            }
        }
        catch { }
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
                                0 => "办公均衡",
                                1 => "狂暴野兽",
                                2 => "极致安静",
                                3 => "满血斗战",
                                _ => "自定义"
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

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var roundPath = CreateRoundedPath(bounds, 10);

        // 1. 深邃太空微渐变背景
        using (var bgBrush = new LinearGradientBrush(
            bounds,
            Color.FromArgb(22, 28, 38),
            Color.FromArgb(13, 17, 24),
            90f))
        {
            g.FillPath(bgBrush, roundPath);
        }

        // 2. 双层科技感发光边框 (外框冷灰，内框微幽蓝)
        using (var borderPen = new Pen(Color.FromArgb(50, 60, 78), 1))
        {
            g.DrawPath(borderPen, roundPath);
        }
        var innerRect = new Rectangle(1, 1, Width - 3, Height - 3);
        using (var innerPath = CreateRoundedPath(innerRect, 9))
        using (var innerPen = new Pen(Color.FromArgb(25, 88, 166, 255), 1))
        {
            g.DrawPath(innerPen, innerPath);
        }

        // 字体与画刷
        using var fontTitle = new Font("Segoe UI", 8.0f, FontStyle.Bold);
        using var fontVal = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        using var fontSub = new Font("Segoe UI", 8.0f, FontStyle.Regular);
        using var fontBadge = new Font("Microsoft YaHei UI", 8.0f, FontStyle.Bold);

        using var brushWhite = new SolidBrush(Color.FromArgb(240, 246, 252));
        using var brushMuted = new SolidBrush(Color.FromArgb(139, 148, 158));
        using var brushCpuText = new SolidBrush(Color.FromArgb(88, 166, 255));
        using var brushGpuText = new SolidBrush(Color.FromArgb(63, 185, 80));
        using var brushTurbo = new SolidBrush(Color.FromArgb(0, 229, 255));
        using var brushWarn = new SolidBrush(Color.FromArgb(248, 81, 73));
        using var brushAmber = new SolidBrush(Color.FromArgb(240, 180, 41));

        // ── 上半区：CPU (左) ──
        // CPU 微型胶囊
        var cpuPill = new Rectangle(12, 10, 32, 16);
        using (var pillPath = CreateRoundedPath(cpuPill, 4))
        using (var pillBg = new SolidBrush(Color.FromArgb(30, 88, 166, 255)))
        using (var pillBorder = new Pen(Color.FromArgb(60, 88, 166, 255), 1))
        {
            g.FillPath(pillBg, pillPath);
            g.DrawPath(pillBorder, pillPath);
        }
        g.DrawString("CPU", fontTitle, brushCpuText, 16, 11);

        // CPU 温度（根据温度平滑变色）
        SolidBrush cpuTempBrush = _cpuTemp >= 88 ? brushWarn : (_cpuTemp >= 78 ? brushAmber : brushWhite);
        g.DrawString($"{_cpuTemp}°C", fontVal, cpuTempBrush, 48, 8);

        // CPU 占用与频率
        string cpuSubText = _cpuFreq > 0 ? $"{_cpuUsage}% {_cpuFreq:F1}G" : $"{_cpuUsage}%";
        g.DrawString(cpuSubText, fontSub, brushMuted, 90, 11);

        // 中轴微垂直光线
        using (var vLineBrush = new LinearGradientBrush(
            new Rectangle(140, 9, 1, 18),
            Color.FromArgb(0, 56, 70, 90),
            Color.FromArgb(120, 56, 70, 90),
            90f))
        using (var vLinePen = new Pen(vLineBrush, 1))
        {
            g.DrawLine(vLinePen, 140, 9, 140, 27);
        }

        // ── 上半区：GPU (右) ──
        // GPU 微型胶囊
        var gpuPill = new Rectangle(150, 10, 32, 16);
        using (var pillPath = CreateRoundedPath(gpuPill, 4))
        using (var pillBg = new SolidBrush(Color.FromArgb(25, 63, 185, 80)))
        using (var pillBorder = new Pen(Color.FromArgb(50, 63, 185, 80), 1))
        {
            g.FillPath(pillBg, pillPath);
            g.DrawPath(pillBorder, pillPath);
        }
        g.DrawString("GPU", fontTitle, brushGpuText, 154, 11);

        // GPU 温度
        SolidBrush gpuTempBrush = _gpuTemp >= 85 ? brushWarn : (_gpuTemp >= 75 ? brushAmber : brushWhite);
        g.DrawString($"{_gpuTemp}°C", fontVal, gpuTempBrush, 186, 8);

        // GPU 功耗
        g.DrawString($"{_gpuWatts:F0}W", fontSub, brushMuted, 228, 11);

        // ── 中部横向能量光导线（两端渐隐）──
        using (var lineBrush = new LinearGradientBrush(
            new Rectangle(10, 36, Width - 20, 1),
            Color.FromArgb(10, 48, 64, 88),
            Color.FromArgb(140, 48, 64, 88),
            0f))
        {
            lineBrush.SetBlendTriangularShape(0.5f);
            using var linePen = new Pen(lineBrush, 1);
            g.DrawLine(linePen, 10, 36, Width - 10, 36);
        }

        // ── 下半区：风扇与模式 ──
        // 风扇图标与 RPM
        g.DrawString("🌀", fontSub, brushMuted, 10, 47);
        g.DrawString($"{_cpuFan} / {_gpuFan} RPM", fontSub, brushWhite, 28, 48);

        // 模式徽章 (Badge)
        if (_turboActive)
        {
            var turboBadge = new Rectangle(Width - 110, 44, 98, 22);
            using var tPath = CreateRoundedPath(turboBadge, 5);
            using var tBg = new SolidBrush(Color.FromArgb(35, 0, 229, 255));
            using var tPen = new Pen(Color.FromArgb(120, 0, 229, 255), 1);
            g.FillPath(tBg, tPath);
            g.DrawPath(tPen, tPath);
            g.DrawString("⚡ TURBO 强冷", fontBadge, brushTurbo, Width - 106, 47);
        }
        else
        {
            var (badgeColor, badgeText) = _mode switch
            {
                "狂暴野兽" => (Color.FromArgb(255, 166, 87), "🔥 狂暴野兽"),
                "极致安静" => (Color.FromArgb(210, 168, 255), "🌙 极致安静"),
                "满血斗战" => (Color.FromArgb(255, 123, 114), "⚔️ 满血斗战"),
                _          => (Color.FromArgb(121, 192, 255), "🛡️ 办公均衡")
            };

            var modeBadge = new Rectangle(Width - 92, 45, 80, 20);
            using var mPath = CreateRoundedPath(modeBadge, 4);
            using var mBg = new SolidBrush(Color.FromArgb(25, badgeColor.R, badgeColor.G, badgeColor.B));
            using var mPen = new Pen(Color.FromArgb(60, badgeColor.R, badgeColor.G, badgeColor.B), 1);
            using var mTextBrush = new SolidBrush(badgeColor);
            g.FillPath(mBg, mPath);
            g.DrawPath(mPen, mPath);
            g.DrawString(badgeText, fontBadge, mTextBrush, Width - 88, 48);
        }
    }

    private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
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

