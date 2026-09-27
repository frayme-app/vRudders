// Copyright (c) 2026 Ermis Catevatis. MIT license; see ../LICENSE.
// Build-only launcher: NSIS must not open a console while signing its uninstaller.
#define UNICODE
#define _UNICODE
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE previous, PWSTR command, int show)
{
    UNREFERENCED_PARAMETER(instance);
    UNREFERENCED_PARAMETER(previous);
    UNREFERENCED_PARAMETER(show);
    if (!command || !*command) return ERROR_BAD_ARGUMENTS;
    STARTUPINFOW startup = {0};
    PROCESS_INFORMATION process = {0};
    startup.cb = sizeof(startup);
    startup.dwFlags = STARTF_USESHOWWINDOW;
    startup.wShowWindow = SW_HIDE;
    if (!CreateProcessW(NULL, command, NULL, NULL, FALSE, CREATE_NO_WINDOW,
        NULL, NULL, &startup, &process)) return (int)GetLastError();
    CloseHandle(process.hThread);
    DWORD result = ERROR_GEN_FAILURE;
    if (WaitForSingleObject(process.hProcess, INFINITE) == WAIT_OBJECT_0)
        GetExitCodeProcess(process.hProcess, &result);
    CloseHandle(process.hProcess);
    return (int)result;
}
