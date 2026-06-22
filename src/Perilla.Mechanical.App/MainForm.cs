using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Perilla.Mechanical.Core.Models;
using Perilla.Mechanical.Core.Pdf;
using Perilla.Mechanical.Core.Services;
using Perilla.Mechanical.Export;

namespace Perilla.Mechanical.App
{
    /// <summary>
    /// 主窗体：四区专业布局
    /// - 顶部：菜单栏 + 工具栏（文件操作/标注工具/AI识别/导出/主题切换）
    /// - 左侧：页面导航面板（页面缩略图/列表）
    /// - 中央：PDF 图纸画布 + 气泡标注层
    /// - 右侧：气泡管理面板（气泡列表 + 属性编辑）
    /// - 底部：状态栏（文件信息/页码/缩放比例/气泡数量）
    /// 支持双主题（深色/浅色）切换
    /// </summary>
    public class MainForm : Form
    {
        // ========== UI 控件 ==========
        private MenuStrip _menu;
        private ToolStrip _toolbar;
        private Panel _leftPanel;       // 左侧导航面板
        private Panel _centerPanel;     // 中央画布面板
        private Panel _rightPanel;      // 右侧气泡管理面板
        private Panel _canvasContainer;  // 画布容器（带背景色）
        private PictureBox _picture;     // PDF 画布
        private StatusStrip _status;

        // 工具栏按钮
        private ToolStripButton _btnOpen, _btnAuto, _btnAddBubble, _btnDeleteBubble;
        private ToolStripButton _btnExportPdf, _btnExportPng, _btnExportExcel;
        private ToolStripButton _btnUndo, _btnRedo;
        private ToolStripButton _btnPrev, _btnNext;
        private ToolStripButton _btnToggleTheme;
        private ToolStripLabel _lblPage, _lblZoom, _lblBubbleCount;
        private ToolStripSeparator _sep1, _sep2, _sep3, _sep4, _sep5;

        // 左侧：页面导航
        private Label _leftTitle;
        private ListView _pageList;

        // 右侧：气泡管理
        private Label _rightTitle;
        private ListView _bubbleList;
        private Panel _propertyPanel;
        private Label _propTitle;
        private Label _lblNum, _lblType, _lblText, _lblNote;
        private TextBox _txtNum, _txtNote;
        private ComboBox _cmbType;
        private TextBox _txtLinkedText;
        private Button _btnApplyProp;

        // 底部状态栏
        private ToolStripStatusLabel _statusFile;
        private ToolStripStatusLabel _statusPage;
        private ToolStripStatusLabel _statusBubbles;
        private ToolStripStatusLabel _statusZoom;
        private ToolStripStatusLabel _statusMessage;

        // ========== 数据状态 ==========
        private string _currentPdf;
        private PdfParsingService _parser;
        private List<List<Bubble>> _pageBubbles;
        private int _currentPage;
        private double _renderScale = 3.0;
        private double _zoomLevel = 1.0;
        private UndoRedoService _undoRedo;
        private bool _pendingAddBubble;
        private Bubble _selectedBubble;
        private bool _isDarkTheme = true;  // 默认深色主题

        // ========== 主题颜色方案 ==========
        private struct ThemeColors
        {
            public Color BackColor;
            public Color PanelColor;
            public Color CanvasColor;
            public Color ToolbarColor;
            public Color TextColor;
            public Color BorderColor;
            public Color TitleBarColor;
            public Color TitleTextColor;
            public Color SelectionColor;
            public Color SelectionTextColor;
            public Color BubbleCircleColor;
            public Color BubbleFillColor;
            public Color BubbleTextColor;
            public Color StatusColor;
            public Color ButtonColor;
            public Color ButtonHoverColor;
        }

        private ThemeColors _darkTheme = new ThemeColors
        {
            BackColor = Color.FromArgb(30, 30, 30),
            PanelColor = Color.FromArgb(45, 45, 48),
            CanvasColor = Color.FromArgb(38, 38, 42),
            ToolbarColor = Color.FromArgb(55, 55, 60),
            TextColor = Color.FromArgb(230, 230, 230),
            BorderColor = Color.FromArgb(80, 80, 85),
            TitleBarColor = Color.FromArgb(60, 60, 65),
            TitleTextColor = Color.FromArgb(245, 245, 245),
            SelectionColor = Color.FromArgb(200, 80, 40),
            SelectionTextColor = Color.White,
            BubbleCircleColor = Color.FromArgb(255, 90, 50),
            BubbleFillColor = Color.FromArgb(255, 200, 150),
            BubbleTextColor = Color.Black,
            StatusColor = Color.FromArgb(50, 50, 55),
            ButtonColor = Color.FromArgb(70, 70, 75),
            ButtonHoverColor = Color.FromArgb(90, 90, 100)
        };

        private ThemeColors _lightTheme = new ThemeColors
        {
            BackColor = Color.FromArgb(240, 240, 245),
            PanelColor = Color.White,
            CanvasColor = Color.FromArgb(230, 230, 235),
            ToolbarColor = Color.FromArgb(250, 250, 250),
            TextColor = Color.FromArgb(40, 40, 45),
            BorderColor = Color.FromArgb(200, 200, 210),
            TitleBarColor = Color.FromArgb(220, 220, 230),
            TitleTextColor = Color.FromArgb(50, 50, 55),
            SelectionColor = Color.FromArgb(255, 120, 80),
            SelectionTextColor = Color.White,
            BubbleCircleColor = Color.FromArgb(220, 60, 30),
            BubbleFillColor = Color.FromArgb(255, 230, 210),
            BubbleTextColor = Color.Black,
            StatusColor = Color.FromArgb(235, 235, 240),
            ButtonColor = Color.White,
            ButtonHoverColor = Color.FromArgb(220, 220, 230)
        };

