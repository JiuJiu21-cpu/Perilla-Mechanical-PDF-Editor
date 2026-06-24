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
    /// 将页面位图 + 气泡导出为 PDF。
    /// 使用 PdfiumViewer 渲染的位图作为页面背景，叠加气泡后生成 PDF。
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
                var writer = new PdfStreamWriter(stream);
                writer.WriteDocument(pages);
            }
        }

        private class PdfStreamWriter
        {
            private readonly Stream _s;
            private readonly List<long> _xref = new List<long>();
            private int _nextObj;

            public PdfStreamWriter(Stream s)
            {
                _s = s;
                _xref.Add(0);
                _nextObj = 1;
            }

            public void WriteDocument(List<ExportPage> pages)
            {
                WriteLine("%PDF-1.4");
                WriteBytes(new byte[] { 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A });

                int fontObj = WriteFontObject();

                int[] pageObjIds = new int[pages.Count];
                for (int i = 0; i < pages.Count; i++)
                {
                    int imageObj = WriteImageObject(pages[i]);
                    int contentObj = WriteContentObject(pages[i], imageObj, fontObj);
                    int pageObj = WritePageObject(pages[i].PageSize, contentObj, imageObj, fontObj);
                    pageObjIds[i] = pageObj;
                }

                int pagesObj = WritePagesObject(pageObjIds);
                int catalogObj = WriteCatalogObject(pagesObj);
                WriteXrefAndTrailer(catalogObj);
            }

            private int WriteFontObject()
            {
                int id = _nextObj++;
                _xref.Add(GetPos());
                WriteLine("{0} 0 obj", id);
                WriteLine("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
                WriteLine("endobj");
                return id;
            }

            private int WriteImageObject(ExportPage page)
            {
                int id = _nextObj++;
                _xref.Add(GetPos());

                Bitmap bmp = page.Background as Bitmap;
                if (bmp == null)
                {
                    WritePlaceholderImage(id);
                    return id;
                }

                int width = bmp.Width;
                int height = bmp.Height;
                byte[] rgbData = ExtractRgb(bmp);
                byte[] compressed = FlateCompress(rgbData);

                WriteLine("{0} 0 obj", id);
                WriteLine("<< /Type /XObject /Subtype /Image");
                WriteLine("   /Width {0} /Height {1}", width, height);
                WriteLine("   /ColorSpace /DeviceRGB /BitsPerComponent 8");
                WriteLine("   /Filter /FlateDecode /Length {0} >>", compressed.Length);
                WriteLine("stream");
                _s.Write(compressed, 0, compressed.Length);
                WriteLine("");
                WriteLine("endstream");
                WriteLine("endobj");
                return id;
            }

            private void WritePlaceholderImage(int id)
            {
                WriteLine("{0} 0 obj", id);
                WriteLine("<< /Type /XObject /Subtype /Image /Width 1 /Height 1");
                WriteLine("   /ColorSpace /DeviceRGB /BitsPerComponent 8");
                WriteLine("   /Filter /FlateDecode /Length 8 >>");
                WriteLine("stream");
                byte[] white = FlateCompress(new byte[] { 255, 255, 255 });
                _s.Write(white, 0, white.Length);
                WriteLine("");
                WriteLine("endstream");
                WriteLine("endobj");
            }

            private byte[] ExtractRgb(Bitmap bmp)
            {
                int w = bmp.Width;
                int h = bmp.Height;
                byte[] result = new byte[w * h * 3];

                Rectangle rect = new Rectangle(0, 0, w, h);
                BitmapData bd = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    int stride = bd.Stride;
                    IntPtr scan0 = bd.Scan0;
                    int rowLen = w * 3;

                    for (int y = 0; y < h; y++)
                    {
                        byte[] line = new byte[rowLen];
                        System.Runtime.InteropServices.Marshal.Copy(
                            new IntPtr(scan0.ToInt64() + y * stride), line, 0, rowLen);
                        for (int x = 0; x < w; x++)
                        {
                            int srcIdx = x * 3;
                            int dstIdx = y * rowLen + x * 3;
                            result[dstIdx] = line[srcIdx + 2];
                            result[dstIdx + 1] = line[srcIdx + 1];
                            result[dstIdx + 2] = line[srcIdx];
                        }
                    }
                }
                finally
                {
                    bmp.UnlockBits(bd);
                }
                return result;
            }

            private int WriteContentObject(ExportPage page, int imageObj, int fontObj)
            {
                int id = _nextObj++;
                _xref.Add(GetPos());

                var sb = new StringBuilder();
                double pw = Math.Max(1, page.PageSize.Width);
                double ph = Math.Max(1, page.PageSize.Height);

                sb.Append("q\n");
                sb.AppendFormat("{0:0.###} 0 0 {1:0.###} 0 0 cm\n/Im{2} Do\nQ\n",
                    pw, ph, imageObj);

                if (page.Bubbles != null && page.Bubbles.Count > 0)
                {
                    sb.Append("q\n");
                    sb.Append("1 0 0 RG\n");
                    sb.Append("1.5 w\n");

                    foreach (var b in page.Bubbles)
                    {
                        double cx = b.Center.X;
                        double cy = b.Center.Y;
                        double r = Math.Max(1.0, b.Radius);
                        double k = 0.552284749831 * r;

                        sb.AppendFormat("{0:0.###} {1:0.###} m\n", cx + r, cy);
                        sb.AppendFormat("{0:0.###} {1:0.###} {2:0.###} {3:0.###} {4:0.###} {5:0.###} c\n",
                            cx + r, cy + k, cx + k, cy + r, cx, cy + r);
                        sb.AppendFormat("{0:0.###} {1:0.###} {2:0.###} {3:0.###} {4:0.###} {5:0.###} c\n",
                            cx - k, cy + r, cx - r, cy + k, cx - r, cy);
                        sb.AppendFormat("{0:0.###} {1:0.###} {2:0.###} {3:0.###} {4:0.###} {5:0.###} c\n",
                            cx - r, cy - k, cx - k, cy - r, cx, cy - r);
                        sb.AppendFormat("{0:0.###} {1:0.###} {2:0.###} {3:0.###} {4:0.###} {5:0.###} c\n",
                            cx + k, cy - r, cx + r, cy - k, cx + r, cy);
                        sb.Append("S\n");

                        if (b.SourceBounds.Width > 0 && b.SourceBounds.Height > 0)
                        {
                            sb.Append("0.8 w\n");
                            sb.AppendFormat("{0:0.###} {1:0.###} m\n{2:0.###} {3:0.###} l\nS\n",
                                cx, cy - r, b.SourceBounds.CenterX, b.SourceBounds.CenterY);
                            sb.Append("1.5 w\n");
                        }
                    }
                    sb.Append("Q\n");

                    sb.Append("BT\n/F1 10 Tf\n0 0 0 rg\n");
                    foreach (var b in page.Bubbles)
                    {
                        string label = string.IsNullOrEmpty(b.Label)
                            ? b.Number.ToString()
                            : b.Label;
                        double estW = label.Length * 10.0 * 0.55;
                        double tx = b.Center.X - estW / 2.0;
                        double ty = b.Center.Y - 3.5;
                        sb.AppendFormat("{0:0.###} {1:0.###} Td\n({2}) Tj\n", tx, ty, EscapePdfString(label));
                    }
                    sb.Append("ET\n");
                }

                byte[] contentBytes = Encoding.ASCII.GetBytes(sb.ToString());

                WriteLine("{0} 0 obj", id);
                WriteLine("<< /Length {0} >>", contentBytes.Length);
                WriteLine("stream");
                _s.Write(contentBytes, 0, contentBytes.Length);
                WriteLine("");
                WriteLine("endstream");
                WriteLine("endobj");
                return id;
            }

            private int WritePageObject(SizeF pageSize, int contentObj, int imageObj, int fontObj)
            {
                int id = _nextObj++;
                _xref.Add(GetPos());
                WriteLine("{0} 0 obj", id);
                WriteLine("<< /Type /Page");
                WriteLine("   /Parent {0} 0 R", PeekNextId());
                WriteLine("   /MediaBox [0 0 {0:0.###} {1:0.###}]", pageSize.Width, pageSize.Height);
                WriteLine("   /Resources << /ProcSet [/PDF /ImageC /Text]");
                WriteLine("      /XObject << /Im{0} {0} 0 R >>", imageObj);
                WriteLine("      /Font << /F1 {0} 0 R >> >>", fontObj);
                WriteLine("   /Contents {0} 0 R >>", contentObj);
                WriteLine("endobj");
                return id;
            }

            private int PeekNextId() { return _nextObj; }

            private int WritePagesObject(int[] pageObjIds)
            {
                int id = _nextObj++;
                _xref.Add(GetPos());
                var kids = new StringBuilder();
                for (int i = 0; i < pageObjIds.Length; i++)
                    kids.AppendFormat("{0} 0 R ", pageObjIds[i]);
                WriteLine("{0} 0 obj", id);
                WriteLine("<< /Type /Pages /Count {0} /Kids [{1}] >>",
                    pageObjIds.Length, kids.ToString().TrimEnd());
                WriteLine("endobj");
                return id;
            }

            private int WriteCatalogObject(int pagesObj)
            {
                int id = _nextObj++;
                _xref.Add(GetPos());
                WriteLine("{0} 0 obj", id);
                WriteLine("<< /Type /Catalog /Pages {0} 0 R >>", pagesObj);
                WriteLine("endobj");
                return id;
            }

            private void WriteXrefAndTrailer(int catalogObj)
            {
                long xrefPos = GetPos();
                WriteLine("xref");
                WriteLine("0 {0}", _xref.Count);
                WriteLine("0000000000 65535 f ");
                for (int i = 1; i < _xref.Count; i++)
                    WriteLine("{0:0000000000} 00000 n ", _xref[i]);
                WriteLine("trailer");
                WriteLine("<< /Size {0} /Root {1} 0 R >>", _xref.Count, catalogObj);
                WriteLine("startxref");
                WriteLine("{0}", xrefPos);
                WriteLine("%%EOF");
            }

            private long GetPos()
            {
                _s.Flush();
                return _s.Position;
            }

            private void WriteLine(string fmt, params object[] args)
            {
                string s = args.Length == 0 ? fmt : string.Format(fmt, args);
                byte[] bytes = Encoding.ASCII.GetBytes(s);
                _s.Write(bytes, 0, bytes.Length);
                _s.WriteByte((byte)'\r');
                _s.WriteByte((byte)'\n');
            }

            private void WriteBytes(byte[] b)
            {
                _s.Write(b, 0, b.Length);
            }

            private static byte[] FlateCompress(byte[] raw)
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

            private static string EscapePdfString(string s)
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
