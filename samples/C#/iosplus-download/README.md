This is a C# .NET 8.0 sample application intended that demonstrates using Web Services V4 to extract trades, audit trail, and order search records from an IOS+ server into a CSV file.

The prerequisites for running this sample are .NET 8.0, and are intended to be supported cross-platform: Windows, Linux, and Mac. 
The solution file was created under Visual Studio 2019, however it should be possible to use Visual Studio Code or another editor to view or edit the code as well as build and run the sample via the command line.

The quickest way to get up an running is by running within Docker. First clone the repository:
```
git clone git@github.com:iress/webservices-v4-docs.git
```

Then set the following environment variables that provide the credentials for authentication and server specification:
```
iosplusdownload_UserName	i.e. tim.robinson	
iosplusdownload_Company		i.e. iress
iosplusdownload_Password	
iosplusdownload_SoapEndpoint	i.e. https://webservices.iress.com.au/v4/SOAP.ASPX
iosplusdownload_IosPlusServer	i.e. IOSPLUSAPIRETAIL3
 ```
 
Then launch the application using docker-compose as follows - note, the configuration is read into the docker container via the above environment variables:

```
docker-compose up --build
```

For those wanting to run the configure and run the sample within a development environment, follow the steps below. They should all be run from the .NET CLI Console.

In order to download and update the WSDLs used in this project, first install the dotnet-svcutil onto system if not already there:
```
dotnet tool install --global dotnet-svcutil
```

(WINDOWS ONLY - Powershell Script) Update WSDL references to the IRESS & IOS+ SOAP endpoints using:
```
update_service_references.ps1
```

(WINDOWS ONLY - Powershell Script) If there are any issues authenticating to the WSDL, download the WSDLs from a browser first to the files Iress.wsdl and IosPlus.wsdl respectively (in the WebServices folder) then run:
```
update_service_references_from_file.ps1
```

To install/restore the dependencies of the project, use:
```
dotnet restore
``` 

To build the project:
```
dotnet build
```

To run the project:
```
dotnet run
```
