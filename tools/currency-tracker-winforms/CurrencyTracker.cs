using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

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

        private TableLayoutPanel root;
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
        private readonly Label statusLabel = new Label();

        public TrackerForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            configFile = Path.Combine(DefaultDataDirectory(), "settings.json");
            dataFile = LoadDataFilePath();
            state = LoadState();
            BuildUi();
            RefreshEverything();
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
                    AppState loaded = serializer.Deserialize<AppState>(json);
                    if (loaded != null && loaded.schemaVersion == CurrentSchemaVersion && loaded.groups != null && loaded.accounts != null && loaded.updates != null)
                    {
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

        private AppState NormalizeState(AppState loaded)
        {
            loaded.groups = loaded.groups.OrderBy(g => g.sortOrder).ThenBy(g => g.name).ToList();
            loaded.accounts = loaded.accounts.OrderBy(a => a.sortOrder).ThenBy(a => a.name).ToList();
            return loaded;
        }

        private void SaveState()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dataFile));
            state.schemaVersion = CurrentSchemaVersion;
            string json = serializer.Serialize(state);
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
            Dictionary<string, string> config = new Dictionary<string, string> { { "dataFile", dataFile } };
            File.WriteAllText(configFile, PrettyJson(serializer.Serialize(config)), Encoding.UTF8);
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
            Width = 1240;
            Height = 980;
            MinimumSize = new Size(1120, 860);

            root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 5, ColumnCount = 1, BackColor = PageBg };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 350));
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
            root.Controls.Add(editor, 0, 2);
            BuildEditor(editor);

            TableLayoutPanel lower = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = PageBg };
            lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
            lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 74));
            root.Controls.Add(lower, 0, 3);
            Panel summaryPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 8, 0), BackColor = PageBg };
            Panel historyPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 0, 0), BackColor = PageBg };
            lower.Controls.Add(summaryPanel, 0, 0);
            lower.Controls.Add(historyPanel, 1, 0);
            BuildCurrentSummary(summaryPanel);
            BuildHistory(historyPanel);

            statusLabel.Dock = DockStyle.Fill;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.ForeColor = TextMuted;
            statusLabel.BackColor = PageBg;
            root.Controls.Add(statusLabel, 0, 4);
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

        private void RefreshHistory()
        {
            summaryGrid.Rows.Clear();
            accountGrid.Rows.Clear();

            FilterItem groupItem = groupFilter.SelectedItem as FilterItem;
            FilterItem accountItem = accountFilter.SelectedItem as FilterItem;
            string selectedGroupId = groupItem == null ? "" : groupItem.Id;
            string selectedAccountId = accountItem == null ? "" : accountItem.Id;

            foreach (UpdateRecord record in FilteredRecords())
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
                AppState loaded = serializer.Deserialize<AppState>(File.ReadAllText(dialog.FileName, Encoding.UTF8));
                if (loaded == null || loaded.schemaVersion != CurrentSchemaVersion || loaded.groups == null || loaded.accounts == null || loaded.updates == null)
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
