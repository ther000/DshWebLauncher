using System.Drawing;
using System.IO;
using DshWebLauncher.Models;
using Forms = System.Windows.Forms;

namespace DshWebLauncher.Services;

public sealed class IconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Icon _blueIcon;
    private readonly Icon _whiteIcon;
    private Forms.ToolStripMenuItem? _startItem;
    private Forms.ToolStripMenuItem? _stopItem;
    private Forms.ToolStripMenuItem? _openItem;

    public IconService()
    {
        _blueIcon = LoadIcon("tray-blue.ico");
        _whiteIcon = LoadIcon("tray-white.ico");
        _notifyIcon = new Forms.NotifyIcon { Visible = true, Text = "DSH Web Launcher", Icon = _whiteIcon };
    }

    public event EventHandler? ShowRequested;
    public event EventHandler? StartRequested;
    public event EventHandler? StopRequested;
    public event EventHandler? OpenWebRequested;
    public event EventHandler? ExitRequested;

    public void Update(RuntimeSnapshot snapshot, string tooltip)
    {
        _notifyIcon.Icon = snapshot.IsRunning ? _blueIcon : _whiteIcon;
        _notifyIcon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;
        if (_startItem is not null) _startItem.Enabled = !snapshot.IsRunning && !snapshot.IsManaged;
        if (_stopItem is not null) _stopItem.Enabled = snapshot.IsManaged;
        if (_openItem is not null) _openItem.Enabled = snapshot.IsRunning;
    }

    public void BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开控制台", null, (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty));
        _startItem = new Forms.ToolStripMenuItem("启动 DSH Web", null, (_, _) => StartRequested?.Invoke(this, EventArgs.Empty));
        _stopItem = new Forms.ToolStripMenuItem("停止受管 DSH Web", null, (_, _) => StopRequested?.Invoke(this, EventArgs.Empty)) { Enabled = false };
        _openItem = new Forms.ToolStripMenuItem("打开 Web 页面", null, (_, _) => OpenWebRequested?.Invoke(this, EventArgs.Empty)) { Enabled = false };
        menu.Items.Add(_startItem); menu.Items.Add(_stopItem); menu.Items.Add(_openItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出并停止受管 DSH", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose(); _blueIcon.Dispose(); _whiteIcon.Dispose();
    }

    private static Icon LoadIcon(string name)
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri($"/DshWebLauncher;component/Assets/{name}", UriKind.Relative));
        if (resource is null) throw new FileNotFoundException($"找不到图标资源：{name}");
        using var stream = resource.Stream;
        using var loadedIcon = new Icon(stream);
        return (Icon)loadedIcon.Clone();
    }
}
