; Copyright (c) 2026 Ermis Catevatis. MIT license.
Unicode true
ManifestDPIAware true
ManifestSupportedOS all
RequestExecutionLevel admin
CRCCheck force
SetCompressor /SOLID lzma
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"
!include "WinVer.nsh"

Name "VRudders ${VERSION}"
OutFile "${OUTPUT}\VRudders-${VERSION}-Setup-x64.exe"
InstallDir "$PROGRAMFILES64\VRudders"
BrandingText "VRudders · Rudder Control"
VIProductVersion "${NUMERIC_VERSION}"
VIAddVersionKey /LANG=1033 "ProductName" "VRudders"
VIAddVersionKey /LANG=1033 "FileDescription" "VRudders Setup"
VIAddVersionKey /LANG=1033 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Copyright © 2026 Ermis Catevatis"

!define MUI_ABORTWARNING
!define MUI_ICON "${ROOT}\src\VRudders\Assets\vrudders.ico"
!define MUI_UNICON "${ROOT}\src\VRudders\Assets\vrudders.ico"
!define MUI_WELCOMEPAGE_TITLE "Get your pedals into the game"
!define MUI_WELCOMEPAGE_TEXT "Install VRudders ${VERSION} for Windows 11 x64.$\r$\n$\r$\nRudder input, calibration, curves, and profiles for virtual HOTAS yaw. Includes the .NET runtime and signed VRudders Yaw controller driver.$\r$\n$\r$\nClose VRudders and finish any flight before continuing. The controller display name changes to VRudders Yaw; its hardware ID and Z protocol stay the same."
!define MUI_FINISHPAGE_TEXT "Start VRudders from the Start menu. Select your pedals and Z, then check physical input, processed yaw, and Windows readback.$\r$\n$\r$\nBind VRudders Yaw in your game's flight or HOTAS controls. Verify bindings after upgrading.$\r$\n$\r$\nBegin with VRudders · Original response. Open the Setup guide for tuning and calibration instructions."
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "${ROOT}\LICENSE"
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "English"

!ifdef SIGN_SCRIPT
!uninstfinalize '"${SIGN_RUNNER}" "${PWSH}" -NoProfile -NonInteractive -File "${SIGN_SCRIPT}" -Metadata "${SIGN_METADATA}" -Path "%1" -CopyTo "${UNINSTALL_ARCHIVE}"' = 0
!endif

Function .onInit
  ${IfNot} ${IsNativeAMD64}
    MessageBox MB_OK|MB_ICONSTOP "This build requires a Windows 11 PC with an Intel/AMD 64-bit processor. ARM64 is not supported."
    SetErrorLevel 1633
    Abort
  ${EndIf}
  ${IfNot} ${AtLeastWin11}
    MessageBox MB_OK|MB_ICONSTOP "This build requires Windows 11."
    SetErrorLevel 1633
    Abort
  ${EndIf}
  SetRegView 64
  SetShellVarContext all
  StrCpy $INSTDIR "$PROGRAMFILES64\VRudders"
  InitPluginsDir
  File /oname=$PLUGINSDIR\VRudders.DriverSetup.exe "${PAYLOAD}\VRudders.DriverSetup.exe"
  nsExec::ExecToStack '"$PLUGINSDIR\VRudders.DriverSetup.exe" check-app'
  Pop $0
  Pop $1
  ${If} $0 != 0
    MessageBox MB_OK|MB_ICONSTOP "Close every VRudders window, then run Setup again.$\r$\n$1"
    SetErrorLevel 1618
    Abort
  ${EndIf}
FunctionEnd

Section "VRudders" MainSection
  SetOutPath "$INSTDIR"
  File /r "${PAYLOAD}\*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  ; Register removal before driver setup so a failed install remains removable.
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\VRudders" "DisplayName" "VRudders"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\VRudders" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\VRudders" "Publisher" "Ermis Catevatis"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\VRudders" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\VRudders" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\VRudders" "DisplayIcon" "$INSTDIR\VRudders.exe"
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\VRudders" "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\VRudders" "NoRepair" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\VRudders" "EstimatedSize" ${SIZE_KB}
  CreateDirectory "$SMPROGRAMS\VRudders"
  CreateShortcut "$SMPROGRAMS\VRudders\VRudders.lnk" "$INSTDIR\VRudders.exe"
  CreateShortcut "$SMPROGRAMS\VRudders\Setup guide.lnk" "$INSTDIR\GETTING-STARTED.html"
  DetailPrint "Installing the signed VRudders virtual controller..."
  ${If} ${Silent}
    nsExec::ExecToStack '"$INSTDIR\VRudders.DriverSetup.exe" install-silent'
  ${Else}
    DetailPrint "Review the Windows Security prompt if shown. Official publisher: Ermis Catevatis."
    nsExec::ExecToStack '"$INSTDIR\VRudders.DriverSetup.exe" install'
  ${EndIf}
  Pop $0
  Pop $1
  DetailPrint "$1"
  FileOpen $2 "$INSTDIR\driver-setup.log" w
  FileWrite $2 "Exit code: $0$\r$\n$1"
  FileClose $2
  ${If} $0 == 3010
    SetRebootFlag true
  ${ElseIf} $0 != 0
    MessageBox MB_OK|MB_ICONSTOP "The app files were installed, but Windows could not install the virtual driver (code $0).$\r$\n$\r$\n$1$\r$\n$\r$\nKeep Windows security enabled. Details are in $INSTDIR\driver-setup.log and C:\Windows\INF\setupapi.dev.log. You can rerun Setup or remove VRudders in Windows Settings." /SD IDOK
    SetErrorLevel 1001
    Abort "Driver installation failed."
  ${EndIf}
SectionEnd

Function un.onInit
  SetRegView 64
  SetShellVarContext all
  ${If} $INSTDIR != "$PROGRAMFILES64\VRudders"
    MessageBox MB_OK|MB_ICONSTOP "Unexpected installation directory. Uninstall was stopped without removing files."
    Abort
  ${EndIf}
FunctionEnd

Section "Uninstall"
  nsExec::ExecToStack '"$INSTDIR\VRudders.DriverSetup.exe" uninstall'
  Pop $0
  Pop $1
  DetailPrint "$1"
  ${If} $0 == 3010
    SetRebootFlag true
  ${ElseIf} $0 != 0
    MessageBox MB_OK|MB_ICONSTOP "Could not remove the VRudders device (code $0). Close VRudders and try again.$\r$\n$1$\r$\nApp files have been kept so you can retry."
    SetErrorLevel 1002
    Abort
  ${EndIf}
  ; Generated exact file list. Never recursively delete the installation directory.
  !include "${UNINSTALL_FILES}"
  Delete "$INSTDIR\driver-setup.log"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\VRudders\VRudders.lnk"
  Delete "$SMPROGRAMS\VRudders\Setup guide.lnk"
  RMDir "$SMPROGRAMS\VRudders"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\VRudders"
SectionEnd
