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

if not exist bin mkdir bin

set REF=/reference:"%FXREF%\mscorlib.dll" /reference:"%FXREF%\System.dll" /reference:"%FXREF%\System.Core.dll" /reference:"%FXREF%\System.Drawing.dll" /reference:"%FXREF%\System.Windows.Forms.dll" /reference:"%FXREF%\System.Xml.dll" /reference:"%FXREF%\System.Xml.Linq.dll" /reference:"%CD%\lib\managed\PdfiumViewer.dll" /reference:"%CD%\lib\managed\EPPlus.dll"

set SRC=src\Perilla.Mechanical.Core\Models\Geometry.cs src\Perilla.Mechanical.Core\Models\RecognitionAndBubble.cs src\Perilla.Mechanical.Core\Models\TrainingSample.cs src\Perilla.Mechanical.Core\Models\GraphicPrimitives.cs src\Perilla.Mechanical.Core\Pdf\PdfiumNative.cs src\Perilla.Mechanical.Core\Pdf\PdfParsingService.cs src\Perilla.Mechanical.Core\Recognition\LinearDimensionRecognizer.cs src\Perilla.Mechanical.Core\Recognition\GDTToleranceRecognizer.cs src\Perilla.Mechanical.Core\Recognition\AnnotationRecognizer.cs src\Perilla.Mechanical.Core\Services\DrawingRecognitionService.cs src\Perilla.Mechanical.Core\Services\BubbleNumberingService.cs src\Perilla.Mechanical.Core\Services\AutomatedProcessingService.cs src\Perilla.Mechanical.Core\Services\UndoRedoService.cs src\Perilla.Mechanical.Core\Services\TrainingSampleStore.cs src\Perilla.Mechanical.Export\ImageExporter.cs src\Perilla.Mechanical.Export\ExcelExporter.cs src\Perilla.Mechanical.Export\PdfExporter.cs src\Perilla.Mechanical.App\MainForm.cs src\Perilla.Mechanical.App\Program.cs

echo.
echo === Building WinForms App ===
"%CSC%" /nologo /target:winexe /platform:x64 /langversion:7.3 /out:bin\Perilla.Mechanical.App.exe %REF% %SRC%
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo BUILD OK

copy /y lib\managed\PdfiumViewer.dll bin\PdfiumViewer.dll > nul
copy /y lib\managed\EPPlus.dll bin\EPPlus.dll > nul
copy /y src\Perilla.Mechanical.App\app.config bin\Perilla.Mechanical.App.exe.config > nul
echo Config file OK

if exist lib\native\pdfium.dll (copy /y lib\native\pdfium.dll bin\pdfium.dll > nul & echo pdfium.dll OK)

echo.
echo === Success: bin\Perilla.Mechanical.App.exe ===
exit /b 0
