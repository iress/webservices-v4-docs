from requests import Session
from requests.auth import HTTPBasicAuth
from zeep.transports import Transport
from zeep import Client, Settings
import logging
import os
import time
import click
import socket
import uuid
import time

# Use click library to process command line arguments - this way we can support the provision of a password and where not passed by user it will prompt them
@click.command()
@click.option('--username', '-u', prompt="IRESS User Name", help='The IRESS username to login to Web Services using.')    
@click.option('--companyname', '-c', prompt="Company Name", help='The company name to login to Web Services using.')    
@click.option('--password', '-p', prompt=True, confirmation_prompt=False, hide_input=True)
@click.option('--iosname', '-i', prompt="IOS+ Server Name", help='The IOS+ server name to connect to.', default="IOSPLUSAPIRETAIL3")  
@click.option('--endpoint', '-e', prompt="Web Services WSDL Endpoint", help='The Web Services WSDL endpoint to connect to.', default="https://webservices.iress.com.au/v4/wsdl.aspx")
@click.option('--ordercount', '-o', prompt="Number of orders to create", help='The total number of orders to create (one at a time).', default="10")
@click.option('--securitycode', '-s', prompt="Security Code", help='The security code to use on order creation.', default="BHP")
@click.option('--exchange', '-x', prompt="Exchange", help='The exchange to use on order creation.', default="ASX")
@click.option('--destination', '-d', prompt="Destination", help='The destination to use on order creation.', default="DESK")
@click.option('--accountcode', '-a', prompt="Account Code", help='The account code to use on order creation.', default="UNKNOWN")

