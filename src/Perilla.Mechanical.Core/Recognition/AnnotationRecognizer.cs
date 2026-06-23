using System;
using System.Collections.Generic;
using Perilla.Mechanical.Core.Models;

namespace Perilla.Mechanical.Core.Recognition
{
    /// <summary>
    /// 文本注解识别器：将所有未被归类为尺寸或公差的文本
    /// 作为图纸注解（Annotation）输出。
    /// </summary>
    public class AnnotationRecognizer
    {
        public List<RecognizedItem> Recognize(List<TextPrimitive> texts,
                                              HashSet<TextPrimitive> alreadyUsed,
                                              int pageIndex)
        {
            var result = new List<RecognizedItem>();
            if (texts == null || texts.Count == 0) return result;

            int id = 1;
            foreach (var t in texts)
            {
                if (alreadyUsed.Contains(t)) continue;
                if (string.IsNullOrWhiteSpace(t.Text)) continue;
                if (t.Text.Length < 2) continue; // 排除孤立字符

                var item = new RecognizedItem
                {
                    Id = id++,
                    Kind = RecognitionKind.Annotation,
                    PageIndex = pageIndex,
                    Position = t.Bounds,
                    RawText = t.Text,
                    Confidence = Confidence.Medium
                };
                item.SourcePrimitives.Add(t);
                result.Add(item);
            }
            return result;
        }
    }
}
