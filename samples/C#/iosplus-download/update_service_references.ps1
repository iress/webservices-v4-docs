
Write-Output "For Iress web services"

cd WebServices
Remove-Item IressServiceReference
dotnet-svcutil.exe "http://webservices.iress.com.au/v4/wsdls.aspx?svc=IRESS&svr=&mf=" -n "*,WS.Iress" -d .\IressServiceReference

Write-Output "For IOS+ web services"

Remove-Item IosPlusServiceReference
dotnet-svcutil.exe "http://webservices.iress.com.au/v4/wsdls.aspx?svc=IOSPlus&svr=IOSPLUSAPIRETAIL3&mf=TradeGetByUser,OrderSearchGetByOrderNumber,AuditTrailGetByOrderNumber,AuditTrailGetByUser,OrderSearchGetByUser" -n "*,WS.IosPlus" -d .\IosPlusServiceReference\