def main(username, companyname, password, iosname, endpoint, ordercount, securitycode, exchange, destination, accountcode):
    # Work out where to store the logs - use the current hostname and date/time in the filename
    hostname = socket.gethostname()
    timeFormatted = time.strftime("%Y%m%d-%H%M%S")
    logOutputFileName = 'ordercreate_{}_{}.log'.format(hostname, timeFormatted)
    directoryName = os.path.dirname(os.path.realpath(__file__))

    # Setup logger
    loggingDateTimeFormat = '%Y-%m-%d %H:%M:%S'
    logFileFullPath = os.path.join(directoryName, logOutputFileName)
    logging.basicConfig(filename=logFileFullPath,level=logging.DEBUG,format='%(asctime)s.%(msecs)03d %(message)s', datefmt=loggingDateTimeFormat)

    # Define a Handler which writes INFO messages or higher to the sys.stderr
    consoleHandler = logging.StreamHandler()
    consoleHandler.setLevel(logging.INFO)
    consoleFormatter = logging.Formatter('%(asctime)s.%(msecs)03d %(message)s', datefmt=loggingDateTimeFormat)
    consoleHandler.setFormatter(consoleFormatter)
    logging.getLogger('').addHandler(consoleHandler)

    # Verify the order count is a valid number
    nOrderCount = 0
    if type(ordercount) == int:
        nOrderCount = ordercount
    else:
        orderCountStr = str(ordercount)
        if not orderCountStr.isdigit():
            logging.error("Order count needs to be provided as a whole number value.")
            return
        else:
            nOrderCount = int(ordercount)

    # Configure Zeep settings
    settings = Settings(strict = False, xml_huge_tree = True)

    # Work out IOS+ WSDL endpoint details
    iosPlusMethodList = "OrderCreate3"
    iosPlusWsdl = "{}?svc=IOSPlus&svr={}&mf={}".format(endpoint, iosname, iosPlusMethodList)

    # Create the Web Services client objects, one for the IOS+ WSDL and one for the IRESS WSDL
    iosPlusClient = None
    iosPlusClientFactory = None
    try:
        userCompany = username + "@" + companyname

        iosPlusSession = Session()
        iosPlusSession.auth = HTTPBasicAuth(userCompany, password)
        iosPlusClient = Client(iosPlusWsdl, settings=settings, transport=Transport(session=iosPlusSession))

        # Check that the service is healthy.
        if iosPlusClient.wsdl.messages == {}:
            logging.error("Accessing Web Services WSDL failed. WSDL URL: {} Error: No supported messages.".format(iosPlusWsdl))
            return

        # Obtain the factories for the client objects
        iosPlusClient.set_ns_prefix("ns0", "http://webservices.iress.com.au/v4/")         
        iosPlusClientFactory = iosPlusClient.type_factory('http://webservices.iress.com.au/v4/')
    except Exception as ex:
        logging.error("Accessing Web Services WSDL failed. WSDL URL: {} Error: {}".format(iosPlusWsdl, str(ex)))
        return
    except:
        logging.error("Accessing Web Services WSDL failed. WSDL URL: {} Error: Unspecified".format(iosPlusWsdl))
        return
    
    # Start the IRESS and IOS+ Service session
    try:
        appId = str(uuid.uuid4()) # Generate a application ID that is unique.
        iressSessionStartResponse = iosPlusClient.service.IRESSSessionStart(iosPlusClientFactory.IRESSSessionStartInput(Header=iosPlusClientFactory.IRESSSessionStartInputHeader(Updates=False), Parameters=iosPlusClientFactory.IRESSSessionStartInputParameters(UserName=username, CompanyName=companyname, ApplicationID=appId, Password=password, SessionNumberToKick=-2, KickLikeSessions=True)))
        iressSessionKey = iressSessionStartResponse.Result.DataRows.DataRow[0].IRESSSessionKey
        serviceSessionStartResponse = iosPlusClient.service.ServiceSessionStart(iosPlusClientFactory.ServiceSessionStartInput(Parameters=iosPlusClientFactory.ServiceSessionStartInputParameters(IRESSSessionKey=iressSessionKey, Service= "IOSPLUS", Server=iosname)))
        serviceSessionKey = serviceSessionStartResponse.Result.DataRows.DataRow[0].ServiceSessionKey
    except Exception as ex:
        logging.error("Web Services session creation failed. Error: {}".format(str(ex)))
        return
    except:
        logging.error("Web Services session creation failed. Error: Unspecified")
        return

    logging.info("Sessions created for user {}@{}, IRESS session key: {} Service Session Key: {}\n\nCreating orders...".format(username, companyname, iressSessionKey, serviceSessionKey))

    # Create orders, one at a time, and measure average latency.
    start_time = time.time()

    for i in range(1, nOrderCount + 1):
        orderCreateRequestID = str(uuid.uuid4()) # Generate a request ID to support paging through the OrderCreate3 method
        orderCreateResponse = None

        try:
            orderCreateResponse = iosPlusClient.service.OrderCreate3(iosPlusClientFactory.OrderCreate3Input(Header=iosPlusClientFactory.OrderCreate3InputHeader(ServiceSessionKey=serviceSessionKey, RequestID=orderCreateRequestID),Parameters=iosPlusClientFactory.OrderCreate3InputParameters(SideCodeArray="1", AccountCodeArray=accountcode, SecurityCodeArray=securitycode, ExchangeArray=exchange, DestinationArray=destination, OrderVolumeArray=100, OrderPriceArray=100)))
        except Exception as ex:
            logging.error("Order create #{} failed. Error: {}".format(i, str(ex)))            
        except:
            logging.error("Order create #{} failed. Error: Unspecified".format(i))            

        # Check requet status to determine if there is any more data available after this response
        if orderCreateResponse:
            orderCreateRequestStatus = orderCreateResponse.Result.Header.StatusCode
            # Status codes: 1 - More data available, 2 - Finished, 3 - Watching for updates.
            # In this script, should only ever hit 2.
            if orderCreateRequestStatus == 2:
                if orderCreateResponse.Result.DataRows:
                    orderCreateDataRows = orderCreateResponse.Result.DataRows.DataRow

                    for orderCreateDataRow in orderCreateDataRows:
                        errorMessage = orderCreateDataRow.ErrorMessage
                        if errorMessage:
                            logging.error("Order create #{} failed - Error: '{}'".format(i, errorMessage))
                        else:
                            logging.info("Order created #{} succeeded - Ord#: {} Destination: {} Account: {}".format(i, orderCreateDataRow.OrderNumber, destination, accountcode))
            else:
                logging.error("Order create #{} failed. Error: Unexpected request status [{}]".format(i, orderCreateRequestStatus))

    end_time = time.time()
    time_taken = end_time - start_time

    logging.info("Finished creating {} orders. Overall time taken: {:.2f}s. Average time per order: {:.4f}s".format(i, time_taken, time_taken/i))

def runMain():
    main()
    
if __name__ == "__main__":
    runMain()