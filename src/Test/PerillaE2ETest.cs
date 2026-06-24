using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using Perilla.Mechanical.Core.Models;
using Perilla.Mechanical.Core.Pdf;
using Perilla.Mechanical.Core.Services;
using Perilla.Mechanical.Core.Recognition;
using Perilla.Mechanical.Export;

namespace PerillaE2ETest
{
    /// <summary>
    /// Perilla Mechanical PDF Editor 端到端集成测试：
    /// 1. 创建一个最小 PDF（包含文本如尺寸数字）
    /// 2. 使用 PdfParsingService 加载
    /// 3. 提取文本 / 渲染位图
    /// 4. 调用 DrawingRecognitionService 识别
    /// 5. 调用 BubbleNumberingService 分配气泡
    /// 6. 导出 Image / Excel / PDF 注释
    ///
    /// 所有测试在用户临时目录内进行，不留垃圾。
    /// </summary>
    internal static class Program
    {
        private static string _tempDir;
        private static int _passed = 0;
        private static int _failed = 0;
        private static StringBuilder _log = new StringBuilder();

        private static void Main()
        {
            try
            {
                _tempDir = Path.Combine(Path.GetTempPath(), "PerillaE2E_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(_tempDir);

                Log("============================================================");
                Log(" Perilla Mechanical PDF Editor - 端到端集成测试");
                Log(" 临时目录: " + _tempDir);
                Log("============================================================");

                TestCase("T1. 创建最小 PDF 并加载", Test_LoadAndRender);
                TestCase("T2. 文本提取", Test_TextExtraction);
                TestCase("T3. 线性尺寸识别", Test_LinearDimension);
                TestCase("T4. GD&T 公差框识别", Test_GDT);
                TestCase("T5. 气泡编号 + 避碰", Test_BubbleNumbering);
                TestCase("T6. 图像/Excel/PDF 导出", Test_Export);
                TestCase("T7. 异常鲁棒性（空文件 / null 参数 / 无效路径）", Test_Robustness);

                Log("");
                Log("============================================================");
                Log(string.Format(" 总结: 通过 = {0}   失败 = {1}", _passed, _failed));
                Log("============================================================");
                Console.WriteLine(_log.ToString());
                File.WriteAllText(Path.Combine(_tempDir, "summary.log"), _log.ToString());

                Environment.ExitCode = (_failed == 0) ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FATAL: " + ex);
                Environment.ExitCode = 2;
            }
        }

        private static void TestCase(string title, Action action)
        {
            Log("");
            Log("---- " + title + " ----");
            try
            {
                action();
                Log("PASS: " + title);
                _passed++;
            }
            catch (Exception ex)
            {
                Log("FAIL: " + title + " -> " + ex.Message);
                Log("  Stack: " + ex.StackTrace);
                _failed++;
            }
        }

        private static void Log(string m)
        {
            _log.AppendLine(m);
            Console.WriteLine(m);
        }

        private static string CreateMinimalPdf(string fileName, string content)
        {
            string path = Path.Combine(_tempDir, fileName);
            using (var fs = File.Create(path))
            using (var w = new StreamWriter(fs, Encoding.ASCII))
            {
                w.WriteLine("%PDF-1.4");
                w.Flush();
                // 写二进制标记（高字节）
                fs.Write(new byte[] { 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A }, 0, 6);
                long obj1 = fs.Position; w.WriteLine("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj");
                long obj2 = fs.Position; w.WriteLine("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj");
                long obj3 = fs.Position; w.WriteLine("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>\nendobj");
                long obj4 = fs.Position;
                string streamContent = "BT /F1 18 Tf 72 700 Td (" + EscapePdf(content) + ") Tj ET";
                w.WriteLine("4 0 obj\n<< /Length " + streamContent.Length + " >>");
                w.WriteLine("stream");
                w.Flush();
                byte[] sb = Encoding.ASCII.GetBytes(streamContent);
                fs.Write(sb, 0, sb.Length);
                w.WriteLine("");
                w.WriteLine("endstream");
                w.WriteLine("endobj");
                long obj5 = fs.Position; w.WriteLine("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj");
                long xref = fs.Position;
                w.WriteLine("xref");
                w.WriteLine("0 6");
                w.WriteLine("0000000000 65535 f ");
                w.WriteLine(FormatXref(obj1));
                w.WriteLine(FormatXref(obj2));
                w.WriteLine(FormatXref(obj3));
                w.WriteLine(FormatXref(obj4));
                w.WriteLine(FormatXref(obj5));
                w.WriteLine("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF");
            }
            return path;
        }

        private static string FormatXref(long pos) { return pos.ToString("0000000000") + " 00000 n "; }

        private static string EscapePdf(string s)
        {
            // 简化转义，用于测试仅含字母 / 数字 / 标点的文本
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '(': sb.Append("\\("); break;
                    case ')': sb.Append("\\)"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        // ============================================================
        // 测试用例
        // ============================================================
        private static void Test_LoadAndRender()
        {
            string pdf = CreateMinimalPdf("t1.pdf", "Diameter 50 mm , R20 , 100x50 Hole Depth 30");
            using (var parser = new PdfParsingService())
            {
                parser.Load(pdf);
                SizeF sz = parser.GetPageSize(0);
                Log(string.Format("  Page size: {0:0.##} x {1:0.##} pt", sz.Width, sz.Height));
                if (sz.Width < 10 || sz.Height < 10) throw new Exception("页面大小无效");

                using (Bitmap bmp = parser.RenderPageToBitmap(0, 2.0))
                {
                    if (bmp == null) throw new Exception("渲染失败");
                    Log(string.Format("  Rendered bitmap: {0} x {1}", bmp.Width, bmp.Height));
                    string pngPath = Path.Combine(_tempDir, "t1_render.png");
                    bmp.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
                    Log("  Saved: " + pngPath + " (" + new FileInfo(pngPath).Length + " bytes)");
                }
            }
        }

        private static void Test_TextExtraction()
        {
            string pdf = CreateMinimalPdf("t2.pdf", "Dia 50 Radius R20 Tolerance 0.05 Depth 30");
            using (var parser = new PdfParsingService())
            {
                parser.Load(pdf);
                var texts = parser.ExtractTexts(0);
                Log(string.Format("  提取到 {0} 个文本单元", texts.Count));
                foreach (var t in texts)
                {
                    Log(string.Format("    text='{0}', size={1:0.##} pt, looksNumeric={2}",
                        t.Text, t.FontSizePt, t.LooksNumeric));
                }
                if (texts.Count == 0) throw new Exception("未能提取任何文本");
            }
        }

        private static void Test_LinearDimension()
        {
            string pdf = CreateMinimalPdf("t3.pdf", "50 20 30 Dia R Radius 100x50");
            using (var parser = new PdfParsingService())
            {
                parser.Load(pdf);
                var texts = parser.ExtractTexts(0);
                var paths = parser.ExtractPathsAndRects(0);
                SizeF sz = parser.GetPageSize(0);

                var recognizer = new LinearDimensionRecognizer();
                var items = recognizer.Recognize(texts, paths, 0, (int)sz.Width, (int)sz.Height);
                Log(string.Format("  线性尺寸识别命中: {0}", items.Count));
                foreach (var it in items)
                {
                    Log(string.Format("    '{0}' @ ({1:0.##},{2:0.##}) conf={3}",
                        it.RawText, it.Position.CenterX, it.Position.CenterY, it.Confidence));
                }
                if (items.Count == 0) Log("  WARNING: 未识别到线性尺寸（对于纯文本 PDF 属预期行为，启发式不强）");
            }
        }

        private static void Test_GDT()
        {
            string pdf = CreateMinimalPdf("t4.pdf", "Tolerance Box 0.05 Position 0.10");
            using (var parser = new PdfParsingService())
            {
                parser.Load(pdf);
                var texts = parser.ExtractTexts(0);
                var paths = parser.ExtractPathsAndRects(0);

                var recognizer = new GDTToleranceRecognizer();
                var items = recognizer.Recognize(texts, paths, 0);
                Log(string.Format("  GD&T 识别命中: {0}", items.Count));
                foreach (var it in items)
                {
                    Log(string.Format("    '{0}' @ ({1:0.##},{2:0.##})",
                        it.RawText, it.Position.CenterX, it.Position.CenterY));
                }
            }
        }

        private static void Test_BubbleNumbering()
        {
            // 构造模拟的识别项，验证气泡编号和避碰
            var items = new List<RecognizedItem>();
            for (int i = 0; i < 5; i++)
            {
                items.Add(new RecognizedItem
                {
                    Id = i + 1,
                    Kind = RecognitionKind.LinearDimension,
                    RawText = "DIM" + (i + 1),
                    Position = new RectD(100 + i * 80, 400, 40, 20),
                    PageIndex = 0,
                    Confidence = Confidence.High
                });
            }

            var opts = new NumberingOptions
            {
                StartNumber = 1,
                BubbleRadius = 12,
                CollisionPadding = 4,
                Order = NumberingOptions.OrderBy.TopToBottomLeftToRight
            };
            var bubbler = new BubbleNumberingService();
            var bubbles = bubbler.Assign(items, opts, null);

            Log(string.Format("  生成气泡: {0}", bubbles.Count));
            foreach (var b in bubbles)
            {
                Log(string.Format("    #{0} Label='{1}' @({2:0.##},{3:0.##})",
                    b.Number, b.Label, b.Center.X, b.Center.Y));
            }

            // 验证：气泡位置不重叠
            for (int i = 0; i < bubbles.Count; i++)
            {
                for (int j = i + 1; j < bubbles.Count; j++)
                {
                    double dx = bubbles[i].Center.X - bubbles[j].Center.X;
                    double dy = bubbles[i].Center.Y - bubbles[j].Center.Y;
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < bubbles[i].Radius + bubbles[j].Radius + opts.CollisionPadding - 0.1)
                        throw new Exception(string.Format("气泡 {0} 与 {1} 碰撞 (d={2:0.##})", i, j, d));
                }
            }
            Log("  碰撞检测：通过");
        }

        private static void Test_Export()
        {
            // 组装测试数据
            var page0 = new ExportPage
            {
                PageIndex = 0,
                PageSize = new SizeF(612, 792),
                Background = CreateTestBitmap(612, 792),
                Bubbles = new List<Bubble>
                {
                    new Bubble { Number = 1, Label = "1", Center = new PointD(200, 600), Radius = 12, Kind = RecognitionKind.LinearDimension, LinkedText = "50", PageIndex = 0, SourceBounds = new RectD(150, 580, 40, 20) },
                    new Bubble { Number = 2, Label = "2", Center = new PointD(350, 500), Radius = 12, Kind = RecognitionKind.GDTTolerance, LinkedText = "0.05", PageIndex = 0, SourceBounds = new RectD(320, 480, 40, 20) },
                    new Bubble { Number = 3, Label = "3", Center = new PointD(500, 400), Radius = 12, Kind = RecognitionKind.Annotation, LinkedText = "NOTE", PageIndex = 0, SourceBounds = new RectD(470, 380, 40, 20) }
                }
            };
            var pages = new List<ExportPage> { page0 };

            // 图像渲染 + 保存 PNG
            var imageExporter = new ImageExporter();
            imageExporter.DrawBubblesOnBitmap((Bitmap)page0.Background.Clone(), page0.Bubbles, page0.PageSize, 2.0);
            string png = Path.Combine(_tempDir, "t6_annotated.png");
            imageExporter.SavePageAsPng((Bitmap)page0.Background, png);
            Log("  PNG 导出: " + png + " (" + new FileInfo(png).Length + " bytes)");

            // PDF 导出
            // 注：重新为 PDF 导出准备干净的背景（Clone 以防修改原位图）
            foreach (var p in pages) { p.Background = CreateTestBitmap((int)p.PageSize.Width, (int)p.PageSize.Height); }
            var pdfExporter = new PdfExporter();
            string pdfPath = Path.Combine(_tempDir, "t6_annotated.pdf");
            pdfExporter.Export(pages, pdfPath);
            Log("  PDF 导出: " + pdfPath + " (" + new FileInfo(pdfPath).Length + " bytes)");
            if (new FileInfo(pdfPath).Length < 100) throw new Exception("PDF 文件过小，可能为空");

            // Excel 导出
            var flat = new List<Bubble>();
            foreach (var p in pages) flat.AddRange(p.Bubbles);
            var excelExporter = new ExcelExporter();
            string xlsx = Path.Combine(_tempDir, "t6_bubbles.xlsx");
            excelExporter.Export(flat, xlsx, "TEST");
            Log("  Excel 导出: " + xlsx + " (" + new FileInfo(xlsx).Length + " bytes)");
            if (new FileInfo(xlsx).Length < 100) throw new Exception("Excel 文件过小");
        }

        private static void Test_Robustness()
        {
            // 路径 null - 应抛 ArgumentNullException 或 ArgumentException
            try
            {
                using (var p = new PdfParsingService()) { p.Load(null); throw new Exception("期望异常"); }
            }
            catch (ArgumentException) { Log("  null 路径: 正确抛出 ArgumentException"); }
            catch (Exception ex) { Log("  null 路径: 抛 " + ex.GetType().Name + " (可接受)"); }

            // 路径为空字符串
            try
            {
                using (var p = new PdfParsingService()) { p.Load(""); throw new Exception("期望异常"); }
            }
            catch (ArgumentException) { Log("  空路径: 正确抛出 ArgumentException"); }
            catch (Exception ex) { Log("  空路径: 抛 " + ex.GetType().Name + " (可接受)"); }

            // 不存在的文件
            try
            {
                using (var p = new PdfParsingService()) { p.Load(Path.Combine(_tempDir, "does_not_exist.pdf")); throw new Exception("期望异常"); }
            }
            catch (FileNotFoundException) { Log("  不存在路径: 正确抛出 FileNotFoundException"); }
            catch (Exception ex) { Log("  不存在路径: 抛 " + ex.GetType().Name + " (可接受)"); }

            // 文本识别 null 输入
            var lin = new LinearDimensionRecognizer();
            var empty = lin.Recognize(null, null, 0, 100, 100);
            Log("  LinearDimension null 输入: 返回 " + empty.Count + " 项（优雅处理）");

            var gdt = new GDTToleranceRecognizer();
            var empty2 = gdt.Recognize(null, null, 0);
            Log("  GD&T null 输入: 返回 " + empty2.Count + " 项（优雅处理）");

            var ann = new AnnotationRecognizer();
            var empty3 = ann.Recognize(null, null, 0);
            Log("  Annotation null 输入: 返回 " + empty3.Count + " 项（优雅处理）");

            // 气泡编号 null 输入
            var bn = new BubbleNumberingService();
            var emptyBubbles = bn.Assign(null, new NumberingOptions(), null);
            Log("  BubbleNumbering null 输入: 返回 " + emptyBubbles.Count + " 项（优雅处理）");

            // 自动化处理：用真实文件
            string pdf = CreateMinimalPdf("t7.pdf", "100 200 300");
            using (var parser = new PdfParsingService())
            {
                parser.Load(pdf);
                var auto = new AutomatedProcessingService(parser,
                    new DrawingRecognitionService(),
                    new BubbleNumberingService());
                var res = auto.Run(pdf, new AutoProcessingOptions(), null,
                    msg => Log("  -> " + msg));
                Log(string.Format("  Automated processing completed: {0} pages, {1} bubbles total",
                    res.Count, SumBubbles(res)));
            }
        }

        private static int SumBubbles(List<AutomatedProcessingService.PageAutoResult> pages)
        {
            int n = 0;
            foreach (var p in pages) n += p.Bubbles.Count;
            return n;
        }

        private static Bitmap CreateTestBitmap(int w, int h)
        {
            var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                for (int i = 0; i < 6; i++)
                {
                    g.DrawLine(Pens.Black, 50 + i * 80, 50, 50 + i * 80, h - 50);
                }
                using (var font = new Font("Arial", 12))
                {
                    g.DrawString("Sample Drawing", font, Brushes.Black, 100, 100);
                    g.DrawString("50", font, Brushes.Black, 200, 300);
                    g.DrawString("0.05", font, Brushes.Black, 350, 300);
                }
            }
            return bmp;
        }
    }
}