        public MainForm()
        {
            Text = "Perilla Mechanical PDF Editor —— 机械设计图纸专用 PDF 标注工具";
            Width = 1400;
            Height = 880;
            MinimumSize = new Size(1024, 600);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9);

            _pageBubbles = new List<List<Bubble>>();

            BuildUi();
            ApplyTheme(_isDarkTheme);
            SetupZoomShortcuts();
            SetupUndoRedo();

            UpdateStatus("就绪 —— 请先打开一个 PDF 图纸文件");
        }

        // ============================================================
        // UI 构建 —— 四区专业布局
        // ============================================================
        private void BuildUi()
        {
            // ===== 菜单栏 =====
            _menu = new MenuStrip { GripMargin = new Padding(0), Font = new Font("Microsoft YaHei UI", 9) };
            var fileMenu = new ToolStripMenuItem("文件(F)");
            fileMenu.DropDownItems.Add("打开 PDF...", null, (s, e) => OnOpenPdf(s, e));
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add("导出为 PDF...", null, (s, e) => OnExportPdf(s, e));
            fileMenu.DropDownItems.Add("导出为 PNG...", null, (s, e) => OnExportPng(s, e));
            fileMenu.DropDownItems.Add("导出为 Excel...", null, (s, e) => OnExportExcel(s, e));
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add("退出", null, (s, e) => Close());

            var editMenu = new ToolStripMenuItem("编辑(E)");
            editMenu.DropDownItems.Add("撤销 (Ctrl+Z)", null, (s, e) => _undoRedo?.Undo());
            editMenu.DropDownItems.Add("重做 (Ctrl+Y)", null, (s, e) => _undoRedo?.Redo());
            editMenu.DropDownItems.Add(new ToolStripSeparator());
            editMenu.DropDownItems.Add("添加气泡 (手动)", null, (s, e) => OnAddManualBubble(s, e));
            editMenu.DropDownItems.Add("删除选中气泡 (Del)", null, (s, e) => DeleteSelectedBubble());
            editMenu.DropDownItems.Add("重新编号当前页", null, (s, e) => RenumberCurrentPage());

            var toolsMenu = new ToolStripMenuItem("工具(T)");
            toolsMenu.DropDownItems.Add("AI 自动识别并编号...", null, (s, e) => OnRunAutomation(s, e));
            toolsMenu.DropDownItems.Add(new ToolStripSeparator());
            var zoomMenu = new ToolStripMenuItem("缩放");
            zoomMenu.DropDownItems.Add("放大 (Ctrl+Plus)", null, (s, e) => ApplyZoom(_zoomLevel * 1.2));
            zoomMenu.DropDownItems.Add("缩小 (Ctrl+Minus)", null, (s, e) => ApplyZoom(_zoomLevel / 1.2));
            zoomMenu.DropDownItems.Add("恢复 100% (Ctrl+0)", null, (s, e) => ApplyZoom(1.0));
            toolsMenu.DropDownItems.Add(zoomMenu);

            var viewMenu = new ToolStripMenuItem("视图(V)");
            viewMenu.DropDownItems.Add("切换深色/浅色主题", null, (s, e) => ToggleTheme());

            var helpMenu = new ToolStripMenuItem("帮助(H)");
            helpMenu.DropDownItems.Add("关于", null, (s, e) =>
                MessageBox.Show("Perilla Mechanical PDF Editor v1.0\n机械设计图纸专用 PDF 标注工具\n\n核心功能：\n• PDF 图纸查看\n• 气泡序号标注\n• AI 智能识别（线性尺寸/形位公差/注解）\n• PDF/PNG/Excel 导出",
                    "关于", MessageBoxButtons.OK, MessageBoxIcon.Information));

            _menu.Items.AddRange(new ToolStripItem[] { fileMenu, editMenu, toolsMenu, viewMenu, helpMenu });
            _menu.Dock = DockStyle.Top;
            Controls.Add(_menu);

            // ===== 工具栏 =====
            _toolbar = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                ImageScalingSize = new Size(20, 20),
                Font = new Font("Microsoft YaHei UI", 9),
                Height = 42,
                ShowItemToolTips = true
            };

            _btnOpen = new ToolStripButton { Text = "📂 打开 PDF", DisplayStyle = ToolStripItemDisplayStyle.Text, Padding = new Padding(8, 2, 8, 2) };
            _btnOpen.Click += OnOpenPdf;

            _btnAuto = new ToolStripButton { Text = "🤖 AI 自动编号", DisplayStyle = ToolStripItemDisplayStyle.Text, Padding = new Padding(8, 2, 8, 2) };
            _btnAuto.Click += OnRunAutomation;

            _btnAddBubble = new ToolStripButton { Text = "✏️ 添加气泡", DisplayStyle = ToolStripItemDisplayStyle.Text, Padding = new Padding(8, 2, 8, 2) };
            _btnAddBubble.Click += OnAddManualBubble;

            _btnDeleteBubble = new ToolStripButton { Text = "🗑️ 删除", DisplayStyle = ToolStripItemDisplayStyle.Text, Padding = new Padding(8, 2, 8, 2) };
            _btnDeleteBubble.Click += (s, e) => DeleteSelectedBubble();

            _sep1 = new ToolStripSeparator();

            _btnUndo = new ToolStripButton { Text = "↶ 撤销", DisplayStyle = ToolStripItemDisplayStyle.Text, Padding = new Padding(8, 2, 8, 2) };
            _btnUndo.Click += (s, e) => _undoRedo?.Undo();
            _btnRedo = new ToolStripButton { Text = "↷ 重做", DisplayStyle = ToolStripItemDisplayStyle.Text, Padding = new Padding(8, 2, 8, 2) };
            _btnRedo.Click += (s, e) => _undoRedo?.Redo();

            _sep2 = new ToolStripSeparator();

