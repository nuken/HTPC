[Setup]
; --- Application Metadata ---
AppName=Nucleus HTPC
AppVersion=1.0.0
AppPublisher=Bobby Vaughn
DefaultDirName={autopf}\NucleusHTPC
DefaultGroupName=Nucleus HTPC
OutputBaseFilename=NucleusHTPC_Installer_v1.0.0
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

[Run]
; Automatically launches the app when the installer finishes
Filename: "{app}\HTPC.exe"; Description: "{cm:LaunchProgram,Nucleus HTPC}"; Flags: nowait postinstall skipifsilent