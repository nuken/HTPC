[Setup]
; --- Application Metadata ---
AppName=Nucleus HTPC
AppVersion=1.1.7
AppPublisher=Bobby Vaughn
DefaultDirName={autopf}\NucleusHTPC
DefaultGroupName=Nucleus HTPC
OutputBaseFilename=NucleusHTPC_Installer_v1.1.7
WizardSmallImageFile=Assets\NucleusSmall.bmp
WizardImageFile=Assets\NucleusBanner.bmp

; --- UI & Compression ---
SetupIconFile=Assets\favicon.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest

; --- Architecture Setup ---
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Files]
; --- Core Application Files ---
; IMPORTANT: Verify this Source path matches your actual dotnet publish output folder!
Source: "bin\Release\net10.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; --- User Data Protection (The Safety Net) ---
; If your app generates a default Preferences.json or Database file in the publish directory, 
; these flags ensure the installer NEVER overwrites them if the user already has them installed.
Source: "bin\Release\net10.0-windows\win-x64\publish\Preferences.json"; DestDir: "{app}"; Flags: onlyifdoesntexist ignoreversion skipifsourcedoesntexist
Source: "bin\Release\net10.0-windows\win-x64\publish\*.db"; DestDir: "{app}"; Flags: onlyifdoesntexist ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\Nucleus HTPC"; Filename: "{app}\HTPC.exe"; IconFilename: "{app}\Assets\favicon.ico"
Name: "{autodesktop}\Nucleus HTPC"; Filename: "{app}\HTPC.exe"; IconFilename: "{app}\Assets\favicon.ico"

[Code]
// This function checks the Windows Registry to see if the 64-bit C++ Redist is installed
function IsVCRedistInstalled(): Boolean;
var
  RegKey: String;
begin
  RegKey := 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64';
  Result := RegKeyExists(HKEY_LOCAL_MACHINE, RegKey);
end;

[Run]
// This downloads and silently installs the C++ redistributable if the check above fails
Filename: "https://aka.ms/vs/17/release/vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; Flags: runascurrentuser shellexec waituntilterminated; Check: not IsVCRedistInstalled

// This launches your app after the installer finishes
Filename: "{app}\HTPC.exe"; Description: "{cm:LaunchProgram,Nucleus HTPC}"; Flags: nowait postinstall skipifsilent