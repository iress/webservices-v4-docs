from zeep import Client, Settings
from decimal import Decimal
import sys
import logging
import os
import time
import sys
import click
import distutils.util
import uuid

def logInfo(message):
    logger = logging.getLogger(__name__)
    logger.info(message)    

def logError(message):
    logger = logging.getLogger(__name__)
    logger.error(message)    

def DeleteAlert(iressClient, iressClientFactory, iressSessionKey, alertId, securityCode, securityOperator, lastPrice, lastPriceOperator, alertMemo):
    #logInfo("Deleting alert with AlertID {} for {} at price {:.3f} [{}]".format(alertId, securityCode, lastPrice, alertMemo))
     
    # Delete alert with the specified alert ID
    alertDeleteInputParameters = iressClientFactory.AlertDeleteInputParameters(AlertIDArray=[alertId])
    try:
        alertDeleteResponse = iressClient.service.AlertDelete(iressClientFactory.AlertDeleteInput(Header=iressClientFactory.AlertDeleteInputHeader(SessionKey=iressSessionKey),Parameters=alertDeleteInputParameters))
    except Exception as ex:
        logError("Alert deletion failed for AlertID {} for {} at price {:.3f} [{}]. Error: {}".format(alertId, securityCode, lastPrice, alertMemo, str(ex)))
        return
    except:
        logError("Alert deletion failed for AlertID {} for {} at price {:.3f} [{}]. Error: Unspecified".format(alertId, securityCode, lastPrice, alertMemo))
        return

    if alertDeleteResponse.Result.DataRows:
        alertDeleteDataRows = alertDeleteResponse.Result.DataRows
        
        for alertDeleteResponseDataRow in alertDeleteDataRows.DataRow:
            errorNumber = alertDeleteResponseDataRow.ErrorNumber
            errorDescription = alertDeleteResponseDataRow.ErrorDescription
            alertId = alertDeleteResponseDataRow.AlertID
            
            if errorNumber == 0:
                logInfo("Alert deletion succeeded for AlertID {} for {} at price {} [{}]".format(alertId, securityCode, lastPrice, alertMemo))
            else:
                logError("Alert deletion failed for AlertID {} for {} at price {} [{}] - ErrorNumber {} ErrorDescription: {}".format(alertId, securityCode, lastPrice, alertMemo, errorNumber, errorDescription))

def WipeExistingAlerts(iressClient, iressClientFactory, iressSessionKey):
    logInfo("Wiping existing alerts")

    alertGetMoreDataAvailable = True
    alertGetRequestID = str(uuid.uuid4()) # Generate a request ID to support paging through the AlertGet method

    # Get existing alerts
    while alertGetMoreDataAvailable:
        try: 
            alertGetInputParameters = iressClientFactory.AlertGetInputParameters()
            alertGetInput = iressClientFactory.AlertGetInput(Header=iressClientFactory.AlertGetInputHeader(SessionKey=iressSessionKey, RequestID=alertGetRequestID),Parameters=alertGetInputParameters)
            alertGetResponse = iressClient.service.AlertGet(alertGetInput)
        except Exception as ex:
            logError("Unable to wipe existing alerts. Alert retrieval failed. Error: {}".format(str(ex)))
            return
        except:
            logError("Unable to wipe existing alerts. Alert retrieval failed. Error: Unspecified")    
            return

        # Check requet status to determine if there is any more data available after this response
        alertGetRequestStatus = alertGetResponse.Result.Header.StatusCode
        if alertGetRequestStatus != 1:
            alertGetMoreDataAvailable = False

        if alertGetResponse.Result.DataRows:
            alertGetDataRows = alertGetResponse.Result.DataRows
            
            for alertGetResponseDataRow in alertGetDataRows.DataRow:
                alertMemo = alertGetResponseDataRow.AlertMemo
                
                # If the alert memo contains the text "PortfolioCode -" we know it was generated previously with this tool. Delete the alert.
                if alertMemo:
                    if alertMemo.find("PortfolioCode -") > -1:
                        alertId = alertGetResponseDataRow.AlertID

                        # Find the index of the Security
                        alertFieldNamesArray = alertGetResponseDataRow.AlertFieldNames.split(";")
                        if ('Security' in alertFieldNamesArray) and ('Last' in alertFieldNamesArray):
                            securityIndex = alertFieldNamesArray.index("Security")
                            lastIndex = alertFieldNamesArray.index("Last")
                            alertFieldValues = alertGetResponseDataRow.AlertFieldValues.split(";")
                            securityCode = alertFieldValues[securityIndex]
                            lastPrice = Decimal(alertFieldValues[lastIndex])
                            alertFieldOperatorArray = alertGetResponseDataRow.AlertFieldOperators.split(";")
                            securityOperator = alertFieldOperatorArray[securityIndex]
                            lastPriceOperator = alertFieldOperatorArray[lastIndex]

                            DeleteAlert(iressClient, iressClientFactory, iressSessionKey, alertId, securityCode, securityOperator, lastPrice, lastPriceOperator, alertMemo)

