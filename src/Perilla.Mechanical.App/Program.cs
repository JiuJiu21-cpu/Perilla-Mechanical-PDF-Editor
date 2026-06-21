using System;
using System.Threading;
using System.Windows.Forms;

namespace Perilla.Mechanical.App
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // 初始许可上下文：EPPlus 5.x 需要
            OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
