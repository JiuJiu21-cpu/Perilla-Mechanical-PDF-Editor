using System;

namespace Perilla.Mechanical.Core.Models
{
    /// <summary>
    /// 2D 矩形（以 PDF 坐标系为准，左下为原点，单位为 pt）。
    /// </summary>
    public struct RectD
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;

        public double Left { get { return X; } }
        public double Bottom { get { return Y; } }
        public double Right { get { return X + Width; } }
        public double Top { get { return Y + Height; } }
        public double CenterX { get { return X + Width * 0.5; } }
        public double CenterY { get { return Y + Height * 0.5; } }

        public RectD(double x, double y, double width, double height)
        {
            X = x; Y = y; Width = width; Height = height;
        }

        public bool Contains(double px, double py)
        {
            return px >= Left && px <= Right && py >= Bottom && py <= Top;
        }

        public bool IntersectsWith(RectD other)
        {
            return !(other.Right < Left || other.Left > Right
                  || other.Top < Bottom || other.Bottom > Top);
        }

        public RectD InflatedBy(double pad)
        {
            return new RectD(X - pad, Y - pad, Width + 2 * pad, Height + 2 * pad);
        }
    }

    /// <summary>
    /// 2D 点（PDF pt 坐标）。
    /// </summary>
    public struct PointD
    {
        public double X;
        public double Y;
        public PointD(double x, double y) { X = x; Y = y; }
    }
}