def CreateQuoteAlert(iressClient, iressClientFactory, iressSessionKey, alertOperator, securityCode, exchange, alertPrice, portfolioCode, memo):
    try:
        alertCreateInputParameters = iressClientFactory.AlertCreateInputParameters(AlertTypeArray=["Quote"], AlertFieldNamesArray=["Security;Last"],AlertFieldOperatorsArray=["==;{}".format(alertOperator)],AlertFieldValuesArray=["{}.{};{:.3f}".format(securityCode, exchange, alertPrice)],ReactivateTimeArray=[0],AlertMemoArray=[memo],UseMessageManagerNotificationsArray=[True])
        alertCreateResponse = iressClient.service.AlertCreate(iressClientFactory.AlertCreateInput(Header=iressClientFactory.AlertCreateInputHeader(SessionKey=iressSessionKey),Parameters=alertCreateInputParameters))
        if alertCreateResponse.Result.DataRows:
            alertCreateDataRows = alertCreateResponse.Result.DataRows
            
            for alertCreateResponseDataRow in alertCreateDataRows.DataRow:
                errorNumber = alertCreateResponseDataRow.ErrorNumber
                errorDescription = alertCreateResponseDataRow.ErrorDescription
                alertId = alertCreateResponseDataRow.AlertID

                if errorNumber == 0:
                    logInfo("Alert created successfully for {}.{} in portfolio {} at price {:.3f} - AlertID: {}".format(securityCode, exchange, portfolioCode, alertPrice, alertId))
                    return True
                else:
                    logError("Alert create failed for {}.{} in portfolio {} at price {:.3f} - ErrorNumber {} ErrorDescription: {}".format(securityCode, exchange, portfolioCode, alertPrice, errorNumber, errorDescription))
                    return False
    except Exception as ex:
        logError("Alert creation failed for {}.{} in portfolio {} at price {:.3f}. Error: {}".format(securityCode, exchange, portfolioCode, alertPrice, str(ex)))
        return False
    except:
        logError("Alert creation failed for {}.{} in portfolio {} at price {:.3f}. Error: Unspecified".format(securityCode, exchange, portfolioCode, alertPrice))        
        return False

def is_number(s):
    try:
        float(s)
        return True
    except ValueError:
        return False

