# Login with an Azure account
Login-AzureRmAccount

$location = 'West Europe'
$resourceName = 'Node'
$resourceGroupName = 'PaoloServiceFabricResourceGroup'
$vmDiagnosticsSettings = ConvertFrom-Json "$(get-content "C:\Projects\Azure\ServiceFabric\IoTDemo\Diagnostics\PaoloVMDiagnosticsSettings.json")"


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
    if ($extensions[$i].Name -eq 'Node_Microsoft.Insights.VMDiagnosticsSettings')
    {
        [System.Collections.ArrayList]$arrayList = $extensions
        $arrayList.RemoveAt($i)
        $resource.Properties.VirtualMachineProfile.ExtensionProfile.Extensions = $arrayList.ToArray()
        Write-Host 'Removing the Node_Microsoft.Insights.VMDiagnosticsSettings extension of the virtual machine scale set' $resourceName '...'
        $stopWatch = [Diagnostics.Stopwatch]::StartNew()
        $resource|Set-AzureRmResource -Force
        $stopWatch.Stop()
        Write-Host 'The virtual machine scale set' $resourceName 'has been successfully updated in' $stopWatch.Elapsed.TotalSeconds 'seconds'
        $ok = $true
        break
    }
}