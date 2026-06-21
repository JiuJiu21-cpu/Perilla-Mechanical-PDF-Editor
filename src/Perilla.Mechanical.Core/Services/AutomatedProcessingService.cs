using System;
using System.Collections.Generic;
using System.Drawing;
using Perilla.Mechanical.Core.Models;
using Perilla.Mechanical.Core.Pdf;

namespace Perilla.Mechanical.Core.Services
{
    public class AutoProcessingOptions
    {
        public NumberingOptions Numbering = new NumberingOptions();
        public double RenderScale = 2.0;
    }

    /// <summary>
    /// 一键自动化处理。调用者给一个 PDF 文件路径，
    /// 它依次解析 → 识别 → 编号 → 渲染。
    /// 结果含：每页的识别项 + 气泡，以及渲染出的 Bitmap (已把气泡绘制在上面)。
    /// </summary>
    public class AutomatedProcessingService
    {
        private readonly PdfParsingService _parser;
        private readonly DrawingRecognitionService _recognition;
        private readonly BubbleNumberingService _numbering;

        public AutomatedProcessingService(PdfParsingService parser,
                                          DrawingRecognitionService recognition,
                                          BubbleNumberingService numbering)
        {
            _parser = parser;
            _recognition = recognition;
            _numbering = numbering;
        }

        public class PageAutoResult
        {
            public int PageIndex;
            public SizeF PageSize;
            public List<RecognizedItem> Items;
            public List<Bubble> Bubbles;
        }

        public List<PageAutoResult> Run(string pdfPath,
                                         AutoProcessingOptions options,
                                         List<Bubble> existingManualBubblesByPageBubbles = null,
                                         Action<string> log = null)
        {
            if (options == null) options = new AutoProcessingOptions();
            if (string.IsNullOrWhiteSpace(pdfPath))
                throw new ArgumentException("PDF 路径不能为空", "pdfPath");
            var result = new List<PageAutoResult>();
            log = log ?? (s => { });

            // 仅在 parser 未加载或加载了不同文档时重新加载，避免重复分配句柄
            if (string.IsNullOrEmpty(_parser.FilePath) || _parser.FilePath != pdfPath || _parser.PageCount == 0)
            {
                _parser.Load(pdfPath);
            }

            if (_parser.PageCount == 0)
            {
                log("  警告：PDF 无任何页面");
                return result;
            }

            for (int pi = 0; pi < _parser.PageCount; pi++)
            {
                log(string.Format("Page {0}/{1} ...", pi + 1, _parser.PageCount));
                SizeF size;
                List<TextPrimitive> texts;
                List<GraphicPrimitive> paths;
                DrawingRecognitionService.RecognitionResult recognized = null;
                try
                {
                    size = _parser.GetPageSize(pi);
                    texts = _parser.ExtractTexts(pi) ?? new List<TextPrimitive>();
                    log(string.Format("  {0} text primitives", texts.Count));

                    paths = _parser.ExtractPathsAndRects(pi) ?? new List<GraphicPrimitive>();
                    log(string.Format("  {0} path primitives", paths.Count));

                    recognized = _recognition.Run(texts, paths, pi, size.Width, size.Height);
                    log(string.Format("  {0} recognized items", recognized.Items.Count));
                }
                catch (Exception ex)
                {
                    log("  解析失败：" + ex.Message);
                    continue;
                }

                var manualBubbles = new List<Bubble>();
                if (existingManualBubblesByPageBubbles != null)
                {
                    foreach (var b in existingManualBubblesByPageBubbles)
                    {
                        if (b.PageIndex == pi && b.IsManual) manualBubbles.Add(b);
                    }
                }

                var bubbles = _numbering.Assign(recognized.Items, options.Numbering, manualBubbles);
                log(string.Format("  {0} bubbles", bubbles.Count));

                result.Add(new PageAutoResult
                {
                    PageIndex = pi,
                    PageSize = size,
                    Items = recognized.Items,
                    Bubbles = bubbles
                });
            }
            return result;
        }
    }
}
