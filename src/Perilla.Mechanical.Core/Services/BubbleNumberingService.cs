using System;
using System.Collections.Generic;
using Perilla.Mechanical.Core.Models;

namespace Perilla.Mechanical.Core.Services
{
    /// <summary>
    /// 气泡编号服务：
    /// - 按 Top→Bottom / Left→Right 的顺序遍历已识别项
    /// - 给每个 item 生成一个气泡（circle），默认放在其右上，避免覆盖被标注文本
    /// - 圆周扫描碰撞检测：优先避免与已有气泡碰撞，也避免覆盖被标注要素
    /// - 手动气泡（IsManual=true）保留，不被自动覆盖
    /// </summary>
    public class BubbleNumberingService
    {
        public List<Bubble> Assign(IEnumerable<RecognizedItem> items,
                                    NumberingOptions options,
                                    List<Bubble> existingManualBubbles = null)
        {
            if (options == null) options = new NumberingOptions();
            var list = new List<RecognizedItem>(items ?? new RecognizedItem[0]);

            // 按指定顺序排序
            if (options.Order == NumberingOptions.OrderBy.TopToBottomLeftToRight)
            {
                list.Sort((a, b) =>
                {
                    int y = b.Position.CenterY.CompareTo(a.Position.CenterY);
                    if (y != 0) return y;
                    return a.Position.CenterX.CompareTo(b.Position.CenterX);
                });
            }
            else
            {
                list.Sort((a, b) =>
                {
                    int x = a.Position.CenterX.CompareTo(b.Position.CenterX);
                    if (x != 0) return x;
                    return b.Position.CenterY.CompareTo(a.Position.CenterY);
                });
            }

            var placed = new List<Bubble>();
            if (existingManualBubbles != null) placed.AddRange(existingManualBubbles);

            int num = options.StartNumber;
            foreach (var item in list)
            {
                double radius = options.BubbleRadius;
                double padding = options.CollisionPadding;
                double cx, cy;
                ComputeBubbleCenter(item, radius, padding, placed, out cx, out cy);

                var bubble = new Bubble
                {
                    Number = num,
                    Label = string.IsNullOrEmpty(options.Prefix) ? num.ToString() : options.Prefix + num,
                    Center = new PointD(cx, cy),
                    Radius = radius,
                    Kind = item.Kind,
                    LinkedText = item.RawText,
                    PageIndex = item.PageIndex,
                    IsManual = false,
                    SourceBounds = item.Position
                };
                placed.Add(bubble);
                num++;
            }

            // 重新按位置编号，使结果从上到下、从左到右升序
            var sortedByPos = new List<Bubble>(placed);
            sortedByPos.Sort((a, b) =>
            {
                int y = b.Center.Y.CompareTo(a.Center.Y);
                if (y != 0) return y;
                return a.Center.X.CompareTo(b.Center.X);
            });
            int n = options.StartNumber;
            foreach (var b in sortedByPos)
            {
                b.Number = n;
                b.Label = string.IsNullOrEmpty(options.Prefix) ? n.ToString() : options.Prefix + n;
                n++;
            }
            return sortedByPos;
        }

        /// <summary>为被标注要素计算一个合理的气泡中心：
        /// 首选位置在要素右上；若发生碰撞或落入要素自身矩形，
        /// 沿 8 方向外扩寻找空位，直到找到一个既不遮挡其他气泡，
        /// 也不遮挡当前要素的位置。</summary>
        private static void ComputeBubbleCenter(RecognizedItem item,
                                                 double radius, double padding,
                                                 List<Bubble> placed, out double cx, out double cy)
        {
            // 首选：右上，距离文本边界一个 radius + padding 的偏移
            double originX = item.Position.Right + radius + padding;
            double originY = item.Position.Top + radius * 0.5;

            if (!CollidesWithAny(originX, originY, radius, padding, placed, item))
            {
                cx = originX; cy = originY;
                return;
            }

            // 按 8 方向外扩搜索，环半径逐步扩大
            double[] angles = { 0, Math.PI * 0.25, -Math.PI * 0.25,
                                Math.PI * 0.5, -Math.PI * 0.5,
                                Math.PI * 0.75, -Math.PI * 0.75, Math.PI };
            double stepX = Math.Max(item.Position.Width, radius * 2);
            double stepY = Math.Max(item.Position.Height, radius * 2);

            for (int ring = 1; ring <= 6; ring++)
            {
                double rX = stepX * ring;
                double rY = stepY * ring;
                foreach (double angle in angles)
                {
                    double nx = item.Position.CenterX + Math.Cos(angle) * rX;
                    double ny = item.Position.CenterY + Math.Sin(angle) * rY;
                    if (!CollidesWithAny(nx, ny, radius, padding, placed, item))
                    {
                        cx = nx; cy = ny;
                        return;
                    }
                }
            }

            // 极端情况：所有方向都冲突，退回至首选偏移
            cx = originX; cy = originY;
        }

        private static bool CollidesWithAny(double cx, double cy, double radius, double padding,
                                            List<Bubble> bubbles, RecognizedItem current)
        {
            // 与其他气泡碰撞
            for (int i = 0; i < bubbles.Count; i++)
            {
                var b = bubbles[i];
                double dx = b.Center.X - cx;
                double dy = b.Center.Y - cy;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < b.Radius + radius + padding) return true;
            }
            // 避免覆盖当前被标注要素本身（气泡矩形与要素矩形相交视为遮挡）
            if (current != null)
            {
                double overlapL = cx - radius;
                double overlapR = cx + radius;
                double overlapB = cy - radius;
                double overlapT = cy + radius;
                if (overlapR > current.Position.Left && overlapL < current.Position.Right &&
                    overlapT > current.Position.Bottom && overlapB < current.Position.Top)
                    return true;
            }
            return false;
        }
    }
}
