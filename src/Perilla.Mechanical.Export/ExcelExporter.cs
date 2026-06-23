using System;
using System.Collections.Generic;
using System.IO;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using Perilla.Mechanical.Core.Models;

namespace Perilla.Mechanical.Export
{
    /// <summary>
    /// 把气泡列表导出为 Excel (xlsx)。
    /// 列：序号 / 类型 / 原文本 / 页号 / 中心(x,y) / 备注
    /// </summary>
    public class ExcelExporter
    {
        static ExcelExporter()
        {
            // 允许 EPPlus 5.x 在无 LicenseContext 时运行（对于非商业目的）。
            // 更严谨的商业使用请在主程序初始化时设置 ExcelPackage.LicenseContext = LicenseContext.Commercial。
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        public void Export(List<Bubble> bubbles, string outputPath, string pdfName)
        {
            if (string.IsNullOrEmpty(outputPath))
                throw new ArgumentException("outputPath");
            if (bubbles == null) bubbles = new List<Bubble>();

            var file = new FileInfo(outputPath);
            if (file.Directory != null && !file.Directory.Exists)
                file.Directory.Create();

            using (var pkg = new ExcelPackage(file))
            {
                var sheet = pkg.Workbook.Worksheets.Add("气泡序号清单");

                // Header
                string[] headers = { "序号", "类型", "原文本", "页号", "X(pt)", "Y(pt)", "备注" };
                for (int i = 0; i < headers.Length; i++)
                {
                    var cell = sheet.Cells[1, i + 1];
                    cell.Value = headers[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightYellow);
                    cell.Style.Border.BorderAround(ExcelBorderStyle.Thin);
                }

                // Rows
                int row = 2;
                foreach (var b in bubbles)
                {
                    sheet.Cells[row, 1].Value = b.Number;
                    sheet.Cells[row, 2].Value = KindLabel(b.Kind);
                    sheet.Cells[row, 3].Value = b.LinkedText ?? "";
                    sheet.Cells[row, 4].Value = b.PageIndex + 1; // 1-based for humans
                    sheet.Cells[row, 5].Value = Math.Round(b.Center.X, 2);
                    sheet.Cells[row, 6].Value = Math.Round(b.Center.Y, 2);
                    sheet.Cells[row, 7].Value = b.IsManual ? "手动" : "自动";
                    for (int c = 1; c <= headers.Length; c++)
                    {
                        sheet.Cells[row, c].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    }
                    row++;
                }

                // Auto fit
                sheet.Cells[sheet.Dimension.Address].AutoFitColumns();

                // 统计 Sheet
                var stats = pkg.Workbook.Worksheets.Add("统计");
                stats.Cells["A1"].Value = "图纸"; stats.Cells["B1"].Value = pdfName ?? Path.GetFileName(outputPath);
                stats.Cells["A2"].Value = "总识别气泡"; stats.Cells["B2"].Value = bubbles.Count;
                int dimCount = 0, gdtCount = 0, annCount = 0, manCount = 0;
                foreach (var b in bubbles)
                {
                    if (b.IsManual) manCount++;
                    else if (b.Kind == RecognitionKind.LinearDimension) dimCount++;
                    else if (b.Kind == RecognitionKind.GDTTolerance) gdtCount++;
                    else annCount++;
                }
                stats.Cells["A3"].Value = "线性尺寸"; stats.Cells["B3"].Value = dimCount;
                stats.Cells["A4"].Value = "形位公差"; stats.Cells["B4"].Value = gdtCount;
                stats.Cells["A5"].Value = "图纸注解"; stats.Cells["B5"].Value = annCount;
                stats.Cells["A6"].Value = "手动气泡"; stats.Cells["B6"].Value = manCount;
                stats.Cells["A1:B6"].AutoFitColumns();

                pkg.Save();
            }
        }

        private static string KindLabel(RecognitionKind k)
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
    }
}
