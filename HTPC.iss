[Setup]
; --- Application Metadata ---
AppName=Nucleus HTPC
AppVersion=1.4.4
AppPublisher=Bobby Vaughn
DefaultDirName={autopf}\NucleusHTPC
DefaultGroupName=Nucleus HTPC
OutputBaseFilename=NucleusHTPC_Installer_v1.4.4
WizardSmallImageFile=Assets\NucleusSmall.bmp
WizardImageFile=Assets\NucleusBanner.bmp

; --- UI & Compression ---
SetupIconFile=Assets\favicon.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin

; --- Architecture Setup ---
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Dirs]
; Grants standard users permission to write/modify the database in the Program Files folder
Name: "{app}"; Permissions: users-modify

[Files]
; --- Core Application Files ---
; IMPORTANT: Verify this Source path matches your actual dotnet publish output folder!
Source: "bin\Release\net10.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; --- User Data Protection (The Safety Net) ---
; If your app generates a default Preferences.json or Database file in the publish directory, 
; these flags ensure the installer NEVER overwrites them if the user already has them installed.
Source: "bin\Release\net10.0-windows\win-x64\publish\Preferences.json"; DestDir: "{app}"; Flags: onlyifdoesntexist ignoreversion skipifsourcedoesntexist
Source: "bin\Release\net10.0-windows\win-x64\publish\*.db"; DestDir: "{app}"; Flags: onlyifdoesntexist ignoreversion skipifsourcedoesntexist
Source: "Dependencies\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\Nucleus HTPC"; Filename: "{app}\HTPC.exe"; IconFilename: "{app}\Assets\favicon.ico"
Name: "{autodesktop}\Nucleus HTPC"; Filename: "{app}\HTPC.exe"; IconFilename: "{app}\Assets\favicon.ico"

[Code]
function IsVCRedistInstalled(): Boolean;
begin
  // This checks for the latest universal Visual C++ Redistributable version key
  Result := RegKeyExists(HKEY_LOCAL_MACHINE, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64') or
            RegValueExists(HKEY_LOCAL_MACHINE, 'SOFTWARE\Microsoft\DevDiv\VC\Servicing\14.0\RuntimeMinimum', 'Version');
end;

[Run]
; Run the file locally from the temp directory
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; Flags: waituntilterminated; Check: not IsVCRedistInstalled

; Add inbound and outbound firewall rules silently during installation
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""HTPC Media Center"" dir=in action=allow program=""{app}\HTPC.exe"" enable=yes profile=any"; Flags: runhidden; StatusMsg: "Configuring Windows Firewall..."
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""HTPC Media Center"" dir=out action=allow program=""{app}\HTPC.exe"" enable=yes profile=any"; Flags: runhidden

; This launches your app after the installer finishes
Filename: "{app}\HTPC.exe"; Description: "{cm:LaunchProgram,Nucleus HTPC}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Clean up and remove the firewall rules during uninstallation
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""HTPC Media Center"""; Flags: runhidden