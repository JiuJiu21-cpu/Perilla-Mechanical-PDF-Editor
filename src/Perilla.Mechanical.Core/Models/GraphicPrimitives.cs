using System;

namespace Perilla.Mechanical.Core.Models
{
    /// <summary>
    /// 从 PDF 页面提取的图形原语 - 作为后续识别算法的输入。
    /// </summary>
    public abstract class GraphicPrimitive
    {
        public RectD Bounds;
        public int PageIndex;
    }

    /// <summary>
    /// 直线段 (可能是尺寸线、延伸线、图形边界)
    /// </summary>
    public class LinePrimitive : GraphicPrimitive
    {
        public PointD Start;
        public PointD End;
        public double Width;

        public bool IsNearHorizontal(double tol = 1.0)
        {
            return Math.Abs(End.Y - Start.Y) <= tol;
        }

        public bool IsNearVertical(double tol = 1.0)
        {
            return Math.Abs(End.X - Start.X) <= tol;
        }

        public double Length()
        {
            double dx = End.X - Start.X;
            double dy = End.Y - Start.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    /// <summary>
    /// 文本项 (数字、公差、注解文字)
    /// </summary>
    public class TextPrimitive : GraphicPrimitive
    {
        public string Text;
        public double FontSizePt;
        public string FontName;
        public bool LooksNumeric;
    }

    /// <summary>
    /// 矩形/闭合路径 (用于检测 GDT 特征框)
    /// </summary>
    public class RectPrimitive : GraphicPrimitive
    {
        public bool IsClosed;
    }

    /// <summary>
    /// 圆/圆弧 — 可能对应 R 尺寸或箭头圆弧端。
    /// </summary>
    public class CirclePrimitive : GraphicPrimitive
    {
        public PointD Center;
        public double Radius;
    }
}
