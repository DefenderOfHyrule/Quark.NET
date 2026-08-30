#include <windows.h>
#include <stdio.h>
#include <stdarg.h>
#include <setupapi.h>
#include <cfgmgr32.h>
#include "libwdi.h"

typedef BOOL (WINAPI *PFN_UpdateDriver)(HWND, LPCSTR, LPCSTR, DWORD, PBOOL);
typedef BOOL (WINAPI *PFN_SetupCopyOEMInf)(PCSTR, PCSTR, DWORD, DWORD, PSTR, DWORD, PDWORD, PSTR*);

#define LEAFLET_VID       0x057E
#define LEAFLET_PID       0x3000
#define LEAFLET_DESC      "Leaflet"
#define INF_NAME          "quark_leaflet.inf"
#define INSTALLFLAG_FORCE 0x00000001

#ifndef QUARK_WDI_DRIVER_TYPE
#define QUARK_WDI_DRIVER_TYPE WDI_LIBUSBK
#endif
#ifndef QUARK_WDI_DRIVER_NAME
#define QUARK_WDI_DRIVER_NAME "libusbK"
#endif
#ifndef QUARK_WDI_CAT_NAME
#define QUARK_WDI_CAT_NAME "libusbK.cat"
#endif

static void enable_load_driver_privilege(void)
{
    HANDLE hToken = NULL;
    if (!OpenProcessToken(GetCurrentProcess(),
                          TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, &hToken))
        return;
    TOKEN_PRIVILEGES tp;
    LookupPrivilegeValueA(NULL, "SeLoadDriverPrivilege",
                          &tp.Privileges[0].Luid);
    tp.PrivilegeCount = 1;
    tp.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;
    AdjustTokenPrivileges(hToken, FALSE, &tp, 0, NULL, NULL);
    CloseHandle(hToken);
}

static void remove_dir(const char *path)
{
    char pattern[MAX_PATH];
    _snprintf_s(pattern, MAX_PATH, _TRUNCATE, "%s\\*", path);

    WIN32_FIND_DATAA ffd;
    HANDLE h = FindFirstFileA(pattern, &ffd);
    if (h == INVALID_HANDLE_VALUE) return;
    do {
        if (!strcmp(ffd.cFileName, ".") || !strcmp(ffd.cFileName, ".."))
            continue;
        char child[MAX_PATH];
        _snprintf_s(child, MAX_PATH, _TRUNCATE, "%s\\%s", path, ffd.cFileName);
        if (ffd.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)
            remove_dir(child);
        else
            DeleteFileA(child);
    } while (FindNextFileA(h, &ffd));
    FindClose(h);
    RemoveDirectoryA(path);
}