            _btnExportPdf = new ToolStripButton { Text = "📄 导出 PDF", DisplayStyle = ToolStripItemDisplayStyle.Text, Padding = new Padding(8, 2, 8, 2) };
            _btnExportPdf.Click += OnExportPdf;
            _btnExportPng = new ToolStripButton { Text = "🖼️ 导出 PNG", DisplayStyle = ToolStripItemDisplayStyle.Text, Padding = new Padding(8, 2, 8, 2) };
            _btnExportPng.Click += OnExportPng;
            _btnExportExcel = new ToolStripButton { Text = "📊 导出 Excel", DisplayStyle = ToolStripItemDisplayStyle.Text, Padding = new Padding(8, 2, 8, 2) };
            _btnExportExcel.Click += OnExportExcel;

            _sep3 = new ToolStripSeparator();

            _btnPrev = new ToolStripButton { Text = "◀ 上一页", DisplayStyle = ToolStripItemDisplayStyle.Text, Padding = new Padding(8, 2, 8, 2) };
            _btnPrev.Click += (s, e) => NavPage(-1);
            _btnNext = new ToolStripButton { Text = "下一页 ▶", DisplayStyle = ToolStripItemDisplayStyle.Text, Padding = new Padding(8, 2, 8, 2) };
            _btnNext.Click += (s, e) => NavPage(1);

            _sep4 = new ToolStripSeparator();

            _lblPage = new ToolStripLabel("页：- / -") { Padding = new Padding(10, 0, 10, 0) };
            _lblZoom = new ToolStripLabel("缩放：100%") { Padding = new Padding(10, 0, 10, 0) };
            _lblBubbleCount = new ToolStripLabel("气泡：0") { Padding = new Padding(10, 0, 10, 0) };

            _sep5 = new ToolStripSeparator();

            _btnToggleTheme = new ToolStripButton { Text = "🌓 主题", DisplayStyle = ToolStripItemDisplayStyle.Text, Padding = new Padding(8, 2, 8, 2), Alignment = ToolStripItemAlignment.Right };
            _btnToggleTheme.Click += (s, e) => ToggleTheme();

            _toolbar.Items.AddRange(new ToolStripItem[] {
                _btnOpen, _sep1, _btnAuto, _btnAddBubble, _btnDeleteBubble, _sep2,
                _btnUndo, _btnRedo, _sep3,
                _btnExportPdf, _btnExportPng, _btnExportExcel, _sep4,
                _btnPrev, _lblPage, _btnNext, _lblZoom, _lblBubbleCount, _sep5, _btnToggleTheme
            });
            _toolbar.Dock = DockStyle.Top;
            Controls.Add(_toolbar);

