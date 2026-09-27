// Copyright (c) 2026 Ermis Catevatis. MIT license; see ../LICENSE.
// Installs/removes only the existing VRudders root device.
#define UNICODE
#define _UNICODE
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <setupapi.h>
#include <newdev.h>
#include <cfgmgr32.h>
#include <tlhelp32.h>
#include <wintrust.h>
#include <softpub.h>
#include <stdio.h>
#include <wchar.h>
#include <strsafe.h>
#include <mmsystem.h>

static const WCHAR HardwareId[] = L"root\\VRuddersPoc";
static const GUID HidClass = { 0x745a17a0, 0x74d3, 0x11d0, {0xb6,0xfe,0x00,0xa0,0xc9,0x0f,0x57,0xda} };

static DWORD Error(const WCHAR* operation)
{
    DWORD code = GetLastError();
    WCHAR message[1024] = {0};
    FormatMessageW(FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
        NULL, code, 0, message, ARRAYSIZE(message), NULL);
    if (code == ERROR_AUTHENTICODE_TRUST_NOT_ESTABLISHED)
        StringCchCopyW(message, ARRAYSIZE(message),
            L"Windows needs publisher approval. Run Setup normally (without /S) and review the Windows Security prompt.");
    fwprintf(stderr, L"%ls failed: %lu (0x%08lX). %ls\n", operation, code, code, message);
    return code ? code : ERROR_GEN_FAILURE;
}

static BOOL IsOurs(HDEVINFO set, SP_DEVINFO_DATA* device)
{
    WCHAR instance[512], ids[4096] = {0};
    DWORD type = 0, bytes = 0;
    if (!IsEqualGUID(&device->ClassGuid, &HidClass) ||
        !SetupDiGetDeviceInstanceIdW(set, device, instance, ARRAYSIZE(instance), NULL) ||
        _wcsnicmp(instance, L"ROOT\\", 5) != 0 ||
        !SetupDiGetDeviceRegistryPropertyW(set, device, SPDRP_HARDWAREID, &type,
            (BYTE*)ids, sizeof(ids) - 2 * sizeof(WCHAR), &bytes) || type != REG_MULTI_SZ) return FALSE;
    for (const WCHAR* id = ids; *id; id += wcslen(id) + 1)
        if (_wcsicmp(id, HardwareId) == 0) return TRUE;
    return FALSE;
}

static DWORD AppRunning(void)
{
    HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snapshot == INVALID_HANDLE_VALUE) return Error(L"Enumerating processes");
    PROCESSENTRY32W process = {0}; process.dwSize = sizeof(process);
    if (!Process32FirstW(snapshot, &process)) { DWORD code = Error(L"Reading process list"); CloseHandle(snapshot); return code; }
    do {
        if (_wcsicmp(process.szExeFile, L"VRudders.exe") == 0) {
            fwprintf(stderr, L"Close every VRudders window before installing or removing the driver.\n");
            CloseHandle(snapshot); return ERROR_BUSY;
        }
    } while (Process32NextW(snapshot, &process));
    DWORD code = GetLastError();
    CloseHandle(snapshot);
    return code == ERROR_NO_MORE_FILES ? ERROR_SUCCESS : code;
}

static DWORD Remove(HDEVINFO set, SP_DEVINFO_DATA* device, BOOL* reboot)
{
    SP_REMOVEDEVICE_PARAMS remove = {0};
    remove.ClassInstallHeader.cbSize = sizeof(SP_CLASSINSTALL_HEADER);
    remove.ClassInstallHeader.InstallFunction = DIF_REMOVE;
    remove.Scope = DI_REMOVEDEVICE_GLOBAL;
    if (!SetupDiSetClassInstallParamsW(set, device, &remove.ClassInstallHeader, sizeof(remove)) ||
        !SetupDiCallClassInstaller(DIF_REMOVE, set, device)) return Error(L"Removing VRudders device");
    SP_DEVINSTALL_PARAMS_W install = {0}; install.cbSize = sizeof(install);
    if (SetupDiGetDeviceInstallParamsW(set, device, &install) && (install.Flags & (DI_NEEDREBOOT | DI_NEEDRESTART))) *reboot = TRUE;
    return ERROR_SUCCESS;
}