__declspec(dllexport) int __stdcall QuarkInstallDriver(HWND hwnd)
{
    HMODULE hNewdev = LoadLibraryA("newdev.dll");
    if (!hNewdev) return WDI_ERROR_NOT_FOUND;

    PFN_UpdateDriver pfnUpdateDriver =
        (PFN_UpdateDriver)GetProcAddress(hNewdev, "UpdateDriverForPlugAndPlayDevicesA");
    if (!pfnUpdateDriver) {
        FreeLibrary(hNewdev);
        return WDI_ERROR_NOT_FOUND;
    }

    HMODULE hSetupapi = LoadLibraryA("setupapi.dll");
    PFN_SetupCopyOEMInf pfnCopyInf = hSetupapi
        ? (PFN_SetupCopyOEMInf)GetProcAddress(hSetupapi, "SetupCopyOEMInfA")
        : NULL;

    struct wdi_options_create_list ocl = {
        .list_all = TRUE, .list_hubs = FALSE, .trim_whitespaces = TRUE,
    };
    struct wdi_device_info *list = NULL;
    struct wdi_device_info *found = NULL;

    if (wdi_create_list(&list, &ocl) == WDI_SUCCESS) {
        for (struct wdi_device_info *d = list; d != NULL; d = d->next) {
            if (d->vid == LEAFLET_VID && d->pid == LEAFLET_PID) {
                found = d;
                break;
            }
        }
    }

    struct wdi_device_info static_dev = {
        .vid = LEAFLET_VID, .pid = LEAFLET_PID, .desc = LEAFLET_DESC,
    };
    struct wdi_device_info *dev = found ? found : &static_dev;

    struct wdi_options_prepare_driver opd = {
        .driver_type     = QUARK_WDI_DRIVER_TYPE,
        .disable_cat     = FALSE,
        .disable_signing = FALSE,
    };

    char drv_dir[MAX_PATH];
    strncpy_s(drv_dir, MAX_PATH, "C:\\quark_drv", _TRUNCATE);

    int r = wdi_prepare_driver(dev, drv_dir, INF_NAME, &opd);
    if (r != WDI_SUCCESS) {
        if (list) wdi_destroy_list(list);
        FreeLibrary(hNewdev);
        if (hSetupapi) FreeLibrary(hSetupapi);
        return r;
    }

    char inf_path[MAX_PATH];
    _snprintf_s(inf_path, MAX_PATH, _TRUNCATE, "%s\\%s", drv_dir, INF_NAME);

    if (GetFileAttributesA(inf_path) == INVALID_FILE_ATTRIBUTES) {
        if (list) wdi_destroy_list(list);
        FreeLibrary(hNewdev);
        if (hSetupapi) FreeLibrary(hSetupapi);
        return WDI_ERROR_NOT_FOUND;
    }

    struct wdi_options_install_cert oic = { .hWnd = NULL, .disable_warning = TRUE };
    wdi_install_trusted_certificate(QUARK_WDI_CAT_NAME, &oic);

    enable_load_driver_privilege();

    const char *hw_id = (dev->hardware_id && dev->hardware_id[0])
        ? dev->hardware_id
        : "USB\\VID_057E&PID_3000";

    BOOL reboot = FALSE;
    BOOL ok = pfnUpdateDriver(NULL, hw_id, inf_path, INSTALLFLAG_FORCE, &reboot);
    DWORD err = GetLastError();

    int ret;
    if (ok) {
        if (pfnCopyInf) {
            char dest[MAX_PATH];
            pfnCopyInf(inf_path, NULL, 4, 0, dest, MAX_PATH, NULL, NULL);
        }
        ret = WDI_SUCCESS;
    } else if (err == ERROR_NO_SUCH_DEVINST) {

        if (pfnCopyInf) {
            char dest[MAX_PATH];
            pfnCopyInf(inf_path, NULL, 4, 0, dest, MAX_PATH, NULL, NULL);
        }

        ret = WDI_ERROR_NOT_FOUND;
        if (dev->device_id != NULL) {
            DEVINST devInst;
            if ((CM_Locate_DevNodeA(&devInst, dev->device_id, 0) == CR_SUCCESS) &&
                (CM_Reenumerate_DevNode(devInst, CM_REENUMERATE_RETRY_INSTALLATION) == CR_SUCCESS)) {
                ret = WDI_SUCCESS;
            }
        }
    } else {
        switch (err) {
            case ERROR_ACCESS_DENIED:  ret = WDI_ERROR_ACCESS;    break;
            case ERROR_NO_MORE_ITEMS:  ret = WDI_ERROR_NOT_FOUND; break;
            case ERROR_FILE_NOT_FOUND: ret = WDI_ERROR_NOT_FOUND; break;
            default:                   ret = WDI_ERROR_OTHER;      break;
        }
    }

    remove_dir(drv_dir);

    if (list) wdi_destroy_list(list);
    FreeLibrary(hNewdev);
    if (hSetupapi) FreeLibrary(hSetupapi);
    return ret;
}

__declspec(dllexport) int __stdcall QuarkIsDriverInstalled(void)
{
    struct wdi_device_info *list = NULL;
    struct wdi_options_create_list ocl = {
        .list_all = TRUE, .list_hubs = FALSE, .trim_whitespaces = TRUE,
    };
    if (wdi_create_list(&list, &ocl) != WDI_SUCCESS) return 0;

    int found = 0;
    for (struct wdi_device_info *d = list; d != NULL; d = d->next) {
        if (d->vid == LEAFLET_VID && d->pid == LEAFLET_PID) {
            if (d->driver &&
                (_stricmp(d->driver, QUARK_WDI_DRIVER_NAME) == 0 ||
                 strstr(d->driver, QUARK_WDI_DRIVER_NAME)))
                found = 1;
            break;
        }
    }
    wdi_destroy_list(list);
    return found;
}

__declspec(dllexport) int __stdcall QuarkIsDevicePresent(void)
{
    struct wdi_device_info *list = NULL;
    struct wdi_options_create_list ocl = {
        .list_all = TRUE, .list_hubs = FALSE, .trim_whitespaces = TRUE,
    };
    if (wdi_create_list(&list, &ocl) != WDI_SUCCESS) return 0;

    int found = 0;
    for (struct wdi_device_info *d = list; d != NULL; d = d->next) {
        if (d->vid == LEAFLET_VID && d->pid == LEAFLET_PID) {
            found = 1;
            break;
        }
    }
    wdi_destroy_list(list);
    return found;
}

