using System;
using System.Collections.Generic;
using Perilla.Mechanical.Core.Models;
using Perilla.Mechanical.Core.Recognition;

namespace Perilla.Mechanical.Core.Services
{
    /// <summary>
    /// 图纸识别服务：串联 LinearDimension / GDT / Annotation 识别器，
    /// 输出一个合并后的识别项列表，每个项都带有类型与位置。
    /// </summary>
    public class DrawingRecognitionService
    {
        private readonly LinearDimensionRecognizer _linearRecognizer;
        private readonly GDTToleranceRecognizer _gdtRecognizer;
        private readonly AnnotationRecognizer _annotationRecognizer;

        public DrawingRecognitionService()
        {
            _linearRecognizer = new LinearDimensionRecognizer();
            _gdtRecognizer = new GDTToleranceRecognizer();
            _annotationRecognizer = new AnnotationRecognizer();
        }

        public class RecognitionResult
        {
            public List<RecognizedItem> Items;
            public HashSet<TextPrimitive> UsedTexts;
        }

        public RecognitionResult Run(List<TextPrimitive> texts,
                                    List<GraphicPrimitive> paths,
                                    int pageIndex,
                                    float pageWidth,
                                    float pageHeight)
        {
            var used = new HashSet<TextPrimitive>();
            var items = new List<RecognizedItem>();

            // 1) 先 GDT（框优先，因为框是强视觉元素）
            var gdts = _gdtRecognizer.Recognize(texts, paths, pageIndex);
            foreach (var g in gdts)
            {
                foreach (var p in g.SourcePrimitives)
                {
                    var tp = p as TextPrimitive;
                    if (tp != null) used.Add(tp);
                }
                items.Add(g);
            }

            // 2) 再线性尺寸
            var dims = _linearRecognizer.Recognize(texts, paths, pageIndex,
                (int)Math.Ceiling(pageWidth), (int)Math.Ceiling(pageHeight));
            foreach (var d in dims)
            {
                if (used.Count > 0)
                {
                    // 与已识别 GDT 冲突则跳过
                    bool conflict = false;
                    foreach (var tp in d.SourcePrimitives)
                    {
                        var txt = tp as TextPrimitive;
                        if (txt != null && used.Contains(txt)) { conflict = true; break; }
                    }
                    if (conflict) continue;
                }
                foreach (var tp in d.SourcePrimitives)
                {
                    var txt = tp as TextPrimitive;
                    if (txt != null) used.Add(txt);
                }
                items.Add(d);
            }

            // 3) 剩余文本作为 Annotation
            var annotations = _annotationRecognizer.Recognize(texts, used, pageIndex);
            items.AddRange(annotations);

            return new RecognitionResult { Items = items, UsedTexts = used };
        }
    }
}