static DWORD CheckCatalog(const WCHAR* path)
{
    WINTRUST_FILE_INFO file = {0}; file.cbStruct = sizeof(file); file.pcwszFilePath = path;
    WINTRUST_DATA trust = {0}; trust.cbStruct = sizeof(trust);
    trust.dwUIChoice = WTD_UI_NONE; trust.fdwRevocationChecks = WTD_REVOKE_WHOLECHAIN;
    trust.dwUnionChoice = WTD_CHOICE_FILE; trust.pFile = &file; trust.dwStateAction = WTD_STATEACTION_VERIFY;
    GUID action = WINTRUST_ACTION_GENERIC_VERIFY_V2;
    LONG status = WinVerifyTrust((HWND)INVALID_HANDLE_VALUE, &action, &trust);
    trust.dwStateAction = WTD_STATEACTION_CLOSE;
    WinVerifyTrust((HWND)INVALID_HANDLE_VALUE, &action, &trust);
    if (status != ERROR_SUCCESS) fwprintf(stderr, L"Driver catalog trust check failed: 0x%08lX. Keep Windows security settings enabled.\n", (DWORD)status);
    return (DWORD)status;
}

static void RefreshLegacyControllerName(void)
{
    // DirectInput/joy.cpl can retain a per-user OEM label after a driver upgrade.
    // Only migrate our exact old label, after installing our verified package.
    // Never create an override, alter a custom name, or visit other user hives.
    static const WCHAR path[] = L"System\\CurrentControlSet\\Control\\MediaProperties\\PrivateProperties\\Joystick\\OEM\\VID_DEED&PID_FEED";
    static const WCHAR renamed[] = L"VRudders Yaw";
    const HKEY hives[] = { HKEY_CURRENT_USER, HKEY_LOCAL_MACHINE };
    BOOL changed = FALSE;
    for (DWORD i = 0; i < ARRAYSIZE(hives); ++i) {
        HKEY key = NULL;
        LSTATUS status = RegOpenKeyExW(hives[i], path, 0, KEY_QUERY_VALUE | KEY_SET_VALUE, &key);
        if (status == ERROR_FILE_NOT_FOUND) continue;
        if (status != ERROR_SUCCESS) {
            fwprintf(stderr, L"Controller label refresh skipped (registry access %lu).\n", (DWORD)status);
            continue;
        }
        WCHAR name[128] = {0}; DWORD bytes = sizeof(name);
        status = RegGetValueW(key, NULL, L"OEMName", RRF_RT_REG_SZ | RRF_ZEROONFAILURE, NULL, name, &bytes);
        if (status == ERROR_SUCCESS && _wcsicmp(name, L"VRudders POC") == 0) {
            status = RegSetValueExW(key, L"OEMName", 0, REG_SZ, (const BYTE*)renamed, sizeof(renamed));
            if (status == ERROR_SUCCESS) changed = TRUE;
            else fwprintf(stderr, L"Controller label refresh failed: %lu. Restart the game after upgrading.\n", (DWORD)status);
        }
        RegCloseKey(key);
    }
    if (changed) {
        MMRESULT result = joyConfigChanged(0);
        wprintf(L"Legacy controller label updated to VRudders Yaw (Windows refresh %u). Restart games to reload their device list.\n", result);
    }
}

static DWORD Install(HDEVINFO set, DWORD count, BOOL verifyOnly, BOOL nonInteractive)
{
    WCHAR inf[MAX_PATH], catalog[MAX_PATH];
    DWORD length = GetModuleFileNameW(NULL, inf, ARRAYSIZE(inf));
    if (!length || length >= ARRAYSIZE(inf)) return ERROR_FILENAME_EXCED_RANGE;
    WCHAR* slash = wcsrchr(inf, L'\\');
    if (!slash) return ERROR_BAD_PATHNAME;
    slash[1] = 0;
    if (FAILED(StringCchCopyW(catalog, ARRAYSIZE(catalog), inf)) ||
        FAILED(StringCchCatW(inf, ARRAYSIZE(inf), L"driver\\VRudders.inf")) ||
        FAILED(StringCchCatW(catalog, ARRAYSIZE(catalog), L"driver\\vrudders.cat"))) return ERROR_FILENAME_EXCED_RANGE;
    DWORD result = CheckCatalog(catalog);
    if (result) return result;
    GUID infClass; WCHAR className[256];
    if (!SetupDiGetINFClassW(inf, &infClass, className, ARRAYSIZE(className), NULL)) return Error(L"Reading driver INF");
    if (!IsEqualGUID(&infClass, &HidClass)) return ERROR_INVALID_DATA;
    if (verifyOnly) { wprintf(L"Catalog signature trusted; INF targets HIDClass. Windows checks catalog membership during installation.\n"); return ERROR_SUCCESS; }
    SP_DEVINFO_DATA created = {0}; created.cbSize = sizeof(created);
    BOOL registered = FALSE, reboot = FALSE;
    if (count == 0) {
        if (!SetupDiCreateDeviceInfoW(set, className, &HidClass, L"VRudders Yaw", NULL, DICD_GENERATE_ID, &created)) return Error(L"Creating VRudders device");
        static const WCHAR multiId[] = L"root\\VRuddersPoc\0";
        if (!SetupDiSetDeviceRegistryPropertyW(set, &created, SPDRP_HARDWAREID, (const BYTE*)multiId, sizeof(multiId)) ||
            !SetupDiCallClassInstaller(DIF_REGISTERDEVICE, set, &created)) return Error(L"Registering VRudders device");
        registered = TRUE;
    }
    // Normal setup must allow Windows to ask the user to trust a newly signed
    // publisher certificate. NONINTERACTIVE fails when that prompt is needed.
    // Only explicitly silent setup suppresses UI; never import trust ourselves.
    // Windows still verifies package/catalog membership in both modes.
    DWORD flags = INSTALLFLAG_FORCE;
    if (nonInteractive) flags |= INSTALLFLAG_NONINTERACTIVE;
    if (!UpdateDriverForPlugAndPlayDevicesW(NULL, HardwareId, inf, flags, &reboot)) {
        result = Error(L"Installing signed VRudders driver");
        if (registered) { BOOL ignored = FALSE; Remove(set, &created, &ignored); }
        return result;
    }
    RefreshLegacyControllerName();
    wprintf(L"VRudders driver installed. Reboot required: %ls\n", reboot ? L"yes" : L"no");
    return reboot ? ERROR_SUCCESS_REBOOT_REQUIRED : ERROR_SUCCESS;
}

