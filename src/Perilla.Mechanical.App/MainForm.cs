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
    /// 主窗体：
    /// 上侧：工具栏（打开 PDF、自动处理、导出 PDF / PNG / Excel、翻页）
    /// 中央：图片框（承载已渲染的 PDF 页面 + 叠加气泡）+ 右侧气泡列表
    /// 状态：当前文件名、页号、识别到的气泡数量
    /// </summary>
    public class MainForm : Form
    {
        private ToolStrip _toolbar;
        private ToolStripButton _btnOpen;
        private ToolStripButton _btnAuto;
        private ToolStripButton _btnAddBubble;
        private ToolStripButton _btnExportPdf;
        private ToolStripButton _btnExportPng;
        private ToolStripButton _btnExportExcel;
        private ToolStripButton _btnUndo;
        private ToolStripButton _btnRedo;
        private ToolStripLabel _lblPage;
        private ToolStripButton _btnPrev;
        private ToolStripButton _btnNext;

        private Panel _leftPanel;
        private PictureBox _picture;
        private Panel _rightPanel;
        private ListView _bubbleList;
        private StatusStrip _status;
        private ToolStripStatusLabel _statusLabel;

        private string _currentPdf;
        private PdfParsingService _parser;
        private List<List<Bubble>> _pageBubbles;     // 每页的气泡列表
        private List<int> _pageNumbers;
        private int _currentPage;
        private double _renderScale = 2.0;
        private UndoRedoService _undoRedo;

        public MainForm()
        {
            Text = "Perilla Mechanical PDF Editor - 机械设计图纸专用 PDF 标注工具";
            Width = 1280;
            Height = 800;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei", 9);

            BuildUi();
            SetupZoomShortcuts();
            SetupUndoRedo();

            _pageBubbles = new List<List<Bubble>>();
            _pageNumbers = new List<int>();
        }

        private void BuildUi()
        {
            // ==== Toolbar ====
            _toolbar = new ToolStrip();
            _toolbar.GripStyle = ToolStripGripStyle.Hidden;
            _toolbar.ImageScalingSize = new Size(24, 24);

            _btnOpen = new ToolStripButton("打开 PDF");
            _btnOpen.Click += OnOpenPdf;

            _btnAuto = new ToolStripButton("自动识别并编号");
            _btnAuto.Click += OnRunAutomation;

            _btnAddBubble = new ToolStripButton("手动添加气泡");
            _btnAddBubble.Click += OnAddManualBubble;

            _btnExportPdf = new ToolStripButton("导出 PDF");
            _btnExportPdf.Click += OnExportPdf;

            _btnExportPng = new ToolStripButton("导出 PNG");
            _btnExportPng.Click += OnExportPng;

            _btnExportExcel = new ToolStripButton("导出 Excel");
            _btnExportExcel.Click += OnExportExcel;

            _btnUndo = new ToolStripButton("撤销");
            _btnUndo.Click += (s, e) => _undoRedo.Undo();
            _btnRedo = new ToolStripButton("重做");
            _btnRedo.Click += (s, e) => _undoRedo.Redo();

            _lblPage = new ToolStripLabel("页：- / -");
            _btnPrev = new ToolStripButton("◀");
            _btnPrev.Click += (s, e) => NavPage(-1);
            _btnNext = new ToolStripButton("▶");
            _btnNext.Click += (s, e) => NavPage(1);

            _toolbar.Items.AddRange(new ToolStripItem[] {
                _btnOpen, new ToolStripSeparator(),
                _btnAuto, _btnAddBubble, new ToolStripSeparator(),
                _btnUndo, _btnRedo, new ToolStripSeparator(),
                _btnExportPdf, _btnExportPng, _btnExportExcel, new ToolStripSeparator(),
                _btnPrev, _lblPage, _btnNext
            });

            // ==== Left panel (picture box) ====
            _leftPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(240, 240, 240) };
            _picture = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            _picture.MouseClick += OnPictureClick;
            _leftPanel.Controls.Add(_picture);

            // ==== Right panel (list) ====
            _rightPanel = new Panel { Dock = DockStyle.Right, Width = 320, BackColor = Color.White };
            var rightTitle = new Label
            {
                Text = "气泡列表（可双击编辑编号）",
                Dock = DockStyle.Top,
                Height = 28,
                BackColor = Color.FromArgb(245, 245, 245),
                Font = new Font("Microsoft YaHei", 9, FontStyle.Bold)
            };
            _bubbleList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                LabelEdit = true
            };
            _bubbleList.Columns.Add("#", 40);
            _bubbleList.Columns.Add("类型", 90);
            _bubbleList.Columns.Add("原文本", 120);
            _bubbleList.AfterLabelEdit += OnBubbleAfterLabelEdit;
            _bubbleList.SelectedIndexChanged += (s, e) => RedrawPicture();

            _rightPanel.Controls.Add(_bubbleList);
            _rightPanel.Controls.Add(rightTitle);

            // ==== Status ====
            _status = new StatusStrip();
            _statusLabel = new ToolStripStatusLabel("就绪 - 请先打开一个 PDF 图纸") { Spring = true };
            _status.Items.Add(_statusLabel);

            Controls.Add(_leftPanel);
            Controls.Add(_rightPanel);
            Controls.Add(_toolbar);
            Controls.Add(_status);
        }

        // ============================================================
        // 打开 PDF
        // ============================================================
        private void OnOpenPdf(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*";
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    OpenPdf(dlg.FileName);
                }
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
                UpdatePageLabel();
                RedrawPicture();
                _statusLabel.Text = "已打开 " + Path.GetFileName(path) + " (" + _parser.PageCount + " 页)";
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开 PDF 失败: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ============================================================
        // 自动识别与编号
        // ============================================================
        private void OnRunAutomation(object sender, EventArgs e)
        {
            if (_parser == null)
            {
                MessageBox.Show("请先打开 PDF 文件。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                var svc = new AutomatedProcessingService(_parser,
                    new DrawingRecognitionService(), new BubbleNumberingService());

                var existingManualBubbles = new List<Bubble>();
                foreach (var bubbles in _pageBubbles)
                {
                    foreach (var b in bubbles) if (b.IsManual) existingManualBubbles.Add(b);
                }

                // 保存撤销前的自动气泡状态
                var previousAutoBubbles = new List<List<Bubble>>();
                foreach (var bubbles in _pageBubbles)
                {
                    var copy = new List<Bubble>();
                    foreach (var b in bubbles) if (!b.IsManual) copy.Add(b);
                    previousAutoBubbles.Add(copy);
                }

                var opts = new AutoProcessingOptions { Numbering = new NumberingOptions() };
                var results = svc.Run(_currentPdf, opts, existingManualBubbles,
                    msg => _statusLabel.Text = msg);

                // 构建新的气泡列表（仅自动气泡）
                var newBubbles = new List<List<Bubble>>();
                foreach (var r in results)
                {
                    var list = new List<Bubble>();
                    foreach (var b in r.Bubbles) if (!b.IsManual) list.Add(b);
                    newBubbles.Add(list);
                }

                // 使用命令模式执行，支持撤销
                _undoRedo.Execute(new AutoRecognizeCommand(_pageBubbles, previousAutoBubbles, newBubbles));

                _statusLabel.Text = string.Format(
                    "自动处理完成 - 共 {0} 页，{1} 个气泡",
                    results.Count, TotalBubbles());

                RedrawPicture();
                UpdateBubbleList();
            }
            catch (Exception ex)
            {
                MessageBox.Show("自动处理失败: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            MessageBox.Show("请在左侧图纸图片上单击位置以放置气泡。",
                "添加气泡", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _pendingAddBubble = true;
            _picture.Cursor = Cursors.Cross;
        }

        private bool _pendingAddBubble;

        private void OnPictureClick(object sender, MouseEventArgs e)
        {
            if (!_pendingAddBubble) return;
            if (_parser == null) return;

            // 将鼠标点击转换为 PDF 坐标
            var size = _parser.GetPageSize(_currentPage);
            if (size.Width <= 0 || size.Height <= 0 || _picture.Image == null) return;

            PointF pdfPt = PictureToPdf(new PointF(e.X, e.Y), size);

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
        }

        private void RenumberBubbles()
        {
            // 全页重新编号
            int n = 1;
            var list = new List<Bubble>();
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

        /// <summary>
        /// 将鼠标在 PictureBox 中的像素坐标转换为 PDF pt 坐标（左下为原点，Y 向上）。
        /// </summary>
        private PointF PictureToPdf(PointF mousePt, SizeF pageSize)
        {
            if (_picture.Image == null) return PointF.Empty;
            Rectangle drawRect = GetDrawRectangle();

            // 1. 计算鼠标相对图像显示矩形的归一化位置 [0..1]
            float nx = (drawRect.Width <= 0) ? 0.5f : (mousePt.X - drawRect.X) / (float)drawRect.Width;
            float ny = (drawRect.Height <= 0) ? 0.5f : (mousePt.Y - drawRect.Y) / (float)drawRect.Height;
            nx = Math.Max(0, Math.Min(1, nx));
            ny = Math.Max(0, Math.Min(1, ny));

            // 2. 归一化位置 → PDF pt 坐标（Y 翻转）
            float pdfX = nx * pageSize.Width;
            float pdfY = (1.0f - ny) * pageSize.Height;
            return new PointF(pdfX, pdfY);
        }

        /// <summary>
        /// PictureBoxSizeMode.Zoom 下，计算图像实际显示矩形（居中 + 等比缩放）。
        /// </summary>
        private Rectangle GetDrawRectangle()
        {
            if (_picture.Image == null) return Rectangle.Empty;
            Size img = _picture.Image.Size;
            Size cw = _picture.ClientSize;
            if (img.Width == 0 || img.Height == 0) return Rectangle.Empty;

            float ri = (float)img.Width / img.Height;
            float rc = (float)cw.Width / cw.Height;
            int w, h;
            if (ri < rc)
            {
                h = cw.Height;
                w = (int)(h * ri);
            }
            else
            {
                w = cw.Width;
                h = (int)(w / ri);
            }
            int x = (cw.Width - w) / 2;
            int y = (cw.Height - h) / 2;
            return new Rectangle(x, y, w, h);
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
                var exporter = new ImageExporter();
                exporter.DrawBubblesOnBitmap(bmp, bubbles, size, effectiveScale);
                var old = _picture.Image;
                _picture.Image = bmp;
                if (old != null) old.Dispose();
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "渲染异常: " + ex.Message;
            }
        }

        private void UpdateBubbleList()
        {
            _bubbleList.Items.Clear();
            if (_parser == null) return;
            var bubbles = _pageBubbles[_currentPage];
            foreach (var b in bubbles)
            {
                var item = new ListViewItem(b.Label) { Tag = b };
                item.SubItems.Add(KindLabel(b.Kind));
                item.SubItems.Add(b.LinkedText ?? "");
                _bubbleList.Items.Add(item);
            }
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

        // ============================================================
        // 缩放功能：Ctrl + +/- / 鼠标滚轮 / Ctrl + 0 还原
        // ============================================================
        private double _zoomLevel = 1.0;
        private const double ZoomMin = 0.2;
        private const double ZoomMax = 5.0;
        private const double ZoomStep = 1.2;

        private void SetupZoomShortcuts()
        {
            this.KeyPreview = true;
            this.KeyDown += OnFormKeyDown;
            _picture.MouseWheel += OnPictureMouseWheel;
        }

        private void OnFormKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control)
            {
                if (e.KeyCode == Keys.Add || e.KeyCode == Keys.Oemplus)
                {
                    ApplyZoom(_zoomLevel * ZoomStep);
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus)
                {
                    ApplyZoom(_zoomLevel / ZoomStep);
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.D0 || e.KeyCode == Keys.NumPad0)
                {
                    ApplyZoom(1.0);
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Z)
                {
                    _undoRedo.Undo();
                    RedrawPicture();
                    UpdateBubbleList();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Y)
                {
                    _undoRedo.Redo();
                    RedrawPicture();
                    UpdateBubbleList();
                    e.Handled = true;
                }
            }
        }

        private void OnPictureMouseWheel(object sender, MouseEventArgs e)
        {
            if (Control.ModifierKeys == Keys.Control)
            {
                if (e.Delta > 0)
                    ApplyZoom(_zoomLevel * ZoomStep);
                else
                    ApplyZoom(_zoomLevel / ZoomStep);
            }
            else
            {
                // 无 Ctrl 时滚轮用于翻页
                if (e.Delta > 0)
                    NavPage(-1);
                else
                    NavPage(1);
            }
        }

        private void ApplyZoom(double newZoom)
        {
            if (newZoom < ZoomMin) newZoom = ZoomMin;
            if (newZoom > ZoomMax) newZoom = ZoomMax;
            _zoomLevel = newZoom;
            RedrawPicture();
            _statusLabel.Text = string.Format("缩放: {0:P0}", _zoomLevel);
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
            RedrawPicture();
            UpdateBubbleList();
        }

        private void UpdatePageLabel()
        {
            if (_parser == null) _lblPage.Text = "页：- / -";
            else _lblPage.Text = string.Format("页：{0} / {1}", _currentPage + 1, _parser.PageCount);
        }

        // ============================================================
        // 导出：PDF / PNG / Excel
        // ============================================================
        private void OnExportPdf(object sender, EventArgs e)
        {
            if (_parser == null) { MessageBox.Show("请先打开 PDF"); return; }
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
                    _statusLabel.Text = "已导出 PDF: " + dlg.FileName;
                    MessageBox.Show("PDF 导出完成:\n" + dlg.FileName, "成功",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("导出失败: " + ex.Message, "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnExportPng(object sender, EventArgs e)
        {
            if (_parser == null) { MessageBox.Show("请先打开 PDF"); return; }
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择 PNG 保存文件夹";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                var exporter = new ImageExporter();
                try
                {
                    for (int i = 0; i < _parser.PageCount; i++)
                    {
                        var size = _parser.GetPageSize(i);
                        using (var bmp = _parser.RenderPageToBitmap(i, _renderScale))
                        {
                            exporter.DrawBubblesOnBitmap(bmp, _pageBubbles[i], size, _renderScale);
                            string path = Path.Combine(dlg.SelectedPath,
                                string.Format("page_{0:D4}.png", i + 1));
                            bmp.Save(path);
                        }
                    }
                    _statusLabel.Text = "已导出 PNG 到 " + dlg.SelectedPath;
                    MessageBox.Show("PNG 导出完成:\n" + dlg.SelectedPath, "成功",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("导出失败: " + ex.Message, "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnExportExcel(object sender, EventArgs e)
        {
            if (_parser == null || _pageBubbles == null) { MessageBox.Show("请先处理 PDF"); return; }
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
                    _statusLabel.Text = "已导出 Excel: " + dlg.FileName;
                    MessageBox.Show("Excel 导出完成:\n" + dlg.FileName, "成功",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("导出失败: " + ex.Message, "错误",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
