@echo off
rem Inicia o Emissor Fiscal e abre o navegador. Para parar, feche esta janela ou pressione Ctrl+C.
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo O .NET SDK nao foi encontrado.
    echo Instale o .NET SDK 10 em https://dotnet.microsoft.com/download e rode este arquivo de novo.
    pause
    exit /b 1
)

echo Iniciando o Emissor Fiscal em http://localhost:5241 ...
echo (a primeira execucao pode demorar um pouco enquanto o projeto e compilado)
echo.

rem Abre o navegador alguns segundos depois, quando o servidor ja estiver no ar.
start "" /b cmd /c "timeout /t 12 /nobreak >nul & start http://localhost:5241"

dotnet run --project src\FiscalPlatform.Web --launch-profile http

echo.
echo O Emissor Fiscal foi encerrado.
pause