            // ===== 主布局容器（使用 SplitContainer 实现四区布局）=====
            var mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                FixedPanel = FixedPanel.Panel1,
                Panel1MinSize = 0,
                Panel2MinSize = 0,
                SplitterDistance = 0,
                Visible = false
            };

            // 左侧：页面导航面板
            _leftPanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = 200,
                Padding = new Padding(0)
            };
            _leftTitle = new Label
            {
                Text = " 📑 页面导航",
                Dock = DockStyle.Top,
                Height = 32,
                Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 8, 0)
            };
            _pageList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.List,
                FullRowSelect = true,
                MultiSelect = false,
                BorderStyle = BorderStyle.None,
                Font = new Font("Microsoft YaHei UI", 9),
                ShowGroups = false
            };
            _pageList.SelectedIndexChanged += OnPageSelected;

            _leftPanel.Controls.Add(_pageList);
            _leftPanel.Controls.Add(_leftTitle);

            // 中央：画布区域
            _centerPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0) };
            _canvasContainer = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
                AutoScroll = true
            };
            _picture = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.AutoSize,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Default
            };
            _picture.MouseClick += OnPictureClick;
            _canvasContainer.Controls.Add(_picture);
            _centerPanel.Controls.Add(_canvasContainer);

            // 右侧：气泡管理面板（列表 + 属性编辑）
            _rightPanel = new Panel
            {
                Dock = DockStyle.Right,
                Width = 340,
                Padding = new Padding(0)
            };

            _rightTitle = new Label
            {
                Text = " 🔢 气泡管理",
                Dock = DockStyle.Top,
                Height = 32,
                Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 8, 0)
            };
            _bubbleList = new ListView
            {
                Dock = DockStyle.Top,
                Height = 380,
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                LabelEdit = true,
                Font = new Font("Microsoft YaHei UI", 9),
                BorderStyle = BorderStyle.None,
                MultiSelect = false
            };
            _bubbleList.Columns.Add("#", 40, HorizontalAlignment.Center);
            _bubbleList.Columns.Add("类型", 80, HorizontalAlignment.Center);
            _bubbleList.Columns.Add("内容", -2, HorizontalAlignment.Left);
            _bubbleList.AfterLabelEdit += OnBubbleAfterLabelEdit;
            _bubbleList.SelectedIndexChanged += OnBubbleSelected;

            // 属性编辑面板
            _propertyPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 8, 12, 12)
            };
            _propTitle = new Label
            {
                Text = "⚙️ 属性编辑",
                Dock = DockStyle.Top,
                Height = 28,
                Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _lblNum = new Label { Text = "序号：", Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.MiddleLeft };
            _txtNum = new TextBox { Dock = DockStyle.Top, Height = 24 };
            _lblType = new Label { Text = "类型：", Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Top = 80 };
            _cmbType = new ComboBox { Dock = DockStyle.Top, Height = 24, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbType.Items.AddRange(new string[] { "手动气泡", "线性尺寸", "形位公差", "图纸注解" });
            _lblText = new Label { Text = "识别内容：", Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.MiddleLeft };
            _txtLinkedText = new TextBox { Dock = DockStyle.Top, Height = 24, ReadOnly = true };
            _lblNote = new Label { Text = "备注说明：", Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.MiddleLeft };
            _txtNote = new TextBox { Dock = DockStyle.Top, Height = 60, Multiline = true, ScrollBars = ScrollBars.Vertical };
            _btnApplyProp = new Button
            {
                Text = "✓ 应用修改",
                Dock = DockStyle.Top,
                Height = 32,
                Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold),
                UseVisualStyleBackColor = true
            };
            _btnApplyProp.Click += OnApplyPropertyChanges;

            // 按倒序添加（Dock=Top 的顺序）
            _propertyPanel.Controls.Add(_btnApplyProp);
            _propertyPanel.Controls.Add(_txtNote);
            _propertyPanel.Controls.Add(_lblNote);
            _propertyPanel.Controls.Add(_txtLinkedText);
            _propertyPanel.Controls.Add(_lblText);
            _propertyPanel.Controls.Add(_cmbType);
            _propertyPanel.Controls.Add(_lblType);
            _propertyPanel.Controls.Add(_txtNum);
            _propertyPanel.Controls.Add(_lblNum);
            _propertyPanel.Controls.Add(_propTitle);

            _rightPanel.Controls.Add(_propertyPanel);
            _rightPanel.Controls.Add(_bubbleList);
            _rightPanel.Controls.Add(_rightTitle);

            // ===== 底部状态栏 =====
            _status = new StatusStrip { Height = 26, SizingGrip = true };
            _statusFile = new ToolStripStatusLabel("文件：未打开") { Spring = false, Padding = new Padding(10, 2, 10, 2), TextAlign = ContentAlignment.MiddleLeft };
            _statusPage = new ToolStripStatusLabel("页：- / -") { Spring = false, Padding = new Padding(10, 2, 10, 2) };
            _statusBubbles = new ToolStripStatusLabel("气泡总数：0") { Spring = false, Padding = new Padding(10, 2, 10, 2) };
            _statusZoom = new ToolStripStatusLabel("缩放：100%") { Spring = false, Padding = new Padding(10, 2, 10, 2) };
            _statusMessage = new ToolStripStatusLabel("") { Spring = true, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.Gray };

            _status.Items.AddRange(new ToolStripItem[] {
                _statusFile, new ToolStripSeparator(),
                _statusPage, new ToolStripSeparator(),
                _statusBubbles, new ToolStripSeparator(),
                _statusZoom, new ToolStripSeparator(),
                _statusMessage
            });

            Controls.Add(_centerPanel);
            Controls.Add(_rightPanel);
            Controls.Add(_leftPanel);
            Controls.Add(_status);

            // 注意：Controls.Add 的顺序决定了 Z-order。
            // 对于 DockStyle，先添加 Fill 的控件，再添加 Left/Right/Top/Bottom 的。
            // 但 WinForms 的处理方式是后添加的控件优先级更高。
            // 这里我们按正确的顺序应该是：先加 Left, Right, Top 再加入 Fill。

            // 重新调整 Controls 顺序以正确处理 Dock
            Controls.Clear();
            Controls.Add(_status);
            Controls.Add(_centerPanel);   // Fill
            Controls.Add(_rightPanel);    // Right
            Controls.Add(_leftPanel);     // Left
            Controls.Add(_toolbar);       // Top
            Controls.Add(_menu);          // Top
            // 让菜单栏在最上面
            Controls.SetChildIndex(_menu, 0);
            Controls.SetChildIndex(_toolbar, 1);
            Controls.SetChildIndex(_leftPanel, 2);
            Controls.SetChildIndex(_rightPanel, 3);
            Controls.SetChildIndex(_status, 4);
            Controls.SetChildIndex(_centerPanel, 5);
        }

        // ============================================================
        // 主题切换
        // ============================================================
        private void ToggleTheme()
        {
            _isDarkTheme = !_isDarkTheme;
            ApplyTheme(_isDarkTheme);
        }

        private void ApplyTheme(bool isDark)
        {
            var theme = isDark ? _darkTheme : _lightTheme;

            // 窗体背景
            BackColor = theme.BackColor;
            ForeColor = theme.TextColor;

            // 菜单栏和工具栏
            _menu.BackColor = theme.ToolbarColor;
            _menu.ForeColor = theme.TextColor;
            foreach (ToolStripItem item in _menu.Items)
            {
                item.ForeColor = theme.TextColor;
                if (item is ToolStripMenuItem mi)
                {
                    mi.BackColor = theme.ToolbarColor;
                    foreach (ToolStripItem sub in mi.DropDownItems)
                    {
                        sub.ForeColor = theme.TextColor;
                        sub.BackColor = theme.PanelColor;
                    }
                }
            }

            _toolbar.BackColor = theme.ToolbarColor;
            _toolbar.ForeColor = theme.TextColor;
            foreach (ToolStripItem item in _toolbar.Items)
            {
                item.ForeColor = theme.TextColor;
                item.BackColor = theme.ToolbarColor;
            }

            // 左侧面板
            _leftPanel.BackColor = theme.PanelColor;
            _leftPanel.ForeColor = theme.TextColor;
            _leftTitle.BackColor = theme.TitleBarColor;
            _leftTitle.ForeColor = theme.TitleTextColor;
            _pageList.BackColor = theme.PanelColor;
            _pageList.ForeColor = theme.TextColor;

            // 中央画布
            _centerPanel.BackColor = theme.CanvasColor;
            _canvasContainer.BackColor = theme.CanvasColor;
            _picture.BackColor = Color.White;  // 画布始终白色

            // 右侧面板
            _rightPanel.BackColor = theme.PanelColor;
            _rightPanel.ForeColor = theme.TextColor;
            _rightTitle.BackColor = theme.TitleBarColor;
            _rightTitle.ForeColor = theme.TitleTextColor;
            _bubbleList.BackColor = theme.PanelColor;
            _bubbleList.ForeColor = theme.TextColor;

            // 属性编辑区
            _propertyPanel.BackColor = theme.PanelColor;
            _propTitle.ForeColor = theme.TextColor;
            _lblNum.ForeColor = theme.TextColor;
            _lblType.ForeColor = theme.TextColor;
            _lblText.ForeColor = theme.TextColor;
            _lblNote.ForeColor = theme.TextColor;
            _txtNum.BackColor = isDark ? Color.FromArgb(65, 65, 70) : Color.White;
            _txtNum.ForeColor = theme.TextColor;
            _txtNote.BackColor = isDark ? Color.FromArgb(65, 65, 70) : Color.White;
            _txtNote.ForeColor = theme.TextColor;
            _txtLinkedText.BackColor = isDark ? Color.FromArgb(65, 65, 70) : Color.White;
            _txtLinkedText.ForeColor = theme.TextColor;
            _cmbType.BackColor = isDark ? Color.FromArgb(65, 65, 70) : Color.White;
            _cmbType.ForeColor = theme.TextColor;
            _btnApplyProp.BackColor = theme.ButtonColor;
            _btnApplyProp.ForeColor = theme.TextColor;
            _btnApplyProp.FlatStyle = FlatStyle.Flat;
            _btnApplyProp.FlatAppearance.BorderColor = theme.BorderColor;

            // 底部状态栏
            _status.BackColor = theme.StatusColor;
            _status.ForeColor = theme.TextColor;
            _statusFile.ForeColor = theme.TextColor;
            _statusPage.ForeColor = theme.TextColor;
            _statusBubbles.ForeColor = theme.TextColor;
            _statusZoom.ForeColor = theme.TextColor;
            _statusMessage.ForeColor = isDark ? Color.FromArgb(180, 180, 190) : Color.Gray;

            _btnToggleTheme.Text = isDark ? "☀️ 浅色" : "🌙 深色";

            // 重绘
            Invalidate(true);
            if (_parser != null) RedrawPicture();
        }

        // ============================================================
        // 打开 PDF
        // ============================================================
        private void OnOpenPdf(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*";
                if (dlg.ShowDialog(this) == DialogResult.OK) OpenPdf(dlg.FileName);
            }
        }

        private void OpenPdf(string path)
        {
            try
            {
                if (_parser != null) _parser.Dispose();
                _parser = new PdfParsingService();
                _parser.Load(path);
                _currentPdf = path;
                _currentPage = 0;
                _pageBubbles = new List<List<Bubble>>();
                for (int i = 0; i < _parser.PageCount; i++) _pageBubbles.Add(new List<Bubble>());
                _undoRedo.Clear();
                UpdatePageList();
                UpdatePageLabel();
                RedrawPicture();
                UpdateBubbleList();
                UpdateStatus("已打开 " + Path.GetFileName(path) + " (" + _parser.PageCount + " 页)");
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开 PDF 失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdatePageList()
        {
            _pageList.Items.Clear();
            for (int i = 0; i < _parser.PageCount; i++)
            {
                var item = new ListViewItem("第 " + (i + 1) + " 页") { Tag = i };
                _pageList.Items.Add(item);
            }
            if (_pageList.Items.Count > 0) _pageList.Items[0].Selected = true;
        }

        private void OnPageSelected(object sender, EventArgs e)
        {
            if (_pageList.SelectedItems.Count == 0) return;
            int idx = (int)_pageList.SelectedItems[0].Tag;
            if (idx != _currentPage)
            {
                _currentPage = idx;
                UpdatePageLabel();
                RedrawPicture();
                UpdateBubbleList();
            }
        }

        // ============================================================
        // AI 自动识别与编号
        // ============================================================
        private void OnRunAutomation(object sender, EventArgs e)
        {
            if (_parser == null)
            {
                MessageBox.Show("请先打开 PDF 文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                UpdateStatus("正在识别图纸内容...");
                var svc = new AutomatedProcessingService(_parser,
                    new DrawingRecognitionService(), new BubbleNumberingService());

                var existingManualBubbles = new List<Bubble>();
                foreach (var bubbles in _pageBubbles)
                    foreach (var b in bubbles) if (b.IsManual) existingManualBubbles.Add(b);

                var previousAutoBubbles = new List<List<Bubble>>();
                foreach (var bubbles in _pageBubbles)
                {
                    var copy = new List<Bubble>();
                    foreach (var b in bubbles) if (!b.IsManual) copy.Add(b);
                    previousAutoBubbles.Add(copy);
                }

                var opts = new AutoProcessingOptions { Numbering = new NumberingOptions() };
                var results = svc.Run(_currentPdf, opts, existingManualBubbles, msg => UpdateStatus(msg));

                var newBubbles = new List<List<Bubble>>();
                foreach (var r in results)
                {
                    var list = new List<Bubble>();
                    foreach (var b in r.Bubbles) if (!b.IsManual) list.Add(b);
                    newBubbles.Add(list);
                }

                _undoRedo.Execute(new AutoRecognizeCommand(_pageBubbles, previousAutoBubbles, newBubbles));

                int total = TotalBubbles();
                UpdateStatus("自动处理完成 - 共 " + results.Count + " 页，" + total + " 个气泡");

                RedrawPicture();
                UpdateBubbleList();
            }
            catch (Exception ex)
            {
                MessageBox.Show("自动处理失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private int TotalBubbles()
        {
            int n = 0;
            foreach (var list in _pageBubbles) n += list.Count;
            return n;
        }

        // ============================================================
        // 手动添加气泡
        // ============================================================
        private void OnAddManualBubble(object sender, EventArgs e)
        {
            if (_parser == null)
            {
                MessageBox.Show("请先打开 PDF 文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            UpdateStatus("添加气泡模式：在图纸上单击以放置气泡（Esc 取消）");
            _pendingAddBubble = true;
            _picture.Cursor = Cursors.Cross;
        }

        private void OnPictureClick(object sender, MouseEventArgs e)
        {
            // 1) 添加气泡模式
            if (_pendingAddBubble)
            {
                if (_parser == null) return;
                var size = _parser.GetPageSize(_currentPage);
                if (size.Width <= 0 || size.Height <= 0 || _picture.Image == null) return;

                PointF pdfPt = PixelToPdfPoint(new PointF(e.X, e.Y), size);
                var bubbles = _pageBubbles[_currentPage];
                var bubble = new Bubble
                {
                    Number = bubbles.Count + 1,
                    Label = (bubbles.Count + 1).ToString(),
                    Center = new PointD(pdfPt.X, pdfPt.Y),
                    Radius = 10,
                    Kind = RecognitionKind.Bubble,
                    LinkedText = "",
                    PageIndex = _currentPage,
                    IsManual = true
                };
                _undoRedo.Execute(new AddBubbleCommand(bubbles, bubble));
                RenumberBubbles();
                _pendingAddBubble = false;
                _picture.Cursor = Cursors.Default;
                RedrawPicture();
                UpdateBubbleList();
                UpdateStatus("已添加气泡 #" + bubble.Label);
                return;
            }

            // 2) 点击选中气泡
            if (_parser != null && _picture.Image != null)
            {
                var size = _parser.GetPageSize(_currentPage);
                PointF pdfPt = PixelToPdfPoint(new PointF(e.X, e.Y), size);
                var bubbles = _pageBubbles[_currentPage];
                double scale = _renderScale * _zoomLevel;
                Bubble selected = null;
                double bestDist = double.MaxValue;
                foreach (var b in bubbles)
                {
                    double dx = b.Center.X - pdfPt.X;
                    double dy = b.Center.Y - pdfPt.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < b.Radius + 3 && dist < bestDist)
                    {
                        bestDist = dist;
                        selected = b;
                    }
                }
                if (selected != null)
                {
                    _selectedBubble = selected;
                    SelectBubbleInList(selected);
                }
            }
        }

        /// <summary>在气泡列表中选中指定气泡</summary>
        private void SelectBubbleInList(Bubble b)
        {
            foreach (ListViewItem item in _bubbleList.Items)
            {
                if (item.Tag == b)
                {
                    item.Selected = true;
                    item.EnsureVisible();
                    break;
                }
            }
        }

        private void RenumberBubbles()
        {
            int n = 1;
            foreach (var pageBubbles in _pageBubbles)
            {
                pageBubbles.Sort((a, b) =>
                {
                    int y = b.Center.Y.CompareTo(a.Center.Y);
                    if (y != 0) return y;
                    return a.Center.X.CompareTo(b.Center.X);
                });
                foreach (var b in pageBubbles) { b.Number = n; b.Label = n.ToString(); n++; }
            }
        }

        private void RenumberCurrentPage()
        {
            if (_parser == null) return;
            var bubbles = _pageBubbles[_currentPage];
            bubbles.Sort((a, b) =>
            {
                int y = b.Center.Y.CompareTo(a.Center.Y);
                if (y != 0) return y;
                return a.Center.X.CompareTo(b.Center.X);
            });
            int n = 1;
            foreach (var b in bubbles) { b.Number = n; b.Label = n.ToString(); n++; }
            RedrawPicture();
            UpdateBubbleList();
            UpdateStatus("已重新编号当前页共 " + bubbles.Count + " 个气泡");
        }

        private void DeleteSelectedBubble()
        {
            if (_parser == null) return;
            if (_selectedBubble == null)
            {
                MessageBox.Show("请先在右侧列表或图纸上选择一个气泡。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var bubbles = _pageBubbles[_currentPage];
            _undoRedo.Execute(new RemoveBubbleCommand(bubbles, _selectedBubble));
            _selectedBubble = null;
            RenumberBubbles();
            RedrawPicture();
            UpdateBubbleList();
            UpdateStatus("已删除气泡，剩余 " + bubbles.Count + " 个气泡");
        }

        // ============================================================
        // 坐标转换：PictureBox 像素坐标 -> PDF pt 坐标（左下原点，Y 向上）
        // 注意：PictureBox 为 AutoSize，所以图像实际大小 = Image.Size
        // ============================================================
        private PointF PixelToPdfPoint(PointF mousePt, SizeF pageSize)
        {
            if (_picture.Image == null) return PointF.Empty;
            int imgW = _picture.Image.Width;
            int imgH = _picture.Image.Height;
            float x = Math.Max(0, Math.Min(imgW, mousePt.X));
            float y = Math.Max(0, Math.Min(imgH, mousePt.Y));
            float pdfX = (x / (float)imgW) * pageSize.Width;
            float pdfY = (1.0f - y / (float)imgH) * pageSize.Height;
            return new PointF(pdfX, pdfY);
        }

        // ============================================================
        // 重绘 + 列表更新
        // ============================================================
        private void RedrawPicture()
        {
            if (_parser == null) return;
            try
            {
                var size = _parser.GetPageSize(_currentPage);
                double effectiveScale = _renderScale * _zoomLevel;
                var bmp = _parser.RenderPageToBitmap(_currentPage, effectiveScale);
                if (bmp == null) return;
                var bubbles = _pageBubbles[_currentPage];

                // 自绘气泡（使用主题颜色）
                DrawBubblesOnBitmap(bmp, bubbles, size, effectiveScale);

                var old = _picture.Image;
                _picture.Image = bmp;
                if (old != null) old.Dispose();

                UpdateBubbleCount();
            }
            catch (Exception ex)
            {
                UpdateStatus("渲染异常: " + ex.Message);
            }
        }

        /// <summary>自绘气泡（使用主题颜色）</summary>
        private void DrawBubblesOnBitmap(Bitmap bmp, List<Bubble> bubbles, SizeF pageSize, double scale)
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                foreach (var b in bubbles)
                {
                    float cx = (float)(b.Center.X * scale);
                    float cy = (float)(pageSize.Height * scale - b.Center.Y * scale);
                    float r = (float)(b.Radius * scale);
                    var rect = new RectangleF(cx - r, cy - r, 2 * r, 2 * r);

                    // 选中状态高亮
                    bool isSelected = (b == _selectedBubble);
                    var circleColor = isSelected ? Color.FromArgb(255, 120, 0) : Color.FromArgb(220, 60, 30);
                    var fillColor = isSelected ? Color.FromArgb(255, 220, 180) : Color.FromArgb(255, 240, 220);

                    using (var pen = new Pen(circleColor, Math.Max(2.0f, r * 0.18f)))
                    using (var brush = new SolidBrush(fillColor))
                    {
                        g.FillEllipse(brush, rect);
                        g.DrawEllipse(pen, rect);
                    }

                    // 编号文字（粗体）
                    using (var font = new Font("Arial", Math.Max(8f, r * 0.9f), FontStyle.Bold))
                    using (var textBrush = new SolidBrush(Color.Black))
                    {
                        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                        g.DrawString(b.Label, font, textBrush, rect, sf);
                    }

                    // 引线（气泡中心到被标注元素）
                    if (b.SourceBounds.Width > 0 && b.SourceBounds.Height > 0)
                    {
                        float tx = (float)(b.SourceBounds.CenterX * scale);
                        float ty = (float)(pageSize.Height * scale - b.SourceBounds.CenterY * scale);
                        using (var leaderPen = new Pen(Color.FromArgb(220, 60, 30), Math.Max(1.0f, r * 0.12f)))
                        {
                            leaderPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                            g.DrawLine(leaderPen, cx, cy, tx, ty);
                        }
                    }
                }
            }
        }

        private void UpdateBubbleList()
        {
            _bubbleList.Items.Clear();
            if (_parser == null) return;
            var bubbles = _pageBubbles[_currentPage];
            bubbles.Sort((a, b) => a.Number.CompareTo(b.Number));
            foreach (var b in bubbles)
            {
                var item = new ListViewItem(b.Label) { Tag = b };
                item.SubItems.Add(KindLabel(b.Kind));
                item.SubItems.Add(b.LinkedText ?? "");
                _bubbleList.Items.Add(item);
                if (b == _selectedBubble) item.Selected = true;
            }
            UpdateBubbleCount();
        }

        private void UpdateBubbleCount()
        {
            int total = TotalBubbles();
            _lblBubbleCount.Text = "气泡：" + total;
            _statusBubbles.Text = "气泡总数：" + total;
        }

        private void OnBubbleSelected(object sender, EventArgs e)
        {
            if (_bubbleList.SelectedItems.Count == 0)
            {
                _selectedBubble = null;
                ClearPropertyPanel();
                return;
            }
            var bubble = _bubbleList.SelectedItems[0].Tag as Bubble;
            _selectedBubble = bubble;
            LoadBubbleToPropertyPanel(bubble);
        }

        private void OnBubbleAfterLabelEdit(object sender, LabelEditEventArgs e)
        {
            if (e.Label == null) return;
            if (e.Item < 0 || e.Item >= _bubbleList.Items.Count) return;
            var bubble = _bubbleList.Items[e.Item].Tag as Bubble;
            if (bubble == null) return;
            bubble.Label = e.Label;
            int n; if (int.TryParse(e.Label, out n)) bubble.Number = n;
            RedrawPicture();
        }

        private void LoadBubbleToPropertyPanel(Bubble b)
        {
            _txtNum.Text = b.Label;
            _cmbType.SelectedIndex = (int)b.Kind;
            _txtLinkedText.Text = b.LinkedText ?? "";
            _txtNote.Text = b.IsManual ? "手动标注" : "AI 识别";
        }

        private void ClearPropertyPanel()
        {
            _txtNum.Text = "";
            _cmbType.SelectedIndex = -1;
            _txtLinkedText.Text = "";
            _txtNote.Text = "";
        }

        private void OnApplyPropertyChanges(object sender, EventArgs e)
        {
            if (_selectedBubble == null) return;
            // 编号
            if (!string.IsNullOrEmpty(_txtNum.Text))
            {
                _selectedBubble.Label = _txtNum.Text;
                int n;
                if (int.TryParse(_txtNum.Text, out n)) _selectedBubble.Number = n;
            }
            // 类型
            if (_cmbType.SelectedIndex >= 0)
            {
                _selectedBubble.Kind = (RecognitionKind)_cmbType.SelectedIndex;
            }
            RedrawPicture();
            UpdateBubbleList();
            UpdateStatus("已更新气泡 #" + _selectedBubble.Label);
        }

        // ============================================================
        // 缩放与快捷键
        // ============================================================
        private const double ZoomMin = 0.2;
        private const double ZoomMax = 5.0;
        private const double ZoomStep = 1.2;

        private void SetupZoomShortcuts()
        {
            this.KeyPreview = true;
            this.KeyDown += OnFormKeyDown;
        }

        private void OnFormKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && _pendingAddBubble)
            {
                _pendingAddBubble = false;
                _picture.Cursor = Cursors.Default;
                UpdateStatus("已取消添加气泡");
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Delete && _selectedBubble != null)
            {
                DeleteSelectedBubble();
                e.Handled = true;
                return;
            }
            if (e.Control)
            {
                if (e.KeyCode == Keys.Add || e.KeyCode == Keys.Oemplus) { ApplyZoom(_zoomLevel * ZoomStep); e.Handled = true; }
                else if (e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus) { ApplyZoom(_zoomLevel / ZoomStep); e.Handled = true; }
                else if (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0) { ApplyZoom(1.0); e.Handled = true; }
                else if (e.KeyCode == Keys.Z) { _undoRedo.Undo(); RedrawPicture(); UpdateBubbleList(); e.Handled = true; }
                else if (e.KeyCode == Keys.Y) { _undoRedo.Redo(); RedrawPicture(); UpdateBubbleList(); e.Handled = true; }
            }
            if (e.KeyCode == Keys.PageUp) { NavPage(-1); e.Handled = true; }
            if (e.KeyCode == Keys.PageDown) { NavPage(1); e.Handled = true; }
        }

        private void ApplyZoom(double newZoom)
        {
            if (newZoom < ZoomMin) newZoom = ZoomMin;
            if (newZoom > ZoomMax) newZoom = ZoomMax;
            _zoomLevel = newZoom;
            RedrawPicture();
            _lblZoom.Text = "缩放：" + _zoomLevel.ToString("P0");
            _statusZoom.Text = "缩放：" + _zoomLevel.ToString("P0");
        }

        private void SetupUndoRedo()
        {
            _undoRedo = new UndoRedoService();
            _undoRedo.StateChanged += () =>
            {
                _btnUndo.Enabled = _undoRedo.CanUndo;
                _btnRedo.Enabled = _undoRedo.CanRedo;
            };
            _btnUndo.Enabled = false;
            _btnRedo.Enabled = false;
        }

        private string KindLabel(RecognitionKind k)
        {
            switch (k)
            {
                case RecognitionKind.LinearDimension: return "线性尺寸";
                case RecognitionKind.GDTTolerance: return "形位公差";
                case RecognitionKind.Annotation: return "图纸注解";
                case RecognitionKind.Bubble: return "手动气泡";
            }
            return k.ToString();
        }

        // ============================================================
        // 翻页
        // ============================================================
        private void NavPage(int delta)
        {
            if (_parser == null) return;
            int next = _currentPage + delta;
            if (next < 0 || next >= _parser.PageCount) return;
            _currentPage = next;
            UpdatePageLabel();
            // 更新页面列表选中状态
            if (_currentPage >= 0 && _currentPage < _pageList.Items.Count)
            {
                _pageList.Items[_currentPage].Selected = true;
                _pageList.Items[_currentPage].EnsureVisible();
            }
            RedrawPicture();
            UpdateBubbleList();
            _selectedBubble = null;
        }

        private void UpdatePageLabel()
        {
            if (_parser == null) { _lblPage.Text = "页：- / -"; _statusPage.Text = "页：- / -"; }
            else
            {
                _lblPage.Text = "页：" + (_currentPage + 1) + " / " + _parser.PageCount;
                _statusPage.Text = "页：" + (_currentPage + 1) + " / " + _parser.PageCount;
            }
        }

        // ============================================================
        // 状态栏更新
        // ============================================================
        private void UpdateStatus(string message)
        {
            _statusMessage.Text = message;
            if (_currentPdf != null) _statusFile.Text = "文件：" + Path.GetFileName(_currentPdf);
            UpdatePageLabel();
            UpdateBubbleCount();
        }

        // ============================================================
        // 导出：PDF / PNG / Excel
        // ============================================================
        private void OnExportPdf(object sender, EventArgs e)
        {
            if (_parser == null) { MessageBox.Show("请先打开 PDF", "提示"); return; }
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "PDF files (*.pdf)|*.pdf";
                dlg.FileName = Path.GetFileNameWithoutExtension(_currentPdf) + "_annotated.pdf";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                var exporter = new PdfExporter();
                var pages = new List<ExportPage>();
                try
                {
                    for (int i = 0; i < _parser.PageCount; i++)
                    {
                        var size = _parser.GetPageSize(i);
                        var bmp = _parser.RenderPageToBitmap(i, _renderScale);
                        pages.Add(new ExportPage
                        {
                            PageIndex = i,
                            PageSize = size,
                            Background = bmp,
                            Bubbles = _pageBubbles[i]
                        });
                    }
                    exporter.Export(pages, dlg.FileName);
                    foreach (var p in pages) if (p.Background != null) p.Background.Dispose();
                    UpdateStatus("已导出 PDF: " + dlg.FileName);
                    MessageBox.Show("PDF 导出完成:\n" + dlg.FileName, "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("导出失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnExportPng(object sender, EventArgs e)
        {
            if (_parser == null) { MessageBox.Show("请先打开 PDF", "提示"); return; }
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择 PNG 保存文件夹";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    for (int i = 0; i < _parser.PageCount; i++)
                    {
                        var size = _parser.GetPageSize(i);
                        using (var bmp = _parser.RenderPageToBitmap(i, _renderScale))
                        {
                            DrawBubblesOnBitmap(bmp, _pageBubbles[i], size, _renderScale);
                            string path = Path.Combine(dlg.SelectedPath,
                                string.Format("page_{0:D4}.png", i + 1));
                            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                    UpdateStatus("已导出 PNG 到 " + dlg.SelectedPath);
                    MessageBox.Show("PNG 导出完成:\n" + dlg.SelectedPath, "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("导出失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnExportExcel(object sender, EventArgs e)
        {
            if (_parser == null || _pageBubbles == null) { MessageBox.Show("请先处理 PDF", "提示"); return; }
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "Excel files (*.xlsx)|*.xlsx";
                dlg.FileName = Path.GetFileNameWithoutExtension(_currentPdf) + "_bubbles.xlsx";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                var flat = new List<Bubble>();
                foreach (var list in _pageBubbles) flat.AddRange(list);
                try
                {
                    new ExcelExporter().Export(flat, dlg.FileName, _currentPdf);
                    UpdateStatus("已导出 Excel: " + dlg.FileName);
                    MessageBox.Show("Excel 导出完成:\n" + dlg.FileName, "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("导出失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
