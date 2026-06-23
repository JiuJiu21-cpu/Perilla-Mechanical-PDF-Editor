using System;

namespace Perilla.Mechanical.Core.Models
{
    /// <summary>
    /// 训练样本：用于AI识别模型的训练数据。
    /// 每个样本记录一个被标注的图纸要素及其正确类型，
    /// 系统通过积累样本逐步提升识别准确率。
    /// </summary>
    public class TrainingSample
    {
        public string SampleId { get; set; }
        public string PdfFileName { get; set; }
        public int PageIndex { get; set; }
        public RectD ElementRegion { get; set; }
        public RecognitionKind CorrectType { get; set; }
        public RecognitionKind? RecognizedType { get; set; }
        public string RawText { get; set; }
        public bool IsCorrect { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Source { get; set; }

        public TrainingSample()
        {
            SampleId = Guid.NewGuid().ToString("N");
            CreatedAt = DateTime.Now;
            IsCorrect = true;
        }
    }
}
