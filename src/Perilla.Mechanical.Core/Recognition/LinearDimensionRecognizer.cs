using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Perilla.Mechanical.Core.Models;

namespace Perilla.Mechanical.Core.Recognition
{
    /// <summary>
    /// 线性尺寸识别器：识别形如 "50"、"⌀20"、"H7" 这样的尺寸文本。
    /// 策略：
    /// 遍历所有 TextPrimitive 中的 "看起来像数字/尺寸" 的文本，
    /// 如果附近有水平或垂直线条（即延伸线 / 尺寸线），则提高置信度。
    /// </summary>
    public class LinearDimensionRecognizer
    {
        // 匹配：可选前缀 + 数字 + 可选小数 + 空格/字母公差/符号
        // 注意：⌀ / Ø / R 前缀都可能出现
        private static readonly Regex DimensionRegex = new Regex(
            @"^[\s]*([⌀ØRr]?)\s*(\d+(?:\.\d+)?)\s*([A-Za-z0-9\-\+±]*)$",
            RegexOptions.Compiled);

        public List<RecognizedItem> Recognize(List<TextPrimitive> texts,
                                           List<GraphicPrimitive> paths,
                                           int pageIndex,
                                           int pageWidth, int pageHeight)
        {
            var result = new List<RecognizedItem>();
            if (texts == null || texts.Count == 0) return result;

            // 预计算：把 line primitives 放入"水平线/垂直线
            var horizontals = new List<LinePrimitive>();
            var verticals = new List<LinePrimitive>();
            foreach (var p in paths)
            {
                var line = p as LinePrimitive;
                if (line == null) continue;
                if (line.IsNearHorizontal()) horizontals.Add(line);
                else if (line.IsNearVertical()) verticals.Add(line);
            }

            int id = 1;
            foreach (var t in texts)
            {
                if (string.IsNullOrWhiteSpace(t.Text)) continue;

                // 尺寸数字
                var m = DimensionRegex.Match(t.Text);
                if (!m.Success) continue;

                // 置信度判断：
                // - 只是纯数字 => Medium
                // - 带前缀 ⌀/R => High
                // - 附近有垂直/水平线条 => +1 级
                // - 但如果数字很大、且附近没有线条 => 不识别
                double lvl = 0;
                string prefix = m.Groups[1].Value;
                string digits = m.Groups[2].Value;
                if (prefix.Length > 0 || digits.Length > 0) lvl += 1.5;
                if (t.LooksNumeric) lvl += 0.5;

                // 检查附近是否有尺寸线（y方向 2x字高, x 方向 1x字宽附近有延伸线）
                bool nearLine = false;
                double tolY = t.Bounds.Height * 2.0;
                double tolX = t.Bounds.Width * 1.5;
                foreach (var line in paths)
                {
                    var lp = line as LinePrimitive;
                    if (lp == null) continue;
                    // 线靠近文本
                    double dxCenter = Math.Abs(lp.Bounds.CenterX - t.Bounds.CenterX);
                    double dyCenter = Math.Abs(lp.Bounds.CenterY - t.Bounds.CenterY);
                    if (lp.IsNearHorizontal())
                    {
                        if (dyCenter < tolY * 2.5 &&
                            t.Bounds.Left < lp.End.X &&
                            t.Bounds.Right > lp.Start.X &&
                            Math.Abs(t.Bounds.CenterY - lp.Bounds.CenterY) < tolY * 2.5)
                        {
                            nearLine = true;
                            break;
                        }
                    }
                    else if (lp.IsNearVertical())
                    {
                        if (dxCenter < tolX * 2.5 &&
                            t.Bounds.Bottom < lp.End.Y &&
                            t.Bounds.Top > lp.Start.Y)
                        {
                            nearLine = true;
                            break;
                        }
                    }
                }

                if (nearLine) lvl += 1.0;

                if (lvl < 1.0) continue; // 过滤掉纯文本

                Confidence conf;
                if (lvl >= 2.5) conf = Confidence.High;
                else if (lvl >= 1.5) conf = Confidence.Medium;
                else conf = Confidence.Low;

                var item = new RecognizedItem
                {
                    Id = id++,
                    Kind = RecognitionKind.LinearDimension,
                    Confidence = conf,
                    PageIndex = pageIndex,
                    Position = t.Bounds,
                    RawText = t.Text
                };
                item.SourcePrimitives.Add(t);

                result.Add(item);
            }

            return result;
        }
    }
}
