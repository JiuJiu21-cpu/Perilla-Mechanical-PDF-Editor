using System;
using System.Drawing;
using System.IO;
using PdfiumViewer;

namespace PerillaTest
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            try
            {
                Console.WriteLine("=== Perilla Mechanical PDF Editor - Test ===");
                Console.WriteLine("PdfiumViewer loaded: " + typeof(PdfDocument).Assembly.FullName);

                // Create a simple PDF if we don't have one, then reload it
                string pdfPath = Path.Combine(Path.GetTempPath(), "test_perilla.pdf");

                // Use a simpler approach - check if PDFium native DLL is in path
                Console.WriteLine("Looking for pdfium.dll ...");
                string currentDir = Directory.GetCurrentDirectory();
                Console.WriteLine("Current dir: " + currentDir);

                // List DLLs in current directory
                foreach (string f in Directory.GetFiles(currentDir, "*.dll"))
                {
                    Console.WriteLine("  " + Path.GetFileName(f));
                }

                // Try to load a PDF document using PdfiumViewer
                // We need a real PDF - create a minimal valid PDF from scratch
                Console.WriteLine("\nCreating minimal test PDF ...");
                CreateMinimalPdf(pdfPath);
                Console.WriteLine("PDF created: " + pdfPath);

                Console.WriteLine("\nLoading PDF with PdfiumViewer ...");
                using (PdfDocument doc = PdfDocument.Load(pdfPath))
                {
                    Console.WriteLine("Page count: " + doc.PageCount);
                    for (int i = 0; i < doc.PageCount; i++)
                    {
                        SizeF size = doc.PageSizes[i];
                        Console.WriteLine("  Page " + (i + 1) + ": " + size.Width + "x" + size.Height + " pt");
                    }

                    // Render first page
                    if (doc.PageCount > 0)
                    {
                        using (Bitmap bmp = (Bitmap)doc.Render(0, 2, 2, 200, 200, false))
                        {
                            string outBmp = Path.Combine(Path.GetTempPath(), "test_render.png");
                            bmp.Save(outBmp);
                            Console.WriteLine("Rendered to: " + outBmp + " (" + bmp.Width + "x" + bmp.Height + ")");
                        }
                    }

                    // Search for text to verify text extraction
                    try
                    {
                        string text = doc.GetPdfText(0);
                        Console.WriteLine("Page 0 text length: " + (text ?? "").Length);
                        if (!string.IsNullOrEmpty(text) && text.Length < 200)
                        {
                            Console.WriteLine("Content: " + text);
                        }
                    }
                    catch (Exception textEx)
                    {
                        Console.WriteLine("  (GetPdfText exception: " + textEx.Message + ")");
                    }
                }

                Console.WriteLine("\n=== SUCCESS ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAILURE: " + ex.ToString());
                Environment.Exit(1);
            }
        }

        // Minimal PDF generator (does not depend on PDFsharp)
        private static void CreateMinimalPdf(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (StreamWriter w = new StreamWriter(fs, System.Text.Encoding.ASCII))
            {
                w.WriteLine("%PDF-1.4");
                w.WriteLine("1 0 obj");
                w.WriteLine("<< /Type /Catalog /Pages 2 0 R >>");
                w.WriteLine("endobj");
                w.WriteLine("2 0 obj");
                w.WriteLine("<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
                w.WriteLine("endobj");
                w.WriteLine("3 0 obj");
                w.WriteLine("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>");
                w.WriteLine("endobj");
                w.WriteLine("4 0 obj");
                w.WriteLine("<< /Length 55 >>");
                w.WriteLine("stream");
                w.WriteLine("BT /F1 24 Tf 100 700 Td (Perilla Mechanical Test) Tj ET");
                w.WriteLine("endstream");
                w.WriteLine("endobj");
                w.WriteLine("5 0 obj");
                w.WriteLine("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
                w.WriteLine("endobj");
                w.Write("xref\n0 6\n0000000000 65535 f \n0000000010 00000 n \n0000000060 00000 n \n0000000111 00000 n \n0000000238 00000 n \n0000000343 00000 n \n");
                w.WriteLine("trailer");
                w.WriteLine("<< /Size 6 /Root 1 0 R >>");
                w.WriteLine("startxref");
                w.WriteLine("410");
                w.WriteLine("%%EOF");
            }
        }
    }
}
