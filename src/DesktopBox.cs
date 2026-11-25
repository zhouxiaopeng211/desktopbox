using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DesktopBox
{
    internal static class Program
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        public static readonly int WM_TOGGLE = RegisterWindowMessage("DesktopBox.Toggle.v1");

        [STAThread]
        private static void Main()
        {
            bool created;
            using (Mutex mutex = new Mutex(true, "DesktopBox.SingleInstance.v1", out created))
            {
                if (!created)
                {
                    IntPtr h = FindWindow(null, "桌面应用盒子");
                    if (h != IntPtr.Zero)
                    {
                        PostMessage(h, WM_TOGGLE, IntPtr.Zero, IntPtr.Zero);
                    }
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new BoxForm());
            }
        }
    }

    internal class Item
    {
        public string Path;
        public string Name;
        public string Category;
        public bool IsDir;
        public bool IsSpecial;
    }

    internal class Tile : Panel
    {
        private readonly Label nameLabel;
        private bool selected;

        public readonly Item Data;

        public Tile(Item item, Image icon)
        {
            Data = item;
            Width = 88;
            Height = 86;
            Margin = new Padding(2);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;

            PictureBox pic = new PictureBox();
            pic.Width = 40;
            pic.Height = 40;
            pic.Left = (Width - 40) / 2;
            pic.Top = 8;
            pic.SizeMode = PictureBoxSizeMode.Zoom;
            pic.Image = icon;
            pic.Enabled = false;

            nameLabel = new Label();
            nameLabel.Left = 1;
            nameLabel.Top = 52;
            nameLabel.Width = Width - 2;
            nameLabel.Height = 30;
            nameLabel.TextAlign = ContentAlignment.TopCenter;
            nameLabel.ForeColor = Color.FromArgb(235, 235, 240);
            nameLabel.Font = new Font("Microsoft YaHei UI", 7.5f);
            nameLabel.Text = item.Name;
            nameLabel.Enabled = false;

            Controls.Add(pic);
            Controls.Add(nameLabel);

            MouseEnter += delegate { if (!selected) BackColor = Color.FromArgb(38, 255, 255, 255); };
            MouseLeave += delegate { if (!selected) BackColor = Color.Transparent; };
        }

        public void SetSelected(bool value)
        {
            selected = value;
            BackColor = value ? Color.FromArgb(70, 106, 166, 255) : Color.Transparent;
        }
    }

    internal class BoxForm : Form
    {
        private const string RecycleBinClsid = "::{645FF040-5081-101B-9F08-00AA002F954E}";
        private const int CAT_ALL = 0;
        private const int CAT_APP = 1;
        private const int CAT_FOLDER = 2;
        private const int CAT_DOC = 3;
        private const int CAT_MEDIA = 4;
        private const int CAT_WEB = 5;
        private const int CAT_OTHER = 6;

        private static readonly string[] CatNames = { "全部", "程序", "文件夹", "文档", "媒体", "网页", "其他" };
        private static readonly string[] CatIds = { "all", "app", "folder", "doc", "media", "web", "other" };

        private readonly List<Item> items = new List<Item>();
        private readonly Dictionary<string, Image> iconCache = new Dictionary<string, Image>();
        private readonly HashSet<string> hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Tile> tiles = new List<Tile>();
        private readonly List<Button> tabButtons = new List<Button>();

        private FlowLayoutPanel grid;
        private TextBox search;
        private Label countLabel;
        private Label emptyLabel;
        private ContextMenuStrip tileMenu;
        private NotifyIcon tray;
        private ToolStripMenuItem desktopIconsItem;
        private bool iconsHidden;
        private System.Windows.Forms.Timer hiderTimer;
        private int category = CAT_ALL;
        private Tile selectedTile;
        private Item menuItem;
        private ToolStripMenuItem emptyBinItem;
        private bool menuOpen;

        private string HiddenFile
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopBox");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "hidden.txt");
            }
        }

        public BoxForm()
        {
            Text = "桌面应用盒子";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(22, 24, 30);
            Size = new Size(580, 540);
            DoubleBuffered = true;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.Dpi;

            BuildUi();
            LoadHidden();
            LoadItems();
            BuildTray();
            RenderGrid();
            StartDesktopHider();
            Shown += delegate
            {
                SendMessage(search.Handle, 0x1501, IntPtr.Zero, "搜索桌面项目…");
                ShowPanel();
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        private void BuildUi()
        {
            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 50;
            header.BackColor = Color.Transparent;

            Label title = new Label();
            title.Text = "桌面应用";
            title.ForeColor = Color.White;
            title.Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
            title.AutoSize = false;
            title.Width = 88;
            title.Height = 28;
            title.Left = 16;
            title.Top = 12;

            search = new TextBox();
            search.BorderStyle = BorderStyle.FixedSingle;
            search.BackColor = Color.FromArgb(38, 40, 48);
            search.ForeColor = Color.White;
            search.Font = new Font("Microsoft YaHei UI", 9f);
            search.Left = 110;
            search.Top = 12;
            search.Width = 400;
            search.Height = 26;
            search.TextChanged += delegate { RenderGrid(); };

            Button close = new Button();
            close.Text = "×";
            close.FlatStyle = FlatStyle.Flat;
            close.FlatAppearance.BorderSize = 0;
            close.ForeColor = Color.FromArgb(200, 200, 210);
            close.BackColor = Color.Transparent;
            close.Font = new Font("Segoe UI", 12f);
            close.Width = 30;
            close.Height = 30;
            close.Left = 534;
            close.Top = 8;
            close.Click += delegate { HidePanel(); };

            header.Controls.Add(title);
            header.Controls.Add(search);
            header.Controls.Add(close);

            Panel tabs = new Panel();
            tabs.Dock = DockStyle.Top;
            tabs.Height = 38;
            tabs.BackColor = Color.Transparent;

            int left = 16;
            for (int i = 0; i < CatNames.Length; i++)
            {
                int index = i;
                Button b = new Button();
                b.Text = CatNames[i];
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.Font = new Font("Microsoft YaHei UI", 8.5f);
                b.Height = 26;
                b.Width = 66;
                b.Left = left;
                b.Top = 6;
                b.Tag = index;
                b.Click += delegate { category = index; RenderGrid(); };
                tabs.Controls.Add(b);
                tabButtons.Add(b);
                left += 70;
            }

            grid = new FlowLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.AutoScroll = true;
            grid.WrapContents = true;
            grid.BackColor = Color.Transparent;
            grid.Padding = new Padding(10, 2, 6, 2);

            emptyLabel = new Label();
            emptyLabel.Dock = DockStyle.Fill;
            emptyLabel.TextAlign = ContentAlignment.MiddleCenter;
            emptyLabel.ForeColor = Color.FromArgb(150, 150, 160);
            emptyLabel.Font = new Font("Microsoft YaHei UI", 9f);
            emptyLabel.Text = "这里还没有项目";
            emptyLabel.Visible = false;

            Panel footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 28;
            footer.BackColor = Color.Transparent;

            countLabel = new Label();
            countLabel.ForeColor = Color.FromArgb(150, 150, 160);
            countLabel.Font = new Font("Microsoft YaHei UI", 8f);
            countLabel.Left = 16;
            countLabel.Top = 6;
            countLabel.AutoSize = true;

            Label hint = new Label();
            hint.Text = "单击选中 · 双击打开 · 右键更多";
            hint.ForeColor = Color.FromArgb(120, 120, 130);
            hint.Font = new Font("Microsoft YaHei UI", 8f);
            hint.AutoSize = true;
            hint.Anchor = AnchorStyles.Right;
            hint.Left = 360;
            hint.Top = 6;
            hint.Width = 210;
            hint.TextAlign = ContentAlignment.MiddleRight;

            footer.Controls.Add(countLabel);
            footer.Controls.Add(hint);

            Controls.Add(grid);
            Controls.Add(emptyLabel);
            Controls.Add(footer);
            Controls.Add(tabs);
            Controls.Add(header);

            tileMenu = new ContextMenuStrip();
            tileMenu.Opening += delegate
            {
                menuOpen = true;
                if (emptyBinItem != null) emptyBinItem.Visible = menuItem != null && menuItem.IsSpecial;
            };
            tileMenu.Closed += delegate { menuOpen = false; };
            tileMenu.Items.Add("打开", null, delegate { OpenItem(menuItem); });
            tileMenu.Items.Add("打开所在位置", null, delegate { RevealItem(menuItem); });
            emptyBinItem = new ToolStripMenuItem("清空回收站", null, delegate { EmptyRecycleBin(); });
            tileMenu.Items.Add(emptyBinItem);
            tileMenu.Items.Add(new ToolStripSeparator());
            tileMenu.Items.Add("从盒子隐藏", null, delegate { HideItem(menuItem); });
        }

        private void StartDesktopHider()
        {
            HideNewDesktopItems();
            hiderTimer = new System.Windows.Forms.Timer();
            hiderTimer.Interval = 1500;
            hiderTimer.Tick += delegate { HideNewDesktopItems(); };
            hiderTimer.Start();
        }

        private void HideNewDesktopItems()
        {
            string[] folders = new string[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
            };

            foreach (string folder in folders)
            {
                if (folder == null || folder.Length == 0 || !Directory.Exists(folder)) continue;
                string[] entries;
                try { entries = Directory.GetFileSystemEntries(folder); }
                catch { continue; }

                foreach (string path in entries)
                {
                    string name = Path.GetFileName(path);
                    if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.Equals("thumbs.db", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.Equals("桌面应用盒子.lnk", StringComparison.OrdinalIgnoreCase)) continue;

                    try
                    {
                        FileAttributes attrs = File.GetAttributes(path);
                        if ((attrs & FileAttributes.Hidden) == 0)
                        {
                            File.SetAttributes(path, attrs | FileAttributes.Hidden);
                        }
                    }
                    catch
                    {
                        /* public desktop or locked file: retry next tick */
                    }
                }
            }
        }

        private void BuildTray()
        {
            tray = new NotifyIcon();
            tray.Icon = SystemIcons.Application;
            tray.Text = "桌面应用盒子";
            tray.Visible = true;

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("显示盒子", null, delegate { ShowPanel(); });
            menu.Items.Add("恢复隐藏项", null, delegate { RestoreHidden(); });
            iconsHidden = ReadIconsHidden();
            desktopIconsItem = new ToolStripMenuItem(iconsHidden ? "显示桌面图标" : "隐藏桌面图标");
            desktopIconsItem.Click += delegate { ToggleDesktopIcons(); };
            menu.Items.Add(desktopIconsItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { Close(); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowPanel(); };
        }

        private void LoadHidden()
        {
            hidden.Clear();
            try
            {
                if (File.Exists(HiddenFile))
                {
                    foreach (string line in File.ReadAllLines(HiddenFile))
                    {
                        if (line.Trim().Length > 0) hidden.Add(line.Trim());
                    }
                }
            }
            catch { }
        }

        private void SaveHidden()
        {
            try { File.WriteAllLines(HiddenFile, new List<string>(hidden).ToArray()); }
            catch { }
        }

        private void RestoreHidden()
        {
            hidden.Clear();
            SaveHidden();
            RenderGrid();
        }

        private static string Classify(string path, out bool isDir)
        {
            isDir = Directory.Exists(path);
            if (isDir) return "folder";

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".lnk" || ext == ".exe" || ext == ".bat" || ext == ".cmd" || ext == ".msi" || ext == ".com") return "app";
            if (ext == ".url" || ext == ".html" || ext == ".htm" || ext == ".website") return "web";
            if (ext == ".doc" || ext == ".docx" || ext == ".pdf" || ext == ".txt" || ext == ".md" || ext == ".xls" || ext == ".xlsx" || ext == ".ppt" || ext == ".pptx" || ext == ".csv" || ext == ".rtf") return "doc";
            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".webp" || ext == ".bmp" || ext == ".svg" || ext == ".mp4" || ext == ".mkv" || ext == ".mov" || ext == ".mp3" || ext == ".wav" || ext == ".flac") return "media";
            return "other";
        }

        private static string DisplayName(string path, bool isDir)
        {
            string name = Path.GetFileName(path);
            if (isDir) return name;
            string ext = Path.GetExtension(path);
            if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || ext.Equals(".url", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileNameWithoutExtension(path);
            }
            return name;
        }

        private void LoadItems()
        {
            items.Clear();
            string[] folders = new string[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
            };

            foreach (string folder in folders)
            {
                if (folder == null || folder.Length == 0 || !Directory.Exists(folder)) continue;
                string[] entries;
                try { entries = Directory.GetFileSystemEntries(folder); }
                catch { continue; }

                foreach (string path in entries)
                {
                    string fileName = Path.GetFileName(path).ToLowerInvariant();
                    if (fileName == "desktop.ini" || fileName == "thumbs.db") continue;
                    if (fileName == "桌面应用盒子.lnk") continue;
                    if (hidden.Contains(path)) continue;

                    bool isDir;
                    string cat = Classify(path, out isDir);
                    Item it = new Item();
                    it.Path = path;
                    it.IsDir = isDir;
                    it.Category = cat;
                    it.Name = DisplayName(path, isDir);
                    items.Add(it);
                }
            }

            items.Sort(delegate(Item a, Item b) { return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase); });

            Item recycle = new Item();
            recycle.Path = RecycleBinClsid;
            recycle.Name = "回收站";
            recycle.Category = "app";
            recycle.IsSpecial = true;
            items.Insert(0, recycle);
        }

        private Image GetIcon(Item it)
        {
            if (it.IsSpecial) return GetRecycleBinIcon();
            if (iconCache.ContainsKey(it.Path)) return iconCache[it.Path];

            SHFILEINFO info = new SHFILEINFO();
            uint flags = SHGFI_ICON | SHGFI_LARGEICON;
            if (it.IsDir) flags |= SHGFI_USEFILEATTRIBUTES;
            IntPtr result = SHGetFileInfo(it.Path, it.IsDir ? FILE_ATTRIBUTE_DIRECTORY : 0, ref info, (uint)Marshal.SizeOf(info), flags);

            Image image = null;
            if (result != IntPtr.Zero && info.hIcon != IntPtr.Zero)
            {
                try
                {
                    using (Icon ico = Icon.FromHandle(info.hIcon))
                    {
                        image = ico.ToBitmap();
                    }
                }
                catch { }
                DestroyIcon(info.hIcon);
            }

            if (image == null) image = SystemIcons.Application.ToBitmap();
            iconCache[it.Path] = image;
            return image;
        }

        private Image GetRecycleBinIcon()
        {
            if (iconCache.ContainsKey("recyclebin")) return iconCache["recyclebin"];

            Image image = null;
            IntPtr pidl = IntPtr.Zero;
            uint attrs = 0;
            try
            {
                int hr = SHParseDisplayName(RecycleBinClsid, IntPtr.Zero, out pidl, 0, out attrs);
                if (hr == 0 && pidl != IntPtr.Zero)
                {
                    SHFILEINFO info = new SHFILEINFO();
                    IntPtr res = SHGetFileInfo(pidl, 0, ref info, (uint)Marshal.SizeOf(info), SHGFI_ICON | SHGFI_LARGEICON | SHGFI_PIDL);
                    if (res != IntPtr.Zero && info.hIcon != IntPtr.Zero)
                    {
                        using (Icon ico = Icon.FromHandle(info.hIcon))
                        {
                            image = ico.ToBitmap();
                        }
                        DestroyIcon(info.hIcon);
                    }
                }
            }
            catch { }
            finally
            {
                if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
            }

            if (image == null) image = SystemIcons.Application.ToBitmap();
            iconCache["recyclebin"] = image;
            return image;
        }

        private void RenderGrid()
        {
            grid.SuspendLayout();
            grid.Controls.Clear();
            tiles.Clear();
            selectedTile = null;

            string query = search.Text.Trim().ToLowerInvariant();
            int shown = 0;

            foreach (Item it in items)
            {
                if (category != CAT_ALL && it.Category != CatIds[category]) continue;
                if (query.Length > 0 && it.Name.ToLowerInvariant().IndexOf(query) < 0) continue;

                Tile tile = new Tile(it, GetIcon(it));
                tile.Click += OnTileClick;
                tile.DoubleClick += OnTileDoubleClick;
                tile.MouseDown += OnTileMouseDown;
                tile.ContextMenuStrip = tileMenu;
                grid.Controls.Add(tile);
                tiles.Add(tile);
                shown++;
            }

            bool isEmpty = shown == 0;
            grid.Visible = !isEmpty;
            emptyLabel.Visible = isEmpty;
            emptyLabel.BringToFront();

            for (int i = 0; i < tabButtons.Count; i++)
            {
                Button b = tabButtons[i];
                if (i == category)
                {
                    b.BackColor = Color.FromArgb(106, 166, 255);
                    b.ForeColor = Color.White;
                }
                else
                {
                    b.BackColor = Color.FromArgb(36, 38, 46);
                    b.ForeColor = Color.FromArgb(200, 200, 210);
                }
            }

            countLabel.Text = "共 " + shown + " 项";
            grid.ResumeLayout();
        }

        private void OnTileClick(object sender, EventArgs e)
        {
            Tile tile = sender as Tile;
            if (tile == null) return;
            if (selectedTile != null) selectedTile.SetSelected(false);
            selectedTile = tile;
            selectedTile.SetSelected(true);
        }

        private void OnTileDoubleClick(object sender, EventArgs e)
        {
            Tile tile = sender as Tile;
            if (tile != null) OpenItem(tile.Data);
        }

        private void OnTileMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                Tile tile = sender as Tile;
                if (tile != null) menuItem = tile.Data;
            }
        }

        private void OpenItem(Item it)
        {
            if (it == null) return;
            try
            {
                if (it.IsSpecial)
                {
                    System.Diagnostics.Process.Start("explorer.exe", "shell:RecycleBinFolder");
                }
                else
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(it.Path) { UseShellExecute = true });
                }
                HidePanel();
            }
            catch (Exception ex)
            {
                MessageBox.Show("打不开：" + ex.Message, "桌面应用");
            }
        }

        private void EmptyRecycleBin()
        {
            DialogResult answer = MessageBox.Show("确定要清空回收站吗？", "清空回收站", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
            try
            {
                SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4);
            }
            catch { }
            HidePanel();
        }

        private void RevealItem(Item it)
        {
            if (it == null) return;
            if (it.IsSpecial)
            {
                OpenItem(it);
                return;
            }
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + it.Path + "\"");
            }
            catch { }
            HidePanel();
        }

        private void HideItem(Item it)
        {
            if (it == null) return;
            hidden.Add(it.Path);
            SaveHidden();
            LoadItems();
            RenderGrid();
        }

        private static bool ReadIconsHidden()
        {
            object value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideIcons", 0);
            if (value == null) return false;
            try { return Convert.ToInt32(value) == 1; }
            catch { return false; }
        }

        private void ToggleDesktopIcons()
        {
            bool target = !iconsHidden;
            try
            {
                Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideIcons", target ? 1 : 0, RegistryValueKind.DWord);
            }
            catch { }

            SetShellHideIcons(target);

            iconsHidden = target;
            if (desktopIconsItem != null)
            {
                desktopIconsItem.Text = iconsHidden ? "显示桌面图标" : "隐藏桌面图标";
            }
        }

        private static void SetShellHideIcons(bool hide)
        {
            IntPtr buffer = Marshal.AllocHGlobal(512);
            try
            {
                for (int i = 0; i < 512; i++) Marshal.WriteByte(buffer, i, 0);
                SHGetSetSettings(buffer, 0x4000, false);
                byte value = Marshal.ReadByte(buffer, 1);
                if (hide) value = (byte)(value | 0x08);
                else value = (byte)(value & 0xF7);
                Marshal.WriteByte(buffer, 1, value);
                SHGetSetSettings(buffer, 0x4000, true);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private void ShowPanel()
        {
            LoadItems();
            RenderGrid();

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Left = wa.Left + 12;
            Top = wa.Bottom - Height - 76;
            Show();
            Activate();
            search.Focus();
            search.SelectAll();
            ApplyRoundedRegion();
        }

        private void HidePanel()
        {
            Hide();
        }

        private void TogglePanel()
        {
            if (Visible) HidePanel();
            else ShowPanel();
        }

        private void ApplyRoundedRegion()
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                int r = 14;
                path.AddArc(0, 0, r * 2, r * 2, 180, 90);
                path.AddArc(Width - r * 2, 0, r * 2, r * 2, 270, 90);
                path.AddArc(Width - r * 2, Height - r * 2, r * 2, r * 2, 0, 90);
                path.AddArc(0, Height - r * 2, r * 2, r * 2, 90, 90);
                path.CloseAllFigures();
                Region = new Region(path);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Program.WM_TOGGLE)
            {
                TogglePanel();
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!menuOpen) HidePanel();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) HidePanel();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
            }
            base.OnFormClosing(e);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_LARGEICON = 0x000000000;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        private const uint SHGFI_PIDL = 0x000000008;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(IntPtr pidl, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("shell32.dll")]
        private static extern void SHGetSetSettings(IntPtr pss, uint mask, bool set);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
