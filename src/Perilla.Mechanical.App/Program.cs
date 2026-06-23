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
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.ThreadException += OnUiThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            try
            {
                try
                {
                    OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
                }
                catch
                {
                }

                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                ShowFatalError(ex);
            }
        }

        private static void OnUiThreadException(object sender, ThreadExceptionEventArgs e)
        {
            try
            {
                ShowErrorDialog("运行时错误", e.Exception);
            }
            catch
            {
                try
                {
                    MessageBox.Show("发生严重错误: " + e.Exception.Message,
                        "错误", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                }
                catch { }
            }
        }

        private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                Exception ex = e.ExceptionObject as Exception;
                if (ex != null)
                {
                    ShowFatalError(ex);
                }
                else
                {
                    MessageBox.Show("发生不可恢复的错误，程序即将退出。",
                        "致命错误", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                }
            }
            catch { }
        }

        private static void ShowFatalError(Exception ex)
        {
            ShowErrorDialog("程序启动失败", ex);
        }

        private static void ShowErrorDialog(string title, Exception ex)
        {
            string msg = title + "：\r\n\r\n" +
                         "错误信息：" + ex.Message + "\r\n\r\n" +
                         "错误类型：" + ex.GetType().Name + "\r\n\r\n" +
                         "堆栈跟踪：\r\n" + ex.StackTrace;

            MessageBox.Show(msg, "Perilla Mechanical PDF Editor - 错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
