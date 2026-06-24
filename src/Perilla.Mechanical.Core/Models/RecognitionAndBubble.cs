using System;
using System.Collections.Generic;

namespace Perilla.Mechanical.Core.Models
{
    public enum RecognitionKind
    {
        LinearDimension,  // 线性尺寸 (例如 50, ⌀20 H7)
        GDTTolerance,      // 形位公差框
        Annotation,        // 图纸注解文本
        Bubble             // 手动气泡
    }

    public enum Confidence
    {
        Low = 1,
        Medium = 2,
        High = 3
    }

    /// <summary>
    /// 识别结果：一个已被归类的机械工程要素。
    /// </summary>
    public class RecognizedItem
    {
        public int Id;
        public RecognitionKind Kind;
        public string RawText;         // 原始文本（用于 Excel 导出）
        public RectD Position;         // 页面坐标
        public int PageIndex;
        public Confidence Confidence;
        public List<GraphicPrimitive> SourcePrimitives;

        public RecognizedItem()
        {
            SourcePrimitives = new List<GraphicPrimitive>();
        }
    }

    /// <summary>
    /// 气泡标注。
    /// </summary>
    public class Bubble
    {
        public int Number;              // 自动编号
        public string Label;            // 显示在圆圈中的文字
        public PointD Center;           // 页面坐标
        public double Radius;           // 页面坐标中的半径（pt）
        public RecognitionKind Kind;    // 对应被识别要素的类型
        public string LinkedText;       // 对应原始文本（用于 Excel）
        public int PageIndex;
        public bool IsManual;           // 手动创建（不被自动算法覆盖）
        public RectD SourceBounds;      // 与被标注要素的 bounds 对应
        public string CircleColor;      // 气泡描边颜色（HTML格式，如 "#DC3C1E"）
        public string FillColor;        // 气泡填充颜色
        public string TextColor;        // 气泡文字颜色

        public override string ToString()
        {
            return string.Format("Bubble #{0} at {1} ({2})", Number, Center, Kind);
        }
    }

    public class NumberingOptions
    {
        public int StartNumber = 1;
        public string Prefix = "";
        public double BubbleRadius = 12;    // pt
        public double CollisionPadding = 6; // pt
        public enum OrderBy { TopToBottomLeftToRight, LeftToRightTopToBottom }
        public OrderBy Order = OrderBy.TopToBottomLeftToRight;
    }
}
