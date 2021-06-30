#Requires -RunAsAdministrator
#Requires -Version 5.1

<#
.SYNOPSIS
    A sample script to demonstrate how to start a process isolated Windows container
    through a .NET language binding for the Host Compute Service. (https://github.com/microsoft/dotnet-computevirtualization)
.DESCRIPTION
    A sample script to demonstrate how to start a process isolated Windows container
    through a .NET language binding for the Host Compute Service. (https://github.com/microsoft/dotnet-computevirtualization)
.PARAMETER ScratchLayerPath
    Provide the path to the scratch layer directory.
.PARAMETER BaseImageName
    Provide the base container image name.
.PARAMETER ContainerId
    Provide an ID for the container.
.PARAMETER ContainerCommand
    Provide the command to be executed inside the container.
.INPUTS
    None. You cannot pipe objects to Invoke-HcsProcessIsolationSample.
.OUTPUTS
    None.
.EXAMPLE
    PS C:\> .\Invoke-HcsProcessIsolationSample.ps1 -ScratchLayerPath "C:\temp" -BaseImageName "mcr.microsoft.com/windows/servercore:ltsc2019" -ContainerId (New-Guid) -ContainerCommand "cmd.exe /k dir C:\ & whoami"
.EXAMPLE
    PS C:\> .\Invoke-HcsProcessIsolationSample.ps1 -ScratchLayerPath "C:\temp" -BaseImageName "mcr.microsoft.com/windows/servercore:ltsc2019" -ContainerCommand "powershell.exe -Command Get-Service"
#>
[CmdletBinding()]
param (
    [Parameter(
        Mandatory = $true,
        HelpMessage = "Provide path to the scratch layer directory.")]
    [string]
    $ScratchLayerPath,
    [Parameter(
        Mandatory = $true,
        HelpMessage = "Provide the base container image name.")]
    [string]
    $BaseImageName,
    [Parameter(
        Mandatory = $false,
        HelpMessage = "Provide the new container's ID.")]
    [string]
    $ContainerId = (New-Guid),
    [Parameter(
        Mandatory = $true,
        HelpMessage = "Provide the command to be executed inside the container.")]
    [string]
    $ContainerCommand
)

Import-Module "Microsoft.Windows.ComputeVirtualization.dll"
$ErrorActionPreference = 'Stop'

function Get-HnsNatNetwork {
    [CmdletBinding()]
    param()
    $networkId = [Microsoft.Windows.ComputeVirtualization.HostComputeService]::FindNatNetwork()
    return $networkId
}

$networkId = Get-HnsNatNetwork -ErrorAction SilentlyContinue

if ($null -eq $networkId) {
    Write-Error -Message "Host Networking Service could not find any networks with network mode 'NAT'."
}
else {
    ("Found network '{0}' with network mode 'NAT' " -f $networkId) |  Write-Host
}

$imageInspectionResponse = (docker image inspect $baseImageName) | ConvertFrom-Json
if ($null -eq $imageInspectionResponse) {
    Write-Error -Message "Image not found."
}

if (!(Test-Path -Path $imageInspectionResponse[0].GraphDriver.Data.Dir)) {
    Write-Error -Message "Directory associated with image not found."
}

$imageLayerChainPath = Join-Path -Path $imageInspectionResponse[-1].GraphDriver.Data.Dir -ChildPath "layerchain.json"
if (!(Test-Path -Path $imageLayerChainPath)) {
    Write-Error -Message "Layerchain.json associated with layer not found."
}

$imageLayerParentLayerPath = Get-Content -Path $imageLayerChainPath | ConvertFrom-Json
if ($null -eq $imageLayerParentLayerPath) {
    Write-Error "Layerchain.json associated with *parent* layer not found."
}

$scratchLayerNewPath = Join-Path -Path $scratchLayerPath -ChildPath $containerId

$parentLayerPaths = New-Object 'System.Collections.Generic.List[Microsoft.Windows.ComputeVirtualization.Layer]'
$parentLayerPaths.Add([Microsoft.Windows.ComputeVirtualization.Layer]@{
        Id   = $containerId
        Path = $imageLayerParentLayerPath
    })

[Microsoft.Windows.ComputeVirtualization.ContainerStorage]::CreateSandbox($scratchLayerNewPath, $parentLayerPaths)
"Created sandbox.VHDX at '{0}'." -f $scratchLayerNewPath | Write-Host

$containerSettings = New-Object 'Microsoft.Windows.ComputeVirtualization.ContainerSettings' -Property @{
    HyperVContainer   = $false
    KillOnClose       = $true
    Layers            = $parentLayerPaths
    MappedDirectories = $null
    NetworkId         = $networkId
    SandboxPath       = $scratchLayerNewPath
    UtilityVmPath     = ""
}

$serverContainer = [Microsoft.Windows.ComputeVirtualization.HostComputeService]::CreateContainer($ContainerId.Guid, $containerSettings)
# 👆  Our process isolated container is now created by the host compute service and
#     queryable through HcsEnumerateComputeSystems but not with 'docker container ls -a'.
#     Although I am not certain, I believe this is due to fact that our container is
#     anything but OCI runtime spec compliant.

$serverContainer.Start();

$processStartInfo = New-Object "Microsoft.Windows.ComputeVirtualization.ProcessStartInfo" -Property @{
    CommandLine            = $ContainerCommand
    KillOnClose            = $true
    RedirectStandardOutput = $true
    RedirectStandardError  = $true
    RedirectStandardInput  = $false # 👈 Keep this false for now, redirecting stdin, stdout and stderr
}                                   # with pure PowerShell would make this demo confusing.

$serverContainerProcess = $serverContainer.CreateProcess($processStartInfo)

Write-Warning -Message ("Executing command: {0}" -f $processStartInfo.CommandLine)

$serverContainerProcess.StandardOutput.ReadToEnd() | Write-Host
$serverContainerProcess.StandardError.ReadToEnd() | Write-Host -ForegroundColor Red

$serverContainerProcess.WaitForExit() | Out-Null
Write-Warning -Message ("Process exited with code {0}." -f $serverContainerProcess.ExitCode)
$serverContainer.Shutdown()
Write-Warning -Message ("Container {0} shut down." -f $ContainerId)
[Microsoft.Windows.ComputeVirtualization.ContainerStorage]::DestroyLayer($scratchLayerNewPath)
Write-Warning -Message ("Scratch layer removed: {0}." -f $scratchLayerNewPath)