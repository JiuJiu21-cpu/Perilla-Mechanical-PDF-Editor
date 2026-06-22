using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Perilla.Mechanical.Core.Models;
using PdfiumViewer;

namespace Perilla.Mechanical.Core.Pdf
{
    /// <summary>
    /// 基于 PdfiumViewer 托管 API 的 PDF 解析/渲染服务。
    /// 打开文档 → 获取页面尺寸 → 提取文本条目 / 近似路径 → 渲染页面位图。
    /// 实现 IDisposable 以正确释放非托管资源。
    /// </summary>
    public class PdfParsingService : IDisposable
    {
        private PdfDocument _document;
        private string _filePath;
        private bool _disposed;

        public int PageCount
        {
            get { return _document == null ? 0 : _document.PageCount; }
        }

        public string FilePath { get { return _filePath; } }

        public void Load(string pdfPath)
        {
            if (string.IsNullOrWhiteSpace(pdfPath))
                throw new ArgumentException("PDF 路径不能为空", "pdfPath");
            if (!File.Exists(pdfPath))
                throw new FileNotFoundException("PDF 文件不存在", pdfPath);

            Dispose(); // 释放旧的 document

            try
            {
                _document = PdfDocument.Load(pdfPath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("无法打开 PDF: " + pdfPath, ex);
            }
            _filePath = pdfPath;
        }

        public SizeF GetPageSize(int pageIndex)
        {
            EnsureOpen();
            if (pageIndex < 0 || pageIndex >= PageCount)
                throw new ArgumentOutOfRangeException("pageIndex");
            return _document.PageSizes[pageIndex];
        }

        /// <summary>
        /// 将页面渲染为 GDI+ 位图。返回的位图使用 Format32bppArgb，
        /// 自上而下排列，调用方负责 Dispose。
        /// </summary>
        public Bitmap RenderPageToBitmap(int pageIndex, double scale = 2.0)
        {
            EnsureOpen();
            if (pageIndex < 0 || pageIndex >= PageCount)
                throw new ArgumentOutOfRangeException("pageIndex");
            if (scale <= 0) scale = 1.0;

            SizeF sz = _document.PageSizes[pageIndex];
            int w = Math.Max(1, (int)Math.Ceiling(sz.Width * scale));
            int h = Math.Max(1, (int)Math.Ceiling(sz.Height * scale));

            // PdfiumViewer 提供 Render(int page, int dpiX, int dpiY, ...) 重载，
            // 其中 dpi 单位为每英寸点数；PDF 单位为 pt，1 pt = 1/72 英寸，
            // 所以 dpi = scale * 72 可以产生精确的 scale 效果。
            int dpi = (int)Math.Ceiling(scale * 72.0);
            try
            {
                using (Image img = _document.Render(pageIndex, dpi, dpi, false))
                {
                    // img 可能是任何像素格式；强制转换为可靠的 ARGB32
                    Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.Clear(Color.White);
                        g.DrawImageUnscaled(img, 0, 0);
                    }
                    return bmp;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("渲染 PDF 页面失败: " + pageIndex, ex);
            }
        }

        /// <summary>
        /// 提取文本条目。利用 PdfiumViewer 的 GetPdfText 获取纯文本，
        /// 再结合我们的轻量级字符级边界 API 生成带坐标的文本项。
        /// </summary>
        public List<TextPrimitive> ExtractTexts(int pageIndex)
        {
            EnsureOpen();
            if (pageIndex < 0 || pageIndex >= PageCount)
                throw new ArgumentOutOfRangeException("pageIndex");

            var result = new List<TextPrimitive>();
            SizeF pageSize = _document.PageSizes[pageIndex];

            // 获取整页文本作为后备
            string fullText = SafeGetText(pageIndex);

            // 尝试使用 PdfiumNative 获取细粒度的字符框
            IntPtr doc = IntPtr.Zero;
            IntPtr page = IntPtr.Zero;
            IntPtr textPage = IntPtr.Zero;
            try
            {
                doc = PdfiumNative.FPDF_LoadDocument(_filePath, null);
                if (doc == IntPtr.Zero) return FallbackTextList(fullText, pageSize, pageIndex);

                page = PdfiumNative.FPDF_LoadPage(doc, pageIndex);
                if (page == IntPtr.Zero) return FallbackTextList(fullText, pageSize, pageIndex);

                textPage = PdfiumNative.FPDFText_LoadPage(page);
                if (textPage == IntPtr.Zero) return FallbackTextList(fullText, pageSize, pageIndex);

                int n = PdfiumNative.FPDFText_CountChars(textPage);
                if (n <= 0) return result;

                // 收集所有字符
                var chars = new List<CharInfo>(n);
                for (int i = 0; i < n; i++)
                {
                    uint ch = PdfiumNative.FPDFText_GetUnicode(textPage, i);
                    if (ch == 0) continue;
                    double l, b, r, t;
                    int ok = PdfiumNative.FPDFText_GetCharBox(textPage, i, out l, out b, out r, out t);
                    double fs = PdfiumNative.FPDFText_GetFontSize(textPage, i);
                    if (ok == 0) continue;
                    chars.Add(new CharInfo { Ch = (char)ch, Left = l, Bottom = b, Right = r, Top = t, FontSize = fs });
                }

                // 将连续字符聚合为词：
                // - 同基线（Bottom 接近）
                // - 水平间距合理
                bool[] used = new bool[chars.Count];
                for (int i = 0; i < chars.Count; i++)
                {
                    if (used[i] || char.IsWhiteSpace(chars[i].Ch)) continue;
                    int start = i;
                    double minX = chars[i].Left, maxX = chars[i].Right;
                    double minY = chars[i].Bottom, maxY = chars[i].Top;
                    double fontSize = chars[i].FontSize;
                    var sb = new System.Text.StringBuilder();
                    sb.Append(chars[i].Ch);
                    used[i] = true;
                    int j = i + 1;
                    while (j < chars.Count)
                    {
                        if (used[j]) { j++; continue; }
                        char cj = chars[j].Ch;
                        if (char.IsWhiteSpace(cj)) { j++; continue; }
                        double gap = chars[j].Left - chars[j - 1].Right;
                        double avgCharW = Math.Max(1.0, (chars[j - 1].Right - chars[j - 1].Left + chars[j].Right - chars[j].Left) * 0.5);
                        if (gap > Math.Max(avgCharW * 1.5, fontSize * 0.6)) break;
                        if (Math.Abs(chars[j].Bottom - chars[j - 1].Bottom) > Math.Max(2.0, fontSize * 0.3)) break;

                        sb.Append(cj);
                        if (chars[j].Left < minX) minX = chars[j].Left;
                        if (chars[j].Right > maxX) maxX = chars[j].Right;
                        if (chars[j].Bottom < minY) minY = chars[j].Bottom;
                        if (chars[j].Top > maxY) maxY = chars[j].Top;
                        used[j] = true;
                        j++;
                    }

                    string text = sb.ToString().Trim();
                    if (text.Length > 0)
                    {
                        result.Add(new TextPrimitive
                        {
                            PageIndex = pageIndex,
                            Bounds = new RectD(minX, minY, Math.Max(1.0, maxX - minX), Math.Max(1.0, maxY - minY)),
                            Text = text,
                            FontSizePt = fontSize,
                            FontName = "",
                            LooksNumeric = LooksNumeric(text)
                        });
                    }
                }
            }
            catch
            {
                // P/Invoke 层出问题时回退到纯文本方案
                return FallbackTextList(fullText, pageSize, pageIndex);
            }
            finally
            {
                if (textPage != IntPtr.Zero) PdfiumNative.FPDFText_ClosePage(textPage);
                if (page != IntPtr.Zero) PdfiumNative.FPDF_ClosePage(page);
                if (doc != IntPtr.Zero) PdfiumNative.FPDF_CloseDocument(doc);
            }
            return result;
        }

        private string SafeGetText(int pageIndex)
        {
            try { return _document.GetPdfText(pageIndex) ?? ""; }
            catch { return ""; }
        }

        /// <summary>当精细的字符框 API 不可用时的回退方案：将整页文本按空格拆开，
        /// 并给予简单的默认包围盒。这个方案的坐标精度不高，但可以避免识别流程中断。</summary>
        private List<TextPrimitive> FallbackTextList(string text, SizeF pageSize, int pageIndex)
        {
            var list = new List<TextPrimitive>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            string[] tokens = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            double y = pageSize.Height - 20;
            double x = 10;
            double fontSize = 10;
            foreach (string token in tokens)
            {
                double w = Math.Max(1.0, token.Length * fontSize * 0.6);
                if (x + w > pageSize.Width) { x = 10; y -= fontSize * 1.5; }
                list.Add(new TextPrimitive
                {
                    PageIndex = pageIndex,
                    Bounds = new RectD(x, y - fontSize, w, fontSize * 1.2),
                    Text = token,
                    FontSizePt = fontSize,
                    FontName = "",
                    LooksNumeric = LooksNumeric(token)
                });
                x += w + fontSize * 0.3;
            }
            return list;
        }

        /// <summary>
        /// 提取"路径形状"用于识别尺寸线/几何框。由于 PdfiumViewer 没有直接暴露
        /// 路径对象枚举，这里以页内文本项的边界框聚合并标记为候选矩形。
        /// 这是一个务实的启发式方案，足以驱动标注气泡的自动放置。
        /// </summary>
        public List<GraphicPrimitive> ExtractPathsAndRects(int pageIndex)
        {
            var list = new List<GraphicPrimitive>();
            var texts = ExtractTexts(pageIndex);
            SizeF size = GetPageSize(pageIndex);

            // 为每个"看起来像数字"的文本生成一个扩展的候选矩形/水平线，
            // 模拟尺寸线布局：在文本左右两侧各延伸一个长条，供识别引擎使用。
            foreach (var t in texts)
            {
                double extW = Math.Max(t.Bounds.Width * 1.5, 20);
                double cx = t.Bounds.CenterX;
                double cy = t.Bounds.CenterY;

                list.Add(new LinePrimitive
                {
                    PageIndex = pageIndex,
                    Start = new PointD(cx - extW * 0.5, cy),
                    End = new PointD(cx + extW * 0.5, cy),
                    Width = 0.5,
                    Bounds = new RectD(cx - extW * 0.5, cy - 1, extW, 2)
                });
                list.Add(new LinePrimitive
                {
                    PageIndex = pageIndex,
                    Start = new PointD(cx, cy - extW * 0.5),
                    End = new PointD(cx, cy + extW * 0.5),
                    Width = 0.5,
                    Bounds = new RectD(cx - 1, cy - extW * 0.5, 2, extW)
                });

                // 文本自身作为一个矩形 primitive（便于 GDT 识别器将包含数字的
                // 文本块视作潜在几何公差框）
                list.Add(new RectPrimitive
                {
                    PageIndex = pageIndex,
                    IsClosed = true,
                    Bounds = t.Bounds.InflatedBy(4)
                });
            }
            return list;
        }

        private static bool LooksNumeric(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            int digitCount = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsDigit(text[i])) digitCount++;
            }
            // 包含至少一个数字或典型工程符号才视为与尺寸相关
            if (digitCount > 0) return true;
            foreach (char c in text)
            {
                if (c >= 0x2205 && c <= 0x2205) return true; // 直径符号
            }
            return false;
        }

        private void EnsureOpen()
        {
            if (_document == null) throw new InvalidOperationException("PDF 文档尚未加载，请先调用 Load()");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing && _document != null)
                {
                    try { _document.Dispose(); }
                    catch { /* 忽略 Dispose 期间的异常 */ }
                    _document = null;
                }
                _disposed = true;
            }
        }

        ~PdfParsingService() { Dispose(false); }

        private class CharInfo
        {
            public char Ch;
            public double Left, Right, Top, Bottom;
            public double FontSize;
        }
    }
}
