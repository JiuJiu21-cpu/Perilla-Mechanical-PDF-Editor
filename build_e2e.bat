@echo off
setlocal
cd /d "%~dp0"

echo === Detecting environment ===

set CSC=%CD%\lib\roslyn\csc.exe
if not exist "%CSC%" (
  echo ERROR: Roslyn compiler not found at lib\roslyn\csc.exe
  exit /b 1
)
echo Using Roslyn csc (C# 7.3+ support) from lib\roslyn

set FXROOT=C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework
for %%v in (v4.8 v4.7.2 v4.7.1 v4.7 v4.6.2) do (
  if exist "%FXROOT%\%%v\mscorlib.dll" (
    set FXVER=%%v
    goto :fxset
  )
)
:fxset
echo Framework: %FXVER%
set FXREF=%FXROOT%\%FXVER%

if not exist bin_e2e mkdir bin_e2e

set REF=/reference:"%FXREF%\mscorlib.dll" /reference:"%FXREF%\System.dll" /reference:"%FXREF%\System.Core.dll" /reference:"%FXREF%\System.Drawing.dll" /reference:"%FXREF%\System.Windows.Forms.dll" /reference:"%FXREF%\System.Xml.dll" /reference:"%FXREF%\System.Xml.Linq.dll" /reference:"%CD%\lib\managed\PdfiumViewer.dll" /reference:"%CD%\lib\managed\EPPlus.dll"

set SRC=src\Perilla.Mechanical.Core\Models\Geometry.cs src\Perilla.Mechanical.Core\Models\RecognitionAndBubble.cs src\Perilla.Mechanical.Core\Models\GraphicPrimitives.cs src\Perilla.Mechanical.Core\Pdf\PdfiumNative.cs src\Perilla.Mechanical.Core\Pdf\PdfParsingService.cs src\Perilla.Mechanical.Core\Recognition\LinearDimensionRecognizer.cs src\Perilla.Mechanical.Core\Recognition\GDTToleranceRecognizer.cs src\Perilla.Mechanical.Core\Recognition\AnnotationRecognizer.cs src\Perilla.Mechanical.Core\Services\DrawingRecognitionService.cs src\Perilla.Mechanical.Core\Services\BubbleNumberingService.cs src\Perilla.Mechanical.Core\Services\AutomatedProcessingService.cs src\Perilla.Mechanical.Export\ImageExporter.cs src\Perilla.Mechanical.Export\ExcelExporter.cs src\Perilla.Mechanical.Export\PdfExporter.cs src\Test\PerillaE2ETest.cs

echo.
echo === Building ===
"%CSC%" /nologo /target:exe /platform:x64 /langversion:7.3 /out:bin_e2e\PerillaE2ETest.exe %REF% %SRC%
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo BUILD OK

copy /y lib\managed\PdfiumViewer.dll bin_e2e\PdfiumViewer.dll > nul
copy /y lib\managed\EPPlus.dll bin_e2e\EPPlus.dll > nul

if exist lib\native\pdfium.dll (copy /y lib\native\pdfium.dll bin_e2e\pdfium.dll > nul & echo pdfium.dll OK) else (echo WARNING: pdfium.dll not found)

echo.
echo === Running E2E tests ===
cd bin_e2e
PerillaE2ETest.exe
set RC=%ERRORLEVEL%
echo.
echo Test exit code: %RC%
exit /b %RC%
