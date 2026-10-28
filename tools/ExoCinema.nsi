; Exo Cinema's install engine (NSIS), ported from Exo Browser's (which ported Exo Launcher's). Exo Cinema Setup
; (setup\) runs it silently and shows its steps in its own window; without WebView2 it runs on its own, with NSIS's
; plain progress screen. It installs per user, no admin: %LOCALAPPDATA%\ExoCinema\app, staged beside it, checked
; against the payload's manifest and swapped in whole (the old one put back if that fails), a Start menu shortcut, an
; optional desktop one, an uninstaller. Your sign-in, settings, caches and the WebView2 profile live beside app\, never
; inside it, so updates never touch them.
;   makensis /DPAYLOAD_DIR=<folder> [/DSETUP_SHELL=ExoSetup.exe] [/DOUTFILE=engine.exe] [/DEXO_TEST_ROOT=<folder>] ExoCinema.nsi
;   /DPAYLOAD_MANIFEST_READY: the payload already carries its payload-manifest.json (a test that changes a file after
;   it was recorded, to see the engine refuse it); otherwise the build records it (installer\New-PayloadManifest.ps1).
; From Setup's window: /S /NOLAUNCH /DESKTOP=0|1 /STATUSFILE=<file>. /S alone (the in-app updater) opens Exo Cinema after.
!ifndef PRODUCT_VERSION
  !searchparse /file "${__FILEDIR__}\..\VERSION" "" PRODUCT_VERSION
!endif
!ifndef PAYLOAD_DIR
  !error "PAYLOAD_DIR required"
!endif
!ifndef OUTFILE
  !define OUTFILE "ExoCinema-Engine.exe"
!endif
!define ICON "${__FILEDIR__}\..\src\ExoCinema\Assets\ExoCinema.ico"

!ifndef PAYLOAD_MANIFEST_READY
  ; Every file the engine carries, with its size and SHA-256, for the check after it is unpacked.
  !system 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "${__FILEDIR__}\installer\New-PayloadManifest.ps1" -PayloadRoot "${PAYLOAD_DIR}" -Version "${PRODUCT_VERSION}"' = 0
!endif

!define PRODUCT_NAME "Exo Cinema"
!define PRODUCT_PUBLISHER "UhhErix"
!define PRODUCT_WEB "https://github.com/UhhErix/ExoCinema"
!ifdef EXO_TEST_ROOT
  ; A walk-through build (/DEXO_TEST_ROOT=<folder>): installs under that folder with its own registry entries and
  ; shortcut names, never over the real Exo Cinema.
  !define ROOT "${EXO_TEST_ROOT}"
  !define LINK_NAME "Exo Cinema (test)"
  !define REG_NAME "ExoCinemaTest"
!else
  !define ROOT "$LOCALAPPDATA\ExoCinema"
  !define LINK_NAME "Exo Cinema"
  !define REG_NAME "ExoCinema"
!endif
!define PRODUCT_DIR_REGKEY "Software\Microsoft\Windows\CurrentVersion\App Paths\${REG_NAME}.exe"
!define PRODUCT_UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${REG_NAME}"

Unicode true
ManifestDPIAware true
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 64
CRCCheck on
XPStyle on

Name "${PRODUCT_NAME}"
Caption "${PRODUCT_NAME} Setup"
UninstallCaption "Uninstall ${PRODUCT_NAME}"
OutFile "${OUTFILE}"
InstallDir "${ROOT}\app"
ShowInstDetails nevershow
ShowUnInstDetails nevershow
AutoCloseWindow true
BrandingText " "
Icon "${ICON}"
UninstallIcon "${ICON}"

VIProductVersion "${PRODUCT_VERSION}.0"
VIAddVersionKey /LANG=1033 "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey /LANG=1033 "CompanyName" "${PRODUCT_PUBLISHER}"
VIAddVersionKey /LANG=1033 "FileDescription" "${PRODUCT_NAME} install engine"
VIAddVersionKey /LANG=1033 "FileVersion" "${PRODUCT_VERSION}"
VIAddVersionKey /LANG=1033 "ProductVersion" "${PRODUCT_VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Copyright (c) 2026 ${PRODUCT_PUBLISHER}"

!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "x64.nsh"
!include "WinMessages.nsh"

Page instfiles
UninstPage instfiles

Var StatusFile  ; /STATUSFILE=: where each step is written for Setup's window
Var DesktopLink ; /DESKTOP=: "" leaves the desktop shortcut as it is, "0" or "1"
Var NoLaunch    ; /NOLAUNCH: Open Exo Cinema is left to Setup's window (or to nobody)

!macro Status TEXT
  DetailPrint "${TEXT}"
  ${If} $StatusFile != ""
    FileOpen $R4 "$StatusFile" a
    FileSeek $R4 0 END
    FileWrite $R4 "${TEXT}$\r$\n"
    FileClose $R4
  ${EndIf}
!macroend

; Closes the Exo Cinema running from this app folder and waits for it to exit. Its windows are WinUI's
; (WinUIDesktopWin32WindowClass), a class Exo Launcher's windows share, so a window counts only when its process runs
; $INSTDIR\ExoCinema.exe: a copy running from anywhere else, and Exo Launcher, are left alone. Asked to close, it
; tells Jellyfin where playback stopped and saves its window; one that hasn't gone after 15 s is ended. Then its
; WebView2 processes are given a moment to let go of the profile. $0 is "0" once none is left, "1" if one would not go.
!macro CloseApp UN
Function ${UN}CloseApp
  StrCpy $5 0
  close_next:
    FindWindow $1 "WinUIDesktopWin32WindowClass" "" "" $5
    StrCmp $1 0 close_done
    StrCpy $5 $1
    System::Call 'user32::GetWindowThreadProcessId(p r1, *i .r2)'
    ; SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_TERMINATE
    System::Call 'kernel32::OpenProcess(i 0x101001, i 0, i r2) p .r3'
    StrCmp $3 0 close_next
    StrCpy $4 ""
    System::Call 'kernel32::QueryFullProcessImageNameW(p r3, i 0, w .r4, *i ${NSIS_MAX_STRLEN})'
    ${If} $4 == "$INSTDIR\ExoCinema.exe"
      SendMessage $1 ${WM_CLOSE} 0 0 /TIMEOUT=5000
      System::Call 'kernel32::WaitForSingleObject(p r3, i 15000) i .r6'
      ${If} $6 != 0
        System::Call 'kernel32::TerminateProcess(p r3, i 1)'
        System::Call 'kernel32::WaitForSingleObject(p r3, i 5000) i .r6'
      ${EndIf}
      System::Call 'kernel32::CloseHandle(p r3)'
      ${If} $6 != 0
        StrCpy $0 1
        Return
      ${EndIf}
      ; Its windows are gone: look again from the start.
      StrCpy $5 0
      Goto close_next
    ${EndIf}
    System::Call 'kernel32::CloseHandle(p r3)'
    Goto close_next
  close_done:
  InitPluginsDir
  File "/oname=$PLUGINSDIR\Wait-ExoCinemaWebView.ps1" "${__FILEDIR__}\installer\Wait-ExoCinemaWebView.ps1"
  nsExec::ExecToLog 'powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "$PLUGINSDIR\Wait-ExoCinemaWebView.ps1" -ProfileDirectory "${ROOT}\WebView2" -AppDirectory "$INSTDIR"'
  Pop $1
  StrCpy $0 0
FunctionEnd
!macroend
!insertmacro CloseApp ""
!insertmacro CloseApp "un."

; Exo Cinema runs on WebView2, part of Windows 11 and of most Windows 10 PCs. Where it's missing (or registered with
; its files gone), Microsoft's own installer is fetched and run, after checking it is signed by Microsoft.
Function EnsureWebView2
  ReadRegStr $0 HKLM "SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" "pv"
  ReadRegStr $1 HKLM "SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" "location"
  ${If} $0 == ""
    ReadRegStr $0 HKCU "SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" "pv"
    ReadRegStr $1 HKCU "SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}" "location"
  ${EndIf}
  ${If} $0 == ""
    Goto webview_install
  ${EndIf}
  ${If} $1 != ""
    IfFileExists "$1\$0\msedgewebview2.exe" webview_ok webview_try_pf
  ${EndIf}
  webview_try_pf:
  IfFileExists "$PROGRAMFILES32\Microsoft\EdgeWebView\Application\$0\msedgewebview2.exe" webview_ok webview_try_user
  webview_try_user:
  IfFileExists "$LOCALAPPDATA\Microsoft\EdgeWebView\Application\$0\msedgewebview2.exe" webview_ok webview_install
  webview_ok:
  Return
  webview_install:
  !insertmacro Status "Getting WebView2"
  FileOpen $3 "$TEMP\exo-cinema-webview2.ps1" w
  FileWrite $3 "$$ErrorActionPreference = 'Stop'$\r$\n"
  FileWrite $3 "$$uri = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703'$\r$\n"
  FileWrite $3 "$$out = Join-Path $$env:TEMP 'MicrosoftEdgeWebview2Setup.exe'$\r$\n"
  FileWrite $3 "Invoke-WebRequest -UseBasicParsing -Uri $$uri -OutFile $$out$\r$\n"
  FileWrite $3 "$$signature = Get-AuthenticodeSignature -LiteralPath $$out$\r$\n"
  FileWrite $3 "if ($$signature.Status -ne 'Valid' -or $$signature.SignerCertificate.Subject -notmatch '(?:^|,\s*)O=Microsoft Corporation(?:,|$$)') { Remove-Item -LiteralPath $$out -Force -ErrorAction SilentlyContinue; throw 'WebView2 bootstrapper signature is not valid for Microsoft Corporation.' }$\r$\n"
  FileClose $3
  nsExec::ExecToLog 'powershell -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "$TEMP\exo-cinema-webview2.ps1"'
  Pop $1
  Delete "$TEMP\exo-cinema-webview2.ps1"
  ${If} $1 != "0"
    DetailPrint "WebView2 download failed ($1). Continuing."
    Return
  ${EndIf}
  ExecWait '"$TEMP\MicrosoftEdgeWebview2Setup.exe" /silent /install' $2
  Delete "$TEMP\MicrosoftEdgeWebview2Setup.exe"
