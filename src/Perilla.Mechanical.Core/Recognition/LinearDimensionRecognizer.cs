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

        // 过滤掉明显不是尺寸的数字（页码、图号、材料编号等）
        private static readonly Regex NonDimensionRegex = new Regex(
            @"^(?:\d{1,2}/\d{1,2}|\d{3,}-\d+|\d{4,}|\d{1,2}\.\d{1,2}\.\d{2,4})$",
            RegexOptions.Compiled);

        public List<RecognizedItem> Recognize(List<TextPrimitive> texts,
                                           List<GraphicPrimitive> paths,
                                           int pageIndex,
                                           int pageWidth, int pageHeight)
        {
            var result = new List<RecognizedItem>();
            if (texts == null || texts.Count == 0) return result;

            // 预计算：把 line primitives 放入水平线/垂直线
            var horizontals = new List<LinePrimitive>();
            var verticals = new List<LinePrimitive>();
            foreach (var p in paths)
            {
                var line = p as LinePrimitive;
                if (line == null) continue;
                if (line.IsNearHorizontal()) horizontals.Add(line);
                else if (line.IsNearVertical()) verticals.Add(line);
            }

            // 第一轮：收集所有候选文本及其置信度
            var candidates = new List<DimensionCandidate>();
            foreach (var t in texts)
            {
                if (string.IsNullOrWhiteSpace(t.Text)) continue;

                // 先过滤明显不是尺寸的模式
                if (NonDimensionRegex.IsMatch(t.Text.Trim())) continue;

                var m = DimensionRegex.Match(t.Text);
                if (!m.Success) continue;

                double lvl = EvaluateConfidence(t, m, paths);
                if (lvl < 1.0) continue; // 过滤掉纯文本

                candidates.Add(new DimensionCandidate
                {
                    Text = t,
                    Match = m,
                    ConfidenceLevel = lvl
                });
            }

            // 第二轮：合并相邻的连续数字（如 "50" 和 "H7" 靠近时合并为 "50 H7"）
            candidates = MergeAdjacentCandidates(candidates);

            // 第三轮：过滤孤立数字（附近没有任何线条且字体很小的纯数字）
            candidates = FilterIsolatedNumbers(candidates, paths);

            int id = 1;
            foreach (var cand in candidates)
            {
                Confidence conf;
                if (cand.ConfidenceLevel >= 2.5) conf = Confidence.High;
                else if (cand.ConfidenceLevel >= 1.5) conf = Confidence.Medium;
                else conf = Confidence.Low;

                var item = new RecognizedItem
                {
                    Id = id++,
                    Kind = RecognitionKind.LinearDimension,
                    Confidence = conf,
                    PageIndex = pageIndex,
                    Position = cand.Text.Bounds,
                    RawText = cand.Text.Text
                };
                item.SourcePrimitives.Add(cand.Text);

                result.Add(item);
            }

            return result;
        }

        private class DimensionCandidate
        {
            public TextPrimitive Text;
            public Match Match;
            public double ConfidenceLevel;
            public bool Merged;
        }

        private double EvaluateConfidence(TextPrimitive t, Match m, List<GraphicPrimitive> paths)
        {
            double lvl = 0;
            string prefix = m.Groups[1].Value;
            string digits = m.Groups[2].Value;

            // 带前缀 ⌀/R 的尺寸置信度更高
            if (prefix.Length > 0) lvl += 2.0;
            else if (digits.Length > 0) lvl += 1.0;

            // LooksNumeric 标记增加置信度
            if (t.LooksNumeric) lvl += 0.5;

            // 字体大小过滤：机械图纸尺寸通常在 2.5pt ~ 5pt 之间
            if (t.FontSizePt >= 2.0 && t.FontSizePt <= 6.0) lvl += 0.5;
            else if (t.FontSizePt > 8.0) lvl -= 0.5; // 大字体通常是标题或注解

            // 检查附近是否有尺寸线
            bool nearLine = false;
            double tolY = t.Bounds.Height * 2.0;
            double tolX = t.Bounds.Width * 1.5;
            foreach (var line in paths)
            {
                var lp = line as LinePrimitive;
                if (lp == null) continue;
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
            else lvl -= 0.3; // 附近无线条降低置信度

            return lvl;
        }

        private List<DimensionCandidate> MergeAdjacentCandidates(List<DimensionCandidate> candidates)
        {
            var result = new List<DimensionCandidate>();
            foreach (var cand in candidates)
            {
                if (cand.Merged) continue;

                // 查找右侧/上方是否有公差标注（如 "50" 旁边有 "H7"）
                foreach (var other in candidates)
                {
                    if (other == cand || other.Merged) continue;

                    double dx = Math.Abs(other.Text.Bounds.CenterX - cand.Text.Bounds.CenterX);
                    double dy = Math.Abs(other.Text.Bounds.CenterY - cand.Text.Bounds.CenterY);
                    double maxDist = Math.Max(cand.Text.Bounds.Width, cand.Text.Bounds.Height) * 3.0;

                    // 水平相邻（公差在数字右侧）或垂直相邻
                    if (dx < maxDist && dy < cand.Text.Bounds.Height * 1.5)
                    {
                        // 合并文本
                        cand.Text.Text = cand.Text.Text + " " + other.Text.Text;
                        cand.Text.Bounds = MergeBounds(cand.Text.Bounds, other.Text.Bounds);
                        cand.ConfidenceLevel = Math.Max(cand.ConfidenceLevel, other.ConfidenceLevel) + 0.5;
                        other.Merged = true;
                    }
                }
                result.Add(cand);
            }
            return result;
        }

        private List<DimensionCandidate> FilterIsolatedNumbers(List<DimensionCandidate> candidates, List<GraphicPrimitive> paths)
        {
            var result = new List<DimensionCandidate>();
            foreach (var cand in candidates)
            {
                // 高置信度候选直接保留
                if (cand.ConfidenceLevel >= 2.0)
                {
                    result.Add(cand);
                    continue;
                }

                // 检查是否孤立：附近没有任何线条
                bool hasNearbyLine = false;
                double searchRadius = Math.Max(cand.Text.Bounds.Width, cand.Text.Bounds.Height) * 4.0;
                foreach (var line in paths)
                {
                    var lp = line as LinePrimitive;
                    if (lp == null) continue;
                    double dx = Math.Abs(lp.Bounds.CenterX - cand.Text.Bounds.CenterX);
                    double dy = Math.Abs(lp.Bounds.CenterY - cand.Text.Bounds.CenterY);
                    if (dx < searchRadius && dy < searchRadius)
                    {
                        hasNearbyLine = true;
                        break;
                    }
                }

                // 孤立且低置信度的过滤掉
                if (!hasNearbyLine && cand.ConfidenceLevel < 1.5)
                    continue;

                result.Add(cand);
            }
            return result;
        }

        private RectD MergeBounds(RectD a, RectD b)
        {
            double left = Math.Min(a.Left, b.Left);
            double bottom = Math.Min(a.Bottom, b.Bottom);
            double right = Math.Max(a.Right, b.Right);
            double top = Math.Max(a.Top, b.Top);
            return new RectD(left, bottom, right - left, top - bottom);
        }
    }
}
