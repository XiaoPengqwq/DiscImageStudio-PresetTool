using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Reflection;
using System.Threading;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("DiscImageStudio 预设分享工具")]
[assembly: AssemblyDescription("导出 / 导入 Disc Image Studio 的盘片预设（CD / DVD / BD）")]
[assembly: AssemblyProduct("DiscImageStudio Preset Tool")]
[assembly: AssemblyCompany("DeepSeek Harness")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]

public class PresetTool : Form
{
    static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiscImageStudio");
    static readonly string PresetFile = Path.Combine(DataDir, "disc-presets.json");

    ListBox lst;
    Label status;
    Button btnExport, btnImport, btnFolder, btnDelete;
    List<Dictionary<string, object>> presets = new List<Dictionary<string, object>>();
    List<string> kinds = new List<string>();
    static readonly string[] KindKeys = { "cdPresets", "dvdPresets", "bluRayPresets" };
    static readonly string[] KindNames = { "CD", "DVD", "BD" };

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern int PrivateExtractIcons(string lpszFile, int nIconIndex, int cxIcon, int cyIcon, IntPtr[] phicon, int[] piconid, int nIcons, int flags);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool FlashWindow(IntPtr hWnd, bool bInvert);

    const int SW_RESTORE = 9;

    static void FocusThisWindow()
    {
        try
        {
            IntPtr h = FindWindow(null, "DiscImageStudio 预设分享工具");
            if (h == IntPtr.Zero) return;
            if (IsIconic(h)) ShowWindow(h, SW_RESTORE);
            SetForegroundWindow(h);
            FlashWindow(h, true);
        }
        catch { }
    }