FunctionEnd

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_OK|MB_ICONSTOP "${PRODUCT_NAME} requires 64-bit Windows." /SD IDOK
    Abort
  ${EndIf}
  SetRegView 64
  ; One managed location, so updates always find it; setup never takes another destination.
  StrCpy $INSTDIR "${ROOT}\app"
  ${GetParameters} $0
  ClearErrors
  ${GetOptions} $0 "/STATUSFILE=" $StatusFile
  IfErrors 0 +2
    StrCpy $StatusFile ""
  ClearErrors
  ${GetOptions} $0 "/DESKTOP=" $1
  IfErrors +2 0
    StrCpy $DesktopLink $1
  ClearErrors
  ${GetOptions} $0 "/NOLAUNCH" $1
  IfErrors +2 0
    StrCpy $NoLaunch 1
  ; On its own screens (no Setup window), a first install gets the desktop shortcut, as Setup's switch defaults to.
  ${IfNot} ${Silent}
  ${AndIf} $DesktopLink == ""
  ${AndIfNot} ${FileExists} "$INSTDIR\ExoCinema.exe"
    StrCpy $DesktopLink 1
  ${EndIf}
FunctionEnd

Section "Exo Cinema" SecApp
  SectionIn RO
  StrCpy $INSTDIR "${ROOT}\app"
  StrCpy $R8 "${ROOT}"
  StrCpy $R3 "0"
  System::Call 'kernel32::GetCurrentProcessId() i .r0'
  StrCpy $R9 "$R8\app.incoming-$0"
  StrCpy $R5 "$R8\app.previous-$0"
  Call EnsureWebView2

  ; Never take over a folder that isn't Exo Cinema's: an ExoCinema.exe marks ours, an empty folder is safe.
  ; ("dir\*.*" matches an empty folder too, its "." entry, so an empty one goes first: RMDir removes nothing else.)
  IfFileExists "$INSTDIR\ExoCinema.exe" target_ok
  RMDir "$INSTDIR"
  IfFileExists "$INSTDIR\*.*" unmanaged_target target_ok
  unmanaged_target:
    StrCpy $R6 "Exo Cinema's app folder holds files from another app. Setup left that folder unchanged."
    Goto install_fail
  target_ok:

  ; Stage the whole app beside the live one, check it, then swap: an update never leaves a half-copied app.
  IfFileExists "$R9\*.*" staging_collision
  IfFileExists "$R5\*.*" backup_collision
  RMDir "$R9"
  RMDir "$R5"
  CreateDirectory "$R9"
  StrCpy $R3 "1"
  SetOutPath "$R9"
  !insertmacro Status "Copying files"
  File /r "${PAYLOAD_DIR}\*.*"
  Goto staging_ready
  staging_collision:
    StrCpy $R6 "Setup couldn't make its own staging folder. Nothing installed was changed."
    Goto install_fail
  backup_collision:
    StrCpy $R6 "Setup found an unexpected rollback folder. Nothing installed was changed."
    Goto install_fail
  staging_ready:
  ; Leave the staged folder (setup's current folder would keep it open) before it is checked and renamed.
  SetOutPath "$TEMP"
  IfFileExists "$R9\ExoCinema.exe" payload_present
    StrCpy $R6 "This copy of Setup is incomplete (ExoCinema.exe is missing). Download it again."
    Goto install_fail
  payload_present:
  ; Every file as it was built: its size and SHA-256 against the manifest, nothing missing, nothing extra.
  !insertmacro Status "Checking files"
  InitPluginsDir
  File "/oname=$PLUGINSDIR\Verify-Payload.ps1" "${__FILEDIR__}\installer\Verify-Payload.ps1"
  nsExec::ExecToLog 'powershell.exe -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "$PLUGINSDIR\Verify-Payload.ps1" -PayloadRoot "$R9"'
  Pop $0
  ${If} $0 != "0"
    StrCpy $R6 "This copy of Setup is damaged: its files didn't match. Download it again. Nothing installed was changed."
    Goto install_fail
  ${EndIf}

  !insertmacro Status "Closing Exo Cinema"
  Call CloseApp
  ${If} $0 != "0"
    StrCpy $R6 "Exo Cinema is still running and didn't close. Close it, then try again. Nothing installed was changed."
    Goto install_fail
  ${EndIf}
  !insertmacro Status "Installing"
  StrCpy $R7 "0"
  IfFileExists "$INSTDIR\ExoCinema.exe" 0 no_old
    ClearErrors
    Rename "$INSTDIR" "$R5"
    IfErrors swap_prepare_fail
    StrCpy $R7 "1"
  no_old:
  CreateDirectory "$R8"
  ClearErrors
  Rename "$R9" "$INSTDIR"
  IfErrors swap_commit_fail
  StrCpy $R3 "0"
  IfFileExists "$INSTDIR\ExoCinema.exe" swap_ok swap_verify_fail

  swap_prepare_fail:
    StrCpy $R6 "Exo Cinema's folder is in use. Close anything opened from it (or restart Windows), then try again. Nothing installed was changed."
    Goto install_fail
  swap_commit_fail:
    ${If} $R7 == "1"
      ; Rename is same-volume and whole: remove only an empty failed target, never a tree setup didn't put there.
      RMDir "$INSTDIR"
      ClearErrors
      Rename "$R5" "$INSTDIR"
      IfErrors rollback_fail
    ${EndIf}
    StrCpy $R6 "The new version couldn't be installed. The one you had was put back."
    Goto install_fail
  swap_verify_fail:
    RMDir /r "$INSTDIR"
    ${If} $R7 == "1"
      ClearErrors
      Rename "$R5" "$INSTDIR"
      IfErrors rollback_fail
    ${EndIf}
    StrCpy $R6 "The new version failed its check. The one you had was put back."
    Goto install_fail
  rollback_fail:
    StrCpy $R6 "Setup couldn't put the previous version back by itself. It is kept in $R5."
    Goto install_fail

  install_fail:
    ${If} $R3 == "1"
      RMDir /r "$R9"
    ${EndIf}
    CreateDirectory "$R8"
    FileOpen $R4 "$R8\update-error.log" w
    FileWrite $R4 "$R6$\r$\n"
    FileClose $R4
    IfSilent silent_fail
      MessageBox MB_OK|MB_ICONSTOP "$R6"
      Goto fail_done
    silent_fail:
    ; The in-app updater closed Exo Cinema for this: put the version that is still there back on screen.
    StrCmp $NoLaunch 1 fail_done
    IfFileExists "$INSTDIR\ExoCinema.exe" 0 fail_done
      Exec '"$INSTDIR\ExoCinema.exe"'
    fail_done:
    SetErrorLevel 1
    Abort

  swap_ok:
  Delete "$R8\update-error.log"
  ${If} $R7 == "1"
    RMDir /r "$R5"
  ${EndIf}

  !insertmacro Status "Finishing"
  ; Shortcuts start in the app folder, not setup's.
  SetOutPath "$INSTDIR"
  CreateDirectory "$SMPROGRAMS"
  CreateShortCut "$SMPROGRAMS\${LINK_NAME}.lnk" "$INSTDIR\ExoCinema.exe" "" "$INSTDIR\ExoCinema.exe" 0
  ${If} $DesktopLink == "1"
    CreateShortCut "$DESKTOP\${LINK_NAME}.lnk" "$INSTDIR\ExoCinema.exe" "" "$INSTDIR\ExoCinema.exe" 0
  ${ElseIf} $DesktopLink == "0"
    Delete "$DESKTOP\${LINK_NAME}.lnk"
  ${EndIf}
  ; Setup's current folder would hold the app folder open (a later update or uninstall would find it in use).
  SetOutPath "$TEMP"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "${PRODUCT_DIR_REGKEY}" "" "$INSTDIR\ExoCinema.exe"
  WriteRegStr HKCU "${PRODUCT_DIR_REGKEY}" "Path" "$INSTDIR"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "DisplayName" "${LINK_NAME}"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "DisplayIcon" "$INSTDIR\ExoCinema.exe"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "DisplayVersion" "${PRODUCT_VERSION}"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "Publisher" "${PRODUCT_PUBLISHER}"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "URLInfoAbout" "${PRODUCT_WEB}"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegDWORD HKCU "${PRODUCT_UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${PRODUCT_UNINST_KEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKCU "${PRODUCT_UNINST_KEY}" "EstimatedSize" "$0"
!ifdef SETUP_SHELL
  ; Setup's window stays with the app as its uninstaller, so removing Exo Cinema looks like Exo too.
  File "/oname=$INSTDIR\ExoSetup.exe" "${SETUP_SHELL}"
  WriteRegStr HKCU "${PRODUCT_UNINST_KEY}" "UninstallString" '"$INSTDIR\ExoSetup.exe" --uninstall'
!endif
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'

  ; What earlier runs left behind: unfinished staging and rollback folders, and Setups the in-app updater downloaded
  ; (the one running now is in use and stays; Exo Cinema clears the folder when it starts).
  !insertmacro Status "Cleaning up"
  FindFirst $0 $1 "$R8\app.incoming-*"
  ${DoWhile} $1 != ""
    RMDir /r "$R8\$1"
    FindNext $0 $1
  ${Loop}
  FindClose $0
  FindFirst $0 $1 "$R8\app.previous-*"
  ${DoWhile} $1 != ""
    RMDir /r "$R8\$1"
    FindNext $0 $1
  ${Loop}
  FindClose $0
  Delete "$R8\updates\ExoCinema-Setup-*"

  ; Setup's window has its own Open button; on the engine's own screens or /S (the in-app updater), Exo Cinema opens now.
  StrCmp $NoLaunch 1 +2
    Exec '"$INSTDIR\ExoCinema.exe"'
SectionEnd

Function un.onInit
  SetRegView 64
  StrCpy $INSTDIR "${ROOT}\app"
FunctionEnd

; Removes the app, its shortcuts and its keys. Your sign-in and settings beside app\ stay (Setup's window can remove
; them too, when you ask it to).
Section "Uninstall"
  SetAutoClose true
  Call un.CloseApp
  ${If} $0 != "0"
    MessageBox MB_OK|MB_ICONSTOP "Exo Cinema is still running. Close it and try again." /SD IDOK
    SetErrorLevel 1
    Abort
  ${EndIf}
  Delete "$SMPROGRAMS\${LINK_NAME}.lnk"
  Delete "$DESKTOP\${LINK_NAME}.lnk"
  ; Only ever the app folder itself, and only when it is Exo Cinema's.
  IfFileExists "$INSTDIR\ExoCinema.exe" 0 +2
    RMDir /r "$INSTDIR"
  DeleteRegKey HKCU "${PRODUCT_UNINST_KEY}"
  DeleteRegKey HKCU "${PRODUCT_DIR_REGKEY}"
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
SectionEnd
