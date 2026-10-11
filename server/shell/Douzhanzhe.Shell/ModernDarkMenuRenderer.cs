using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Douzhanzhe.Shell;

/// <summary>
/// 现代暗黑科技风右键菜单渲染器 (Modern Dark HUD Menu Renderer)
/// 替代 WinForms 经典浅色/蓝框默认菜单，与控制台深色极客风格浑然一体
/// </summary>
public sealed class ModernDarkMenuRenderer : ToolStripProfessionalRenderer
{
    public ModernDarkMenuRenderer() : base(new DarkMenuColorTable())
    {
        RoundedEdges = true;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // 深邃太空黑蓝渐变背景
        using var brush = new LinearGradientBrush(
            e.AffectedBounds,
            Color.FromArgb(20, 26, 36),
            Color.FromArgb(14, 18, 25),
            90f);
        g.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // 细腻的深灰微光边框
        using var borderPen = new Pen(Color.FromArgb(48, 56, 70), 1);
        var rect = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        g.DrawRectangle(borderPen, rect);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var item = e.Item;

        if (item.Selected && item.Enabled)
        {
            // 悬停态：科技蓝微发光圆角胶囊背景
            var bounds = new Rectangle(3, 1, item.Width - 6, item.Height - 2);
            using var path = CreateRoundedPath(bounds, 4);
            using var brush = new LinearGradientBrush(
                bounds,
                Color.FromArgb(38, 52, 76),
                Color.FromArgb(28, 38, 56),
                90f);
            g.FillPath(brush, path);

            // 悬停微边框
            using var pen = new Pen(Color.FromArgb(64, 88, 128), 1);
            g.DrawPath(pen, path);
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // 选中时晶莹高光白，未选中时柔和象牙银
        e.TextColor = e.Item.Selected ? Color.FromArgb(240, 246, 252) : Color.FromArgb(201, 209, 217);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var g = e.Graphics;
        // 柔和微光分割线
        using var pen = new Pen(Color.FromArgb(36, 44, 58), 1);
        int itemH = e.Item?.Height ?? 6;
        int itemW = e.Item?.Width ?? (e.ToolStrip?.Width ?? 120);
        int y = itemH / 2;
        g.DrawLine(pen, 8, y, itemW - 8, y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        // 子菜单展开箭头：选中时青蓝高光，平常为银灰
        e.ArrowColor = (e.Item?.Selected ?? false) ? Color.FromArgb(88, 166, 255) : Color.FromArgb(139, 148, 158);
        base.OnRenderArrow(e);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // 消除默认左侧浅色槽边，全暗处理
        var g = e.Graphics;
        using var brush = new SolidBrush(Color.FromArgb(18, 23, 32));
        g.FillRectangle(brush, e.AffectedBounds);
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
}

/// <summary>
/// 现代暗黑科技风配色表
/// </summary>
public sealed class DarkMenuColorTable : ProfessionalColorTable
{
    public override Color MenuBorder => Color.FromArgb(48, 56, 70);
    public override Color MenuItemBorder => Color.FromArgb(64, 88, 128);
    public override Color MenuItemSelected => Color.FromArgb(38, 52, 76);
    public override Color ToolStripDropDownBackground => Color.FromArgb(16, 21, 30);
    public override Color ImageMarginGradientBegin => Color.FromArgb(16, 21, 30);
    public override Color ImageMarginGradientMiddle => Color.FromArgb(16, 21, 30);
    public override Color ImageMarginGradientEnd => Color.FromArgb(16, 21, 30);
    public override Color SeparatorDark => Color.FromArgb(36, 44, 58);
    public override Color SeparatorLight => Color.FromArgb(36, 44, 58);
    public override Color CheckBackground => Color.FromArgb(38, 52, 76);
    public override Color CheckSelectedBackground => Color.FromArgb(48, 64, 96);
}