    static bool ActivateFolderWindow(string folder)
    {
        try
        {
            string target = new Uri(folder).AbsoluteUri.TrimEnd('/');
            Type t = Type.GetTypeFromProgID("Shell.Application");
            if (t == null) return false;
            dynamic shell = Activator.CreateInstance(t);
            dynamic wins = shell.Windows();
            int n = wins.Count;
            for (int i = 0; i < n; i++)
            {
                dynamic w = wins.Item(i);
                string url = null;
                try { url = (string)w.LocationURL; } catch { }
                if (string.IsNullOrEmpty(url)) continue;
                if (!string.Equals(url.TrimEnd('/'), target, StringComparison.OrdinalIgnoreCase)) continue;
                try { w.Visible = true; } catch { }
                long lh = 0;
                try { lh = (long)w.HWND; } catch { }
                if (lh != 0)
                {
                    IntPtr hwnd = new IntPtr(lh);
                    if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
                    SetForegroundWindow(hwnd);
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    static Icon LoadAppIcon()
    {
        try
        {
            var ph = new IntPtr[1];
            var pi = new int[1];
            int n = PrivateExtractIcons(Application.ExecutablePath, 0, 256, 256, ph, pi, 1, 0);
            if (n > 0 && ph[0] != IntPtr.Zero)
            {
                using (var ic = Icon.FromHandle(ph[0]))
                {
                    var cloned = (Icon)ic.Clone();
                    DestroyIcon(ph[0]);
                    return cloned;
                }
            }
        }
        catch { }
        try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch { return null; }
    }

    public PresetTool()
    {
        Text = "DiscImageStudio 预设分享工具";
        ClientSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(24, 26, 32);
        ForeColor = Color.Gainsboro;
        Font = new Font("Microsoft YaHei UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        try { Icon = LoadAppIcon(); } catch { }

        var title = new Label();
        title.Text = "DiscImageStudio 预设分享";
        title.Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold);
        title.ForeColor = Color.White;
        title.AutoSize = true;
        title.Location = new Point(20, 16);
        Controls.Add(title);

        var sub = new Label();
        sub.Text = "本机预设列表 —— 选一条导出成 .discpreset 发给对方；对方用同一个工具点「导入预设」即可。";
        sub.ForeColor = Color.FromArgb(150, 156, 168);
        sub.AutoSize = true;
        sub.Location = new Point(22, 50);
        Controls.Add(sub);

        lst = new ListBox();
        lst.Location = new Point(20, 84);
        lst.Size = new Size(500, 350);
        lst.BackColor = Color.FromArgb(34, 37, 45);
        lst.ForeColor = Color.Gainsboro;
        lst.BorderStyle = BorderStyle.FixedSingle;
        lst.Font = new Font("Microsoft YaHei UI", 10f);
        lst.IntegralHeight = false;
        lst.DoubleClick += delegate { ExportSelected(); };
        Controls.Add(lst);

        btnExport = MakeButton("导出选中 →", 540, 84, 196, 40, Color.FromArgb(58, 120, 220));
        btnExport.Click += delegate { ExportSelected(); };

        btnImport = MakeButton("导入预设…", 540, 132, 196, 40, Color.FromArgb(56, 150, 100));
        btnImport.Click += delegate { ImportPreset(); };

        btnFolder = MakeButton("打开预设文件夹", 540, 180, 196, 36, Color.FromArgb(70, 74, 86));
        btnFolder.Click += delegate { OpenFolder(); };

        btnDelete = MakeButton("删除选中", 540, 226, 196, 36, Color.FromArgb(178, 72, 72));
        btnDelete.Click += delegate { DeleteSelected(); };

        var hint = new Label();
        hint.Text = "对方收到 .discpreset 后：\r\n1. 关掉 DiscImageStudio\r\n2. 打开本工具，点「导入预设…」\r\n3. 选中文件，完成\r\n\r\n导入会自动备份原文件，\r\nid 撞了会自动改名。";
        hint.ForeColor = Color.FromArgb(130, 136, 148);
        hint.AutoSize = true;
        hint.Location = new Point(542, 276);
        Controls.Add(hint);

        status = new Label();
        status.Text = "";
        status.ForeColor = Color.FromArgb(150, 190, 150);
        status.AutoSize = false;
        status.Size = new Size(720, 40);
        status.Location = new Point(20, 452);
        Controls.Add(status);

        LoadPresets();
    }

    Button MakeButton(string text, int x, int y, int w, int h, Color back)
    {
        var b = new Button();
        b.Text = text;
        b.Location = new Point(x, y);
        b.Size = new Size(w, h);
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.BackColor = back;
        b.ForeColor = Color.White;
        b.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
        b.Cursor = Cursors.Hand;
        Controls.Add(b);
        return b;
    }

    static JavaScriptSerializer NewSerializer()
    {
        var s = new JavaScriptSerializer();
        s.MaxJsonLength = int.MaxValue;
        return s;
    }

    void LoadPresets()
    {
        presets.Clear();
        kinds.Clear();
        lst.Items.Clear();
        if (!File.Exists(PresetFile))
        {
            status.ForeColor = Color.FromArgb(230, 170, 90);
            status.Text = "没有找到 " + PresetFile;
            return;
        }
        try
        {
            var obj = NewSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(PresetFile, Encoding.UTF8));
            if (obj != null)
            {
                for (int k = 0; k < KindKeys.Length; k++)
                {
                    object arr;
                    if (!obj.TryGetValue(KindKeys[k], out arr) || !(arr is IEnumerable)) continue;
                    foreach (var it in (IEnumerable)arr)
                    {
                        var d = it as Dictionary<string, object>;
                        if (d != null) { presets.Add(d); kinds.Add(KindNames[k]); }
                    }
                }
            }
            for (int i = 0; i < presets.Count; i++)
            {
                var d = presets[i];
                string size = GetStr(d, "totalSectors");
                if (size.Length == 0) size = GetStr(d, "sectors");
                lst.Items.Add(string.Format("[{0}] {1}    {2} ~ {3} mm    {4} 扇区",
                    kinds[i], GetStr(d, "displayName"), GetNum(d, "innerRadiusMm"), GetNum(d, "outerRadiusMm"), size));
            }
            int nc = 0, nd = 0, nb = 0;
            foreach (var kk in kinds)
            {
                if (kk == "CD") nc++;
                else if (kk == "DVD") nd++;
                else nb++;
            }
            status.ForeColor = Color.FromArgb(150, 190, 150);
            status.Text = string.Format("已载入 {0} 条预设（CD {1} · DVD {2} · BD {3}）", presets.Count, nc, nd, nb);
        }
        catch (Exception ex)
        {
            status.ForeColor = Color.FromArgb(220, 110, 110);
            status.Text = "读取失败：" + ex.Message;
        }
    }

    static string GetStr(Dictionary<string, object> d, string k)
    {
        object v;
        return d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : "";
    }

    static string GetNum(Dictionary<string, object> d, string k)
    {
        object v;
        if (d.TryGetValue(k, out v) && v != null)
        {
            double n;
            if (double.TryParse(Convert.ToString(v), out n)) return n.ToString("0.######");
        }
        return "?";
    }

    void ExportSelected()
    {
        if (lst.SelectedIndex < 0)
        {
            status.ForeColor = Color.FromArgb(230, 170, 90);
            status.Text = "先在上面选一条预设";
            return;
        }
        var d = presets[lst.SelectedIndex];
        string discType = kinds[lst.SelectedIndex];
        string name = GetStr(d, "displayName");
        if (name.Length == 0) name = "preset";

        var sfd = new SaveFileDialog();
        sfd.Title = "导出预设";
        sfd.Filter = "DiscImageStudio 预设 (*.discpreset;*.dvdpreset)|*.discpreset;*.dvdpreset|JSON (*.json)|*.json";
        sfd.FileName = SafeFileName(name) + ".discpreset";
        if (sfd.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var sb = new StringBuilder();
            sb.Append("{\r\n");
            sb.Append("  \"kind\": \"discimagestudio-preset\",\r\n");
            sb.Append("  \"formatVersion\": 2,\r\n");
            sb.Append("  \"exportedFrom\": \"").Append(Esc(Environment.MachineName)).Append("\",\r\n");
            sb.Append("  \"exportedAt\": \"").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("\",\r\n");
            sb.Append("  \"discType\": \"").Append(discType).Append("\",\r\n");
            sb.Append("  \"preset\": ");
            sb.Append(NewSerializer().Serialize(d));
            sb.Append("\r\n}\r\n");
            File.WriteAllText(sfd.FileName, sb.ToString(), new UTF8Encoding(false));
            status.ForeColor = Color.FromArgb(150, 190, 150);
            status.Text = "已导出：" + Path.GetFileName(sfd.FileName);
        }
        catch (Exception ex)
        {
            status.ForeColor = Color.FromArgb(220, 110, 110);
            status.Text = "导出失败：" + ex.Message;
        }
    }

    static string Esc(string s)
    {
        return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    static string SafeFileName(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        s = s.Trim();
        if (s.Length > 60) s = s.Substring(0, 60);
        return s.Length == 0 ? "preset" : s;
    }

    void ImportPreset()
    {
        var ofd = new OpenFileDialog();
        ofd.Title = "选择预设文件";
        ofd.Filter = "DiscImageStudio 预设 (*.dvdpreset)|*.dvdpreset|JSON (*.json)|*.json|所有文件 (*.*)|*.*";
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var json = NewSerializer();
            var root = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(ofd.FileName, Encoding.UTF8));
            Dictionary<string, object> incoming = null;

            object p;
            if (root != null && root.TryGetValue("preset", out p)) incoming = p as Dictionary<string, object>;
            if (incoming == null && root != null && root.ContainsKey("id")) incoming = root;
            if (incoming == null)
            {
                status.ForeColor = Color.FromArgb(220, 110, 110);
                status.Text = "这个文件里没有找到预设数据";
                return;
            }

            if (!File.Exists(PresetFile))
            {
                Directory.CreateDirectory(DataDir);
                File.WriteAllText(PresetFile,
                    "{\"schemaVersion\":4,\"cdPresets\":[],\"dvdPresets\":[],\"disabledCdPresetIds\":[],\"disabledDvdPresetIds\":[],\"bluRayPresets\":[],\"disabledBluRayPresetIds\":[]}",
                    new UTF8Encoding(false));
            }
            else
            {
                File.Copy(PresetFile, PresetFile + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true);
            }

            string discType = "DVD";
            object dt;
            if (root != null && root.TryGetValue("discType", out dt) && dt != null)
            {
                discType = Convert.ToString(dt).Trim().ToUpperInvariant();
            }
            if (discType != "CD" && discType != "DVD" && discType != "BD")
            {
                if (incoming.ContainsKey("linearVelocityMmPerSecond")) discType = "CD";
                else if (incoming.ContainsKey("invertMapping")) discType = "BD";
                else discType = "DVD";
            }
            string arrayKey = discType == "CD" ? "cdPresets" : (discType == "BD" ? "bluRayPresets" : "dvdPresets");

            var doc = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(PresetFile, Encoding.UTF8));
            object arrObj;
            if (!doc.TryGetValue(arrayKey, out arrObj) || !(arrObj is IList))
            {
                doc[arrayKey] = new List<object>();
                arrObj = doc[arrayKey];
            }
            var list = (IList)arrObj;

            string wantId = GetStr(incoming, "id");
            bool dup = false;
            foreach (var it in list)
            {
                var d = it as Dictionary<string, object>;
                if (d != null && GetStr(d, "id") == wantId) { dup = true; break; }
            }
            if (dup)
            {
                var rnd = new Random();
                incoming["id"] = wantId + "-dup" + rnd.Next(100, 999);
            }

            list.Add(incoming);
            File.WriteAllText(PresetFile, json.Serialize(doc), new UTF8Encoding(false));

            status.ForeColor = Color.FromArgb(150, 190, 150);
            status.Text = "导入成功 [" + discType + "]：" + GetStr(incoming, "displayName") + (dup ? "（id 重名已自动改名）" : "");
            LoadPresets();
        }
        catch (Exception ex)
        {
            status.ForeColor = Color.FromArgb(220, 110, 110);
            status.Text = "导入失败：" + ex.Message;
        }
    }

    void DeleteSelected()
    {
        if (lst.SelectedIndex < 0)
        {
            status.ForeColor = Color.FromArgb(230, 170, 90);
            status.Text = "先在上面选一条预设";
            return;
        }
        var d = presets[lst.SelectedIndex];
        string kind = kinds[lst.SelectedIndex];
        string name = GetStr(d, "displayName");
        string id = GetStr(d, "id");

        var r = MessageBox.Show(
            string.Format("确定要删除这条预设吗？\n\n[{0}] {1}\nid: {2}\n\n删除前会自动备份一份 disc-presets.json，但列表里这条会立刻消失，无法通过本工具撤销。", kind, name, id),
            "删除预设",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (r != DialogResult.Yes) return;

        try
        {
            var json = NewSerializer();
            File.Copy(PresetFile, PresetFile + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true);
            var doc = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(PresetFile, Encoding.UTF8));
            string arrayKey = kind == "CD" ? "cdPresets" : (kind == "BD" ? "bluRayPresets" : "dvdPresets");
            object arrObj;
            bool removed = false;
            if (doc.TryGetValue(arrayKey, out arrObj) && arrObj is IList)
            {
                var list = (IList)arrObj;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var item = list[i] as Dictionary<string, object>;
                    if (item != null && GetStr(item, "id") == id) { list.RemoveAt(i); removed = true; }
                }
            }
            if (!removed)
            {
                status.ForeColor = Color.FromArgb(220, 110, 110);
                status.Text = "没在文件里找到这条预设，可能已经被别处改过";
                return;
            }
            File.WriteAllText(PresetFile, json.Serialize(doc), new UTF8Encoding(false));
            LoadPresets();
            status.ForeColor = Color.FromArgb(150, 190, 150);
            status.Text = "已删除 [" + kind + "]：" + name;
        }
        catch (Exception ex)
        {
            status.ForeColor = Color.FromArgb(220, 110, 110);
            status.Text = "删除失败：" + ex.Message;
        }
    }

    void OpenFolder()
    {
        try
        {
            if (!Directory.Exists(DataDir)) Directory.CreateDirectory(DataDir);
            if (ActivateFolderWindow(DataDir)) return;
            System.Diagnostics.Process.Start("explorer.exe", DataDir);
        }
        catch { }
    }

    [STAThread]
    public static void Main()
    {
        bool isNew = false;
        using (var mutex = new Mutex(true, "Global\\DiscImageStudioPresetTool_3E7A", out isNew))
        {
            if (!isNew)
            {
                FocusThisWindow();
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new PresetTool());
            GC.KeepAlive(mutex);
        }
    }
}
