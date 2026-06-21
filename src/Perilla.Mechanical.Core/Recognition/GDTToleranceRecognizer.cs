using System;
using System.Collections.Generic;
using Perilla.Mechanical.Core.Models;

namespace Perilla.Mechanical.Core.Recognition
{
    /// <summary>
    /// 形位公差框识别器。
    /// 策略：查找有"矩形特征"的 path 元素（一个大矩形 + 内部若干竖线），
    /// 然后在矩形内部查找数字/符号文本。
    /// 为简洁起见：这里用启发式——扫描所有 RectPrimitive，
    /// 若某个矩形在其上下/左右相邻位置有另一个矩形，且内部有数字/符号文本，则识别为 GDT 框。
    /// </summary>
    public class GDTToleranceRecognizer
    {
        public List<RecognizedItem> Recognize(List<TextPrimitive> texts,
                                               List<GraphicPrimitive> paths,
                                               int pageIndex)
        {
            var result = new List<RecognizedItem>();
            if (paths == null || paths.Count == 0) return result;

            int id = 1;

            // 1) 找"宽高比在 [1.5, 10] 之间的矩形"作为候选公差框
            var candidates = new List<RectPrimitive>();
            foreach (var p in paths)
            {
                var r = p as RectPrimitive;
                if (r == null) continue;
                double w = r.Bounds.Width, h = r.Bounds.Height;
                if (w < 10 || h < 5) continue; // 太小的排除
                double aspect = w / Math.Max(h, 0.1);
                if (aspect >= 1.5 && aspect <= 15.0)
                {
                    candidates.Add(r);
                }
            }

            // 2) 对每个候选，检查其内部是否有文本
            foreach (var candidate in candidates)
            {
                var innerTexts = new List<TextPrimitive>();
                foreach (var t in texts)
                {
                    if (candidate.Bounds.Contains(t.Bounds.CenterX, t.Bounds.CenterY) ||
                        candidate.Bounds.IntersectsWith(t.Bounds))
                    {
                        innerTexts.Add(t);
                    }
                }

                if (innerTexts.Count == 0) continue;

                // 至少一个文本看起来像数字/符号
                bool anyNumeric = false;
                string rawText = "";
                innerTexts.Sort((a, b) => a.Bounds.CenterX.CompareTo(b.Bounds.CenterX));
                foreach (var t in innerTexts)
                {
                    if (t.LooksNumeric) anyNumeric = true;
                    rawText = rawText == "" ? t.Text : rawText + " " + t.Text;
                }
                if (!anyNumeric && innerTexts.Count < 2) continue;

                Confidence conf;
                if (innerTexts.Count >= 2 && innerTexts.Count <= 5 && anyNumeric) conf = Confidence.High;
                else if (anyNumeric) conf = Confidence.Medium;
                else conf = Confidence.Low;

                var item = new RecognizedItem
                {
                    Id = id++,
                    Kind = RecognitionKind.GDTTolerance,
                    Confidence = conf,
                    PageIndex = pageIndex,
                    Position = candidate.Bounds,
                    RawText = rawText
                };
                item.SourcePrimitives.Add(candidate);
                foreach (var t in innerTexts) item.SourcePrimitives.Add(t);

                result.Add(item);
            }

            return result;
        }
    }
}
