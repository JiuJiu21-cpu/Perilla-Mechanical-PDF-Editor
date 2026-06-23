using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Perilla.Mechanical.Core.Models;

namespace Perilla.Mechanical.Export
{
    /// <summary>
    /// 渲染：
    /// - 把已渲染的 PDF 底图 + 气泡/编号 叠加；
    /// - 生成 PNG 图片；
    /// - 生成 Excel 列表（通过 EPPlus）。
    /// 生成带批注的 PDF 用 PDFsharp 实现，写一个简化版：把 Bitmap 嵌入到新 PDF 页。
    /// </summary>
    public class ImageExporter
    {
        public void DrawBubblesOnBitmap(Bitmap bitmap,
                                        IEnumerable<Bubble> bubbles,
                                        SizeF pageSize,
                                        double renderScale)
        {
            if (bitmap == null) return;
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                using (var pen = new Pen(Color.Red, 2.0f))
                using (var font = new Font("Arial", 10f, FontStyle.Bold))
                using (var brush = new SolidBrush(Color.Black))
                {
                    foreach (var b in bubbles)
                    {
                        if (b.PageIndex < 0) continue; // 非本页的跳过
                        // 坐标转换：PDF (左下为原点, Y 向上) -> Bitmap (左上为原点, Y 向下)
                        float cx = (float)(b.Center.X * renderScale);
                        float cy = (float)(pageSize.Height * renderScale - b.Center.Y * renderScale);
                        float r = (float)(b.Radius * renderScale);
                        var rect = new RectangleF(cx - r, cy - r, 2 * r, 2 * r);

                        // 红色圆圈
                        g.DrawEllipse(pen, rect.X, rect.Y, rect.Width, rect.Height);

                        // 编号文字（黑色粗体）
                        var strFormat = new StringFormat
                        {
                            Alignment = StringAlignment.Center,
                            LineAlignment = StringAlignment.Center
                        };
                        g.DrawString(b.Label, font, brush, rect, strFormat);

                        // 绘制一个细长的引线（从圆圈底部指向原图）
                        if (b.SourceBounds.Width > 0)
                        {
                            float targetX = (float)(b.SourceBounds.CenterX * renderScale);
                            float targetY = (float)(pageSize.Height * renderScale - b.SourceBounds.CenterY * renderScale);
                            using (var leader = new Pen(Color.Red, 1.0f))
                            {
                                g.DrawLine(leader, cx, cy + r, targetX, targetY);
                            }
                        }
                    }
                }
            }
        }

        public void SavePageAsPng(Bitmap bitmap, string path)
        {
            if (bitmap == null) return;
            bitmap.Save(path, ImageFormat.Png);
        }
    }
}
