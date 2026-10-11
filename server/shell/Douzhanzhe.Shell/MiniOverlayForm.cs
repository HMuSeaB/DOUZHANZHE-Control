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
    private string _sizeMode = "standard"; // standard (356x88), large (400x98), compact (312x80)
    private float _fanRotation = 0f;

    public MiniOverlayForm(string configDir)
    {
        _stateFilePath = Path.Combine(configDir, "overlay-state.json");
        _http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(800) };

        // 窗口基础属性：现代科技风悬浮挂件
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = GetSizeFromMode(_sizeMode);
        BackColor = Color.FromArgb(16, 21, 30);
        DoubleBuffered = true;

        RestorePosition();
        ApplyRoundedRegion();
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
        BuildContextMenu();

        // 定时轮询
        _updateTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _updateTimer.Tick += async (s, e) =>
        {
            _fanRotation = (_fanRotation + 45f) % 360f;
            await FetchTelemetryAsync();
        };
        _updateTimer.Start();
    }

    private static Size GetSizeFromMode(string mode) => mode switch
    {
        "large"   => new Size(400, 98),
        "compact" => new Size(312, 80),
        _         => new Size(356, 88)
    };

    private void BuildContextMenu()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new ModernDarkMenuRenderer(),
            ShowImageMargin = false,
            Font = new Font("Microsoft YaHei UI", 9.0f, FontStyle.Regular),
            Padding = new Padding(4, 4, 4, 4)
        };

        // 尺寸调节
        var sizeMenu = new ToolStripMenuItem("📐 挂件显示尺寸");
        sizeMenu.DropDown.Renderer = new ModernDarkMenuRenderer();
        if (sizeMenu.DropDown is ToolStripDropDownMenu sizeDrop) sizeDrop.ShowImageMargin = false;
        sizeMenu.DropDown.Font = new Font("Microsoft YaHei UI", 9.0f, FontStyle.Regular);
        sizeMenu.DropDownItems.Add("标准视野 (356 × 88) [默认]", null, (s, e) => SetSizeMode("standard"));
        sizeMenu.DropDownItems.Add("大屏视界 (400 × 98)", null, (s, e) => SetSizeMode("large"));
        sizeMenu.DropDownItems.Add("紧凑微型 (312 × 80)", null, (s, e) => SetSizeMode("compact"));
        menu.Items.Add(sizeMenu);

        // 透明度调节
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
    }

    private void SetSizeMode(string mode)
    {
        _sizeMode = mode;
        Size = GetSizeFromMode(mode);
        ApplyRoundedRegion();
        SavePosition();
        Invalidate();
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
                if (root.TryGetProperty("sizeMode", out var szProp))
                {
                    var sz = szProp.GetString();
                    if (!string.IsNullOrEmpty(sz))
                    {
                        _sizeMode = sz;
                        Size = GetSizeFromMode(_sizeMode);
                    }
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

            var json = JsonSerializer.Serialize(new
            {
                x = Location.X,
                y = Location.Y,
                opacity = _opacity,
                sizeMode = _sizeMode
            });
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
        using var roundPath = CreateRoundedPath(bounds, 12);

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
        using (var innerPath = CreateRoundedPath(innerRect, 11))
        using (var innerPen = new Pen(Color.FromArgb(28, 88, 166, 255), 1))
        {
            g.DrawPath(innerPen, innerPath);
        }

        // 字体与画刷 (大屏模式微调字号)
        bool isLarge = _sizeMode == "large";
        using var fontTitle = new Font("Segoe UI", isLarge ? 8.5f : 8.0f, FontStyle.Bold);
        using var fontVal = new Font("Segoe UI", isLarge ? 12.0f : 10.5f, FontStyle.Bold);
        using var fontSub = new Font("Segoe UI", isLarge ? 9.0f : 8.0f, FontStyle.Regular);
        using var fontBadge = new Font("Microsoft YaHei UI", isLarge ? 8.5f : 8.0f, FontStyle.Bold);

        using var brushWhite = new SolidBrush(Color.FromArgb(240, 246, 252));
        using var brushMuted = new SolidBrush(Color.FromArgb(139, 148, 158));
        using var brushCpuText = new SolidBrush(Color.FromArgb(88, 166, 255));
        using var brushGpuText = new SolidBrush(Color.FromArgb(63, 185, 80));
        using var brushTurbo = new SolidBrush(Color.FromArgb(0, 229, 255));
        using var brushWarn = new SolidBrush(Color.FromArgb(248, 81, 73));
        using var brushAmber = new SolidBrush(Color.FromArgb(250, 204, 21));

        float topY = isLarge ? 12f : 10f;
        float midX = Width / 2f;

        // ── 上半区：CPU (左侧自适应流式排版，绝不重叠) ──
        float curCpuX = 14f;
        // CPU 微型胶囊 Pill
        var cpuPill = new RectangleF(curCpuX, topY, 34, 18);
        using (var pillPath = CreateRoundedPath(Rectangle.Round(cpuPill), 4))
        using (var pillBg = new SolidBrush(Color.FromArgb(30, 88, 166, 255)))
        using (var pillBorder = new Pen(Color.FromArgb(60, 88, 166, 255), 1))
        {
            g.FillPath(pillBg, pillPath);
            g.DrawPath(pillBorder, pillPath);
        }
        g.DrawString("CPU", fontTitle, brushCpuText, curCpuX + 4, topY + 1.5f);
        curCpuX += 40f;

        // CPU 温度（智能三段色温）
        SolidBrush cpuTempBrush = _cpuTemp >= 88 ? brushWarn : (_cpuTemp >= 78 ? brushAmber : brushWhite);
        string cpuTempStr = $"{_cpuTemp}°C";
        var cpuTempSize = g.MeasureString(cpuTempStr, fontVal);
        g.DrawString(cpuTempStr, fontVal, cpuTempBrush, curCpuX, topY - 1.5f);
        curCpuX += cpuTempSize.Width + 4f;

        // CPU 占用与频率
        string cpuSubText = _cpuFreq > 0 ? $"{_cpuUsage}% {_cpuFreq:F1}G" : $"{_cpuUsage}%";
        g.DrawString(cpuSubText, fontSub, brushMuted, curCpuX, topY + 1.5f);

        // 中轴微垂直光纤分割线
        using (var vLineBrush = new LinearGradientBrush(
            new RectangleF(midX - 1, topY - 1, 2, 20),
            Color.FromArgb(0, 56, 70, 90),
            Color.FromArgb(140, 56, 70, 90),
            90f))
        using (var vLinePen = new Pen(vLineBrush, 1))
        {
            g.DrawLine(vLinePen, midX, topY - 1, midX, topY + 19);
        }

        // ── 上半区：GPU (右侧自适应流式排版，绝不重叠) ──
        float curGpuX = midX + 10f;
        // GPU 微型胶囊 Pill
        var gpuPill = new RectangleF(curGpuX, topY, 34, 18);
        using (var pillPath = CreateRoundedPath(Rectangle.Round(gpuPill), 4))
        using (var pillBg = new SolidBrush(Color.FromArgb(25, 63, 185, 80)))
        using (var pillBorder = new Pen(Color.FromArgb(50, 63, 185, 80), 1))
        {
            g.FillPath(pillBg, pillPath);
            g.DrawPath(pillBorder, pillPath);
        }
        g.DrawString("GPU", fontTitle, brushGpuText, curGpuX + 4, topY + 1.5f);
        curGpuX += 40f;

        // GPU 温度
        SolidBrush gpuTempBrush = _gpuTemp >= 85 ? brushWarn : (_gpuTemp >= 75 ? brushAmber : brushWhite);
        string gpuTempStr = $"{_gpuTemp}°C";
        var gpuTempSize = g.MeasureString(gpuTempStr, fontVal);
        g.DrawString(gpuTempStr, fontVal, gpuTempBrush, curGpuX, topY - 1.5f);
        curGpuX += gpuTempSize.Width + 4f;

        // GPU 功耗
        string gpuWattStr = $"{_gpuWatts:F0}W";
        g.DrawString(gpuWattStr, fontSub, brushMuted, curGpuX, topY + 1.5f);

        // ── 中部横向能量光纤导轨（两端渐隐）──
        float splitY = isLarge ? 46f : 41f;
        using (var lineBrush = new LinearGradientBrush(
            new RectangleF(12, splitY, Width - 24, 1),
            Color.FromArgb(10, 48, 64, 88),
            Color.FromArgb(140, 48, 64, 88),
            0f))
        {
            lineBrush.SetBlendTriangularShape(0.5f);
            using var linePen = new Pen(lineBrush, 1);
            g.DrawLine(linePen, 12, splitY, Width - 12, splitY);
        }

        // ── 下半区：风扇与模式 ──
        float botY = isLarge ? 56f : 50f;

        // 自绘极简科技风矢量风扇图标（彻底消除方块乱码）
        float fanCenterX = 22f;
        float fanCenterY = botY + 8f;
        float fanRadius = isLarge ? 7f : 6.5f;
        DrawVectorFan(g, fanCenterX, fanCenterY, fanRadius, Color.FromArgb(56, 189, 248), _fanRotation);

        // 风扇转速读数
        string fanText = $"大 {_cpuFan} / 小 {_gpuFan} RPM";
        g.DrawString(fanText, fontSub, brushWhite, 36f, botY + 1.5f);

        // 右侧模式徽章：动态测量宽度，绝不被裁切！
        var (badgeColor, badgeTitle) = _turboActive
            ? (Color.FromArgb(0, 229, 255), "TURBO 强冷")
            : _mode switch
            {
                "狂暴野兽" => (Color.FromArgb(255, 166, 87), "狂暴野兽"),
                "极致安静" => (Color.FromArgb(210, 168, 255), "极致安静"),
                "满血斗战" => (Color.FromArgb(255, 123, 114), "满血斗战"),
                _          => (Color.FromArgb(121, 192, 255), "办公均衡")
            };

        var badgeTextSize = g.MeasureString(badgeTitle, fontBadge);
        float badgeW = badgeTextSize.Width + 18f;
        float badgeH = isLarge ? 24f : 20f;
        float badgeX = Width - badgeW - 14f;
        float badgeY = botY;

        var modeBadgeRect = new RectangleF(badgeX, badgeY, badgeW, badgeH);
        using (var mPath = CreateRoundedPath(Rectangle.Round(modeBadgeRect), 4))
        using (var mBg = new SolidBrush(Color.FromArgb(30, badgeColor.R, badgeColor.G, badgeColor.B)))
        using (var mPen = new Pen(Color.FromArgb(90, badgeColor.R, badgeColor.G, badgeColor.B), 1))
        using (var mTextBrush = new SolidBrush(badgeColor))
        {
            g.FillPath(mBg, mPath);
            g.DrawPath(mPen, mPath);
            g.DrawString(badgeTitle, fontBadge, mTextBrush, badgeX + 9f, badgeY + (isLarge ? 4f : 2.5f));
        }
    }

    /// <summary>
    /// 自绘科技感微型矢量风扇轮廓（圆环 + 3 片旋转微叶片，彻底告别字符集方块乱码）
    /// </summary>
    private static void DrawVectorFan(Graphics g, float cx, float cy, float radius, Color color, float rotationAngle)
    {
        using var pen = new Pen(color, 1.3f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        // 外圈微光圆环
        using var ringPen = new Pen(Color.FromArgb(60, color.R, color.G, color.B), 1f);
        g.DrawEllipse(ringPen, cx - radius, cy - radius, radius * 2, radius * 2);

        // 中心微轴点
        using var centerBrush = new SolidBrush(color);
        g.FillEllipse(centerBrush, cx - 1.5f, cy - 1.5f, 3f, 3f);

        // 3 片流线叶片
        for (int i = 0; i < 3; i++)
        {
            float rad = (rotationAngle + i * 120f) * (float)Math.PI / 180f;
            float endX = cx + (float)Math.Cos(rad) * (radius - 1f);
            float endY = cy + (float)Math.Sin(rad) * (radius - 1f);
            g.DrawLine(pen, cx, cy, endX, endY);
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


