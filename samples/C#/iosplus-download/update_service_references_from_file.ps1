
Write-Output "For Iress web services"

cd WebServices
Remove-Item IressServiceReference
dotnet-svcutil.exe .\Iress.wsdl -n "*,WS.Iress" -d .\IressServiceReference

Write-Output "For IOS+ web services"

Remove-Item IosPlusServiceReference
dotnet-svcutil.exe .\IosPlus.wsdl -n "*,WS.IosPlus" -d .\IosPlusServiceReference\