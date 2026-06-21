using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Text;
using Perilla.Mechanical.Core.Models;

namespace Perilla.Mechanical.Export
{
    /// <summary>
    /// 将页面位图 + 气泡导出为 PDF（无第三方 PDF 库依赖）。
    /// - 位图作为 Image XObject（Flate 压缩，RGB 8bpc）
    /// - 气泡采用 PDF 原语绘制：红色圆圈 + 数字 + 引线
    /// - 字体使用标准 14 种 Type1 字体：Helvetica（无需嵌入）
    /// </summary>
    public class PdfExporter
    {
        public void Export(List<ExportPage> pages, string outputPath)
        {
            if (pages == null) throw new ArgumentNullException("pages");
            if (string.IsNullOrEmpty(outputPath))
                throw new ArgumentException("outputPath 不能为空");

            using (var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            {
                var builder = new PdfBuilder(stream);
                builder.Build(pages);
            }
        }

        private class PdfBuilder
        {
            private readonly Stream _s;
            private readonly List<long> _xref = new List<long>();
            private int _nextObj;

            public PdfBuilder(Stream s)
            {
                _s = s;
                _xref.Add(0); // 对象 0 作为占位（空闲对象）
                _nextObj = 1;
            }

            public void Build(List<ExportPage> pages)
            {
                // 1) 写 PDF 头
                WriteLine("%PDF-1.4");
                WriteBytes(new byte[] { 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A }); // 二进制标识

                // 2) 对象 1：/F1 字体（Helvetica）
                int fontObj = NewFontObject();

                // 3) 每一页：图像 XObject → 内容流 → Page 对象
                int[] pageObjIds = new int[pages.Count];
                for (int i = 0; i < pages.Count; i++)
                {
                    int imageObj = NewImageXObject(pages[i].Background);
                    int contentObj = NewPageContent(pages[i], imageObj, fontObj);
                    int pageObj = NewPageObject(pages[i].PageSize, contentObj, imageObj, fontObj);
                    pageObjIds[i] = pageObj;
                }

                // 4) Pages 对象（注意：pages[i].Parent 引用这个对象，我们固定它为 _nextObj）
                int pagesObj = NewPagesObject(pageObjIds);

                // 5) Catalog
                int catalogObj = NewCatalog(pagesObj);

                // 6) xref + trailer
                WriteXrefAndTrailer(catalogObj);
            }

            private int NewFontObject()
            {
                int objId = _nextObj++;
                _xref.Add(GetPos());
                WriteLine("{0} 0 obj", objId);
                WriteLine("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
                WriteLine("endobj");
                return objId;
            }

            private int NewImageXObject(Image image)
            {
                int objId = _nextObj++;

                if (image == null)
                {
                    // 写一个 1x1 白色图像
                    _xref.Add(GetPos());
                    WriteLine("{0} 0 obj", objId);
                    WriteLine("<< /Type /XObject /Subtype /Image /Width 1 /Height 1");
                    WriteLine("   /ColorSpace /DeviceGray /BitsPerComponent 1");
                    WriteLine("   /Filter /FlateDecode /Length 6 >>");
                    WriteLine("stream");
                    // Deflate 编码的 8 个零位（一行 1 像素 = 1 字节）
                    byte[] deflated = Deflate(new byte[] { 0x00 });
                    _s.Write(deflated, 0, deflated.Length);
                    WriteLine("");
                    WriteLine("endstream");
                    WriteLine("endobj");
                    return objId;
                }

                int width = image.Width;
                int height = image.Height;

                // 将图像转换为 24bpp RGB
                byte[] rgb;
                using (var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.White);
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImageUnscaled(image, 0, 0);
                    }
                    // 读取像素并转换为 RGB（自上而下）
                    var rect = new Rectangle(0, 0, width, height);
                    BitmapData bd = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                    try
                    {
                        int rowLen = width * 3;
                        int srcStride = bd.Stride;
                        rgb = new byte[rowLen * height];
                        IntPtr scan0 = bd.Scan0;
                        for (int y = 0; y < height; y++)
                        {
                            byte[] line = new byte[rowLen];
                            System.Runtime.InteropServices.Marshal.Copy(
                                new IntPtr(scan0.ToInt64() + y * srcStride), line, 0, rowLen);
                            // BGR → RGB
                            for (int x = 0; x < width; x++)
                            {
                                byte bv = line[x * 3];
                                line[x * 3] = line[x * 3 + 2];
                                line[x * 3 + 2] = bv;
                            }
                            Buffer.BlockCopy(line, 0, rgb, y * rowLen, rowLen);
                        }
                    }
                    finally
                    {
                        bmp.UnlockBits(bd);
                    }
                }

                byte[] compressed = Deflate(rgb);

                _xref.Add(GetPos());
                WriteLine("{0} 0 obj", objId);
                WriteLine("<< /Type /XObject /Subtype /Image /Width {0} /Height {1}", width, height);
                WriteLine("   /ColorSpace /DeviceRGB /BitsPerComponent 8");
                WriteLine("   /Filter /FlateDecode /Length {0} >>", compressed.Length);
                WriteLine("stream");
                _s.Write(compressed, 0, compressed.Length);
                WriteLine("");
                WriteLine("endstream");
                WriteLine("endobj");
                return objId;
            }

            private int NewPageContent(ExportPage page, int imageObj, int fontObj)
            {
                int objId = _nextObj++;

                var sb = new StringBuilder();
                double pageW = Math.Max(1, page.PageSize.Width);
                double pageH = Math.Max(1, page.PageSize.Height);

                // 内容流：
                // 1. 将图像缩放到整个页面（使用 cm 矩阵）并绘制
                //    Do 命令默认使用图像默认大小（1x1 用户单位），所以用矩阵
                //    [pageW 0 0 pageH 0 0] 将图像尺寸 1×1 放大到页面尺寸
                sb.Append("q\n");
                sb.AppendFormat("{0:0.##} 0 0 {1:0.##} 0 0 cm\n/Im{2} Do\nQ\n",
                    pageW, pageH, imageObj);

                // 2. 绘制气泡图形（圆 + 引线）
                if (page.Bubbles != null && page.Bubbles.Count > 0)
                {
                    sb.Append("q\n1 0 0 RG\n1.5 w\n"); // 红色描边，1.5 pt 线宽
                    foreach (var b in page.Bubbles)
                    {
                        double cx = b.Center.X;
                        double cy = b.Center.Y;
                        double r = Math.Max(1.0, b.Radius);
                        double k = 0.552284749831 * r;

                        // 圆：起点 -> 四段贝塞尔 -> 闭合 -> 描边
                        sb.AppendFormat("{0:0.##} {1:0.##} m\n", cx + r, cy);
                        sb.AppendFormat("{0:0.##} {1:0.##} {2:0.##} {3:0.##} {4:0.##} {5:0.##} c\n",
                            cx + r, cy + k, cx + k, cy + r, cx, cy + r);
                        sb.AppendFormat("{0:0.##} {1:0.##} {2:0.##} {3:0.##} {4:0.##} {5:0.##} c\n",
                            cx - k, cy + r, cx - r, cy + k, cx - r, cy);
                        sb.AppendFormat("{0:0.##} {1:0.##} {2:0.##} {3:0.##} {4:0.##} {5:0.##} c\n",
                            cx - r, cy - k, cx - k, cy - r, cx, cy - r);
                        sb.AppendFormat("{0:0.##} {1:0.##} {2:0.##} {3:0.##} {4:0.##} {5:0.##} c\n",
                            cx + k, cy - r, cx + r, cy - k, cx + r, cy);
                        sb.Append("S\n"); // 仅描边，不填充

                        // 引线
                        if (b.SourceBounds.Width > 0)
                        {
                            sb.Append("0.8 w\n");
                            sb.AppendFormat("{0:0.##} {1:0.##} m\n{2:0.##} {3:0.##} l\nS\n",
                                cx, cy - r, b.SourceBounds.CenterX, b.SourceBounds.CenterY);
                            sb.Append("1.5 w\n");
                        }
                    }
                    sb.Append("Q\n");

                    // 3. 气泡中心的数字文本（黑色，Helvetica 10 pt）
                    sb.Append("BT\n/F1 10 Tf\n0 0 0 rg\n");
                    foreach (var b in page.Bubbles)
                    {
                        string label = string.IsNullOrEmpty(b.Label)
                            ? b.Number.ToString()
                            : b.Label;
                        double estW = label.Length * 10.0 * 0.55;
                        double tx = b.Center.X - estW / 2.0;
                        double ty = b.Center.Y - 3.5;
                        sb.AppendFormat("{0:0.##} {1:0.##} Td\n({2}) Tj\n", tx, ty, Escape(label));
                    }
                    sb.Append("ET\n");
                }

                byte[] contentBytes = Encoding.ASCII.GetBytes(sb.ToString());

                _xref.Add(GetPos());
                WriteLine("{0} 0 obj", objId);
                WriteLine("<< /Length {0} >>", contentBytes.Length);
                WriteLine("stream");
                _s.Write(contentBytes, 0, contentBytes.Length);
                WriteLine("");
                WriteLine("endstream");
                WriteLine("endobj");
                return objId;
            }

            private int NewPageObject(SizeF pageSize, int contentObj, int imageObj, int fontObj)
            {
                int objId = _nextObj++;
                _xref.Add(GetPos());
                WriteLine("{0} 0 obj", objId);
                WriteLine("<< /Type /Page");
                WriteLine("   /Parent {0} 0 R", PeekNextId()); // Parent 是下一个 NewPagesObject 的 ID
                WriteLine("   /MediaBox [0 0 {0:0.##} {1:0.##}]", pageSize.Width, pageSize.Height);
                WriteLine("   /Resources << /ProcSet [/PDF /ImageC /Text]");
                WriteLine("                    /XObject << /Im{0} {0} 0 R >>", imageObj);
                WriteLine("                    /Font << /F1 {0} 0 R >> >>", fontObj);
                WriteLine("   /Contents {0} 0 R >>", contentObj);
                WriteLine("endobj");
                return objId;
            }

            private int PeekNextId() { return _nextObj; } // 用于预测 Pages 对象编号

            private int NewPagesObject(int[] pageObjIds)
            {
                int objId = _nextObj++;
                _xref.Add(GetPos());
                WriteLine("{0} 0 obj", objId);
                var kids = new StringBuilder();
                for (int i = 0; i < pageObjIds.Length; i++)
                {
                    kids.AppendFormat("{0} 0 R ", pageObjIds[i]);
                }
                WriteLine("<< /Type /Pages /Count {0} /Kids [{1}] >>",
                    pageObjIds.Length, kids.ToString().TrimEnd());
                WriteLine("endobj");
                return objId;
            }

            private int NewCatalog(int pagesObj)
            {
                int objId = _nextObj++;
                _xref.Add(GetPos());
                WriteLine("{0} 0 obj", objId);
                WriteLine("<< /Type /Catalog /Pages {0} 0 R >>", pagesObj);
                WriteLine("endobj");
                return objId;
            }

            private void WriteXrefAndTrailer(int catalogObj)
            {
                long xrefPos = GetPos();
                WriteLine("xref");
                WriteLine("0 {0}", _xref.Count);
                WriteLine("0000000000 65535 f ");
                for (int i = 1; i < _xref.Count; i++)
                {
                    WriteLine("{0:0000000000} 00000 n ", _xref[i]);
                }
                WriteLine("trailer");
                WriteLine("<< /Size {0} /Root {1} 0 R >>", _xref.Count, catalogObj);
                WriteLine("startxref");
                WriteLine("{0}", xrefPos);
                WriteLine("%%EOF");
            }

            // ========== 辅助 ==========
            private long GetPos() { _s.Flush(); return _s.Position; }

            private void WriteLine(string fmt, params object[] args)
            {
                string s = args.Length == 0 ? fmt : string.Format(fmt, args);
                byte[] bytes = Encoding.ASCII.GetBytes(s);
                _s.Write(bytes, 0, bytes.Length);
                _s.WriteByte((byte)'\r');
                _s.WriteByte((byte)'\n');
            }

            private void WriteBytes(byte[] b) { _s.Write(b, 0, b.Length); }

            private static byte[] Deflate(byte[] raw)
            {
                using (var ms = new MemoryStream())
                {
                    using (var ds = new DeflateStream(ms, CompressionMode.Compress, true))
                    {
                        ds.Write(raw, 0, raw.Length);
                    }
                    return ms.ToArray();
                }
            }

            private static string Escape(string s)
            {
                if (string.IsNullOrEmpty(s)) return "";
                var sb = new StringBuilder(s.Length);
                foreach (char ch in s)
                {
                    switch (ch)
                    {
                        case '\\': sb.Append("\\\\"); break;
                        case '(': sb.Append("\\("); break;
                        case ')': sb.Append("\\)"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (ch < 32 || ch > 126) sb.Append('?');
                            else sb.Append(ch);
                            break;
                    }
                }
                return sb.ToString();
            }
        }
    }

    public class ExportPage
    {
        public int PageIndex;
        public SizeF PageSize;
        public System.Drawing.Image Background;
        public List<Bubble> Bubbles;
    }
}
