# Login with an Azure account
Login-AzureRmAccount

$location = 'West Europe'
$resourceName = 'Node'
$resourceGroupName = 'BaboServiceFabricResourceGroup'
$extensionName = 'Node_MicrosoftMonitoringAgent'
$extension = ConvertFrom-Json "$(get-content "C:\Projects\Azure\ServiceFabric\IoTDemo\Diagnostics\BaboMicrosoftMonitoringAgentVMExtension.json")"

# Get the resource group for the VMSS
$resourceGroup = Get-AzureRmResourceGroup  -Name $resourceGroupName  -Location $location
$stopWatch = [Diagnostics.Stopwatch]::StartNew()
$resource = Get-AzureRmResource -ResourceGroupName $resourceGroupName -ResourceName $resourceName -ExpandProperties
$stopWatch.Stop()
Write-Host 'The virtual machine scale set' $resourceName 'has been successfully retrieved from the' $resourceGroupName 'in' $stopWatch.Elapsed.TotalSeconds 'seconds'
$extensions = $resource.Properties.VirtualMachineProfile.ExtensionProfile.Extensions
$ok = $false
for ($i = 0; $i -lt $extensions.Count; $i++)
{
    if ($extensions[$i].Name -eq $extensionName)
    {
        $extensions[$i] = $extension
        Write-Host 'Updating the' $extensionName 'extension of the virtual machine scale set' $resourceName '...'
        $stopWatch = [Diagnostics.Stopwatch]::StartNew()
        $resource|Set-AzureRmResource -Force
        $stopWatch.Stop()
        Write-Host 'The virtual machine scale set' $resourceName 'has been successfully updated in' $stopWatch.Elapsed.TotalSeconds 'seconds'
        $ok = $true
        break
    }
}
if (-not $ok)
{
    [System.Collections.ArrayList]$arrayList = $extensions
    $arrayList.Add($extension)
    $resource.Properties.VirtualMachineProfile.ExtensionProfile.Extensions = $arrayList.ToArray()
    Write-Host 'Adding the' $extensionName 'extension to the virtual machine scale set' $resourceName '...'
    $stopWatch = [Diagnostics.Stopwatch]::StartNew()
    $resource|Set-AzureRmResource -Force
    $stopWatch.Stop()
    Write-Host 'The virtual machine scale set' $resourceName 'has been successfully updated in' $stopWatch.Elapsed.TotalSeconds 'seconds'
}