int wmain(int argc, WCHAR** argv)
{
    if (argc == 2 && wcscmp(argv[1], L"check-app") == 0) return (int)AppRunning();
    if (argc != 2 || (wcscmp(argv[1], L"status") && wcscmp(argv[1], L"verify") && wcscmp(argv[1], L"install") && wcscmp(argv[1], L"install-silent") && wcscmp(argv[1], L"uninstall"))) {
        fwprintf(stderr, L"Usage: VRudders.DriverSetup.exe status|check-app|verify|install|install-silent|uninstall\n"); return ERROR_BAD_ARGUMENTS;
    }
    if (wcscmp(argv[1], L"verify") == 0) return (int)Install(INVALID_HANDLE_VALUE, 0, TRUE, TRUE);
    BOOL silentInstall = wcscmp(argv[1], L"install-silent") == 0;
    BOOL installing = wcscmp(argv[1], L"install") == 0 || silentInstall;
    BOOL statusOnly = wcscmp(argv[1], L"status") == 0;
    if (!statusOnly) { DWORD busy = AppRunning(); if (busy) return (int)busy; }
    HDEVINFO set = SetupDiGetClassDevsW(&HidClass, NULL, NULL, 0);
    if (set == INVALID_HANDLE_VALUE) return (int)Error(L"Enumerating HID devices");
    DWORD count = 0, result = 0;
    BOOL reboot = FALSE;
    SP_DEVINFO_DATA device = {0}; device.cbSize = sizeof(device);
    for (DWORD index = 0; ; ++index) {
        if (!SetupDiEnumDeviceInfo(set, index, &device)) {
            if (GetLastError() != ERROR_NO_MORE_ITEMS) result = Error(L"Enumerating device");
            break;
        }
        if (!IsOurs(set, &device)) continue;
        ++count;
        WCHAR instance[512];
        if (SetupDiGetDeviceInstanceIdW(set, &device, instance, ARRAYSIZE(instance), NULL)) wprintf(L"Found %ls\n", instance);
        if (statusOnly) {
            ULONG flags = 0, problem = 0;
            CONFIGRET status = CM_Get_DevNode_Status(&flags, &problem, device.DevInst, 0);
            wprintf(L"Device status: CM=%lu, flags=0x%08lX, problem=%lu\n", status, flags, problem);
            if (status != CR_SUCCESS || problem || !(flags & DN_STARTED)) result = ERROR_DEVICE_NOT_CONNECTED;
        } else if (wcscmp(argv[1], L"uninstall") == 0) {
            result = Remove(set, &device, &reboot);
            if (result) break;
        }
    }
    if (!result && installing) {
        if (count > 1) { fwprintf(stderr, L"Multiple VRudders devices found; remove duplicates before installing.\n"); result = ERROR_DUP_NAME; }
        else result = Install(set, count, FALSE, silentInstall);
    } else if (!result && statusOnly) result = count == 0 ? ERROR_FILE_NOT_FOUND : count > 1 ? ERROR_DUP_NAME : ERROR_SUCCESS;
    else if (!result) {
        wprintf(L"Removed %lu VRudders device(s). Signed package remains in Driver Store for reinstall.\n", count);
        if (reboot) result = ERROR_SUCCESS_REBOOT_REQUIRED;
    }
    SetupDiDestroyDeviceInfoList(set);
    return (int)result;
}
