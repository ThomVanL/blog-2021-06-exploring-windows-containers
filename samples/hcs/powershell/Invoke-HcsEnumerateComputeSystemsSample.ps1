#Requires -RunAsAdministrator
#Requires -Version 5.1

<#
.SYNOPSIS
    A sample script to demonstrate how to interface with a portion of unmanaged code located in 'vmcompute.dll'.
.DESCRIPTION
    A sample script to demonstrate how to interface with a portion of unmanaged code located in 'vmcompute.dll'.
.INPUTS
    None. You cannot pipe objects to Invoke-HcsEnumerateComputeSystemsSample.
.OUTPUTS
    A table that lists the different compute systems on a Windows Host.
.EXAMPLE
    PS C:\> .\Invoke-HcsEnumerateComputeSystemsSample.ps1
.EXAMPLE
    PS C:\> .\Invoke-HcsEnumerateComputeSystemsSample.ps1
#>
[CmdletBinding()]
param ()

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class VmCompute{
    [DllImport("vmcompute.dll", ExactSpelling = true)]
    public static extern int HcsEnumerateComputeSystems(string query, [MarshalAs(UnmanagedType.LPWStr)] out string computeSystems, [MarshalAs(UnmanagedType.LPWStr)] out string result);
}
'@

$computeSystemsJson = ""
$result = ""
$query = ""
$hresult = [VmCompute]::HcsEnumerateComputeSystems($query, [ref] $computeSystemsJson, [ref]$result)

if ($hresult -eq 0) {
    if ($computeSystemsJson) {
        $computeSystems = $computeSystemsJson | ConvertFrom-Json
        if ($computeSystems) {
            $computeSystems | Format-Table -AutoSize
        }
        else {
            Write-Warning -Message "No compute systems found"
        }
    }
}
else {
    [System.Runtime.InteropServices.Marshal]::GetExceptionForHR($hresult).Message
}