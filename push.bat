@echo off
rem Usage:
rem   push.bat                 -> review changes, confirm, commit + push
rem   push.bat v1.1.0          -> same, then tag + push tag (triggers release)
setlocal

cd /d "%~dp0"

rem --- show pending changes ---
git diff --quiet && git diff --cached --quiet
if errorlevel 1 (
    echo Changed files:
    echo ----------------------------------------
    git status --short
    echo ----------------------------------------
    set /p CONFIRM=Commit ALL of the above? [Y/N]:
    if /i not "%CONFIRM%"=="Y" (
        echo Aborted. Nothing committed.
        echo Tip: use VS Code Git panel to stage files selectively.
        pause
        exit /b 0
    )
    set /p MSG=Commit message:
    if "%MSG%"=="" set MSG=update
    git add -A
    git commit -m "%MSG%"
    if errorlevel 1 exit /b 1
) else (
    echo No local changes to commit.
)

rem --- push main ---
git push
if errorlevel 1 exit /b 1

rem --- optional: tag + push tag to trigger release ---
if not "%~1"=="" (
    git tag %~1
    if errorlevel 1 exit /b 1
    git push origin %~1
    if errorlevel 1 exit /b 1
    echo.
    echo Tag %~1 pushed. GitHub Actions will build and publish the release.
)

echo.
echo Done.
pause