# Use click library to process command line arguments - this way we can support the provision of a password and where not passed by user it will prompt them
@click.command()
@click.option('--username', '-u', prompt="IRESS User name", help='The IRESS username to login to Web Services using.')    
@click.option('--companyname', '-c', prompt="Company name", help='The company name to login to Web Services using.')    
@click.option('--password', '-p', prompt=True, confirmation_prompt=False, hide_input=True)
@click.option('--iosname', '-i', prompt="IOS+ server name", help='The IOS+ server name to connect to.')    
@click.option('--thresholdvalue', '-t', prompt="Position threshold value", help='The threshold value in absolute terms that a portfolio position must be greater than in order to be considered for the alert creation.')
@click.option('--percentchange', '-p', prompt="Percentage change", help='The percentage change in the start of day average price to generate an alert for.')
@click.option('--endpoint', '-e', prompt="Web Services WSDL endpoint", help='The Web Services WSDL endpoint to connect to.')
@click.option('--wipeexistingalerts', '-w', prompt="Wipe existing alerts", help='Indicates whether to wipe existing alerts created by the Portfolio Alerter tool.')
def main(username, companyname, password, iosname, thresholdvalue, percentchange, endpoint, wipeexistingalerts):
    # Setup logger
    logDirectory = os.path.dirname(os.path.realpath(__file__))
    logOutputFileName = 'portfolioAlerter_{}.log'.format(time.strftime("%Y%m%d-%H%M%S"))
    logFileFullPath = os.path.join(logDirectory, logOutputFileName)
    logger = logging.getLogger(__name__)
    logger.setLevel(logging.DEBUG)
    logFormatter = logging.Formatter(fmt='%(asctime)s.%(msecs)03d %(message)s',datefmt='%Y-%m-%d %H:%M:%S')
    fileHandler = logging.FileHandler(logFileFullPath)
    fileHandler.setLevel(logging.DEBUG)
    consoleHandler = logging.StreamHandler()
    consoleHandler.setLevel(logging.DEBUG)
    fileHandler.setFormatter(logFormatter)    
    consoleHandler.setFormatter(logFormatter)    
    logger.addHandler(fileHandler)
    logger.addHandler(consoleHandler)

    # Determine whether we need to wipe existing alerts or not
    bWipeExistingAlerts = distutils.util.strtobool(wipeexistingalerts)    

    if is_number(thresholdvalue) == False:
        logError("Threshold value must be a floating point number.")
        return

    thresholdvalue_float = float(thresholdvalue)
    
    if is_number(percentchange) == False:
        logError("Percent change must be a floating point number.")
        return
    
    percentchange_float = float(percentchange)

    if percentchange_float <= 0.0:
        logError("Percent change must be greater than 0.")
        return

    longMultiplier = 1 - (percentchange_float / 100)
    shortMultiplier = 1 + (percentchange_float / 100)

    # Configure Zeep settings
    settings = Settings(strict = False, xml_huge_tree = True)

    # Work out IOS+ WSDL endpoint details
    iosPlusMethodList = "PortfolioGet,PortfolioPositionDetailGet"
    iosPlusWsdl = "{}?un={}&cp={}&pw={}&svc=IOSPlus&svr={}&mf={}".format(endpoint, username, companyname, password, iosname, iosPlusMethodList)

    # Work out IRESS  WSDL endpoint details
    iressMethodList = "AlertCreate,AlertGet,AlertDelete"
    iressWsdl = "{}?un={}&cp={}&pw={}&svc=IRESS&svr={}&mf={}".format(endpoint, username, companyname, password, "", iressMethodList)

    # Create the Web Services client objects, one for the IOS+ WSDL and one for the IRESS WSDL
    iosPlusClient = Client(iosPlusWsdl, settings=settings)
    iressClient = Client(iressWsdl, settings=settings)

    # Obtain the factories for the client objects
    iosPlusClientFactory = iosPlusClient.type_factory('ns0')
    iressClientFactory = iressClient.type_factory('ns0')

    # Start the IRESS and IOS Service session
    iressSessionStartResponse = iosPlusClient.service.IRESSSessionStart(iosPlusClientFactory.IRESSSessionStartInput(Header=iosPlusClientFactory.IRESSSessionStartInputHeader(Updates=False), Parameters=iosPlusClientFactory.IRESSSessionStartInputParameters(UserName=username, CompanyName=companyname, Password=password)))
    iressSessionKey = iressSessionStartResponse.Result.DataRows.DataRow[0].IRESSSessionKey
    serviceSessionStartResponse = iosPlusClient.service.ServiceSessionStart(iosPlusClientFactory.ServiceSessionStartInput(Parameters=iosPlusClientFactory.ServiceSessionStartInputParameters(IRESSSessionKey=iressSessionKey, Service= "IOSPLUS", Server=iosname)))
    serviceSessionKey = serviceSessionStartResponse.Result.DataRows.DataRow[0].ServiceSessionKey

    # Clear existing alerts for the current user
    if bWipeExistingAlerts:
        WipeExistingAlerts(iressClient, iressClientFactory, iressSessionKey)

    # Get a list of all the portfolio codes for the given user
    logInfo("Retrieving list of portfolios for user {}@{}".format(username, companyname))
    portfolioCount = 0
    alertsToCreateCount = 0
    alertsCreatedCount = 0
    portfolioGetMoreDataAvailable = True
    portfolioGetRequestID = str(uuid.uuid4()) # Generate a request ID to support paging through the PortfolioGet method

    while portfolioGetMoreDataAvailable:
        try:
            portfolioGetResponse = iosPlusClient.service.PortfolioGet(iosPlusClientFactory.PortfolioGetInput(Header=iosPlusClientFactory.PortfolioGetInputHeader(ServiceSessionKey=serviceSessionKey, RequestID=portfolioGetRequestID),Parameters=iosPlusClientFactory.PortfolioGetInputParameters(AccessMode=0, FilterBy=0, FilterMode=0, FilterText="", IncludeInactive=True)))
        except Exception as ex:
            logError("Portfolio retrieval failed. Error: {}".format(str(ex)))
            return
        except:
            logError("Portfolio retrieval failed. Error: Unspecified")
            return

        # Check requet status to determine if there is any more data available after this response
        portfolioGetRequestStatus = portfolioGetResponse.Result.Header.StatusCode
        if portfolioGetRequestStatus != 1:
            portfolioGetMoreDataAvailable = False

        if portfolioGetResponse.Result.DataRows:
            portfolioGetDataRows = portfolioGetResponse.Result.DataRows.DataRow

            # For each portfolio, request the position details.
            for portfolioGetDataRow in portfolioGetDataRows:
                portfolioCode = portfolioGetDataRow.PortfolioCode
                portfolioCount = portfolioCount + 1
                portfolioPositionDetailGetRequestID = str(uuid.uuid4()) # Generate a request ID to support paging through the PortfolioPositionDetailGet method
                portfolioPositionDetailGetMoreDataAvailable = True

                # Get the Portfolio details for each portfolio
                while portfolioPositionDetailGetMoreDataAvailable:
                    try:
                        #logInfo("Retrieving list of portfolios positions for PortfolioCode {}...".format(portfolioCode))
                        
                        portfolioPositionDetailGetResponse = iosPlusClient.service.PortfolioPositionDetailGet(iosPlusClientFactory.PortfolioPositionDetailGetInput(Header=iosPlusClientFactory.PortfolioPositionDetailGetInputHeader(ServiceSessionKey=serviceSessionKey, RequestID=portfolioPositionDetailGetRequestID), Parameters=iosPlusClientFactory.PortfolioPositionDetailGetInputParameters(AccessMode=0, PortfolioCodeArray=[portfolioCode], IncludePositionsFromPortfoliosWithSameCashAccountArray=[False])))

                        # Check requet status to determine if there is any more data available after this response
                        portfolioPositionDetailGetGetRequestStatus = portfolioPositionDetailGetResponse.Result.Header.StatusCode
                        if portfolioPositionDetailGetGetRequestStatus != 1:
                            portfolioPositionDetailGetMoreDataAvailable = False

                        if portfolioPositionDetailGetResponse.Result.DataRows:
                            for portfolioPositionDetailGetDataRow in portfolioPositionDetailGetResponse.Result.DataRows.DataRow:
                                # If the position is greater than the threshold value provided on input, grab the details of the position and create an alert for it
                                portfolioCode = portfolioPositionDetailGetDataRow.PortfolioCode
                                securityCode = portfolioPositionDetailGetDataRow.SecurityCode
                                exchange = portfolioPositionDetailGetDataRow.Exchange
                                averagePriceSOD = portfolioPositionDetailGetDataRow.AveragePriceStartOfDay
                                volumeSOD = portfolioPositionDetailGetDataRow.VolumeStartOfDay
                                actualValue = portfolioPositionDetailGetDataRow.ActualValue
                                
                                absActualValue = abs(actualValue)
                                if absActualValue > thresholdvalue_float:
                                    alertsToCreateCount = alertsToCreateCount + 1

                                    # Create a Quote alert. If position is short, create an alert to notify the user if the position has gone up by more than the specified amount.
                                    # For longs, the alert will notify the user if position the has gone down by more than the specified amount.
                                    if volumeSOD < 0:
                                        alertOperator = ">="
                                        alertPrice = shortMultiplier * averagePriceSOD
                                    else:
                                        alertOperator = "<="
                                        alertPrice = longMultiplier * averagePriceSOD

                                    # Create a quote alert for the position that exceeded threshold
                                    if CreateQuoteAlert(iressClient, iressClientFactory, iressSessionKey, alertOperator, securityCode, exchange, alertPrice, portfolioCode, "PortfolioCode - {}".format(portfolioCode)):
                                        alertsCreatedCount = alertsCreatedCount + 1                            
                    except Exception as ex:
                        logError("Portfolio position retrieval failed for PortfolioCode {}. Error: {}".format(portfolioCode, str(ex)))
                    except:
                        logError("Portfolio position retrieval failed for PortfolioCode {}. Error: Unspecified".format(portfolioCode))
                    
        else:
            logInfo("No portfolios associated with the requesting user.")    
            return

    logInfo("Finished creating alerts. Portfolios scanned: {} Alerts to create: {} Alerts created successfully: {}".format(portfolioCount, alertsToCreateCount, alertsCreatedCount))

def runMain():
    main()
    
if __name__ == "__main__":
    runMain()