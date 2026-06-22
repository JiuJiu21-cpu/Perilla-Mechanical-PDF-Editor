using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Perilla.Mechanical.Core.Pdf
{
    /// <summary>
    /// 轻量级 PDFium 原生 API 封装。
    /// 注意：本项目主要依赖 PdfiumViewer 托管 API（见 PdfParsingService.cs）。
    /// 此处仅保留少量补充 P/Invoke，用于获取字符级边界矩形等 PdfiumViewer
    /// 未公开的高级功能；调用方需确保已通过 PdfiumViewer 完成库初始化。
    /// </summary>
    internal static class PdfiumNative
    {
        private const string DLL = "pdfium.dll";

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDF_InitLibrary();

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDF_LoadDocument(string path, string password);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDF_GetPageCount(IntPtr document);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDF_LoadPage(IntPtr document, int index);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDF_ClosePage(IntPtr page);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern double FPDF_GetPageWidthF(IntPtr page);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern double FPDF_GetPageHeightF(IntPtr page);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFText_LoadPage(IntPtr page);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFText_CountChars(IntPtr text_page);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint FPDFText_GetUnicode(IntPtr text_page, int index);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern double FPDFText_GetFontSize(IntPtr text_page, int index);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFText_GetCharBox(IntPtr text_page, int index,
            out double left, out double bottom, out double right, out double top);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDFText_ClosePage(IntPtr text_page);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDF_CloseDocument(IntPtr document);
    }
}
