using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace CurrencyTrackerWinForms
{
    public class GroupInfo
    {
        public string id { get; set; }
        public string name { get; set; }
        public int sortOrder { get; set; }
    }

    public class AccountInfo
    {
        public string id { get; set; }
        public string groupId { get; set; }
        public string name { get; set; }
        public int sortOrder { get; set; }
    }

    public class AccountSnapshot
    {
        public string accountId { get; set; }
        public string accountName { get; set; }
        public string groupId { get; set; }
        public string groupName { get; set; }
        public int balance { get; set; }
        public int delta { get; set; }
    }

    public class GroupSnapshot
    {
        public string groupId { get; set; }
        public string groupName { get; set; }
        public int balance { get; set; }
        public int delta { get; set; }
    }

    public class UpdateRecord
    {
        public string id { get; set; }
        public string at { get; set; }
        public string note { get; set; }
        public List<AccountSnapshot> accountSnapshots { get; set; }
        public List<GroupSnapshot> groupSnapshots { get; set; }
        public int totalBalance { get; set; }
        public int totalDelta { get; set; }
    }

    public class AppState
    {
        public int schemaVersion { get; set; }
        public List<GroupInfo> groups { get; set; }
        public List<AccountInfo> accounts { get; set; }
        public List<UpdateRecord> updates { get; set; }
    }

    public class FilterItem
    {
        public string Id { get; private set; }
        public string Label { get; private set; }

        public FilterItem(string id, string label)
        {
            Id = id;
            Label = label;
        }

        public override string ToString()
        {
            return Label;
        }
    }

    public class TrackerForm : Form
    {
        private const int CurrentSchemaVersion = 2;
        private static readonly Color PageBg = Color.FromArgb(245, 247, 244);
        private static readonly Color CardBg = Color.White;
        private static readonly Color SoftGreen = Color.FromArgb(238, 243, 234);
        private static readonly Color BorderGreen = Color.FromArgb(216, 223, 213);
        private static readonly Color TextDark = Color.FromArgb(29, 37, 34);
        private static readonly Color TextMuted = Color.FromArgb(99, 112, 105);
        private static readonly Color PrimaryGreen = Color.FromArgb(39, 103, 73);
        private static readonly Color WarmBrown = Color.FromArgb(124, 45, 18);

        private readonly string configFile;
        private string dataFile;
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private AppState state;
        private bool windowBoundsInitialized;

        private TableLayoutPanel root;
        private SplitContainer mainSplit;
        private SplitContainer lowerSplit;
        private readonly Dictionary<string, TextBox> accountInputs = new Dictionary<string, TextBox>();
        private readonly Label totalLabel = new Label();
        private readonly Label draftGainLabel = new Label();
        private readonly Label countLabel = new Label();
        private readonly Label accountCountLabel = new Label();
        private readonly DateTimePicker updateTime = new DateTimePicker();
        private readonly TextBox noteInput = new TextBox();
        private readonly DateTimePicker startDate = new DateTimePicker();
        private readonly DateTimePicker endDate = new DateTimePicker();
        private readonly ComboBox groupFilter = new ComboBox();
        private readonly ComboBox accountFilter = new ComboBox();
        private readonly DataGridView summaryGrid = new DataGridView();
        private readonly DataGridView accountGrid = new DataGridView();
        private readonly Chart weeklyChart = new Chart();
        private readonly Label statusLabel = new Label();

        public TrackerForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            configFile = Path.Combine(DefaultDataDirectory(), "settings.json");
            dataFile = LoadDataFilePath();
            state = LoadState();
            BuildUi();
            ApplySavedWindowBounds();
            windowBoundsInitialized = true;
            SetDefaultHistoryDateRange();
            RefreshEverything();
            ResizeEnd += (sender, args) => SaveWindowBounds();
            FormClosing += (sender, args) => SaveWindowBounds();
            Shown += delegate
            {
                ApplyMainSplitterDistance();
                ApplyLowerSplitterDistance();
            };
        }

        private static AppState EmptyState()
        {
            return new AppState
            {
                schemaVersion = CurrentSchemaVersion,
                groups = new List<GroupInfo>(),
                accounts = new List<AccountInfo>(),
                updates = new List<UpdateRecord>(),
            };
        }

        private AppState LoadState()
        {
            try
            {
                if (File.Exists(dataFile))
                {
                    string json = File.ReadAllText(dataFile, Encoding.UTF8);
                    AppState loaded = LoadOrMigrateState(json);
                    if (loaded != null)
                    {
                        if (loaded.schemaVersion == CurrentSchemaVersion)
                        {
                            WriteState(loaded);
                        }
                        return NormalizeState(loaded);
                    }

                    string backup = dataFile + ".legacy-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                    File.Copy(dataFile, backup, true);
                    MessageBox.Show("检测到旧版数据格式，已另存为：\n\n" + backup + "\n\n新版将从空数据开始。", "数据格式已更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("本地记录读取失败，已使用空数据启动。\n\n" + ex.Message, "读取失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return EmptyState();
        }

        private AppState LoadOrMigrateState(string json)
        {
            AppState loaded = serializer.Deserialize<AppState>(json);
            if (loaded != null && loaded.schemaVersion == CurrentSchemaVersion && loaded.groups != null && loaded.accounts != null && loaded.updates != null)
            {
                return loaded;
            }

            AppState migrated;
            if (TryMigrateLegacyState(json, out migrated))
            {
                return migrated;
            }

            return null;
        }

        private bool TryMigrateLegacyState(string json, out AppState migrated)
        {
            migrated = null;
            Dictionary<string, object> root = serializer.Deserialize<Dictionary<string, object>>(json);
            if (root == null || !root.ContainsKey("accounts") || !root.ContainsKey("updates"))
            {
                return false;
            }

            List<object> legacyAccounts = AsList(root["accounts"]);
            if (legacyAccounts.Count == 0)
            {
                migrated = EmptyState();
                return true;
            }

            List<string> groupNames = new List<string>();
            foreach (object item in legacyAccounts)
            {
                Dictionary<string, object> account = AsDict(item);
                string groupName = ToText(ValueOrNull(account, "group"));
                if (string.IsNullOrWhiteSpace(groupName)) groupName = "未分组";
                if (!groupNames.Contains(groupName)) groupNames.Add(groupName);
            }

            Dictionary<string, string> groupIdsByName = new Dictionary<string, string>();
            List<GroupInfo> groups = new List<GroupInfo>();
            for (int i = 0; i < groupNames.Count; i++)
            {
                string id = "g" + (i + 1).ToString("00");
                groupIdsByName[groupNames[i]] = id;
                groups.Add(new GroupInfo { id = id, name = groupNames[i], sortOrder = i });
            }

            List<AccountInfo> accounts = new List<AccountInfo>();
            Dictionary<string, Dictionary<string, object>> legacyAccountById = new Dictionary<string, Dictionary<string, object>>();
            for (int i = 0; i < legacyAccounts.Count; i++)
            {
                Dictionary<string, object> account = AsDict(legacyAccounts[i]);
                string id = ToText(ValueOrNull(account, "id"));
                if (string.IsNullOrWhiteSpace(id)) id = "a" + (i + 1).ToString("00");
                string groupName = ToText(ValueOrNull(account, "group"));
                if (string.IsNullOrWhiteSpace(groupName)) groupName = "未分组";
                string name = ToText(ValueOrNull(account, "name"));
                if (string.IsNullOrWhiteSpace(name)) name = "账号" + (i + 1).ToString();
                legacyAccountById[id] = account;
                accounts.Add(new AccountInfo { id = id, groupId = groupIdsByName[groupName], name = name, sortOrder = i });
            }

            List<UpdateRecord> updates = new List<UpdateRecord>();
            foreach (object item in AsList(root["updates"]))
            {
                Dictionary<string, object> record = AsDict(item);
                Dictionary<string, object> balances = AsDict(ValueOrNull(record, "balances"));
                Dictionary<string, object> deltas = AsDict(ValueOrNull(record, "deltas"));
                Dictionary<string, object> groupTotals = AsDict(ValueOrNull(record, "groupTotals"));
                Dictionary<string, object> groupDeltas = AsDict(ValueOrNull(record, "groupDeltas"));

                List<AccountSnapshot> accountSnapshots = new List<AccountSnapshot>();
                foreach (AccountInfo account in accounts.OrderBy(a => a.sortOrder))
                {
                    Dictionary<string, object> legacyAccount = legacyAccountById[account.id];
                    string groupName = ToText(ValueOrNull(legacyAccount, "group"));
                    if (string.IsNullOrWhiteSpace(groupName)) groupName = "未分组";
                    int balance = balances.ContainsKey(account.id) ? ToInt(balances[account.id]) : ToInt(ValueOrNull(legacyAccount, "balance"));
                    int delta = deltas.ContainsKey(account.id) ? ToInt(deltas[account.id]) : 0;
                    accountSnapshots.Add(new AccountSnapshot
                    {
                        accountId = account.id,
                        accountName = account.name,
                        groupId = account.groupId,
                        groupName = groupName,
                        balance = balance,
                        delta = delta,
                    });
                }

                List<GroupSnapshot> groupSnapshots = new List<GroupSnapshot>();
                foreach (GroupInfo group in groups.OrderBy(g => g.sortOrder))
                {
                    List<AccountSnapshot> inGroup = accountSnapshots.Where(a => a.groupId == group.id).ToList();
                    int balance = groupTotals.ContainsKey(group.name) ? ToInt(groupTotals[group.name]) : inGroup.Sum(a => a.balance);
                    int delta = groupDeltas.ContainsKey(group.name) ? ToInt(groupDeltas[group.name]) : inGroup.Sum(a => a.delta);
                    groupSnapshots.Add(new GroupSnapshot
                    {
                        groupId = group.id,
                        groupName = group.name,
                        balance = balance,
                        delta = delta,
                    });
                }

                updates.Add(new UpdateRecord
                {
                    id = ToText(ValueOrNull(record, "id")),
                    at = ToText(ValueOrNull(record, "at")),
                    note = ToText(ValueOrNull(record, "note")),
                    accountSnapshots = accountSnapshots,
                    groupSnapshots = groupSnapshots,
                    totalBalance = record.ContainsKey("totalBalance") ? ToInt(record["totalBalance"]) : accountSnapshots.Sum(a => a.balance),
                    totalDelta = record.ContainsKey("totalDelta") ? ToInt(record["totalDelta"]) : accountSnapshots.Sum(a => a.delta),
                });
            }

            migrated = new AppState
            {
                schemaVersion = CurrentSchemaVersion,
                groups = groups,
                accounts = accounts,
                updates = updates,
            };
            return true;
        }

        private static List<object> AsList(object value)
        {
            if (value == null) return new List<object>();
            ArrayList arrayList = value as ArrayList;
            if (arrayList != null) return arrayList.Cast<object>().ToList();
            object[] array = value as object[];
            if (array != null) return array.ToList();
            return new List<object>();
        }

        private static Dictionary<string, object> AsDict(object value)
        {
            Dictionary<string, object> dict = value as Dictionary<string, object>;
            return dict ?? new Dictionary<string, object>();
        }

        private static object ValueOrNull(Dictionary<string, object> dict, string key)
        {
            return dict != null && dict.ContainsKey(key) ? dict[key] : null;
        }

        private static string ToText(object value)
        {
            return value == null ? "" : Convert.ToString(value);
        }

        private static int ToInt(object value)
        {
            if (value == null) return 0;
            int number;
            return int.TryParse(Convert.ToString(value), out number) ? number : 0;
        }

        private AppState NormalizeState(AppState loaded)
        {
            loaded.groups = loaded.groups.OrderBy(g => g.sortOrder).ThenBy(g => g.name).ToList();
            loaded.accounts = loaded.accounts.OrderBy(a => a.sortOrder).ThenBy(a => a.name).ToList();
            return loaded;
        }

        private void SaveState()
        {
            WriteState(state);
        }

        private void WriteState(AppState target)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dataFile));
            target.schemaVersion = CurrentSchemaVersion;
            string json = serializer.Serialize(target);
            File.WriteAllText(dataFile, PrettyJson(json), Encoding.UTF8);
        }

        private static string DefaultDataDirectory()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameCurrencyTracker");
        }

        private string LoadDataFilePath()
        {
            Directory.CreateDirectory(DefaultDataDirectory());
            string defaultPath = Path.Combine(DefaultDataDirectory(), "currency_records.json");
            try
            {
                if (File.Exists(configFile))
                {
                    Dictionary<string, string> config = serializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(configFile, Encoding.UTF8));
                    if (config != null && config.ContainsKey("dataFile") && !string.IsNullOrWhiteSpace(config["dataFile"]))
                    {
                        return config["dataFile"];
                    }
                }
            }
            catch
            {
                return defaultPath;
            }
            return defaultPath;
        }

        private void SaveDataFilePath()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configFile));
            Dictionary<string, string> config = ReadConfig();
            config["dataFile"] = dataFile;
            File.WriteAllText(configFile, PrettyJson(serializer.Serialize(config)), Encoding.UTF8);
        }

        private Dictionary<string, string> ReadConfig()
        {
            try
            {
                if (File.Exists(configFile))
                {
                    Dictionary<string, string> config = serializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(configFile, Encoding.UTF8));
                    if (config != null) return config;
                }
            }
            catch
            {
            }
            return new Dictionary<string, string>();
        }

        private int LoadConfigInt(string key, int fallback)
        {
            Dictionary<string, string> config = ReadConfig();
            int value;
            if (config.ContainsKey(key) && int.TryParse(config[key], out value) && value > 0)
            {
                return value;
            }
            return fallback;
        }

        private void SaveConfigInt(string key, int value)
        {
            if (value <= 0) return;
            Dictionary<string, string> config = ReadConfig();
            config[key] = value.ToString();
            Directory.CreateDirectory(Path.GetDirectoryName(configFile));
            File.WriteAllText(configFile, PrettyJson(serializer.Serialize(config)), Encoding.UTF8);
        }

        private bool LoadConfigBool(string key, bool fallback)
        {
            Dictionary<string, string> config = ReadConfig();
            bool value;
            if (config.ContainsKey(key) && bool.TryParse(config[key], out value))
            {
                return value;
            }
            return fallback;
        }

        private void SaveWindowBounds()
        {
            if (WindowState == FormWindowState.Minimized) return;
            Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            if (bounds.Width < MinimumSize.Width || bounds.Height < MinimumSize.Height) return;

            Dictionary<string, string> config = ReadConfig();
            config["windowX"] = bounds.X.ToString();
            config["windowY"] = bounds.Y.ToString();
            config["windowWidth"] = bounds.Width.ToString();
            config["windowHeight"] = bounds.Height.ToString();
            config["windowMaximized"] = (WindowState == FormWindowState.Maximized).ToString();
            Directory.CreateDirectory(Path.GetDirectoryName(configFile));
            File.WriteAllText(configFile, PrettyJson(serializer.Serialize(config)), Encoding.UTF8);
        }

        private void ApplySavedWindowBounds()
        {
            int width = Math.Max(MinimumSize.Width, LoadConfigInt("windowWidth", Width));
            int height = Math.Max(MinimumSize.Height, LoadConfigInt("windowHeight", Height));
            int x = LoadConfigInt("windowX", int.MinValue);
            int y = LoadConfigInt("windowY", int.MinValue);

            Rectangle bounds;
            if (x == int.MinValue || y == int.MinValue)
            {
                bounds = CenteredBounds(width, height);
            }
            else
            {
                bounds = new Rectangle(x, y, width, height);
                if (!IsVisibleOnAnyScreen(bounds)) bounds = CenteredBounds(width, height);
            }

            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            if (LoadConfigBool("windowMaximized", false))
            {
                WindowState = FormWindowState.Maximized;
            }
        }

        private static Rectangle CenteredBounds(int width, int height)
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            width = Math.Min(width, area.Width);
            height = Math.Min(height, area.Height);
            return new Rectangle(area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2, width, height);
        }

        private static bool IsVisibleOnAnyScreen(Rectangle bounds)
        {
            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle visible = Rectangle.Intersect(screen.WorkingArea, bounds);
                if (visible.Width >= 160 && visible.Height >= 120)
                {
                    return true;
                }
            }
            return false;
        }

        private static string PrettyJson(string json)
        {
            int indent = 0;
            bool quoted = false;
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < json.Length; i++)
            {
                char ch = json[i];
                if (ch == '"' && (i == 0 || json[i - 1] != '\\')) quoted = !quoted;
                if (!quoted && (ch == '{' || ch == '['))
                {
                    builder.Append(ch).AppendLine();
                    builder.Append(new string(' ', ++indent * 2));
                }
                else if (!quoted && (ch == '}' || ch == ']'))
                {
                    builder.AppendLine();
                    builder.Append(new string(' ', --indent * 2)).Append(ch);
                }
                else if (!quoted && ch == ',')
                {
                    builder.Append(ch).AppendLine();
                    builder.Append(new string(' ', indent * 2));
                }
                else if (!quoted && ch == ':')
                {
                    builder.Append(": ");
                }
                else
                {
                    builder.Append(ch);
                }
            }
            return builder.ToString();
        }

        private void BuildUi()
        {
            Controls.Clear();
            accountInputs.Clear();

            Text = "金条更新记录";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = PageBg;
            ForeColor = TextDark;
            MinimumSize = new Size(1120, 860);
            if (!windowBoundsInitialized)
            {
                Width = 1240;
                Height = 980;
            }

            root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 4, ColumnCount = 1, BackColor = PageBg };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            Controls.Add(root);

            Panel top = new Panel { Dock = DockStyle.Fill, BackColor = PageBg };
            Label title = new Label { Text = "金条更新记录", Font = new Font(Font.FontFamily, 21, FontStyle.Bold), AutoSize = true, Location = new Point(0, 2), ForeColor = TextDark };
            top.Controls.Add(title);
            AddTopButton(top, "恢复空数据", ResetData, 0, 104);
            AddTopButton(top, "数据位置", ChangeDataLocation, 112, 96);
            AddTopButton(top, "账号管理", OpenAccountManager, 216, 96);
            AddTopButton(top, "导入备份", ImportBackup, 320, 96);
            AddTopButton(top, "导出备份", ExportBackup, 424, 96);
            root.Controls.Add(top, 0, 0);

            TableLayoutPanel metrics = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, BackColor = PageBg };
            for (int i = 0; i < 4; i++) metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            root.Controls.Add(metrics, 0, 1);
            AddMetric(metrics, "当前总计", totalLabel, 0);
            AddMetric(metrics, "本次待保存收益", draftGainLabel, 1);
            AddMetric(metrics, "历史更新次数", countLabel, 2);
            AddMetric(metrics, "账号数量", accountCountLabel, 3);

            GroupBox editor = new GroupBox { Text = "手动更新", Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = CardBg, ForeColor = TextDark };
            mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 7,
                BackColor = BorderGreen,
            };
            mainSplit.SplitterMoved += (sender, args) => SaveConfigInt("mainSplitter", mainSplit.SplitterDistance);
            mainSplit.HandleCreated += (sender, args) => ApplyMainSplitterDistance();
            root.Controls.Add(mainSplit, 0, 2);
            mainSplit.Panel1.Controls.Add(editor);
            BuildEditor(editor);

            lowerSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 7,
                BackColor = BorderGreen,
            };
            lowerSplit.SplitterMoved += (sender, args) => SaveConfigInt("lowerSplitter", lowerSplit.SplitterDistance);
            lowerSplit.HandleCreated += (sender, args) => ApplyLowerSplitterDistance();
            mainSplit.Panel2.Controls.Add(lowerSplit);
            Panel summaryPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 8, 0), BackColor = PageBg };
            Panel historyPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 0, 0), BackColor = PageBg };
            lowerSplit.Panel1.Controls.Add(summaryPanel);
            lowerSplit.Panel2.Controls.Add(historyPanel);
            BuildCurrentSummary(summaryPanel);
            BuildHistory(historyPanel);

            statusLabel.Dock = DockStyle.Fill;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.ForeColor = TextMuted;
            statusLabel.BackColor = PageBg;
            root.Controls.Add(statusLabel, 0, 3);
        }

        private void ApplyMainSplitterDistance()
        {
            if (mainSplit == null || mainSplit.Height <= 0) return;
            int minTop = Math.Min(190, Math.Max(50, mainSplit.Height / 5));
            int minBottom = Math.Min(230, Math.Max(80, mainSplit.Height / 5));
            if (mainSplit.Height > minTop + minBottom + mainSplit.SplitterWidth)
            {
                mainSplit.Panel1MinSize = minTop;
                mainSplit.Panel2MinSize = minBottom;
            }
            int defaultDistance = Math.Min(350, Math.Max(minTop, mainSplit.Height / 3));
            int distance = LoadConfigInt("mainSplitter", defaultDistance);
            int max = mainSplit.Height - minBottom - mainSplit.SplitterWidth;
            distance = Math.Max(minTop, Math.Min(distance, max));
            if (distance > 0 && distance < mainSplit.Height)
            {
                mainSplit.SplitterDistance = distance;
            }
        }

        private void ApplyLowerSplitterDistance()
        {
            if (lowerSplit == null || lowerSplit.Width <= 0) return;
            int minLeft = Math.Min(220, Math.Max(80, lowerSplit.Width / 6));
            int minRight = Math.Min(360, Math.Max(120, lowerSplit.Width / 5));
            if (lowerSplit.Width > minLeft + minRight + lowerSplit.SplitterWidth)
            {
                lowerSplit.Panel1MinSize = minLeft;
                lowerSplit.Panel2MinSize = minRight;
            }
            int defaultDistance = Math.Min(320, Math.Max(minLeft, lowerSplit.Width / 4));
            int distance = LoadConfigInt("lowerSplitter", defaultDistance);
            int max = lowerSplit.Width - minRight - lowerSplit.SplitterWidth;
            distance = Math.Max(minLeft, Math.Min(distance, max));
            if (distance > 0 && distance < lowerSplit.Width)
            {
                lowerSplit.SplitterDistance = distance;
            }
        }

        private void AddTopButton(Control parent, string text, EventHandler handler, int rightOffset, int width)
        {
            Button button = new Button { Text = text, Width = width, Height = 32, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            StyleButton(button, text == "恢复空数据" ? Color.FromArgb(154, 52, 18) : TextDark, CardBg);
            button.Location = new Point(parent.Width - width - rightOffset, 5);
            button.Click += handler;
            parent.Controls.Add(button);
            parent.Resize += (sender, args) => button.Location = new Point(parent.Width - width - rightOffset, 5);
        }

        private void AddMetric(TableLayoutPanel parent, string title, Label valueLabel, int column)
        {
            Panel card = new Panel { Dock = DockStyle.Fill, BackColor = CardBg, Padding = new Padding(14), Margin = new Padding(5) };
            card.Paint += PaintCardBorder;
            Label label = new Label { Text = title, Dock = DockStyle.Top, Height = 24, ForeColor = TextMuted, BackColor = CardBg };
            valueLabel.Dock = DockStyle.Top;
            valueLabel.Height = 44;
            valueLabel.Font = new Font(Font.FontFamily, 18, FontStyle.Bold);
            valueLabel.ForeColor = title == "账号数量" ? WarmBrown : TextDark;
            valueLabel.BackColor = CardBg;
            card.Controls.Add(valueLabel);
            card.Controls.Add(label);
            parent.Controls.Add(card, column, 0);
        }

        private void PaintCardBorder(object sender, PaintEventArgs e)
        {
            Control control = (Control)sender;
            Rectangle rect = new Rectangle(0, 0, control.Width - 1, control.Height - 1);
            using (Pen pen = new Pen(BorderGreen)) e.Graphics.DrawRectangle(pen, rect);
        }

        private void StyleButton(Button button, Color foreColor, Color backColor)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = BorderGreen;
            button.FlatAppearance.BorderSize = 1;
            button.BackColor = backColor;
            button.ForeColor = foreColor;
            button.Cursor = Cursors.Hand;
        }

        private void BuildEditor(GroupBox editor)
        {
            FlowLayoutPanel meta = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, WrapContents = false, BackColor = CardBg };
            editor.Controls.Add(meta);
            meta.Controls.Add(new Label { Text = "更新时间", AutoSize = true, Padding = new Padding(0, 8, 0, 0), ForeColor = TextMuted, BackColor = CardBg });
            updateTime.Format = DateTimePickerFormat.Custom;
            updateTime.CustomFormat = "yyyy-MM-dd HH:mm";
            updateTime.Width = 190;
            meta.Controls.Add(updateTime);
            meta.Controls.Add(new Label { Text = "备注", AutoSize = true, Padding = new Padding(12, 8, 0, 0), ForeColor = TextMuted, BackColor = CardBg });
            noteInput.Width = 330;
            meta.Controls.Add(noteInput);
            Button save = new Button { Text = "保存本次更新", Width = 130, Height = 34 };
            StyleButton(save, Color.White, PrimaryGreen);
            save.Click += SaveUpdate;
            meta.Controls.Add(save);
            Button fill = new Button { Text = "填入当前记录", Width = 126, Height = 34 };
            StyleButton(fill, TextDark, CardBg);
            fill.Click += (sender, args) => FillCurrentValues();
            meta.Controls.Add(fill);

            Panel scrollHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = CardBg, Padding = new Padding(0, 54, 0, 0) };
            editor.Controls.Add(scrollHost);

            if (!state.accounts.Any())
            {
                Label empty = new Label
                {
                    Text = "还没有账号。请点击右上角“账号管理”添加分组和账号。",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = TextMuted,
                    BackColor = CardBg,
                };
                scrollHost.Controls.Add(empty);
                return;
            }

            FlowLayoutPanel groupsPanel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, BackColor = CardBg };
            scrollHost.Controls.Add(groupsPanel);

            foreach (GroupInfo group in OrderedGroups())
            {
                List<AccountInfo> accounts = OrderedAccounts().Where(a => a.groupId == group.id).ToList();
                if (!accounts.Any()) continue;
                GroupBox box = new GroupBox { Text = group.name, Width = 370, Height = Math.Max(104, 34 + accounts.Count * 38), Padding = new Padding(10), Margin = new Padding(7), BackColor = SoftGreen, ForeColor = TextDark };
                TableLayoutPanel grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = SoftGreen };
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
                box.Controls.Add(grid);
                for (int row = 0; row < accounts.Count; row++)
                {
                    AccountInfo account = accounts[row];
                    Label name = new Label { Text = account.name, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, BackColor = SoftGreen, ForeColor = TextDark };
                    TextBox input = new TextBox { Text = CurrentBalance(account.id).ToString(), Dock = DockStyle.Fill, TextAlign = HorizontalAlignment.Right, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(3, 2, 3, 2) };
                    input.TextChanged += (sender, args) => RefreshMetrics();
                    accountInputs[account.id] = input;
                    grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
                    grid.Controls.Add(name, 0, row);
                    grid.Controls.Add(input, 1, row);
                }
                groupsPanel.Controls.Add(box);
            }
        }

        private void BuildCurrentSummary(Control parent)
        {
            GroupBox box = new GroupBox { Text = "当前分组合计", Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = CardBg, ForeColor = TextDark };
            parent.Controls.Add(box);
            Panel scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = CardBg };
            box.Controls.Add(scroll);
            TableLayoutPanel table = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 0, AutoSize = true, BackColor = CardBg };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            scroll.Controls.Add(table);
            foreach (GroupInfo group in OrderedGroups())
            {
                AddSummaryRow(table, group.name, Fmt(CurrentGroupTotal(group.id)), false);
            }
            AddSummaryRow(table, "总计", Fmt(CurrentInputTotal()), true);
            AddSummaryRow(table, "本周总收益", FmtGain(CurrentWeekTotalDelta()), true);
        }

        private void AddSummaryRow(TableLayoutPanel table, string label, string valueText, bool total)
        {
            int row = table.RowCount;
            table.RowCount++;
            Label key = new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, BackColor = CardBg, ForeColor = TextMuted };
            Label value = new Label { Text = valueText, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Font = new Font(Font.FontFamily, 12, FontStyle.Bold), BackColor = CardBg, ForeColor = total ? WarmBrown : TextDark };
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            table.Controls.Add(key, 0, row);
            table.Controls.Add(value, 1, row);
        }

        private void BuildHistory(Control parent)
        {
            GroupBox box = new GroupBox { Text = "历史查询", Dock = DockStyle.Fill, Padding = new Padding(10), BackColor = CardBg, ForeColor = TextDark };
            parent.Controls.Add(box);
            FlowLayoutPanel filters = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, WrapContents = false, BackColor = CardBg };
            box.Controls.Add(filters);

            filters.Controls.Add(new Label { Text = "起始", AutoSize = true, Padding = new Padding(0, 8, 0, 0), ForeColor = TextMuted, BackColor = CardBg });
            startDate.Format = DateTimePickerFormat.Custom;
            startDate.CustomFormat = "yyyy-MM-dd";
            startDate.ShowCheckBox = true;
            startDate.Width = 166;
            filters.Controls.Add(startDate);
            filters.Controls.Add(new Label { Text = "结束", AutoSize = true, Padding = new Padding(14, 8, 0, 0), ForeColor = TextMuted, BackColor = CardBg });
            endDate.Format = DateTimePickerFormat.Custom;
            endDate.CustomFormat = "yyyy-MM-dd";
            endDate.ShowCheckBox = true;
            endDate.Width = 166;
            filters.Controls.Add(endDate);

            groupFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            groupFilter.Width = 118;
            groupFilter.FlatStyle = FlatStyle.Flat;
            filters.Controls.Add(groupFilter);

            accountFilter.DropDownStyle = ComboBoxStyle.DropDownList;
            accountFilter.Width = 156;
            accountFilter.FlatStyle = FlatStyle.Flat;
            filters.Controls.Add(accountFilter);

            Button query = new Button { Text = "查询", Width = 84, Height = 34 };
            StyleButton(query, Color.White, PrimaryGreen);
            query.Click += (sender, args) => RefreshHistory();
            filters.Controls.Add(query);

            TabControl tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 5) };
            box.Controls.Add(tabs);
            tabs.BringToFront();
            AddTab(tabs, "分组/总收益", summaryGrid, new[] { "时间", "备注", "总收益", "分组收益", "总计余额" });
            AddTab(tabs, "账号明细", accountGrid, new[] { "时间", "分组", "账号", "收益", "更新后", "备注" });
            AddChartTab(tabs);
        }

        private void AddChartTab(TabControl tabs)
        {
            TabPage page = new TabPage("收益图表");
            Panel panel = new Panel { Dock = DockStyle.Fill, BackColor = CardBg, Padding = new Padding(8) };
            FlowLayoutPanel tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = CardBg };
            Button openPie = new Button { Text = "打开饼图", Width = 104, Height = 32 };
            StyleButton(openPie, Color.White, WarmBrown);
            openPie.Click += (sender, args) => ShowWeeklyPieWindow();
            tools.Controls.Add(openPie);
            Button openLarge = new Button { Text = "打开折线大图", Width = 124, Height = 32 };
            StyleButton(openLarge, Color.White, PrimaryGreen);
            openLarge.Click += (sender, args) => ShowWeeklyChartWindow();
            tools.Controls.Add(openLarge);

            ConfigureWeeklyChart(weeklyChart);
            panel.Controls.Add(weeklyChart);
            panel.Controls.Add(tools);
            tools.BringToFront();
            page.Controls.Add(panel);
            tabs.TabPages.Add(page);
        }

        private void ConfigureWeeklyChart(Chart chart)
        {
            chart.Dock = DockStyle.Fill;
            chart.BackColor = CardBg;
            chart.BorderlineColor = BorderGreen;
            chart.ChartAreas.Clear();
            chart.Legends.Clear();
            chart.Series.Clear();
            chart.Titles.Clear();

            ChartArea area = new ChartArea("WeeklyIncome");
            area.BackColor = CardBg;
            area.AxisX.MajorGrid.LineColor = Color.FromArgb(230, 236, 228);
            area.AxisY.MajorGrid.LineColor = Color.FromArgb(230, 236, 228);
            area.AxisX.LabelStyle.Format = "MM-dd";
            area.AxisX.IntervalAutoMode = IntervalAutoMode.VariableCount;
            area.AxisX.Title = "周六结算日";
            area.AxisY.Title = "收益";
            area.AxisY.LabelStyle.Format = "N0";
            area.AxisX.LineColor = BorderGreen;
            area.AxisY.LineColor = BorderGreen;
            chart.ChartAreas.Add(area);

            Legend legend = new Legend("Legend");
            legend.Docking = Docking.Top;
            legend.Alignment = StringAlignment.Center;
            legend.BackColor = CardBg;
            chart.Legends.Add(legend);
        }

        private void ConfigurePieChart(Chart chart)
        {
            chart.Dock = DockStyle.Fill;
            chart.BackColor = CardBg;
            chart.BorderlineColor = BorderGreen;
            chart.ChartAreas.Clear();
            chart.Legends.Clear();
            chart.Series.Clear();
            chart.Titles.Clear();

            ChartArea area = new ChartArea("WeeklySource");
            area.BackColor = CardBg;
            chart.ChartAreas.Add(area);

            Legend legend = new Legend("Legend");
            legend.Docking = Docking.Right;
            legend.Alignment = StringAlignment.Center;
            legend.BackColor = CardBg;
            chart.Legends.Add(legend);
        }

        private void AddTab(TabControl tabs, string title, DataGridView grid, string[] columns)
        {
            TabPage page = new TabPage(title);
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.ReadOnly = true;
            grid.RowHeadersVisible = false;
            grid.ScrollBars = ScrollBars.Both;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.BackgroundColor = CardBg;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.GridColor = Color.FromArgb(237, 241, 234);
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = SoftGreen;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextDark;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
            grid.ColumnHeadersHeight = 40;
            grid.RowTemplate.Height = 34;
            grid.DefaultCellStyle.BackColor = CardBg;
            grid.DefaultCellStyle.ForeColor = TextDark;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 222);
            grid.DefaultCellStyle.SelectionForeColor = TextDark;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(251, 251, 247);
            foreach (string column in columns) grid.Columns.Add(column, column);
            page.Controls.Add(grid);
            tabs.TabPages.Add(page);
        }

        private IEnumerable<GroupInfo> OrderedGroups()
        {
            return state.groups.OrderBy(g => g.sortOrder).ThenBy(g => g.name);
        }

        private IEnumerable<AccountInfo> OrderedAccounts()
        {
            return state.accounts.OrderBy(a => a.sortOrder).ThenBy(a => a.name);
        }

        private int CurrentBalance(string accountId)
        {
            UpdateRecord latest = state.updates.LastOrDefault();
            AccountSnapshot snapshot = latest == null ? null : latest.accountSnapshots.FirstOrDefault(s => s.accountId == accountId);
            return snapshot == null ? 0 : snapshot.balance;
        }

        private int CurrentInputBalance(string accountId)
        {
            return accountInputs.ContainsKey(accountId) ? CleanNumber(accountInputs[accountId].Text) : CurrentBalance(accountId);
        }

        private int CurrentGroupTotal(string groupId)
        {
            return state.accounts.Where(a => a.groupId == groupId).Sum(a => CurrentInputBalance(a.id));
        }

        private int CurrentInputTotal()
        {
            return state.accounts.Sum(a => CurrentInputBalance(a.id));
        }

        private void RefreshEverything()
        {
            RefreshFilters();
            RefreshMetrics();
            RefreshHistory();
            SaveState();
            statusLabel.Text = "数据文件：" + dataFile;
        }

        private void SetDefaultHistoryDateRange()
        {
            List<DateTime> dates = state.updates
                .Select(record =>
                {
                    DateTime parsed;
                    return DateTime.TryParse(record.at, out parsed) ? parsed.Date : DateTime.Today;
                })
                .ToList();

            DateTime first = dates.Count == 0 ? DateTime.Today : dates.Min();
            DateTime last = dates.Count == 0 ? DateTime.Today : dates.Max();
            if (last < DateTime.Today) last = DateTime.Today;

            startDate.Value = first;
            startDate.Checked = true;
            endDate.Value = last;
            endDate.Checked = true;
        }

        private void FillCurrentValues()
        {
            foreach (AccountInfo account in state.accounts)
            {
                if (accountInputs.ContainsKey(account.id)) accountInputs[account.id].Text = CurrentBalance(account.id).ToString();
            }
            updateTime.Value = DateTime.Now;
            RefreshMetrics();
        }

        private void RefreshMetrics()
        {
            int total = CurrentInputTotal();
            int draftGain = state.accounts.Sum(a => CurrentInputBalance(a.id) - CurrentBalance(a.id));
            totalLabel.Text = Fmt(total);
            draftGainLabel.Text = FmtGain(draftGain);
            countLabel.Text = state.updates.Count.ToString();
            accountCountLabel.Text = state.accounts.Count.ToString();
        }

        private void SaveUpdate(object sender, EventArgs e)
        {
            if (!state.accounts.Any())
            {
                MessageBox.Show("请先通过账号管理添加账号。", "没有账号", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            UpdateRecord record = MakeRecord(noteInput.Text.Trim(), updateTime.Value);
            state.updates.Add(record);
            noteInput.Clear();
            SaveState();
            RebuildMainUi();
            statusLabel.Text = "已保存：" + DisplayTime(record.at) + "，本次总收益 " + FmtGain(record.totalDelta);
        }

        private UpdateRecord MakeRecord(string note, DateTime at)
        {
            List<AccountSnapshot> accountSnapshots = new List<AccountSnapshot>();
            foreach (AccountInfo account in OrderedAccounts())
            {
                GroupInfo group = state.groups.FirstOrDefault(g => g.id == account.groupId);
                int balance = CurrentInputBalance(account.id);
                int previous = CurrentBalance(account.id);
                accountSnapshots.Add(new AccountSnapshot
                {
                    accountId = account.id,
                    accountName = account.name,
                    groupId = account.groupId,
                    groupName = group == null ? "未分组" : group.name,
                    balance = balance,
                    delta = balance - previous,
                });
            }

            List<GroupSnapshot> groupSnapshots = accountSnapshots
                .GroupBy(s => new { s.groupId, s.groupName })
                .Select(g => new GroupSnapshot
                {
                    groupId = g.Key.groupId,
                    groupName = g.Key.groupName,
                    balance = g.Sum(s => s.balance),
                    delta = g.Sum(s => s.delta),
                })
                .OrderBy(g => g.groupName)
                .ToList();

            return new UpdateRecord
            {
                id = Guid.NewGuid().ToString(),
                at = at.ToString("yyyy-MM-dd HH:mm"),
                note = note,
                accountSnapshots = accountSnapshots,
                groupSnapshots = groupSnapshots,
                totalBalance = accountSnapshots.Sum(s => s.balance),
                totalDelta = accountSnapshots.Sum(s => s.delta),
            };
        }

        private void RefreshFilters()
        {
            FilterItem oldGroupItem = groupFilter.SelectedItem as FilterItem;
            FilterItem oldAccountItem = accountFilter.SelectedItem as FilterItem;
            string oldGroup = oldGroupItem == null ? "" : oldGroupItem.Id;
            string oldAccount = oldAccountItem == null ? "" : oldAccountItem.Id;

            groupFilter.Items.Clear();
            groupFilter.Items.Add(new FilterItem("", "全部分组"));
            foreach (FilterItem item in HistoricalGroupItems()) groupFilter.Items.Add(item);
            groupFilter.SelectedItem = groupFilter.Items.Cast<FilterItem>().FirstOrDefault(i => i.Id == oldGroup) ?? groupFilter.Items[0];

            accountFilter.Items.Clear();
            accountFilter.Items.Add(new FilterItem("", "全部账号"));
            foreach (FilterItem item in HistoricalAccountItems()) accountFilter.Items.Add(item);
            accountFilter.SelectedItem = accountFilter.Items.Cast<FilterItem>().FirstOrDefault(i => i.Id == oldAccount) ?? accountFilter.Items[0];
        }

        private IEnumerable<FilterItem> HistoricalGroupItems()
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            foreach (GroupInfo group in state.groups) map[group.id] = group.name;
            foreach (UpdateRecord record in state.updates)
            {
                foreach (GroupSnapshot group in record.groupSnapshots ?? new List<GroupSnapshot>())
                {
                    if (!map.ContainsKey(group.groupId)) map[group.groupId] = group.groupName;
                }
            }
            return map.OrderBy(kv => kv.Value).Select(kv => new FilterItem(kv.Key, kv.Value));
        }

        private IEnumerable<FilterItem> HistoricalAccountItems()
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            foreach (AccountInfo account in state.accounts)
            {
                GroupInfo group = state.groups.FirstOrDefault(g => g.id == account.groupId);
                map[account.id] = (group == null ? "未分组" : group.name) + " - " + account.name;
            }
            foreach (UpdateRecord record in state.updates)
            {
                foreach (AccountSnapshot account in record.accountSnapshots ?? new List<AccountSnapshot>())
                {
                    if (!map.ContainsKey(account.accountId)) map[account.accountId] = account.groupName + " - " + account.accountName;
                }
            }
            return map.OrderBy(kv => kv.Value).Select(kv => new FilterItem(kv.Key, kv.Value));
        }

        private List<UpdateRecord> FilteredRecords()
        {
            DateTime start = startDate.Checked ? startDate.Value.Date : DateTime.MinValue;
            DateTime end = endDate.Checked ? endDate.Value.Date.AddDays(1).AddTicks(-1) : DateTime.MaxValue;
            return state.updates
                .Where(r =>
                {
                    DateTime at;
                    if (!DateTime.TryParse(r.at, out at)) at = DateTime.Now;
                    return at >= start && at <= end;
                })
                .OrderByDescending(r => r.at)
                .ToList();
        }

        private int CurrentWeekTotalDelta()
        {
            DateTime currentWeek = WeekEndingSaturday(DateTime.Now);
            return state.updates
                .Where(record => IsRecordInWeek(record, currentWeek))
                .Sum(record => record.totalDelta);
        }

        private void RefreshHistory()
        {
            summaryGrid.Rows.Clear();
            accountGrid.Rows.Clear();

            FilterItem groupItem = groupFilter.SelectedItem as FilterItem;
            FilterItem accountItem = accountFilter.SelectedItem as FilterItem;
            string selectedGroupId = groupItem == null ? "" : groupItem.Id;
            string selectedAccountId = accountItem == null ? "" : accountItem.Id;
            List<UpdateRecord> records = FilteredRecords();

            foreach (UpdateRecord record in records)
            {
                List<GroupSnapshot> groups = (record.groupSnapshots ?? new List<GroupSnapshot>())
                    .Where(g => string.IsNullOrEmpty(selectedGroupId) || g.groupId == selectedGroupId)
                    .ToList();
                if (string.IsNullOrEmpty(selectedAccountId))
                {
                    summaryGrid.Rows.Add(
                        DisplayTime(record.at),
                        string.IsNullOrWhiteSpace(record.note) ? "无备注" : record.note,
                        FmtGain(groups.Sum(g => g.delta)),
                        string.Join(" / ", groups.Select(g => g.groupName + " " + FmtGain(g.delta)).ToArray()),
                        Fmt(groups.Sum(g => g.balance)));
                }

                foreach (AccountSnapshot account in record.accountSnapshots ?? new List<AccountSnapshot>())
                {
                    if (!string.IsNullOrEmpty(selectedGroupId) && account.groupId != selectedGroupId) continue;
                    if (!string.IsNullOrEmpty(selectedAccountId) && account.accountId != selectedAccountId) continue;
                    accountGrid.Rows.Add(
                        DisplayTime(record.at),
                        account.groupName,
                        account.accountName,
                        FmtGain(account.delta),
                        Fmt(account.balance),
                        string.IsNullOrWhiteSpace(record.note) ? "无备注" : record.note);
                }
            }

            RefreshWeeklyChart(weeklyChart, records, selectedGroupId, selectedAccountId);
        }

        private void ShowWeeklyChartWindow()
        {
            FilterItem groupItem = groupFilter.SelectedItem as FilterItem;
            FilterItem accountItem = accountFilter.SelectedItem as FilterItem;
            string selectedGroupId = groupItem == null ? "" : groupItem.Id;
            string selectedAccountId = accountItem == null ? "" : accountItem.Id;

            Form chartWindow = new Form
            {
                Text = "周收益折线图",
                BackColor = PageBg,
                StartPosition = FormStartPosition.CenterParent,
                Width = 1120,
                Height = 760,
                MinimumSize = new Size(820, 560),
                Icon = Icon
            };

            Chart largeChart = new Chart();
            ConfigureWeeklyChart(largeChart);
            largeChart.Margin = new Padding(12);
            chartWindow.Controls.Add(largeChart);
            RefreshWeeklyChart(largeChart, records: FilteredRecords(), selectedGroupId: selectedGroupId, selectedAccountId: selectedAccountId);
            chartWindow.Show(this);
        }

        private void ShowWeeklyPieWindow()
        {
            Form chartWindow = new Form
            {
                Text = "周收益来源饼图",
                BackColor = PageBg,
                StartPosition = FormStartPosition.CenterParent,
                Width = 980,
                Height = 720,
                MinimumSize = new Size(780, 540),
                Icon = Icon
            };

            Panel panel = new Panel { Dock = DockStyle.Fill, BackColor = CardBg, Padding = new Padding(12) };
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, BackColor = CardBg };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            panel.Controls.Add(layout);

            FlowLayoutPanel tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, WrapContents = false, BackColor = CardBg };
            tools.Controls.Add(new Label { Text = "结算周", AutoSize = true, Padding = new Padding(0, 8, 0, 0), ForeColor = TextMuted, BackColor = CardBg });
            ComboBox pieWeekFilter = new ComboBox { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            tools.Controls.Add(pieWeekFilter);
            tools.Controls.Add(new Label { Text = "查看", AutoSize = true, Padding = new Padding(16, 8, 0, 0), ForeColor = TextMuted, BackColor = CardBg });
            ComboBox pieModeFilter = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            tools.Controls.Add(pieModeFilter);

            Chart pieChart = new Chart();
            ConfigurePieChart(pieChart);
            Label pieTotalLabel = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 64,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font(Font.FontFamily, 20F, FontStyle.Bold),
                ForeColor = Color.FromArgb(218, 67, 45),
                BackColor = CardBg,
                Padding = new Padding(0, 0, 42, 0)
            };

            layout.Controls.Add(tools, 0, 0);
            layout.Controls.Add(pieChart, 0, 1);
            layout.Controls.Add(pieTotalLabel, 0, 2);
            chartWindow.Controls.Add(panel);

            List<UpdateRecord> records = FilteredRecords();
            RefreshPieWeekFilter(pieWeekFilter, records);
            pieModeFilter.Items.Clear();
            pieModeFilter.Items.Add("按组查看");
            pieModeFilter.Items.Add("按账号查看");
            pieModeFilter.SelectedIndex = 0;
            pieWeekFilter.SelectedIndexChanged += (sender, args) => RefreshPieChart(pieChart, pieTotalLabel, pieWeekFilter, pieModeFilter, records);
            pieModeFilter.SelectedIndexChanged += (sender, args) => RefreshPieChart(pieChart, pieTotalLabel, pieWeekFilter, pieModeFilter, records);
            chartWindow.Shown += (sender, args) => RefreshPieChart(pieChart, pieTotalLabel, pieWeekFilter, pieModeFilter, records);
            chartWindow.Show(this);
        }

        private void RefreshWeeklyChart(Chart chart, List<UpdateRecord> records, string selectedGroupId, string selectedAccountId)
        {
            chart.Series.Clear();
            chart.Titles.Clear();

            List<UpdateRecord> orderedRecords = records
                .OrderBy(record =>
                {
                    DateTime at;
                    return DateTime.TryParse(record.at, out at) ? at : DateTime.MinValue;
                })
                .ToList();

            if (orderedRecords.Count == 0)
            {
                Title empty = new Title("当前筛选范围内没有收益记录");
                empty.ForeColor = TextMuted;
                empty.Font = new Font(Font.FontFamily, 11F, FontStyle.Regular);
                chart.Titles.Add(empty);
                return;
            }

            if (!string.IsNullOrEmpty(selectedAccountId))
            {
                FilterItem accountItem = HistoricalAccountItems().FirstOrDefault(item => item.Id == selectedAccountId);
                string accountName = accountItem == null ? "账号收益" : accountItem.Label;
                AddWeeklySeries(chart, accountName, orderedRecords, record => GetAccountDelta(record, selectedAccountId), PrimaryGreen);
                return;
            }

            if (!string.IsNullOrEmpty(selectedGroupId))
            {
                FilterItem groupItem = HistoricalGroupItems().FirstOrDefault(item => item.Id == selectedGroupId);
                string groupName = groupItem == null ? "分组收益" : groupItem.Label;
                AddWeeklySeries(chart, groupName, orderedRecords, record => GetGroupDelta(record, selectedGroupId), PrimaryGreen);
                return;
            }

            AddWeeklySeries(chart, "总收益", orderedRecords, record => record.totalDelta, WarmBrown);
            foreach (FilterItem group in HistoricalGroupItems())
            {
                AddWeeklySeries(chart, group.Label, orderedRecords, record => GetGroupDelta(record, group.Id), SeriesColor(chart.Series.Count));
            }
        }

        private void AddWeeklySeries(Chart chart, string name, List<UpdateRecord> records, Func<UpdateRecord, int> selector, Color color)
        {
            Dictionary<DateTime, int> weeklyTotals = new Dictionary<DateTime, int>();
            foreach (UpdateRecord record in records)
            {
                DateTime at;
                if (!DateTime.TryParse(record.at, out at)) continue;
                DateTime saturday = WeekEndingSaturday(at);
                if (!weeklyTotals.ContainsKey(saturday)) weeklyTotals[saturday] = 0;
                weeklyTotals[saturday] += selector(record);
            }

            Series series = new Series(name);
            series.ChartType = SeriesChartType.Line;
            series.BorderWidth = 3;
            series.MarkerStyle = MarkerStyle.Circle;
            series.MarkerSize = 7;
            series.Color = color;
            series.XValueType = ChartValueType.DateTime;
            series.IsValueShownAsLabel = true;
            series.LabelForeColor = TextDark;
            series.Font = new Font(Font.FontFamily, 8F, FontStyle.Bold);
            series.ToolTip = "#SERIESNAME\n#VALX{yyyy-MM-dd}: #VALY{N0}";

            foreach (KeyValuePair<DateTime, int> point in weeklyTotals.OrderBy(item => item.Key))
            {
                int pointIndex = series.Points.AddXY(point.Key, point.Value);
                series.Points[pointIndex].Label = FmtGain(point.Value);
            }

            chart.Series.Add(series);
            ChartArea area = chart.ChartAreas["WeeklyIncome"];
            area.RecalculateAxesScale();
        }

        private static DateTime WeekEndingSaturday(DateTime value)
        {
            int daysUntilSaturday = ((int)DayOfWeek.Saturday - (int)value.DayOfWeek + 7) % 7;
            return value.Date.AddDays(daysUntilSaturday);
        }

        private void RefreshPieWeekFilter(ComboBox pieWeekFilter, List<UpdateRecord> records)
        {
            pieWeekFilter.Items.Clear();
            foreach (DateTime week in records
                .Select(record =>
                {
                    DateTime at;
                    return DateTime.TryParse(record.at, out at) ? WeekEndingSaturday(at) : DateTime.MinValue;
                })
                .Where(date => date != DateTime.MinValue)
                .Distinct()
                .OrderByDescending(date => date))
            {
                pieWeekFilter.Items.Add(week.ToString("yyyy-MM-dd"));
            }
            if (pieWeekFilter.Items.Count > 0) pieWeekFilter.SelectedIndex = 0;
        }

        private void RefreshPieChart(Chart pieChart, Label pieTotalLabel, ComboBox pieWeekFilter, ComboBox pieModeFilter, List<UpdateRecord> records)
        {
            pieChart.Series.Clear();
            pieChart.Titles.Clear();
            pieTotalLabel.Text = "";
            if (pieWeekFilter.SelectedItem == null)
            {
                AddPieEmptyTitle(pieChart, "当前筛选范围内没有可绘制的周收益");
                return;
            }

            DateTime selectedWeek;
            if (!DateTime.TryParse(pieWeekFilter.SelectedItem.ToString(), out selectedWeek))
            {
                AddPieEmptyTitle(pieChart, "结算周格式不正确");
                return;
            }

            bool byAccount = pieModeFilter.SelectedItem != null && pieModeFilter.SelectedItem.ToString() == "按账号查看";
            Dictionary<string, int> values = byAccount
                ? WeeklyAccountDeltas(records, selectedWeek)
                : WeeklyGroupDeltas(records, selectedWeek);
            int weekTotal = WeekTotalDelta(records, selectedWeek);
            pieTotalLabel.Text = "本周总收益：" + FmtGain(weekTotal);

            List<KeyValuePair<string, int>> nonZeroValues = values
                .Where(item => item.Value != 0)
                .OrderByDescending(item => Math.Abs(item.Value))
                .ToList();

            if (nonZeroValues.Count == 0)
            {
                AddPieEmptyTitle(pieChart, selectedWeek.ToString("yyyy-MM-dd") + " 没有非零收益来源");
                return;
            }

            Series series = new Series(byAccount ? "账号收益来源" : "分组收益来源");
            series.ChartType = SeriesChartType.Pie;
            series["PieLabelStyle"] = "Outside";
            series["PieLineColor"] = "Gray";
            series.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold);
            series.LabelForeColor = TextDark;
            series.ToolTip = "#VALX: #CUSTOMPROPERTY(Delta)";

            foreach (KeyValuePair<string, int> item in nonZeroValues)
            {
                int pointIndex = series.Points.AddXY(item.Key, Math.Abs(item.Value));
                DataPoint point = series.Points[pointIndex];
                point.Label = item.Key + " " + FmtGain(item.Value);
                point.LegendText = item.Key;
                point.SetCustomProperty("Delta", FmtGain(item.Value));
            }

            pieChart.Series.Add(series);
            Title title = new Title(selectedWeek.ToString("yyyy-MM-dd") + " 周收益来源 - " + (byAccount ? "按账号" : "按组"));
            title.ForeColor = TextDark;
            title.Font = new Font(Font.FontFamily, 12F, FontStyle.Bold);
            title.Docking = Docking.Top;
            title.DockedToChartArea = "WeeklySource";
            title.IsDockedInsideChartArea = false;
            pieChart.Titles.Add(title);
        }

        private Dictionary<string, int> WeeklyGroupDeltas(List<UpdateRecord> records, DateTime selectedWeek)
        {
            Dictionary<string, int> values = new Dictionary<string, int>();
            foreach (UpdateRecord record in records.Where(record => IsRecordInWeek(record, selectedWeek)))
            {
                foreach (GroupSnapshot group in record.groupSnapshots ?? new List<GroupSnapshot>())
                {
                    string key = string.IsNullOrWhiteSpace(group.groupName) ? "未分组" : group.groupName;
                    if (!values.ContainsKey(key)) values[key] = 0;
                    values[key] += group.delta;
                }
            }
            return values;
        }

        private Dictionary<string, int> WeeklyAccountDeltas(List<UpdateRecord> records, DateTime selectedWeek)
        {
            Dictionary<string, int> values = new Dictionary<string, int>();
            foreach (UpdateRecord record in records.Where(record => IsRecordInWeek(record, selectedWeek)))
            {
                foreach (AccountSnapshot account in record.accountSnapshots ?? new List<AccountSnapshot>())
                {
                    string groupName = string.IsNullOrWhiteSpace(account.groupName) ? "未分组" : account.groupName;
                    string accountName = string.IsNullOrWhiteSpace(account.accountName) ? "未命名账号" : account.accountName;
                    string key = groupName + " - " + accountName;
                    if (!values.ContainsKey(key)) values[key] = 0;
                    values[key] += account.delta;
                }
            }
            return values;
        }

        private static int WeekTotalDelta(List<UpdateRecord> records, DateTime selectedWeek)
        {
            return records
                .Where(record => IsRecordInWeek(record, selectedWeek))
                .Sum(record => record.totalDelta);
        }

        private static bool IsRecordInWeek(UpdateRecord record, DateTime selectedWeek)
        {
            DateTime at;
            return DateTime.TryParse(record.at, out at) && WeekEndingSaturday(at) == selectedWeek.Date;
        }

        private void AddPieEmptyTitle(Chart pieChart, string text)
        {
            Title empty = new Title(text);
            empty.ForeColor = TextMuted;
            empty.Font = new Font(Font.FontFamily, 11F, FontStyle.Regular);
            pieChart.Titles.Add(empty);
        }

        private static int GetGroupDelta(UpdateRecord record, string groupId)
        {
            GroupSnapshot snapshot = (record.groupSnapshots ?? new List<GroupSnapshot>()).FirstOrDefault(group => group.groupId == groupId);
            return snapshot == null ? 0 : snapshot.delta;
        }

        private static int GetAccountDelta(UpdateRecord record, string accountId)
        {
            AccountSnapshot snapshot = (record.accountSnapshots ?? new List<AccountSnapshot>()).FirstOrDefault(account => account.accountId == accountId);
            return snapshot == null ? 0 : snapshot.delta;
        }

        private static Color SeriesColor(int index)
        {
            Color[] colors =
            {
                PrimaryGreen,
                Color.FromArgb(37, 99, 235),
                Color.FromArgb(147, 51, 234),
                Color.FromArgb(217, 119, 6),
                Color.FromArgb(14, 116, 144),
                Color.FromArgb(190, 18, 60),
            };
            return colors[Math.Abs(index) % colors.Length];
        }

        private void OpenAccountManager(object sender, EventArgs e)
        {
            using (AccountManagerForm form = new AccountManagerForm(state))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    state.groups = form.Groups;
                    state.accounts = form.Accounts;
                    SaveState();
                    RebuildMainUi();
                }
            }
        }

        private void RebuildMainUi()
        {
            BuildUi();
            SetDefaultHistoryDateRange();
            RefreshEverything();
        }

        private void ExportBackup(object sender, EventArgs e)
        {
            SaveFileDialog dialog = new SaveFileDialog
            {
                Filter = "JSON 文件|*.json",
                FileName = "金条更新记录-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".json",
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            SaveState();
            File.Copy(dataFile, dialog.FileName, true);
            statusLabel.Text = "已导出备份：" + dialog.FileName;
        }

        private void ImportBackup(object sender, EventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog { Filter = "JSON 文件|*.json" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                AppState loaded = LoadOrMigrateState(File.ReadAllText(dialog.FileName, Encoding.UTF8));
                if (loaded == null || loaded.groups == null || loaded.accounts == null || loaded.updates == null)
                {
                    throw new Exception("备份格式不兼容。新版只接受通用化后的 schemaVersion 2 数据。");
                }
                state = NormalizeState(loaded);
                SaveState();
                RebuildMainUi();
                statusLabel.Text = "已导入备份：" + dialog.FileName;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ChangeDataLocation(object sender, EventArgs e)
        {
            SaveState();
            SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "选择数据文件位置",
                Filter = "JSON 文件|*.json",
                FileName = Path.GetFileName(dataFile),
                InitialDirectory = Directory.Exists(Path.GetDirectoryName(dataFile)) ? Path.GetDirectoryName(dataFile) : DefaultDataDirectory(),
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            try
            {
                string newPath = dialog.FileName;
                Directory.CreateDirectory(Path.GetDirectoryName(newPath));
                if (!string.Equals(Path.GetFullPath(dataFile), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(dataFile, newPath, true);
                }
                dataFile = newPath;
                SaveDataFilePath();
                SaveState();
                statusLabel.Text = "数据文件位置已更改：" + dataFile;
                MessageBox.Show("数据文件位置已更改。\n\n" + dataFile, "数据位置", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "更改数据位置失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ResetData(object sender, EventArgs e)
        {
            if (MessageBox.Show("会清空当前新版数据，并恢复为空白通用工具。确定继续吗？", "恢复空数据", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            state = EmptyState();
            SaveState();
            RebuildMainUi();
            statusLabel.Text = "已恢复为空数据。";
        }

        private static int CleanNumber(string value)
        {
            int number;
            string cleaned = (value ?? "0").Replace(",", "").Trim();
            return int.TryParse(cleaned, out number) && number > 0 ? number : 0;
        }

        private static string Fmt(int value)
        {
            return value.ToString("N0");
        }

        private static string FmtGain(int value)
        {
            return value > 0 ? "+" + Fmt(value) : Fmt(value);
        }

        private static string DisplayTime(string value)
        {
            DateTime time;
            return DateTime.TryParse(value, out time) ? time.ToString("yyyy-MM-dd HH:mm") : value;
        }
    }

    public class AccountManagerForm : Form
    {
        private readonly List<GroupInfo> groups;
        private readonly List<AccountInfo> accounts;
        private readonly DataGridView groupGrid = new DataGridView();
        private readonly DataGridView accountGrid = new DataGridView();

        public List<GroupInfo> Groups { get { return groups; } }
        public List<AccountInfo> Accounts { get { return accounts; } }

        public AccountManagerForm(AppState state)
        {
            groups = state.groups.Select(g => new GroupInfo { id = g.id, name = g.name, sortOrder = g.sortOrder }).OrderBy(g => g.sortOrder).ToList();
            accounts = state.accounts.Select(a => new AccountInfo { id = a.id, groupId = a.groupId, name = a.name, sortOrder = a.sortOrder }).OrderBy(a => a.sortOrder).ToList();
            BuildUi();
            RefreshGrids();
        }

        private void BuildUi()
        {
            Text = "账号管理";
            Font = new Font("Microsoft YaHei UI", 9F);
            Width = 900;
            Height = 620;
            MinimumSize = new Size(760, 520);
            StartPosition = FormStartPosition.CenterParent;

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 2 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            Controls.Add(root);

            GroupBox groupBox = new GroupBox { Text = "分组", Dock = DockStyle.Fill, Padding = new Padding(8) };
            GroupBox accountBox = new GroupBox { Text = "账号", Dock = DockStyle.Fill, Padding = new Padding(8) };
            root.Controls.Add(groupBox, 0, 0);
            root.Controls.Add(accountBox, 1, 0);

            BuildGrid(groupGrid);
            groupGrid.Columns.Add("name", "分组名称");
            groupBox.Controls.Add(groupGrid);
            FlowLayoutPanel groupButtons = ButtonsPanel();
            AddSmallButton(groupButtons, "新增", (s, e) => AddGroup());
            AddSmallButton(groupButtons, "删除", (s, e) => DeleteGroup());
            AddSmallButton(groupButtons, "上移", (s, e) => MoveGroup(-1));
            AddSmallButton(groupButtons, "下移", (s, e) => MoveGroup(1));
            groupBox.Controls.Add(groupButtons);
            groupButtons.BringToFront();

            BuildGrid(accountGrid);
            accountGrid.Columns.Add("name", "账号名称");
            DataGridViewComboBoxColumn groupColumn = new DataGridViewComboBoxColumn { Name = "group", HeaderText = "所属分组", DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton };
            accountGrid.Columns.Add(groupColumn);
            accountBox.Controls.Add(accountGrid);
            FlowLayoutPanel accountButtons = ButtonsPanel();
            AddSmallButton(accountButtons, "新增", (s, e) => AddAccount());
            AddSmallButton(accountButtons, "删除", (s, e) => DeleteAccount());
            AddSmallButton(accountButtons, "上移", (s, e) => MoveAccount(-1));
            AddSmallButton(accountButtons, "下移", (s, e) => MoveAccount(1));
            accountBox.Controls.Add(accountButtons);
            accountButtons.BringToFront();

            FlowLayoutPanel bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            Button save = new Button { Text = "保存", Width = 90, Height = 30 };
            Button cancel = new Button { Text = "取消", Width = 90, Height = 30 };
            save.Click += (s, e) => SaveAndClose();
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            bottom.Controls.Add(save);
            bottom.Controls.Add(cancel);
            root.Controls.Add(bottom, 0, 1);
            root.SetColumnSpan(bottom, 2);
        }

        private void BuildGrid(DataGridView grid)
        {
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.RowHeadersVisible = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.BackgroundColor = Color.White;
            grid.ColumnHeadersHeight = 32;
            grid.RowTemplate.Height = 30;
            grid.Padding = new Padding(0, 34, 0, 0);
        }

        private FlowLayoutPanel ButtonsPanel()
        {
            return new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, WrapContents = false };
        }

        private void AddSmallButton(FlowLayoutPanel panel, string text, EventHandler handler)
        {
            Button button = new Button { Text = text, Width = 64, Height = 28 };
            button.Click += handler;
            panel.Controls.Add(button);
        }

        private void RefreshGrids()
        {
            groupGrid.Rows.Clear();
            foreach (GroupInfo group in groups.OrderBy(g => g.sortOrder))
            {
                int index = groupGrid.Rows.Add(group.name);
                groupGrid.Rows[index].Tag = group.id;
            }

            DataGridViewComboBoxColumn groupColumn = (DataGridViewComboBoxColumn)accountGrid.Columns["group"];
            groupColumn.Items.Clear();
            foreach (GroupInfo group in groups.OrderBy(g => g.sortOrder)) groupColumn.Items.Add(group.name);

            accountGrid.Rows.Clear();
            foreach (AccountInfo account in accounts.OrderBy(a => a.sortOrder))
            {
                GroupInfo group = groups.FirstOrDefault(g => g.id == account.groupId);
                int index = accountGrid.Rows.Add(account.name, group == null ? "" : group.name);
                accountGrid.Rows[index].Tag = account.id;
            }
        }

        private void PullGridValues()
        {
            for (int i = 0; i < groupGrid.Rows.Count; i++)
            {
                string id = (string)groupGrid.Rows[i].Tag;
                GroupInfo group = groups.First(g => g.id == id);
                group.name = Convert.ToString(groupGrid.Rows[i].Cells[0].Value ?? "").Trim();
                group.sortOrder = i;
            }

            for (int i = 0; i < accountGrid.Rows.Count; i++)
            {
                string id = (string)accountGrid.Rows[i].Tag;
                AccountInfo account = accounts.First(a => a.id == id);
                account.name = Convert.ToString(accountGrid.Rows[i].Cells[0].Value ?? "").Trim();
                string groupName = Convert.ToString(accountGrid.Rows[i].Cells[1].Value ?? "");
                GroupInfo group = groups.FirstOrDefault(g => g.name == groupName);
                account.groupId = group == null ? "" : group.id;
                account.sortOrder = i;
            }
        }

        private void AddGroup()
        {
            PullGridValues();
            groups.Add(new GroupInfo { id = Guid.NewGuid().ToString(), name = "新分组", sortOrder = groups.Count });
            RefreshGrids();
        }

        private void DeleteGroup()
        {
            PullGridValues();
            if (groupGrid.CurrentRow == null) return;
            string id = (string)groupGrid.CurrentRow.Tag;
            if (accounts.Any(a => a.groupId == id))
            {
                MessageBox.Show("该分组下还有账号，请先移动或删除这些账号。", "不能删除分组", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            groups.RemoveAll(g => g.id == id);
            RefreshGrids();
        }

        private void MoveGroup(int offset)
        {
            PullGridValues();
            if (groupGrid.CurrentRow == null) return;
            int index = groupGrid.CurrentRow.Index;
            int target = index + offset;
            if (target < 0 || target >= groups.Count) return;
            GroupInfo item = groups[index];
            groups.RemoveAt(index);
            groups.Insert(target, item);
            for (int i = 0; i < groups.Count; i++) groups[i].sortOrder = i;
            RefreshGrids();
            groupGrid.Rows[target].Selected = true;
        }

        private void AddAccount()
        {
            PullGridValues();
            if (!groups.Any())
            {
                MessageBox.Show("请先新增分组。", "没有分组", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            accounts.Add(new AccountInfo { id = Guid.NewGuid().ToString(), name = "新账号", groupId = groups.OrderBy(g => g.sortOrder).First().id, sortOrder = accounts.Count });
            RefreshGrids();
        }

        private void DeleteAccount()
        {
            PullGridValues();
            if (accountGrid.CurrentRow == null) return;
            string id = (string)accountGrid.CurrentRow.Tag;
            accounts.RemoveAll(a => a.id == id);
            RefreshGrids();
        }

        private void MoveAccount(int offset)
        {
            PullGridValues();
            if (accountGrid.CurrentRow == null) return;
            int index = accountGrid.CurrentRow.Index;
            int target = index + offset;
            if (target < 0 || target >= accounts.Count) return;
            AccountInfo item = accounts[index];
            accounts.RemoveAt(index);
            accounts.Insert(target, item);
            for (int i = 0; i < accounts.Count; i++) accounts[i].sortOrder = i;
            RefreshGrids();
            accountGrid.Rows[target].Selected = true;
        }

        private void SaveAndClose()
        {
            PullGridValues();
            if (groups.Any(g => string.IsNullOrWhiteSpace(g.name)))
            {
                MessageBox.Show("分组名称不能为空。", "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (groups.GroupBy(g => g.name).Any(g => g.Count() > 1))
            {
                MessageBox.Show("分组名称不能重复。", "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (accounts.Any(a => string.IsNullOrWhiteSpace(a.name)))
            {
                MessageBox.Show("账号名称不能为空。", "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (accounts.Any(a => string.IsNullOrWhiteSpace(a.groupId) || groups.All(g => g.id != a.groupId)))
            {
                MessageBox.Show("每个账号都必须选择一个有效分组。", "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrackerForm());
        }
    }
}
