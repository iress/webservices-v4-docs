using CommandLine;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using Microsoft.Extensions.Configuration;
//using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using System.ServiceModel.Dispatcher;
using WS.IosPlus;
using WS.Iress;

namespace IosPlusDownload
{
    public class MaxFaultSizeBehavior : IEndpointBehavior
	{
		private readonly int _size;

		public MaxFaultSizeBehavior( int size )
		{
			_size = size;
		}

		public void Validate( ServiceEndpoint endpoint )
		{
		}

		public void AddBindingParameters( ServiceEndpoint endpoint, BindingParameterCollection bindingParameters )
		{
		}

		public void ApplyDispatchBehavior( ServiceEndpoint endpoint, EndpointDispatcher endpointDispatcher )
		{
		}

		public void ApplyClientBehavior( ServiceEndpoint endpoint, ClientRuntime clientRuntime )
		{
			clientRuntime.MaxFaultSize = _size;
		}
	}

    partial class Program
    {
        public static class WebServicesClient
        {
            //public const int MAX_SIZE = 1024000 * 10;
            public const int MAX_SIZE = int.MaxValue;

            /// <summary>
            /// Creates the HTTP binding.
            /// </summary>
            /// <returns></returns>
            public static BasicHttpBinding CreateHttpBinding()
            {
                return new BasicHttpBinding(BasicHttpSecurityMode.Transport)
                {
                    MaxReceivedMessageSize = MAX_SIZE,
                    MaxBufferSize = MAX_SIZE,
                    MaxBufferPoolSize = MAX_SIZE,
                };
            }

            /// <summary>
            /// Configures the inner channel.
            /// </summary>
            /// <param name="channel">The channel.</param>
            public static void Configure(IClientChannel channel)
            {
                channel.OperationTimeout = new TimeSpan(0, 10, 0);
            }

            /// <summary>
            /// Configures the endpoint.
            /// </summary>
            /// <param name="serviceEndpoint">The service endpoint.</param>
            public static void Configure(ServiceEndpoint serviceEndpoint)
            {
                serviceEndpoint.EndpointBehaviors.Add(new MaxFaultSizeBehavior(MAX_SIZE));
            }
        }
 
        public class IosPlusApi : IAsyncDisposable
        {
            ILogger<IosPlusApi> _logger;
            ReportConfiguration _config = null;
            WS.Iress.IRESSSoapClient _iressClient = null;
            IOSPLUSSoapClient _iosClient = null;
            ReferenceDataCache _referenceDataCache = null;
            private string _iosPlusServerName = null;
            private string _iressSessionKey = null;
            private string _iosPlusSessionKey = null;
            public static readonly string FIELD_DELIMITER = ",";
            private static readonly int SECURITY_INFO_BATCH_SIZE = 100; // Max per SecurityInformationGet is 1000, but to lessen chance of timeouts use 100
            private static readonly int REQUEST_TIMEOUT_TRADES = 60;
            private static readonly int REQUEST_TIMEOUT_SECURITY_INFO = 60;
            private static readonly int REQUEST_TIMEOUT_AUDIT_TRAIL = 60;
            private static readonly int REQUEST_TIMEOUT_ORDER_SEARCH = 60;

            //public IosPlusApi(ILogger<IosPlusApi> logger, ReportConfiguration config)
            public IosPlusApi(ILoggerFactory loggerFactory, ReportConfiguration config)
            {
                _config = config;
                _referenceDataCache = new ReferenceDataCache(this);
                _logger = loggerFactory.CreateLogger<IosPlusApi>();
            }

            public async ValueTask DisposeAsync()
            {
                if (_iosPlusSessionKey != null)
                {
                    await LogoutIosPlusAsync();
                }

                if (_iressSessionKey != null)
                {
                    await LogoutIressAsync();
                }
            }


            public static class TradeCsvHeaders
            {
                public const string TradeNumber = "TradeNumber";
                public const string OrderNumber = "OrderNumber";
                public const string AccountCode = "AccountCode";
                public const string SecurityCode = "SecurityCode";
                public const string Exchange = "Exchange";
                public const string SEDOL = "SEDOL";
                public const string ISIN = "ISIN";
                public const string Destination = "Destination";
                public const string SubDestination = "SubDestination";
                public const string BuyOrSell = "BuyOrSell";
                public const string TradeVolume = "TradeVolume";
                public const string TradePrice = "TradePrice";
                public const string TradeValue = "TradeValue";
                public const string TradeFXRate = "TradeFXRate";
                public const string TradeDateTime = "TradeDateTime";
                public const string Principal = "Principal";
                public const string OpposingBrokerNumber = "OpposingBrokerNumber";
                public const string PrimaryClientOrderID = "PrimaryClientOrderID";
                public const string SecondaryClientOrderID = "SecondaryClientOrderID";
                public const string TradeMarkers = "TradeMarkers";
                public const string DestinationUserID = "DestinationUserID";
                public const string DestinationOrderNumber = "DestinationOrderNumber";
                public const string DestinationTradeNumber = "DestinationTradeNumber";
                public const string CancelledByTradeNumber = "CancelledByTradeNumber";
                public const string MarketDataOrderNumber = "MarketDataOrderNumber";
                public const string MarketDataTradeNumber = "MarketDataTradeNumber";
                public const string FXRateBidPriceOnOrder = "FXRateBidPriceOnOrder";
                public const string FXRateAskPriceOnOrder = "FXRateAskPriceOnOrder";
                public const string FXRateBidPriceOnTrade = "FXRateBidPriceOnTrade";
                public const string FXRateAskPriceOnTrade = "FXRateAskPriceOnTrade";
                public const string SourcePrice = "SourcePrice";
                public const string SourceCurrency = "SourceCurrency";
                public const string SideCode = "SideCode";
                public const string OrderDetails = "OrderDetails";
                public const string PriceMultiplier = "PriceMultiplier";
                public const string SettlementValue = "SettlementValue";
                public const string SettlementFXRate = "SettlementFXRate";
                public const string SettlementPrice = "SettlementPrice";
                public const string Organization = "Organization";
                public const string BookingDestination = "BookingDestination";
                public const string TradeMarketDetail = "TradeMarketDetail";
                public const string PostTradeStatusNumber = "PostTradeStatusNumber";
                public const string TradeSequenceNumber = "TradeSequenceNumber";
                public const string TradeDateTimeGMT = "TradeDateTimeGMT";
                public const string ExchangeTradeDateTime = "ExchangeTradeDateTime";
                public const string LocalMarketTradeDate = "LocalMarketTradeDate";
            }

            public static class AuditTrailCsvHeaders
            {
                public const string AuditTrailNumber = "AuditTrailNumber";
                public const string OrderNumber = "OrderNumber";
                public const string ParentOrderNumber = "ParentOrderNumber";
                public const string AuditLogDateTime = "AuditLogDateTime";
                public const string ExchangeDateTime = "ExchangeDateTime";
                public const string AccountCode = "AccountCode";
                public const string SecurityCode = "SecurityCode";
                public const string Exchange = "Exchange";
                public const string SEDOL = "SEDOL";
                public const string ISIN = "ISIN";
                public const string Destination = "Destination";
                public const string SubDestination = "SubDestination";
                public const string BuyOrSell = "BuyOrSell";
                public const string PricingInstructions = "PricingInstructions";
                public const string OrderState = "OrderState";
                public const string LastAction = "LastAction";
                public const string ActionStatus = "ActionStatus";
                public const string OrderVolume = "OrderVolume";
                public const string OrderPrice = "OrderPrice";
                public const string EventSummary = "EventSummary";
                public const string EventDescription = "EventDescription";
                public const string DoneVolumeTotal = "DoneVolumeTotal";
                public const string DoneValueTotal = "DoneValueTotal";
                public const string Lifetime = "Lifetime";
                public const string ExecutionInstructions = "ExecutionInstructions";
                public const string PrimaryClientOrderID = "PrimaryClientOrderID";
                public const string SecondaryClientOrderID = "SecondaryClientOrderID";
                public const string OrderGroup = "OrderGroup";
                public const string OrderDetails = "OrderDetails";
                public const string ExpiryDateTime = "ExpiryDateTime";
                public const string Currency = "Currency";
                public const string InternalOrderStatus = "InternalOrderStatus";
                public const string ExternalOrderStatus = "ExternalOrderStatus";
                public const string PCName = "PCName";
                public const string EventUserCode = "EventUserCode";
                public const string DestinationVolume = "DestinationVolume";
                public const string DestinationPrice = "DestinationPrice";
                public const string DestinationOrderNumber = "DestinationOrderNumber";
                public const string DestinationUserID = "DestinationUserID";
                public const string LastErrorNumber = "LastErrorNumber";
                public const string BidPrice = "BidPrice";
                public const string AskPrice = "AskPrice";
                public const string BidVolume = "BidVolume";
                public const string AskVolume = "AskVolume";
                public const string CustomColumns = "CustomColumns";
                public const string OrderMatchID = "OrderMatchID";
                public const string RemainingVolume = "RemainingVolume";
                public const string LastActionUserCode = "LastActionUserCode";
                public const string WorkedByUserCode = "WorkedByUserCode";
                public const string UpdateReasonMask = "UpdateReasonMask";
                public const string OrderFlagsMask = "OrderFlagsMask";
                public const string MarketDataOrderNumber = "MarketDataOrderNumber";
                public const string SideCode = "SideCode";
                public const string OrderGiver = "OrderGiver";
                public const string OrderTaker = "OrderTaker";
                public const string BackOfficeStatus = "BackOfficeStatus";
                public const string BackOfficeStatusDescription = "BackOfficeStatusDescription";
                public const string SettlementDoneValueTotal = "SettlementDoneValueTotal";
                public const string SettlementDoneValueToday = "SettlementDoneValueToday";
                public const string TrailerCodeOnMask = "TrailerCodeOnMask";
                public const string TrailerCodes = "TrailerCodes";
                public const string StructuredEventDetails = "StructuredEventDetails";
                public const string MarketDetail = "MarketDetail";
                public const string PostTradeStatusNumber = "PostTradeStatusNumber";
                public const string OrderLocked = "OrderLocked";
                public const string OrderLockedUserCode = "OrderLockedUserCode";
                public const string AccountDesignation = "AccountDesignation";
                public const string OrderValue = "OrderValue";
                public const string OrderRoutingType = "OrderRoutingType";
                public const string AlgoVolume = "AlgoVolume";
                public const string AlgoPrice = "AlgoPrice";
                public const string AlgoInMarketCount = "AlgoInMarketCount";
                public const string UpdateReasonMask2 = "UpdateReasonMask2";
                public const string AdvisorCode = "AdvisorCode";
                public const string EstimatedVolume = "EstimatedVolume";
                public const string EstimatedPrice = "EstimatedPrice";
                public const string EstimatedValue = "EstimatedValue";
                public const string ExtraOrderDetails = "ExtraOrderDetails";
                public const string WarehouseVolume = "WarehouseVolume";
                public const string SecurityDescription = "SecurityDescription";
                public const string OrderParked = "OrderParked";
            }

            public class IosPlusAuditTrailModel
            {
                [Name(AuditTrailCsvHeaders.AuditTrailNumber)]
                public long AuditTrailNumber { get; set; }

                [Name(AuditTrailCsvHeaders.OrderNumber)]
                public long OrderNumber { get; set; }

                [Name(AuditTrailCsvHeaders.ParentOrderNumber)]
                public long ParentOrderNumber { get; set; }

                [Name(AuditTrailCsvHeaders.AuditLogDateTime)]
                public DateTime? AuditLogDateTime { get; set; }

                [Name(AuditTrailCsvHeaders.ExchangeDateTime)]
                public string ExchangeDateTime { get; set; }
               
                [Name(AuditTrailCsvHeaders.AccountCode)]
                public string AccountCode { get; set; }

                [Name(AuditTrailCsvHeaders.SecurityCode)]
                public string SecurityCode { get; set; }

                [Name(AuditTrailCsvHeaders.Exchange)]
                public string Exchange { get; set; }

                [Name(AuditTrailCsvHeaders.SEDOL)]
                public string SEDOL { get; set; }

                [Name(AuditTrailCsvHeaders.ISIN)]
                public string ISIN { get; set; }

                [Name(AuditTrailCsvHeaders.Destination)]
                public string Destination { get; set; }

                [Name(AuditTrailCsvHeaders.SubDestination)]
                public string SubDestination { get; set; }

                [Name(AuditTrailCsvHeaders.BuyOrSell)]
                public string BuyOrSell { get; set; }

                [Name(AuditTrailCsvHeaders.PricingInstructions)]
                public string PricingInstructions { get; set; }

                [Name(AuditTrailCsvHeaders.OrderState)]
                public string OrderState { get; set; }

                [Name(AuditTrailCsvHeaders.LastAction)]
                public string LastAction { get; set; }

                [Name(AuditTrailCsvHeaders.ActionStatus)]
                public string ActionStatus { get; set; }

                [Name(AuditTrailCsvHeaders.OrderVolume)]
                public double OrderVolume { get; set; }

                [Name(AuditTrailCsvHeaders.OrderPrice)]
                public double OrderPrice { get; set; }

                [Name(AuditTrailCsvHeaders.EventSummary)]
                public string EventSummary { get; set; }

                [Name(AuditTrailCsvHeaders.EventDescription)]
                public string EventDescription { get; set; }

                [Name(AuditTrailCsvHeaders.DoneVolumeTotal)]
                public double DoneVolumeTotal { get; set; }

                [Name(AuditTrailCsvHeaders.DoneValueTotal)]
                public double DoneValueTotal { get; set; }

                [Name(AuditTrailCsvHeaders.Lifetime)]
                public string Lifetime { get; set; }

                [Name(AuditTrailCsvHeaders.ExecutionInstructions)]
                public string ExecutionInstructions { get; set; }

                [Name(AuditTrailCsvHeaders.PrimaryClientOrderID)]
                public string PrimaryClientOrderID { get; set; }

                [Name(AuditTrailCsvHeaders.SecondaryClientOrderID)]
                public string SecondaryClientOrderID { get; set; }

                [Name(AuditTrailCsvHeaders.OrderGroup)]
                public string OrderGroup { get; set; }

                [Name(AuditTrailCsvHeaders.OrderDetails)]
                public string OrderDetails { get; set; }

                [Name(AuditTrailCsvHeaders.ExpiryDateTime)]
                public DateTime? ExpiryDateTime { get; set; }
                
                [Name(AuditTrailCsvHeaders.Currency)]
                public string Currency { get; set; }

                [Name(AuditTrailCsvHeaders.InternalOrderStatus)]
                public string InternalOrderStatus { get; set; }

                [Name(AuditTrailCsvHeaders.ExternalOrderStatus)]
                public string ExternalOrderStatus { get; set; }

                [Name(AuditTrailCsvHeaders.PCName)]
                public string PCName { get; set; }

                [Name(AuditTrailCsvHeaders.EventUserCode)]
                public string EventUserCode { get; set; }

                [Name(AuditTrailCsvHeaders.DestinationVolume)]
                public double DestinationVolume { get; set; }

                [Name(AuditTrailCsvHeaders.DestinationPrice)]
                public double DestinationPrice { get; set; }

                [Name(AuditTrailCsvHeaders.DestinationOrderNumber)]
                public string DestinationOrderNumber { get; set; }

                [Name(AuditTrailCsvHeaders.DestinationUserID)]
                public string DestinationUserID { get; set; }

                [Name(AuditTrailCsvHeaders.LastErrorNumber)]
                public int LastErrorNumber { get; set; }

                [Name(AuditTrailCsvHeaders.BidPrice)]
                public double BidPrice { get; set; }

                [Name(AuditTrailCsvHeaders.AskPrice)]
                public double AskPrice { get; set; }

                [Name(AuditTrailCsvHeaders.BidVolume)]
                public double BidVolume { get; set; }

                [Name(AuditTrailCsvHeaders.AskVolume)]
                public double AskVolume { get; set; }

                [Name(AuditTrailCsvHeaders.CustomColumns)]
                public string CustomColumns { get; set; }

                [Name(AuditTrailCsvHeaders.OrderMatchID)]
                public string OrderMatchID { get; set; }

                [Name(AuditTrailCsvHeaders.RemainingVolume)]
                public double RemainingVolume { get; set; }

                [Name(AuditTrailCsvHeaders.LastActionUserCode)]
                public string LastActionUserCode { get; set; }

                [Name(AuditTrailCsvHeaders.WorkedByUserCode)]
                public string WorkedByUserCode { get; set; }

                [Name(AuditTrailCsvHeaders.UpdateReasonMask)]
                public long UpdateReasonMask { get; set; }

                [Name(AuditTrailCsvHeaders.OrderFlagsMask)]
                public long OrderFlagsMask { get; set; }

                [Name(AuditTrailCsvHeaders.MarketDataOrderNumber)]
                public long MarketDataOrderNumber { get; set; }

                [Name(AuditTrailCsvHeaders.SideCode)]
                public string SideCode { get; set; }

                [Name(AuditTrailCsvHeaders.OrderGiver)]
                public string OrderGiver { get; set; }

                [Name(AuditTrailCsvHeaders.OrderTaker)]
                public string OrderTaker { get; set; }

                [Name(AuditTrailCsvHeaders.BackOfficeStatus)]
                public string BackOfficeStatus { get; set; }

                [Name(AuditTrailCsvHeaders.BackOfficeStatusDescription)]
                public string BackOfficeStatusDescription { get; set; }

                [Name(AuditTrailCsvHeaders.SettlementDoneValueTotal)]
                public double SettlementDoneValueTotal { get; set; }

                [Name(AuditTrailCsvHeaders.SettlementDoneValueToday)]
                public double SettlementDoneValueToday { get; set; }

                [Name(AuditTrailCsvHeaders.TrailerCodeOnMask)]
                public long TrailerCodeOnMask { get; set; }

                [Name(AuditTrailCsvHeaders.TrailerCodes)]
                public string TrailerCodes { get; set; }

                [Name(AuditTrailCsvHeaders.StructuredEventDetails)]
                public string StructuredEventDetails { get; set; }

                [Name(AuditTrailCsvHeaders.MarketDetail)]
                public string MarketDetail { get; set; }

                [Name(AuditTrailCsvHeaders.PostTradeStatusNumber)]
                public short PostTradeStatusNumber { get; set; }

                [Name(AuditTrailCsvHeaders.OrderLocked)]
                public bool OrderLocked { get; set; }

                [Name(AuditTrailCsvHeaders.OrderLockedUserCode)]
                public string OrderLockedUserCode { get; set; }

                [Name(AuditTrailCsvHeaders.AccountDesignation)]
                public string AccountDesignation { get; set; }

                [Name(AuditTrailCsvHeaders.OrderValue)]
                public double OrderValue { get; set; }

                [Name(AuditTrailCsvHeaders.OrderRoutingType)]
                public string OrderRoutingType { get; set; }

                [Name(AuditTrailCsvHeaders.AlgoVolume)]
                public double AlgoVolume { get; set; }

                [Name(AuditTrailCsvHeaders.AlgoPrice)]
                public double AlgoPrice { get; set; }

                [Name(AuditTrailCsvHeaders.AlgoInMarketCount)]
                public double AlgoInMarketCount { get; set; }

                [Name(AuditTrailCsvHeaders.UpdateReasonMask2)]
                public long UpdateReasonMask2 { get; set; }

                [Name(AuditTrailCsvHeaders.AdvisorCode)]
                public string AdvisorCode { get; set; }

                [Name(AuditTrailCsvHeaders.EstimatedVolume)]
                public double EstimatedVolume { get; set; }

                [Name(AuditTrailCsvHeaders.EstimatedPrice)]
                public double EstimatedPrice { get; set; }

                [Name(AuditTrailCsvHeaders.EstimatedValue)]
                public double EstimatedValue { get; set; }

                [Name(AuditTrailCsvHeaders.ExtraOrderDetails)]
                public string ExtraOrderDetails { get; set; }

                [Name(AuditTrailCsvHeaders.WarehouseVolume)]
                public double WarehouseVolume { get; set; }

                [Name(AuditTrailCsvHeaders.SecurityDescription)]
                public string SecurityDescription { get; set; }

                [Name(AuditTrailCsvHeaders.OrderParked)]
                public bool OrderParked { get; set; }
            }

            public sealed class IosPlusAuditTrailMap : ClassMap<IosPlusAuditTrailModel>
            {
                //public IosPlusAuditTrailMap()
                //{
                //    Map(m => m.BuyOrSell).Name(AuditTrailCsvHeaders.BuyOrSell);

                //}
                private static string ExtractDateTime(DateTime? dt)
                {
                    if (!dt.HasValue)
                    {
                        return "";
                    }

                    var dateTime = dt.Value.ToString("yyyyMMdd HH:mm:ss.ffffff");
                    if (TimeZoneInfo.Local.IsDaylightSavingTime(dt.Value))
                    {
                        dateTime += " +0100s";
                    }
                    else
                    {
                        dateTime += " +0000";
                    }

                    return dateTime;
                }

                public IosPlusAuditTrailMap()
                {
                    AutoMap(CultureInfo.InvariantCulture);
                    Map(m => m.AuditTrailNumber).Name(AuditTrailCsvHeaders.AuditTrailNumber);
                    Map(m => m.OrderNumber).Name(AuditTrailCsvHeaders.OrderNumber);
                    Map(m => m.ParentOrderNumber).Name(AuditTrailCsvHeaders.ParentOrderNumber);
                    Map(m => m.AuditLogDateTime).Name(AuditTrailCsvHeaders.AuditLogDateTime);
                    Map(m => m.ExchangeDateTime).Name(AuditTrailCsvHeaders.ExchangeDateTime);
                    Map(m => m.AccountCode).Name(AuditTrailCsvHeaders.AccountCode);
                    Map(m => m.SecurityCode).Name(AuditTrailCsvHeaders.SecurityCode);
                    Map(m => m.Exchange).Name(AuditTrailCsvHeaders.Exchange);
                    Map(m => m.SEDOL).Name(AuditTrailCsvHeaders.SEDOL);
                    Map(m => m.ISIN).Name(AuditTrailCsvHeaders.ISIN);
                    Map(m => m.Destination).Name(AuditTrailCsvHeaders.Destination);
                    Map(m => m.SubDestination).Name(AuditTrailCsvHeaders.SubDestination);
                    Map(m => m.BuyOrSell).Name(AuditTrailCsvHeaders.BuyOrSell);
                    Map(m => m.PricingInstructions).Name(AuditTrailCsvHeaders.PricingInstructions);
                    Map(m => m.OrderState).Name(AuditTrailCsvHeaders.OrderState);
                    Map(m => m.LastAction).Name(AuditTrailCsvHeaders.LastAction);
                    Map(m => m.ActionStatus).Name(AuditTrailCsvHeaders.ActionStatus);
                    Map(m => m.OrderVolume).Name(AuditTrailCsvHeaders.OrderVolume);
                    Map(m => m.OrderPrice).Name(AuditTrailCsvHeaders.OrderPrice);
                    Map(m => m.EventSummary).Name(AuditTrailCsvHeaders.EventSummary);
                    Map(m => m.EventDescription).Name(AuditTrailCsvHeaders.EventDescription);
                    Map(m => m.DoneVolumeTotal).Name(AuditTrailCsvHeaders.DoneVolumeTotal);
                    Map(m => m.DoneValueTotal).Name(AuditTrailCsvHeaders.DoneValueTotal);
                    Map(m => m.Lifetime).Name(AuditTrailCsvHeaders.Lifetime);
                    Map(m => m.ExecutionInstructions).Name(AuditTrailCsvHeaders.ExecutionInstructions);
                    Map(m => m.PrimaryClientOrderID).Name(AuditTrailCsvHeaders.PrimaryClientOrderID);
                    Map(m => m.SecondaryClientOrderID).Name(AuditTrailCsvHeaders.SecondaryClientOrderID);
                    Map(m => m.OrderGroup).Name(AuditTrailCsvHeaders.OrderGroup);
                    Map(m => m.OrderDetails).Name(AuditTrailCsvHeaders.OrderDetails);
                    Map(m => m.ExpiryDateTime).Name(AuditTrailCsvHeaders.ExpiryDateTime);
                    Map(m => m.Currency).Name(AuditTrailCsvHeaders.Currency);
                    Map(m => m.InternalOrderStatus).Name(AuditTrailCsvHeaders.InternalOrderStatus);
                    Map(m => m.ExternalOrderStatus).Name(AuditTrailCsvHeaders.ExternalOrderStatus);
                    Map(m => m.PCName).Name(AuditTrailCsvHeaders.PCName);
                    Map(m => m.EventUserCode).Name(AuditTrailCsvHeaders.EventUserCode);
                    Map(m => m.DestinationVolume).Name(AuditTrailCsvHeaders.DestinationVolume);
                    Map(m => m.DestinationPrice).Name(AuditTrailCsvHeaders.DestinationPrice);
                    Map(m => m.DestinationOrderNumber).Name(AuditTrailCsvHeaders.DestinationOrderNumber);
                    Map(m => m.DestinationUserID).Name(AuditTrailCsvHeaders.DestinationUserID);
                    Map(m => m.LastErrorNumber).Name(AuditTrailCsvHeaders.LastErrorNumber);
                    Map(m => m.BidPrice).Name(AuditTrailCsvHeaders.BidPrice);
                    Map(m => m.AskPrice).Name(AuditTrailCsvHeaders.AskPrice);
                    Map(m => m.BidVolume).Name(AuditTrailCsvHeaders.BidVolume);
                    Map(m => m.AskVolume).Name(AuditTrailCsvHeaders.AskVolume);
                    Map(m => m.CustomColumns).Name(AuditTrailCsvHeaders.CustomColumns);
                    Map(m => m.OrderMatchID).Name(AuditTrailCsvHeaders.OrderMatchID);
                    Map(m => m.RemainingVolume).Name(AuditTrailCsvHeaders.RemainingVolume);
                    Map(m => m.LastActionUserCode).Name(AuditTrailCsvHeaders.LastActionUserCode);
                    Map(m => m.WorkedByUserCode).Name(AuditTrailCsvHeaders.WorkedByUserCode);
                    Map(m => m.UpdateReasonMask).Name(AuditTrailCsvHeaders.UpdateReasonMask);
                    Map(m => m.OrderFlagsMask).Name(AuditTrailCsvHeaders.OrderFlagsMask);
                    Map(m => m.MarketDataOrderNumber).Name(AuditTrailCsvHeaders.MarketDataOrderNumber);
                    Map(m => m.SideCode).Name(AuditTrailCsvHeaders.SideCode);
                    Map(m => m.OrderGiver).Name(AuditTrailCsvHeaders.OrderGiver);
                    Map(m => m.OrderTaker).Name(AuditTrailCsvHeaders.OrderTaker);
                    Map(m => m.BackOfficeStatus).Name(AuditTrailCsvHeaders.BackOfficeStatus);
                    Map(m => m.BackOfficeStatusDescription).Name(AuditTrailCsvHeaders.BackOfficeStatusDescription);
                    Map(m => m.SettlementDoneValueTotal).Name(AuditTrailCsvHeaders.SettlementDoneValueTotal);
                    Map(m => m.SettlementDoneValueToday).Name(AuditTrailCsvHeaders.SettlementDoneValueToday);
                    Map(m => m.TrailerCodeOnMask).Name(AuditTrailCsvHeaders.TrailerCodeOnMask);
                    Map(m => m.TrailerCodes).Name(AuditTrailCsvHeaders.TrailerCodes);
                    Map(m => m.StructuredEventDetails).Name(AuditTrailCsvHeaders.StructuredEventDetails);
                    Map(m => m.MarketDetail).Name(AuditTrailCsvHeaders.MarketDetail);
                    Map(m => m.PostTradeStatusNumber).Name(AuditTrailCsvHeaders.PostTradeStatusNumber);
                    Map(m => m.OrderLocked).Name(AuditTrailCsvHeaders.OrderLocked);
                    Map(m => m.OrderLockedUserCode).Name(AuditTrailCsvHeaders.OrderLockedUserCode);
                    Map(m => m.AccountDesignation).Name(AuditTrailCsvHeaders.AccountDesignation);
                    Map(m => m.OrderValue).Name(AuditTrailCsvHeaders.OrderValue);
                    Map(m => m.OrderRoutingType).Name(AuditTrailCsvHeaders.OrderRoutingType);
                    Map(m => m.AlgoVolume).Name(AuditTrailCsvHeaders.AlgoVolume);
                    Map(m => m.AlgoPrice).Name(AuditTrailCsvHeaders.AlgoPrice);
                    Map(m => m.AlgoInMarketCount).Name(AuditTrailCsvHeaders.AlgoInMarketCount);
                    Map(m => m.UpdateReasonMask2).Name(AuditTrailCsvHeaders.UpdateReasonMask2);
                    Map(m => m.AdvisorCode).Name(AuditTrailCsvHeaders.AdvisorCode);
                    Map(m => m.EstimatedVolume).Name(AuditTrailCsvHeaders.EstimatedVolume);
                    Map(m => m.EstimatedPrice).Name(AuditTrailCsvHeaders.EstimatedPrice);
                    Map(m => m.EstimatedValue).Name(AuditTrailCsvHeaders.EstimatedValue);
                    Map(m => m.ExtraOrderDetails).Name(AuditTrailCsvHeaders.ExtraOrderDetails);
                    Map(m => m.WarehouseVolume).Name(AuditTrailCsvHeaders.WarehouseVolume);
                    Map(m => m.SecurityDescription).Name(AuditTrailCsvHeaders.SecurityDescription);
                    Map(m => m.OrderParked).Name(AuditTrailCsvHeaders.OrderParked);
                }
            }

            public class IosPlusTradeModel
            {
                [Name(TradeCsvHeaders.TradeNumber)]
                public long TradeNumber { get; set; }

                [Name(TradeCsvHeaders.OrderNumber)]
                public long OrderNumber { get; set; }

                [Name(TradeCsvHeaders.AccountCode)]
                public string AccountCode { get; set; }

                [Name(TradeCsvHeaders.SecurityCode)]
                public string SecurityCode { get; set; }

                [Name(TradeCsvHeaders.Exchange)]
                public string Exchange { get; set; }

                [Name(TradeCsvHeaders.SEDOL)]
                public string SEDOL { get; set; }

                [Name(TradeCsvHeaders.ISIN)]
                public string ISIN { get; set; }

                [Name(TradeCsvHeaders.Destination)]
                public string Destination { get; set; }

                [Name(TradeCsvHeaders.SubDestination)]
                public string SubDestination { get; set; }

                [Name(TradeCsvHeaders.BuyOrSell)]
                public string BuyOrSell { get; set; }

                [Name(TradeCsvHeaders.TradeVolume)]
                public double TradeVolume { get; set; }

                [Name(TradeCsvHeaders.TradePrice)]
                public double TradePrice { get; set; }

                [Name(TradeCsvHeaders.TradeValue)]
                public double TradeValue { get; set; }

                [Name(TradeCsvHeaders.TradeFXRate)]
                public double TradeFXRate { get; set; }

                [Name(TradeCsvHeaders.TradeDateTime)]
                public DateTime? TradeDateTime { get; set; }

                [Name(TradeCsvHeaders.Principal)]
                public bool Principal { get; set; }

                [Name(TradeCsvHeaders.OpposingBrokerNumber)]
                public int OpposingBrokerNumber { get; set; }

                [Name(TradeCsvHeaders.PrimaryClientOrderID)]
                public string PrimaryClientOrderID { get; set; }

                [Name(TradeCsvHeaders.SecondaryClientOrderID)]
                public string SecondaryClientOrderID { get; set; }

                [Name(TradeCsvHeaders.TradeMarkers)]
                public string TradeMarkers { get; set; }

                [Name(TradeCsvHeaders.DestinationUserID)]
                public string DestinationUserID { get; set; }

                [Name(TradeCsvHeaders.DestinationOrderNumber)]
                public string DestinationOrderNumber { get; set; }

                [Name(TradeCsvHeaders.DestinationTradeNumber)]
                public string DestinationTradeNumber { get; set; }

                [Name(TradeCsvHeaders.CancelledByTradeNumber)]
                public long CancelledByTradeNumber { get; set; }

                [Name(TradeCsvHeaders.MarketDataOrderNumber)]
                public long MarketDataOrderNumber { get; set; }

                [Name(TradeCsvHeaders.MarketDataTradeNumber)]
                public long MarketDataTradeNumber { get; set; }

                [Name(TradeCsvHeaders.FXRateBidPriceOnOrder)]
                public double FXRateBidPriceOnOrder { get; set; }

                [Name(TradeCsvHeaders.FXRateAskPriceOnOrder)]
                public double FXRateAskPriceOnOrder { get; set; }

                [Name(TradeCsvHeaders.FXRateBidPriceOnTrade)]
                public double FXRateBidPriceOnTrade { get; set; }

                [Name(TradeCsvHeaders.FXRateAskPriceOnTrade)]
                public double FXRateAskPriceOnTrade { get; set; }

                [Name(TradeCsvHeaders.SourcePrice)]
                public double SourcePrice { get; set; }

                [Name(TradeCsvHeaders.SourceCurrency)]
                public string SourceCurrency { get; set; }

                [Name(TradeCsvHeaders.SideCode)]
                public string SideCode { get; set; }

                [Name(TradeCsvHeaders.OrderDetails)]
                public string OrderDetails { get; set; }

                [Name(TradeCsvHeaders.PriceMultiplier)]
                public double PriceMultiplier { get; set; }

                [Name(TradeCsvHeaders.SettlementValue)]
                public double SettlementValue { get; set; }

                [Name(TradeCsvHeaders.SettlementFXRate)]
                public double SettlementFXRate { get; set; }

                [Name(TradeCsvHeaders.SettlementPrice)]
                public double SettlementPrice { get; set; }

                [Name(TradeCsvHeaders.Organization)]
                public string Organization { get; set; }

                [Name(TradeCsvHeaders.BookingDestination)]
                public string BookingDestination { get; set; }

                [Name(TradeCsvHeaders.TradeMarketDetail)]
                public string TradeMarketDetail { get; set; }

                [Name(TradeCsvHeaders.PostTradeStatusNumber)]
                public short PostTradeStatusNumber { get; set; }

                [Name(TradeCsvHeaders.TradeSequenceNumber)]
                public long TradeSequenceNumber { get; set; }

                [Name(TradeCsvHeaders.TradeDateTimeGMT)]
                public DateTime? TradeDateTimeGMT { get; set; }

                [Name(TradeCsvHeaders.ExchangeTradeDateTime)]
                public DateTime? ExchangeTradeDateTime { get; set; }

                [Name(TradeCsvHeaders.LocalMarketTradeDate)]
                public DateTime? LocalMarketTradeDate { get; set; }
            }

            public sealed class IosPlusTradeMap : ClassMap<IosPlusTradeModel>
            {
                //public IosPlusTradeMap()
                //{
                //    Map(m => m.BuyOrSell).Name(TradeCsvHeaders.BuyOrSell);

                //}
                private static string ExtractDateTime(DateTime? dt)
                {
                    if (!dt.HasValue)
                    {
                        return "";
                    }

                    var dateTime = dt.Value.ToString("yyyyMMdd HH:mm:ss.ffffff");
                    if (TimeZoneInfo.Local.IsDaylightSavingTime(dt.Value))
                    {
                        dateTime += " +0100s";
                    }
                    else
                    {
                        dateTime += " +0000";
                    }

                    return dateTime;

                }

                public IosPlusTradeMap()
                {
                    AutoMap(CultureInfo.InvariantCulture);
                    //Map(m => m.TradeDateTime).Convert(m => ExtractDateTime(m.Value.TradeDateTime));
                    //Map(m => m.TradeDate).TypeConverterOption.Format("yyyyMMdd");
                    //Map(m => m.TradeTime).TypeConverterOption.Format("H:mm:ss");
                    //Map(m => m.EnteredDate).TypeConverterOption.Format("yyyyMMdd");
                    //Map(m => m.EnteredTime).TypeConverterOption.Format("H:mm:ss");
                    //Map(m => m.AmendedDate).TypeConverterOption.Format("yyyyMMdd");
                    //Map(m => m.AmendedTime).TypeConverterOption.Format("H:mm:ss");
                    //Map(m => m.SettlementDate).TypeConverterOption.Format("yyyyMMdd");
                    //Map(m => m.AmendedDatetime).Convert(m => ExtractDateTime(m.Value.AmendedDatetime));
                    //Map(m => m.EnteredDatetime).Convert(m => ExtractDateTime(m.Value.EnteredDatetime));
                    Map(m => m.TradeNumber).Name(TradeCsvHeaders.TradeNumber);
                    Map(m => m.OrderNumber).Name(TradeCsvHeaders.OrderNumber);
                    Map(m => m.AccountCode).Name(TradeCsvHeaders.AccountCode);
                    Map(m => m.SecurityCode).Name(TradeCsvHeaders.SecurityCode);
                    Map(m => m.Exchange).Name(TradeCsvHeaders.Exchange);
                    Map(m => m.SEDOL).Name(TradeCsvHeaders.SEDOL);
                    Map(m => m.ISIN).Name(TradeCsvHeaders.ISIN);
                    Map(m => m.Destination).Name(TradeCsvHeaders.Destination);
                    Map(m => m.SubDestination).Name(TradeCsvHeaders.SubDestination);
                    Map(m => m.BuyOrSell).Name(TradeCsvHeaders.BuyOrSell);
                    Map(m => m.TradeVolume).Name(TradeCsvHeaders.TradeVolume);
                    Map(m => m.TradePrice).Name(TradeCsvHeaders.TradePrice);
                    Map(m => m.TradeValue).Name(TradeCsvHeaders.TradeValue);
                    Map(m => m.TradeFXRate).Name(TradeCsvHeaders.TradeFXRate);
                    Map(m => m.TradeDateTime).Name(TradeCsvHeaders.TradeDateTime);
                    Map(m => m.Principal).Name(TradeCsvHeaders.Principal);
                    Map(m => m.OpposingBrokerNumber).Name(TradeCsvHeaders.OpposingBrokerNumber);
                    Map(m => m.PrimaryClientOrderID).Name(TradeCsvHeaders.PrimaryClientOrderID);
                    Map(m => m.SecondaryClientOrderID).Name(TradeCsvHeaders.SecondaryClientOrderID);
                    Map(m => m.TradeMarkers).Name(TradeCsvHeaders.TradeMarkers);
                    Map(m => m.DestinationUserID).Name(TradeCsvHeaders.DestinationUserID);
                    Map(m => m.DestinationOrderNumber).Name(TradeCsvHeaders.DestinationOrderNumber);
                    Map(m => m.DestinationTradeNumber).Name(TradeCsvHeaders.DestinationTradeNumber);
                    Map(m => m.CancelledByTradeNumber).Name(TradeCsvHeaders.CancelledByTradeNumber);
                    Map(m => m.MarketDataOrderNumber).Name(TradeCsvHeaders.MarketDataOrderNumber);
                    Map(m => m.MarketDataTradeNumber).Name(TradeCsvHeaders.MarketDataTradeNumber);
                    Map(m => m.FXRateBidPriceOnOrder).Name(TradeCsvHeaders.FXRateBidPriceOnOrder);
                    Map(m => m.FXRateAskPriceOnOrder).Name(TradeCsvHeaders.FXRateAskPriceOnOrder);
                    Map(m => m.FXRateBidPriceOnTrade).Name(TradeCsvHeaders.FXRateBidPriceOnTrade);
                    Map(m => m.FXRateAskPriceOnTrade).Name(TradeCsvHeaders.FXRateAskPriceOnTrade);
                    Map(m => m.SourcePrice).Name(TradeCsvHeaders.SourcePrice);
                    Map(m => m.SourceCurrency).Name(TradeCsvHeaders.SourceCurrency);
                    Map(m => m.SideCode).Name(TradeCsvHeaders.SideCode);
                    Map(m => m.OrderDetails).Name(TradeCsvHeaders.OrderDetails);
                    Map(m => m.PriceMultiplier).Name(TradeCsvHeaders.PriceMultiplier);
                    Map(m => m.SettlementValue).Name(TradeCsvHeaders.SettlementValue);
                    Map(m => m.SettlementFXRate).Name(TradeCsvHeaders.SettlementFXRate);
                    Map(m => m.SettlementPrice).Name(TradeCsvHeaders.SettlementPrice);
                    Map(m => m.Organization).Name(TradeCsvHeaders.Organization);
                    Map(m => m.BookingDestination).Name(TradeCsvHeaders.BookingDestination);
                    Map(m => m.TradeMarketDetail).Name(TradeCsvHeaders.TradeMarketDetail);
                    Map(m => m.PostTradeStatusNumber).Name(TradeCsvHeaders.PostTradeStatusNumber);
                    Map(m => m.TradeSequenceNumber).Name(TradeCsvHeaders.TradeSequenceNumber);
                    Map(m => m.TradeDateTimeGMT).Name(TradeCsvHeaders.TradeDateTimeGMT);
                    Map(m => m.ExchangeTradeDateTime).Name(TradeCsvHeaders.ExchangeTradeDateTime);
                    Map(m => m.LocalMarketTradeDate).Name(TradeCsvHeaders.LocalMarketTradeDate);
                }
            }

            public static class OrderSearchCsvHeaders
            {
                public const string RootParentOrderNumber = "RootParentOrderNumber";
                public const string OrderNumber = "OrderNumber";
                public const string ParentOrderNumber = "ParentOrderNumber";
                public const string AccountCode = "AccountCode";
                public const string SecurityCode = "SecurityCode";
                public const string Exchange = "Exchange";
                public const string SEDOL = "SEDOL";
                public const string ISIN = "ISIN";
                public const string Destination = "Destination";
                public const string SubDestination = "SubDestination";
                public const string BuyOrSell = "BuyOrSell";
                public const string PricingInstructions = "PricingInstructions";
                public const string OrderState = "OrderState";
                public const string LastAction = "LastAction";
                public const string ActionStatus = "ActionStatus";
                public const string OrderVolume = "OrderVolume";
                public const string OrderPrice = "OrderPrice";
                public const string RemainingVolume = "RemainingVolume";
                public const string DoneVolumeTotal = "DoneVolumeTotal";
                public const string DoneValueTotal = "DoneValueTotal";
                public const string UncommittedVolume = "UncommittedVolume";
                public const string AveragePrice = "AveragePrice";
                public const string InternalOrderStatus = "InternalOrderStatus";
                public const string ExternalOrderStatus = "ExternalOrderStatus";
                public const string Lifetime = "Lifetime";
                public const string ExecutionInstructions = "ExecutionInstructions";
                public const string Currency = "Currency";
                public const string PrimaryClientOrderID = "PrimaryClientOrderID";
                public const string SecondaryClientOrderID = "SecondaryClientOrderID";
                public const string ExpiryDateTime = "ExpiryDateTime";
                public const string OrderGroup = "OrderGroup";
                public const string OrderDetails = "OrderDetails";
                public const string DoneVolumeToday = "DoneVolumeToday";
                public const string DoneValueToday = "DoneValueToday";
                public const string StateDescription = "StateDescription";
                public const string CreateDateTime = "CreateDateTime";
                public const string UpdateDateTime = "UpdateDateTime";
                public const string WorkingGroup = "WorkingGroup";
                public const string WorkedByUserCode = "WorkedByUserCode";
                public const string OrderVWAPStartTime = "OrderVWAPStartTime";
                public const string OrderVWAPEndTime = "OrderVWAPEndTime";
                public const string StartMarketVolume = "StartMarketVolume";
                public const string StartMarketValue = "StartMarketValue";
                public const string DestinationVolume = "DestinationVolume";
                public const string DestinationPrice = "DestinationPrice";
                public const string DestinationStatus = "DestinationStatus";
                public const string DestinationOrderNumber = "DestinationOrderNumber";
                public const string DestinationUserID = "DestinationUserID";
                public const string Organisation = "Organisation";
                public const string AverageFXRate = "AverageFXRate";
                public const string Principal = "Principal";
                public const string LastErrorNumber = "LastErrorNumber";
                public const string CustomColumns = "CustomColumns";
                public const string SecurityType = "SecurityType";
                public const string OrderMatchID = "OrderMatchID";
                public const string MarketDataOrderNumber = "MarketDataOrderNumber";
                public const string SideCode = "SideCode";
                public const string OrderGiver = "OrderGiver";
                public const string OrderTaker = "OrderTaker";
                public const string BackOfficeStatus = "BackOfficeStatus";
                public const string BackOfficeStatusDescription = "BackOfficeStatusDescription";
                public const string EffectiveDoneVolume = "EffectiveDoneVolume";
                public const string SideDescription = "SideDescription";
                public const string ExecutionInstructionsDictionary = "ExecutionInstructionsDictionary";
                public const string PriceMultiplier = "PriceMultiplier";
                public const string SettlementDoneValueTotal = "SettlementDoneValueTotal";
                public const string SettlementDoneValueToday = "SettlementDoneValueToday";
                public const string SettlementAveragePrice = "SettlementAveragePrice";
                public const string TrailerCodes = "TrailerCodes";
                public const string LastActionUserCode = "LastActionUserCode";
                public const string ClientSequenceNumber = "ClientSequenceNumber";
                public const string MarketDetail = "MarketDetail";
                public const string PostTradeStatusNumber = "PostTradeStatusNumber";
                public const string OrderLockedUserCode = "OrderLockedUserCode";
                public const string OrderLockedDateTime = "OrderLockedDateTime";
                public const string BackOfficeProvider = "BackOfficeProvider";
                public const string BasketName = "BasketName";
                public const string EstimatedPrice = "EstimatedPrice";
                public const string RootParentOrderCreatorUserCode = "RootParentOrderCreatorUserCode";
                public const string OrderValue = "OrderValue";
                public const string OrderRoutingType = "OrderRoutingType";
                public const string AlgoVolume = "AlgoVolume";
                public const string AlgoPrice = "AlgoPrice";
                public const string AlgoInMarketCount = "AlgoInMarketCount";
                public const string AdvisorCode = "AdvisorCode";
                public const string EstimatedVolume = "EstimatedVolume";
                public const string EstimatedValue = "EstimatedValue";
                public const string ExtraOrderDetails = "ExtraOrderDetails";
                public const string WarehouseVolume = "WarehouseVolume";
                public const string SecurityDescription = "SecurityDescription";
                public const string StartVolume = "StartVolume";
                public const string OrderParkedDateTime = "OrderParkedDateTime";
                public const string OrderParkedPreviousElapsedTime = "OrderParkedPreviousElapsedTime";
                public const string OrderLockedPreviousElapsedTime = "OrderLockedPreviousElapsedTime";
                public const string UncommittedValue = "UncommittedValue";
            }

            public class IosPlusOrderSearchModel
            {
                [Name(OrderSearchCsvHeaders.RootParentOrderNumber)]
                public long RootParentOrderNumber { get; set; }

                [Name(OrderSearchCsvHeaders.OrderNumber)]
                public long OrderNumber { get; set; }

                [Name(OrderSearchCsvHeaders.ParentOrderNumber)]
                public long ParentOrderNumber { get; set; }

                [Name(OrderSearchCsvHeaders.AccountCode)]
                public string AccountCode { get; set; }

                [Name(OrderSearchCsvHeaders.SecurityCode)]
                public string SecurityCode { get; set; }

                [Name(OrderSearchCsvHeaders.Exchange)]
                public string Exchange { get; set; }

                [Name(OrderSearchCsvHeaders.SEDOL)]
                public string SEDOL { get; set; }

                [Name(OrderSearchCsvHeaders.ISIN)]
                public string ISIN { get; set; }

                [Name(OrderSearchCsvHeaders.Destination)]
                public string Destination { get; set; }

                [Name(OrderSearchCsvHeaders.SubDestination)]
                public string SubDestination { get; set; }

                [Name(OrderSearchCsvHeaders.BuyOrSell)]
                public string BuyOrSell { get; set; }

                [Name(OrderSearchCsvHeaders.PricingInstructions)]
                public string PricingInstructions { get; set; }

                [Name(OrderSearchCsvHeaders.OrderState)]
                public string OrderState { get; set; }

                [Name(OrderSearchCsvHeaders.LastAction)]
                public string LastAction { get; set; }

                [Name(OrderSearchCsvHeaders.ActionStatus)]
                public string ActionStatus { get; set; }

                [Name(OrderSearchCsvHeaders.OrderVolume)]
                public double OrderVolume { get; set; }

                [Name(OrderSearchCsvHeaders.OrderPrice)]
                public double OrderPrice { get; set; }

                [Name(OrderSearchCsvHeaders.RemainingVolume)]
                public double RemainingVolume { get; set; }

                [Name(OrderSearchCsvHeaders.DoneVolumeTotal)]
                public double DoneVolumeTotal { get; set; }

                [Name(OrderSearchCsvHeaders.DoneValueTotal)]
                public double DoneValueTotal { get; set; }

                [Name(OrderSearchCsvHeaders.UncommittedVolume)]
                public double UncommittedVolume { get; set; }

                [Name(OrderSearchCsvHeaders.AveragePrice)]
                public double AveragePrice { get; set; }

                [Name(OrderSearchCsvHeaders.InternalOrderStatus)]
                public string InternalOrderStatus { get; set; }

                [Name(OrderSearchCsvHeaders.ExternalOrderStatus)]
                public string ExternalOrderStatus { get; set; }

                [Name(OrderSearchCsvHeaders.Lifetime)]
                public string Lifetime { get; set; }

                [Name(OrderSearchCsvHeaders.ExecutionInstructions)]
                public string ExecutionInstructions { get; set; }

                [Name(OrderSearchCsvHeaders.Currency)]
                public string Currency { get; set; }

                [Name(OrderSearchCsvHeaders.PrimaryClientOrderID)]
                public string PrimaryClientOrderID { get; set; }

                [Name(OrderSearchCsvHeaders.SecondaryClientOrderID)]
                public string SecondaryClientOrderID { get; set; }

                [Name(OrderSearchCsvHeaders.ExpiryDateTime)]
                public DateTime? ExpiryDateTime { get; set; }

                [Name(OrderSearchCsvHeaders.OrderGroup)]
                public string OrderGroup { get; set; }

                [Name(OrderSearchCsvHeaders.OrderDetails)]
                public string OrderDetails { get; set; }

                [Name(OrderSearchCsvHeaders.DoneVolumeToday)]
                public double DoneVolumeToday { get; set; }

                [Name(OrderSearchCsvHeaders.DoneValueToday)]
                public double DoneValueToday { get; set; }

                [Name(OrderSearchCsvHeaders.StateDescription)]
                public string StateDescription { get; set; }

                [Name(OrderSearchCsvHeaders.CreateDateTime)]
                public DateTime? CreateDateTime { get; set; }

                [Name(OrderSearchCsvHeaders.UpdateDateTime)]
                public DateTime? UpdateDateTime { get; set; }

                [Name(OrderSearchCsvHeaders.WorkingGroup)]
                public string WorkingGroup { get; set; }

                [Name(OrderSearchCsvHeaders.WorkedByUserCode)]
                public string WorkedByUserCode { get; set; }

                [Name(OrderSearchCsvHeaders.OrderVWAPStartTime)]
                public DateTime? OrderVWAPStartTime { get; set; }

                [Name(OrderSearchCsvHeaders.OrderVWAPEndTime)]
                public DateTime? OrderVWAPEndTime { get; set; }

                [Name(OrderSearchCsvHeaders.StartMarketVolume)]
                public double StartMarketVolume { get; set; }

                [Name(OrderSearchCsvHeaders.StartMarketValue)]
                public double StartMarketValue { get; set; }

                [Name(OrderSearchCsvHeaders.DestinationVolume)]
                public double DestinationVolume { get; set; }

                [Name(OrderSearchCsvHeaders.DestinationPrice)]
                public double DestinationPrice { get; set; }

                [Name(OrderSearchCsvHeaders.DestinationStatus)]
                public string DestinationStatus { get; set; }

                [Name(OrderSearchCsvHeaders.DestinationOrderNumber)]
                public string DestinationOrderNumber { get; set; }

                [Name(OrderSearchCsvHeaders.DestinationUserID)]
                public string DestinationUserID { get; set; }

                [Name(OrderSearchCsvHeaders.Organisation)]
                public string Organisation { get; set; }

                [Name(OrderSearchCsvHeaders.AverageFXRate)]
                public double AverageFXRate { get; set; }
                
                [Name(OrderSearchCsvHeaders.Principal)]
                public bool Principal { get; set; }

                [Name(OrderSearchCsvHeaders.LastErrorNumber)]
                public int LastErrorNumber { get; set; }
                
                [Name(OrderSearchCsvHeaders.CustomColumns)]
                public string CustomColumns { get; set; }

                [Name(OrderSearchCsvHeaders.SecurityType)]
                public int SecurityType { get; set; }

                [Name(OrderSearchCsvHeaders.OrderMatchID)]
                public string OrderMatchID { get; set; }

                [Name(OrderSearchCsvHeaders.MarketDataOrderNumber)]
                public long MarketDataOrderNumber { get; set; }

                [Name(OrderSearchCsvHeaders.SideCode)]
                public string SideCode { get; set; }

                [Name(OrderSearchCsvHeaders.OrderGiver)]
                public string OrderGiver { get; set; }

                [Name(OrderSearchCsvHeaders.OrderTaker)]
                public string OrderTaker { get; set; }

                [Name(OrderSearchCsvHeaders.BackOfficeStatus)]
                public string BackOfficeStatus { get; set; }

                [Name(OrderSearchCsvHeaders.BackOfficeStatusDescription)]
                public string BackOfficeStatusDescription { get; set; }

                [Name(OrderSearchCsvHeaders.EffectiveDoneVolume)]
                public double EffectiveDoneVolume { get; set; }

                [Name(OrderSearchCsvHeaders.SideDescription)]
                public string SideDescription { get; set; }

                [Name(OrderSearchCsvHeaders.PriceMultiplier)]
                public double PriceMultiplier { get; set; }

                [Name(OrderSearchCsvHeaders.SettlementDoneValueTotal)]
                public double SettlementDoneValueTotal { get; set; }

                [Name(OrderSearchCsvHeaders.SettlementDoneValueToday)]
                public double SettlementDoneValueToday { get; set; }

                [Name(OrderSearchCsvHeaders.SettlementAveragePrice)]
                public double SettlementAveragePrice { get; set; }

                [Name(OrderSearchCsvHeaders.TrailerCodes)]
                public string TrailerCodes { get; set; }

                [Name(OrderSearchCsvHeaders.LastActionUserCode)]
                public string LastActionUserCode { get; set; }

                [Name(OrderSearchCsvHeaders.ClientSequenceNumber)]
                public long ClientSequenceNumber { get; set; }

                [Name(OrderSearchCsvHeaders.MarketDetail)]
                public string MarketDetail { get; set; }

                [Name(OrderSearchCsvHeaders.PostTradeStatusNumber)]
                public short PostTradeStatusNumber { get; set; }

                [Name(OrderSearchCsvHeaders.OrderLockedUserCode)]
                public string OrderLockedUserCode { get; set; }

                [Name(OrderSearchCsvHeaders.OrderLockedDateTime)]
                public DateTime? OrderLockedDateTime { get; set; }

                [Name(OrderSearchCsvHeaders.BackOfficeProvider)]
                public string BackOfficeProvider { get; set; }

                [Name(OrderSearchCsvHeaders.BasketName)]
                public string BasketName { get; set; }

                [Name(OrderSearchCsvHeaders.EstimatedPrice)]
                public double EstimatedPrice { get; set; }

                [Name(OrderSearchCsvHeaders.RootParentOrderCreatorUserCode)]
                public string RootParentOrderCreatorUserCode { get; set; }

                [Name(OrderSearchCsvHeaders.OrderValue)]
                public double OrderValue { get; set; }

                [Name(OrderSearchCsvHeaders.OrderRoutingType)]
                public string OrderRoutingType { get; set; }

                [Name(OrderSearchCsvHeaders.AlgoVolume)]
                public double AlgoVolume { get; set; }

                [Name(OrderSearchCsvHeaders.AlgoPrice)]
                public double AlgoPrice { get; set; }

                [Name(OrderSearchCsvHeaders.AlgoInMarketCount)]
                public int AlgoInMarketCount { get; set; }

                [Name(OrderSearchCsvHeaders.AdvisorCode)]
                public string AdvisorCode { get; set; }

                [Name(OrderSearchCsvHeaders.EstimatedVolume)]
                public double EstimatedVolume { get; set; }

                [Name(OrderSearchCsvHeaders.EstimatedValue)]
                public double EstimatedValue { get; set; }

                [Name(OrderSearchCsvHeaders.ExtraOrderDetails)]
                public string ExtraOrderDetails { get; set; }

                [Name(OrderSearchCsvHeaders.WarehouseVolume)]
                public double WarehouseVolume { get; set; }

                [Name(OrderSearchCsvHeaders.SecurityDescription)]
                public string SecurityDescription { get; set; }

                [Name(OrderSearchCsvHeaders.StartVolume)]
                public double StartVolume { get; set; }

                [Name(OrderSearchCsvHeaders.OrderParkedDateTime)]
                public DateTime? OrderParkedDateTime { get; set; }

                [Name(OrderSearchCsvHeaders.OrderParkedPreviousElapsedTime)]
                public long OrderParkedPreviousElapsedTime { get; set; }

                [Name(OrderSearchCsvHeaders.OrderLockedPreviousElapsedTime)]
                public long OrderLockedPreviousElapsedTime { get; set; }

                [Name(OrderSearchCsvHeaders.UncommittedValue)]
                public double UncommittedValue { get; set; }
            }

            public sealed class IosPlusOrderSearchMap : ClassMap<IosPlusOrderSearchModel>
            {
                //public IosPlusOrderSearchMap()
                //{
                //    Map(m => m.BuyOrSell).Name(OrderSearchCsvHeaders.BuyOrSell);

                //}
                private static string ExtractDateTime(DateTime? dt)
                {
                    if (!dt.HasValue)
                    {
                        return "";
                    }

                    var dateTime = dt.Value.ToString("yyyyMMdd HH:mm:ss.ffffff");
                    if (TimeZoneInfo.Local.IsDaylightSavingTime(dt.Value))
                    {
                        dateTime += " +0100s";
                    }
                    else
                    {
                        dateTime += " +0000";
                    }

                    return dateTime;
                }

                public IosPlusOrderSearchMap()
                {
                    AutoMap(CultureInfo.InvariantCulture);
                    Map(m => m.RootParentOrderNumber).Name(OrderSearchCsvHeaders.RootParentOrderNumber);
                    Map(m => m.OrderNumber).Name(OrderSearchCsvHeaders.OrderNumber);
                    Map(m => m.ParentOrderNumber).Name(OrderSearchCsvHeaders.ParentOrderNumber);
                    Map(m => m.AccountCode).Name(OrderSearchCsvHeaders.AccountCode);
                    Map(m => m.SecurityCode).Name(OrderSearchCsvHeaders.SecurityCode);
                    Map(m => m.Exchange).Name(OrderSearchCsvHeaders.Exchange);
                    Map(m => m.SEDOL).Name(OrderSearchCsvHeaders.SEDOL);
                    Map(m => m.ISIN).Name(OrderSearchCsvHeaders.ISIN);
                    Map(m => m.Destination).Name(OrderSearchCsvHeaders.Destination);
                    Map(m => m.SubDestination).Name(OrderSearchCsvHeaders.SubDestination);
                    Map(m => m.BuyOrSell).Name(OrderSearchCsvHeaders.BuyOrSell);
                    Map(m => m.PricingInstructions).Name(OrderSearchCsvHeaders.PricingInstructions);
                    Map(m => m.OrderState).Name(OrderSearchCsvHeaders.OrderState);
                    Map(m => m.LastAction).Name(OrderSearchCsvHeaders.LastAction);
                    Map(m => m.ActionStatus).Name(OrderSearchCsvHeaders.ActionStatus);
                    Map(m => m.OrderVolume).Name(OrderSearchCsvHeaders.OrderVolume);
                    Map(m => m.OrderPrice).Name(OrderSearchCsvHeaders.OrderPrice);
                    Map(m => m.RemainingVolume).Name(OrderSearchCsvHeaders.RemainingVolume);
                    Map(m => m.DoneVolumeTotal).Name(OrderSearchCsvHeaders.DoneVolumeTotal);
                    Map(m => m.DoneValueTotal).Name(OrderSearchCsvHeaders.DoneValueTotal);
                    Map(m => m.UncommittedVolume).Name(OrderSearchCsvHeaders.UncommittedVolume);
                    Map(m => m.AveragePrice).Name(OrderSearchCsvHeaders.AveragePrice);
                    Map(m => m.InternalOrderStatus).Name(OrderSearchCsvHeaders.InternalOrderStatus);
                    Map(m => m.ExternalOrderStatus).Name(OrderSearchCsvHeaders.ExternalOrderStatus);
                    Map(m => m.Lifetime).Name(OrderSearchCsvHeaders.Lifetime);
                    Map(m => m.ExecutionInstructions).Name(OrderSearchCsvHeaders.ExecutionInstructions);
                    Map(m => m.Currency).Name(OrderSearchCsvHeaders.Currency);
                    Map(m => m.PrimaryClientOrderID).Name(OrderSearchCsvHeaders.PrimaryClientOrderID);
                    Map(m => m.SecondaryClientOrderID).Name(OrderSearchCsvHeaders.SecondaryClientOrderID);
                    Map(m => m.ExpiryDateTime).Name(OrderSearchCsvHeaders.ExpiryDateTime);
                    Map(m => m.OrderGroup).Name(OrderSearchCsvHeaders.OrderGroup);
                    Map(m => m.OrderDetails).Name(OrderSearchCsvHeaders.OrderDetails);
                    Map(m => m.DoneVolumeToday).Name(OrderSearchCsvHeaders.DoneVolumeToday);
                    Map(m => m.DoneValueToday).Name(OrderSearchCsvHeaders.DoneValueToday);
                    Map(m => m.StateDescription).Name(OrderSearchCsvHeaders.StateDescription);
                    Map(m => m.CreateDateTime).Name(OrderSearchCsvHeaders.CreateDateTime);
                    Map(m => m.UpdateDateTime).Name(OrderSearchCsvHeaders.UpdateDateTime);
                    Map(m => m.WorkingGroup).Name(OrderSearchCsvHeaders.WorkingGroup);
                    Map(m => m.WorkedByUserCode).Name(OrderSearchCsvHeaders.WorkedByUserCode);
                    Map(m => m.OrderVWAPStartTime).Name(OrderSearchCsvHeaders.OrderVWAPStartTime);
                    Map(m => m.OrderVWAPEndTime).Name(OrderSearchCsvHeaders.OrderVWAPEndTime);
                    Map(m => m.StartMarketVolume).Name(OrderSearchCsvHeaders.StartMarketVolume);
                    Map(m => m.StartMarketValue).Name(OrderSearchCsvHeaders.StartMarketValue);
                    Map(m => m.DestinationVolume).Name(OrderSearchCsvHeaders.DestinationVolume);
                    Map(m => m.DestinationPrice).Name(OrderSearchCsvHeaders.DestinationPrice);
                    Map(m => m.DestinationStatus).Name(OrderSearchCsvHeaders.DestinationStatus);
                    Map(m => m.DestinationOrderNumber).Name(OrderSearchCsvHeaders.DestinationOrderNumber);
                    Map(m => m.DestinationUserID).Name(OrderSearchCsvHeaders.DestinationUserID);
                    Map(m => m.Organisation).Name(OrderSearchCsvHeaders.Organisation);
                    Map(m => m.AverageFXRate).Name(OrderSearchCsvHeaders.AverageFXRate);
                    Map(m => m.Principal).Name(OrderSearchCsvHeaders.Principal);
                    Map(m => m.LastErrorNumber).Name(OrderSearchCsvHeaders.LastErrorNumber);
                    Map(m => m.CustomColumns).Name(OrderSearchCsvHeaders.CustomColumns);
                    Map(m => m.SecurityType).Name(OrderSearchCsvHeaders.SecurityType);
                    Map(m => m.OrderMatchID).Name(OrderSearchCsvHeaders.OrderMatchID);
                    Map(m => m.MarketDataOrderNumber).Name(OrderSearchCsvHeaders.MarketDataOrderNumber);
                    Map(m => m.SideCode).Name(OrderSearchCsvHeaders.SideCode);
                    Map(m => m.OrderGiver).Name(OrderSearchCsvHeaders.OrderGiver);
                    Map(m => m.OrderTaker).Name(OrderSearchCsvHeaders.OrderTaker);
                    Map(m => m.BackOfficeStatus).Name(OrderSearchCsvHeaders.BackOfficeStatus);
                    Map(m => m.BackOfficeStatusDescription).Name(OrderSearchCsvHeaders.BackOfficeStatusDescription);
                    Map(m => m.EffectiveDoneVolume).Name(OrderSearchCsvHeaders.EffectiveDoneVolume);
                    Map(m => m.SideDescription).Name(OrderSearchCsvHeaders.SideDescription);
                    Map(m => m.PriceMultiplier).Name(OrderSearchCsvHeaders.PriceMultiplier);
                    Map(m => m.SettlementDoneValueTotal).Name(OrderSearchCsvHeaders.SettlementDoneValueTotal);
                    Map(m => m.SettlementDoneValueToday).Name(OrderSearchCsvHeaders.SettlementDoneValueToday);
                    Map(m => m.SettlementAveragePrice).Name(OrderSearchCsvHeaders.SettlementAveragePrice);
                    Map(m => m.TrailerCodes).Name(OrderSearchCsvHeaders.TrailerCodes);
                    Map(m => m.LastActionUserCode).Name(OrderSearchCsvHeaders.LastActionUserCode);
                    Map(m => m.ClientSequenceNumber).Name(OrderSearchCsvHeaders.ClientSequenceNumber);
                    Map(m => m.MarketDetail).Name(OrderSearchCsvHeaders.MarketDetail);
                    Map(m => m.PostTradeStatusNumber).Name(OrderSearchCsvHeaders.PostTradeStatusNumber);
                    Map(m => m.OrderLockedUserCode).Name(OrderSearchCsvHeaders.OrderLockedUserCode);
                    Map(m => m.OrderLockedDateTime).Name(OrderSearchCsvHeaders.OrderLockedDateTime);
                    Map(m => m.BackOfficeProvider).Name(OrderSearchCsvHeaders.BackOfficeProvider);
                    Map(m => m.BasketName).Name(OrderSearchCsvHeaders.BasketName);
                    Map(m => m.EstimatedPrice).Name(OrderSearchCsvHeaders.EstimatedPrice);
                    Map(m => m.RootParentOrderCreatorUserCode).Name(OrderSearchCsvHeaders.RootParentOrderCreatorUserCode);
                    Map(m => m.OrderValue).Name(OrderSearchCsvHeaders.OrderValue);
                    Map(m => m.OrderRoutingType).Name(OrderSearchCsvHeaders.OrderRoutingType);
                    Map(m => m.AlgoVolume).Name(OrderSearchCsvHeaders.AlgoVolume);
                    Map(m => m.AlgoPrice).Name(OrderSearchCsvHeaders.AlgoPrice);
                    Map(m => m.AlgoInMarketCount).Name(OrderSearchCsvHeaders.AlgoInMarketCount);
                    Map(m => m.AdvisorCode).Name(OrderSearchCsvHeaders.AdvisorCode);
                    Map(m => m.EstimatedVolume).Name(OrderSearchCsvHeaders.EstimatedVolume);
                    Map(m => m.EstimatedValue).Name(OrderSearchCsvHeaders.EstimatedValue);
                    Map(m => m.ExtraOrderDetails).Name(OrderSearchCsvHeaders.ExtraOrderDetails);
                    Map(m => m.WarehouseVolume).Name(OrderSearchCsvHeaders.WarehouseVolume);
                    Map(m => m.SecurityDescription).Name(OrderSearchCsvHeaders.SecurityDescription);
                    Map(m => m.StartVolume).Name(OrderSearchCsvHeaders.StartVolume);
                    Map(m => m.OrderParkedDateTime).Name(OrderSearchCsvHeaders.OrderParkedDateTime);
                    Map(m => m.OrderParkedPreviousElapsedTime).Name(OrderSearchCsvHeaders.OrderParkedPreviousElapsedTime);
                    Map(m => m.OrderLockedPreviousElapsedTime).Name(OrderSearchCsvHeaders.OrderLockedPreviousElapsedTime);
                    Map(m => m.UncommittedValue).Name(OrderSearchCsvHeaders.UncommittedValue);
                }
            }

            public async Task LoginIressAsync()
            {
                _logger.LogInformation("Logging in to Iress...");

                var input = new WS.IosPlus.IRESSSessionStartInput
                {
                    Parameters = new WS.IosPlus.IRESSSessionStartInputParameters
                    {
                        UserName = _config.UserName,
                        CompanyName = _config.Company,
                        Password = _config.Password,
                        ApplicationID = "iosplus-download",
                        ApplicationLabel = "IOS+ Download",
                    },
                    Header = new WS.IosPlus.IRESSSessionStartInputHeader
                    {

                    }
                };

                try
                {
                    var output = await _iosClient.IRESSSessionStartAsync(input);
                    _iressSessionKey = output.Result.DataRows[0].IRESSSessionKey;
                }
                catch (Exception e)
                {
                    if (e.Message != null)
                        _logger.LogError("Error while logging in to Iress: " + e.Message);
                    else
                        _logger.LogError("Error while logging in to Iress: " + e.ToString());

                    _iressSessionKey = null;
                }
            }

            public async Task LogoutIressAsync()
            {
                _logger.LogInformation("Logging out of Iress...");

                var input = new WS.IosPlus.IRESSSessionEndInput
                {
                    Parameters = new WS.IosPlus.IRESSSessionEndInputParameters
                    {

                    },
                    Header = new WS.IosPlus.IRESSSessionEndInputHeader
                    {
                        SessionKey = _iressSessionKey,
                    }
                };

                var output = await _iosClient.IRESSSessionEndAsync(input);
                _iressSessionKey = null;
            }

            public async Task LoginIosPlusAsync()
            {
                string requestID = Guid.NewGuid().ToString();

                _iosPlusServerName = _config.IosPlusServer;

                _logger.LogInformation($"Logging in to IOS+ {_iosPlusServerName} using RequestID {requestID}...");

                var input = new WS.IosPlus.ServiceSessionStartInput
                {
                    Parameters = new WS.IosPlus.ServiceSessionStartInputParameters
                    {
                        Server = _iosPlusServerName,
                        Service = "IOSPLUS",
                        IRESSSessionKey = _iressSessionKey,
                    },
                    Header = new WS.IosPlus.ServiceSessionStartInputHeader
                    {
                        RequestID = requestID,
                    }
                };

                try
                {
                    var output = await _iosClient.ServiceSessionStartAsync(input);
                    _iosPlusSessionKey = output.Result.DataRows[0].ServiceSessionKey;
                }
                catch (Exception e)
                {
                    if (e.Message != null)
                        _logger.LogError("Error while logging in to IOS+: " + e.Message);
                    else
                        _logger.LogError("Error while logging in to IOS+: " + e.ToString());

                    _iosPlusSessionKey = null;
                    await LogoutIressAsync();
                }
            }

            public async Task LogoutIosPlusAsync()
            {
                _logger.LogInformation($"Logging out of IOS+ {_iosPlusServerName}...");

                var input = new WS.IosPlus.ServiceSessionEndInput
                {
                    Parameters = new WS.IosPlus.ServiceSessionEndInputParameters
                    {

                    },
                    Header = new WS.IosPlus.ServiceSessionEndInputHeader
                    {
                        ServiceSessionKey = _iosPlusSessionKey,
                    }
                };

                var output = await _iosClient.ServiceSessionEndAsync(input);
                _iosPlusSessionKey = null;
            }

            public async Task<IList<SecurityInformationGetDataRow>> GetSecurityInformationAsync(IEnumerable<string> securityList)
            {
                var securityInformationGetRowList = new List<SecurityInformationGetDataRow>();
                int totalBatchCount  = 0;
                int remainingBatchCount = 0;
                int securityCount = securityList.Count();

                if (securityCount > 0)
                {
                    totalBatchCount = ((securityCount - 1) / SECURITY_INFO_BATCH_SIZE) + 1;
                }

                remainingBatchCount = totalBatchCount;
                
                while (remainingBatchCount > 0)
                {
                    // Work out the batch of securities to request (based on batch size of securities used
                    int currentBatchNumber = totalBatchCount - remainingBatchCount + 1;
                    var securityTextBatch = securityList.Skip((currentBatchNumber - 1) * SECURITY_INFO_BATCH_SIZE).Take(SECURITY_INFO_BATCH_SIZE).ToArray();
                    
                    string requestID = Guid.NewGuid().ToString();
                    _logger.LogInformation($"Retrieving security information (batch {currentBatchNumber}/{totalBatchCount}) using RequestID {requestID}...");
                    
                    var input = new SecurityInformationGetInput
                    {
                        Parameters = new SecurityInformationGetInputParameters
                        {
                            SecurityTextArray = securityTextBatch.ToArray()
                        },
                        Header = new SecurityInformationGetInputHeader
                        {
                            SessionKey = _iressSessionKey,
                            Updates = false,
                            RequestID = requestID,
                            Timeout = REQUEST_TIMEOUT_SECURITY_INFO
                        }
                    };

                    SecurityInformationGetOutput output;

                    do
                    {
                        output = await _iressClient.SecurityInformationGetAsync(input);
                        securityInformationGetRowList.AddRange(output.Result.DataRows);
                    } while (output.Result.Header.StatusCode == 1);

                    remainingBatchCount--;
                }

                return securityInformationGetRowList;
            }

            public async Task<IList<TradeGetByUserDataRow>> GetTradesAsync(DateTime dateToGetTrades)
            {
                string requestID = Guid.NewGuid().ToString();
                _logger.LogInformation($"Retrieving trades from IOS+ {_iosPlusServerName} for {dateToGetTrades.ToString()} using RequestID {requestID}...");

                var tradesRowList = new List<TradeGetByUserDataRow>();
                var fromDate = new DateTime(dateToGetTrades.Year, dateToGetTrades.Month, dateToGetTrades.Day, 0, 0, 0);
                var toDate = new DateTime(dateToGetTrades.Year, dateToGetTrades.Month, dateToGetTrades.Day, 23, 59, 59);
                var input = new TradeGetByUserInput
                {
                    Parameters = new TradeGetByUserInputParameters
                    {
                        TradeDateTimeFrom = fromDate,
                        TradeDateTimeTo = toDate,
                    },
                    Header = new TradeGetByUserInputHeader
                    {
                        ServiceSessionKey = _iosPlusSessionKey,
                        Updates = false,
                        RequestID = requestID,
                        Timeout = REQUEST_TIMEOUT_TRADES
                    }
                };

                
                TradeGetByUserOutput output;

                do
                {
                    output = await _iosClient.TradeGetByUserAsync(input);
                    tradesRowList.AddRange(output.Result.DataRows);
                } while (output.Result.Header.StatusCode == 1);

                return tradesRowList;
            }

            public static IosPlusTradeModel PopulateTradeRecord(TradeGetByUserDataRow tradeRow, ReferenceDataCache cache)
            {
                var securityExchangeKey = $"{tradeRow.SecurityCode}.{tradeRow.Exchange}";
                ReferenceData referenceData;
                var result = cache.ReferenceDataLookup.TryGetValue(securityExchangeKey, out referenceData);
                if (!result)
                    referenceData = new ReferenceData(null, null);

                return new IosPlusTradeModel
                {
                    TradeNumber = tradeRow.TradeNumber,
                    OrderNumber = tradeRow.OrderNumber,
                    AccountCode = tradeRow.AccountCode,
                    SecurityCode = tradeRow.SecurityCode,
                    Exchange = tradeRow.Exchange,
                    SEDOL = referenceData.SEDOL ?? "", // Populate SEDOL from reference data. Populate with empty string if not available
                    ISIN= referenceData.ISIN ?? "", // Populate ISIN from reference data. Populate with empty string if not available
                    Destination = tradeRow.Destination,
                    SubDestination = tradeRow.SubDestination,
                    BuyOrSell = tradeRow.SideCode == "1" ? "B" : "S",
                    TradeVolume = tradeRow.TradeVolume ?? 0.00,
                    TradePrice = tradeRow.TradePrice ?? 0.00,
                    TradeValue = tradeRow.TradeValue ?? 0.00,
                    TradeFXRate = tradeRow.TradeFXRate ?? 0.00,
                    TradeDateTime = tradeRow.TradeDateTime,
                    Principal = tradeRow.Principal ?? false, // ToDo: Should we just support null/empty?
                    OpposingBrokerNumber = tradeRow.OpposingBrokerNumber ?? 0,
                    PrimaryClientOrderID = tradeRow.PrimaryClientOrderID,
                    SecondaryClientOrderID = tradeRow.SecondaryClientOrderID,
                    TradeMarkers = tradeRow.TradeMarkers,
                    DestinationUserID = tradeRow.DestinationUserID,
                    DestinationOrderNumber = tradeRow.DestinationOrderNumber,
                    DestinationTradeNumber = tradeRow.DestinationTradeNumber,
                    CancelledByTradeNumber = tradeRow.CancelledByTradeNumber ?? 0,
                    MarketDataOrderNumber = tradeRow.MarketDataOrderNumber ?? 0,
                    MarketDataTradeNumber = tradeRow.MarketDataTradeNumber ?? 0,
                    FXRateBidPriceOnOrder = tradeRow.FXRateBidPriceOnOrder ?? 0.00,
                    FXRateAskPriceOnOrder = tradeRow.FXRateAskPriceOnOrder ?? 0.00,
                    FXRateBidPriceOnTrade = tradeRow.FXRateBidPriceOnTrade ?? 0.00,
                    FXRateAskPriceOnTrade = tradeRow.FXRateAskPriceOnTrade ?? 0.00,
                    SourcePrice = tradeRow.SourcePrice ?? 0.00,
                    SourceCurrency = tradeRow.SourceCurrency,
                    SideCode = tradeRow.SideCode,
                    OrderDetails = tradeRow.OrderDetails,
                    PriceMultiplier = tradeRow.PriceMultiplier ?? 1.0, // ToDo: IS 1 ok as default here?
                    SettlementValue = tradeRow.SettlementValue ?? 0.00,
                    SettlementFXRate = tradeRow.SettlementFXRate ?? 0.00,
                    SettlementPrice = tradeRow.SettlementPrice ?? 0.00,
                    Organization = tradeRow.Organization,
                    BookingDestination = tradeRow.BookingDestination,
                    TradeMarketDetail = tradeRow.TradeMarketDetail,
                    PostTradeStatusNumber = tradeRow.PostTradeStatusNumber ?? 0,
                    TradeSequenceNumber = tradeRow.TradeSequenceNumber ?? 0,
                    TradeDateTimeGMT = tradeRow.TradeDateTimeGMT,
                    ExchangeTradeDateTime = tradeRow.ExchangeTradeDateTime,
                    LocalMarketTradeDate = tradeRow.LocalMarketTradeDate,
                };
            }

            public async Task<IList<AuditTrailGetByUserDataRow>> GetAuditTrailAsync(DateTime dateToGetAuditTrail)
            {
                string requestID = Guid.NewGuid().ToString();
                _logger.LogInformation($"Retrieving audit trail from IOS+ {_iosPlusServerName} for {dateToGetAuditTrail.ToString()} using RequestID {requestID}...");

                var fromDate = new DateTime(dateToGetAuditTrail.Year, dateToGetAuditTrail.Month, dateToGetAuditTrail.Day, 0, 0, 0);
                var toDate = new DateTime(dateToGetAuditTrail.Year, dateToGetAuditTrail.Month, dateToGetAuditTrail.Day, 23, 59, 59);
                var input = new AuditTrailGetByUserInput
                {
                    Parameters = new AuditTrailGetByUserInputParameters
                    {
                        AuditLogDateTimeFrom = fromDate,
                        AuditLogDateTimeTo = toDate,
                    },
                    Header = new AuditTrailGetByUserInputHeader
                    {
                        ServiceSessionKey = _iosPlusSessionKey,
                        Updates = false,
                        RequestID = requestID,
                        Timeout = REQUEST_TIMEOUT_AUDIT_TRAIL
                    }
                };

                var auditTrailRowList = new List<AuditTrailGetByUserDataRow>();
                AuditTrailGetByUserOutput output;

                do
                {
                    output = await _iosClient.AuditTrailGetByUserAsync(input);
                    auditTrailRowList.AddRange(output.Result.DataRows);
                } while (output.Result.Header.StatusCode == 1);

                return auditTrailRowList;
            }

            public async Task<IList<OrderSearchGetByUserDataRow>> GetOrderSearchAsync(DateTime dateToGetOrderSearch)
            {
                string requestID = Guid.NewGuid().ToString();
                _logger.LogInformation($"Retrieving audit trail from IOS+ {_iosPlusServerName} for {dateToGetOrderSearch.ToString()} using RequestID {requestID}...");

                var fromDate = new DateTime(dateToGetOrderSearch.Year, dateToGetOrderSearch.Month, dateToGetOrderSearch.Day, 0, 0, 0);
                var toDate = new DateTime(dateToGetOrderSearch.Year, dateToGetOrderSearch.Month, dateToGetOrderSearch.Day, 23, 59, 59);
                var input = new OrderSearchGetByUserInput
                {
                    Parameters = new OrderSearchGetByUserInputParameters
                    {
                        DateTimeFrom = fromDate,
                        DateTimeTo = toDate,
                    },
                    Header = new OrderSearchGetByUserInputHeader
                    {
                        ServiceSessionKey = _iosPlusSessionKey,
                        Updates = false,
                        RequestID = requestID,
                        Timeout = REQUEST_TIMEOUT_ORDER_SEARCH
                    }
                };

                var orderSearchRowList = new List<OrderSearchGetByUserDataRow>();
                OrderSearchGetByUserOutput output;

                do
                {
                    output = await _iosClient.OrderSearchGetByUserAsync(input);
                    orderSearchRowList.AddRange(output.Result.DataRows);
                } while (output.Result.Header.StatusCode == 1);

                return orderSearchRowList;
            }

            public void WriteReport(string filename, IEnumerable<IosPlusTradeModel> tradeRowList)
            {
                _logger.LogInformation($"Writing trades to file {filename}...");

                var csvWriterConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    Delimiter = FIELD_DELIMITER,
                    ShouldQuote = args => args.Field.Contains(FIELD_DELIMITER)
                };

                using (var writer = new StreamWriter(filename))
                using (var csv = new CsvWriter(writer, csvWriterConfig))
                {
                    csv.Context.RegisterClassMap<IosPlusTradeMap>();
                    csv.WriteHeader<IosPlusTradeModel>();
                    csv.NextRecord();
                    csv.WriteRecords(tradeRowList);
                }
            }

            public void WriteReport(string filename, IEnumerable<IosPlusAuditTrailModel> auditTrailRowList)
            {
                _logger.LogInformation($"Writing audit trail to file {filename}...");

                var csvWriterConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    Delimiter = FIELD_DELIMITER,
                    ShouldQuote = args => args.Field.Contains(FIELD_DELIMITER)
                };

                using (var writer = new StreamWriter(filename))
                using (var csv = new CsvWriter(writer, csvWriterConfig))
                {
                    csv.Context.RegisterClassMap<IosPlusAuditTrailMap>();
                    csv.WriteHeader<IosPlusAuditTrailModel>();
                    csv.NextRecord();
                    csv.WriteRecords(auditTrailRowList);
                }
            }

            public void WriteReport(string filename, IEnumerable<IosPlusOrderSearchModel> orderSearchRowList)
            {
                _logger.LogInformation($"Writing order search to file {filename}...");

                var csvWriterConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    Delimiter = FIELD_DELIMITER,
                    ShouldQuote = args => args.Field.Contains(FIELD_DELIMITER)
                };

                using (var writer = new StreamWriter(filename))
                using (var csv = new CsvWriter(writer, csvWriterConfig))
                {
                    csv.Context.RegisterClassMap<IosPlusOrderSearchMap>();
                    csv.WriteHeader<IosPlusOrderSearchModel>();
                    csv.NextRecord();
                    csv.WriteRecords(orderSearchRowList);
                }
            }

            public static IosPlusAuditTrailModel PopulateAuditTrailRecord(AuditTrailGetByUserDataRow auditTrailRow, ReferenceDataCache cache)
            {
                var securityExchangeKey = $"{auditTrailRow.SecurityCode}.{auditTrailRow.Exchange}";
                ReferenceData referenceData;
                var result = cache.ReferenceDataLookup.TryGetValue(securityExchangeKey, out referenceData);
                if (!result)
                    referenceData = new ReferenceData(null, null);

                return new IosPlusAuditTrailModel
                {
                    AuditTrailNumber = auditTrailRow.AuditTrailNumber,
                    OrderNumber = auditTrailRow.OrderNumber,
                    ParentOrderNumber = auditTrailRow.ParentOrderNumber ?? 0,
                    AuditLogDateTime = auditTrailRow.AuditLogDateTime,
                    ExchangeDateTime = auditTrailRow.ExchangeDateTime,
                    AccountCode = auditTrailRow.AccountCode,
                    SecurityCode = auditTrailRow.SecurityCode,
                    Exchange = auditTrailRow.Exchange,
                    SEDOL = referenceData.SEDOL ?? "", // Populate SEDOL from reference data. Populate with empty string if not available
                    ISIN = referenceData.ISIN ?? "", // Populate ISIN from reference data. Populate with empty string if not available
                    Destination = auditTrailRow.Destination,
                    SubDestination = auditTrailRow.SubDestination,
                    BuyOrSell = auditTrailRow.SideCode == "1" ? "B" : "S",
                    PricingInstructions = auditTrailRow.PricingInstructions,
                    OrderState = auditTrailRow.OrderState,
                    LastAction = auditTrailRow.LastAction,
                    ActionStatus = auditTrailRow.ActionStatus,
                    OrderVolume = auditTrailRow.OrderVolume ?? 0.00,
                    OrderPrice = auditTrailRow.OrderPrice ?? 0.00,
                    EventSummary = auditTrailRow.EventSummary,
                    EventDescription = auditTrailRow.EventDescription,
                    DoneVolumeTotal = auditTrailRow.DoneVolumeTotal ?? 0.00,
                    DoneValueTotal = auditTrailRow.DoneValueTotal ?? 0.00,
                    Lifetime = auditTrailRow.Lifetime,
                    ExecutionInstructions = auditTrailRow.ExecutionInstructions,
                    PrimaryClientOrderID = auditTrailRow.PrimaryClientOrderID,
                    SecondaryClientOrderID = auditTrailRow.SecondaryClientOrderID,
                    OrderGroup = auditTrailRow.OrderGroup,
                    OrderDetails = auditTrailRow.OrderDetails,
                    ExpiryDateTime = auditTrailRow.ExpiryDateTime,
                    Currency = auditTrailRow.Currency,
                    InternalOrderStatus = auditTrailRow.InternalOrderStatus,
                    ExternalOrderStatus = auditTrailRow.ExternalOrderStatus,
                    PCName = auditTrailRow.PCName,
                    EventUserCode = auditTrailRow.EventUserCode,
                    DestinationVolume = auditTrailRow.DestinationVolume ?? 0.00,
                    DestinationPrice = auditTrailRow.DestinationPrice ?? 0.00,
                    DestinationOrderNumber = auditTrailRow.DestinationOrderNumber,
                    DestinationUserID = auditTrailRow.DestinationUserID,
                    LastErrorNumber = auditTrailRow.LastErrorNumber ?? 0, 
                    BidPrice = auditTrailRow.BidPrice ?? 0.00,
                    AskPrice = auditTrailRow.AskPrice ?? 0.00,
                    BidVolume = auditTrailRow.BidVolume ?? 0,
                    AskVolume = auditTrailRow.AskVolume ?? 0,
                    CustomColumns = auditTrailRow.CustomColumns,
                    OrderMatchID = auditTrailRow.OrderMatchID,
                    RemainingVolume = auditTrailRow.RemainingVolume ?? 0.00,
                    LastActionUserCode = auditTrailRow.LastActionUserCode,
                    WorkedByUserCode = auditTrailRow.WorkedByUserCode,
                    UpdateReasonMask = auditTrailRow.UpdateReasonMask ?? 0,
                    OrderFlagsMask = auditTrailRow.OrderFlagsMask ?? 0,
                    MarketDataOrderNumber = auditTrailRow.MarketDataOrderNumber ?? 0,
                    SideCode = auditTrailRow.SideCode,
                    OrderGiver = auditTrailRow.OrderGiver,
                    OrderTaker = auditTrailRow.OrderTaker,
                    BackOfficeStatus = auditTrailRow.BackOfficeStatus,
                    BackOfficeStatusDescription = auditTrailRow.BackOfficeStatusDescription,
                    SettlementDoneValueTotal = auditTrailRow.SettlementDoneValueTotal ?? 0.00,
                    SettlementDoneValueToday = auditTrailRow.SettlementDoneValueToday ?? 0.00,
                    TrailerCodeOnMask = auditTrailRow.TrailerCodeOnMask ?? 0,
                    TrailerCodes = auditTrailRow.TrailerCodes,
                    //StructuredEventDetails = auditTrailRow.StructuredEventDetails,
                    MarketDetail = auditTrailRow.MarketDetail,
                    PostTradeStatusNumber = auditTrailRow.PostTradeStatusNumber ?? 0,
                    OrderLocked = auditTrailRow.OrderLocked ?? false,
                    OrderLockedUserCode = auditTrailRow.OrderLockedUserCode,
                    AccountDesignation = auditTrailRow.AccountDesignation,
                    OrderValue = auditTrailRow.OrderValue ?? 0.00,
                    OrderRoutingType = auditTrailRow.OrderRoutingType,
                    AlgoVolume = auditTrailRow.AlgoVolume ?? 0.00,
                    AlgoPrice = auditTrailRow.AlgoPrice ?? 0.00,
                    AlgoInMarketCount = auditTrailRow.AlgoInMarketCount ?? 0,
                    UpdateReasonMask2 = auditTrailRow.UpdateReasonMask2 ?? 0,
                    AdvisorCode = auditTrailRow.AdvisorCode,
                    EstimatedVolume = auditTrailRow.EstimatedVolume ?? 0.00,
                    EstimatedPrice = auditTrailRow.EstimatedPrice ?? 0.00,
                    EstimatedValue = auditTrailRow.EstimatedValue ?? 0.00,
                    //ExtraOrderDetails = auditTrailRow.ExtraOrderDetails,
                    WarehouseVolume = auditTrailRow.WarehouseVolume ?? 0.00,
                    SecurityDescription = auditTrailRow.SecurityDescription,
                    OrderParked = auditTrailRow.OrderParked ?? false,
                };
            }

            public static IosPlusOrderSearchModel PopulateOrderSearchRecord(OrderSearchGetByUserDataRow orderSearchRow, ReferenceDataCache cache)
            {
                var securityExchangeKey = $"{orderSearchRow.SecurityCode}.{orderSearchRow.Exchange}";
                ReferenceData referenceData;
                var result = cache.ReferenceDataLookup.TryGetValue(securityExchangeKey, out referenceData);
                if (!result)
                    referenceData = new ReferenceData(null, null);

                return new IosPlusOrderSearchModel
                {
                    RootParentOrderNumber = orderSearchRow.RootParentOrderNumber ?? 0,
                    OrderNumber = orderSearchRow.OrderNumber,
                    ParentOrderNumber = orderSearchRow.ParentOrderNumber ?? 0,
                    AccountCode = orderSearchRow.AccountCode,
                    SecurityCode = orderSearchRow.SecurityCode,
                    Exchange = orderSearchRow.Exchange,
                    SEDOL = referenceData.SEDOL ?? "", // Populate SEDOL from reference data. Populate with empty string if not available
                    ISIN = referenceData.ISIN ?? "", // Populate ISIN from reference data. Populate with empty string if not available
                    Destination = orderSearchRow.Destination,
                    SubDestination = orderSearchRow.SubDestination,
                    BuyOrSell = orderSearchRow.SideCode == "1" ? "B" : "S",
                    PricingInstructions = orderSearchRow.PricingInstructions,
                    OrderState = orderSearchRow.OrderState,
                    LastAction = orderSearchRow.LastAction,
                    ActionStatus = orderSearchRow.ActionStatus,
                    OrderVolume = orderSearchRow.OrderVolume ?? 0.00,
                    OrderPrice = orderSearchRow.OrderPrice ?? 0.00,
                    RemainingVolume = orderSearchRow.RemainingVolume ?? 0.00,
                    DoneVolumeTotal = orderSearchRow.DoneVolumeTotal ?? 0.00,
                    DoneValueTotal = orderSearchRow.DoneValueTotal ?? 0.00,
                    UncommittedVolume = orderSearchRow.UncommittedVolume ?? 0.00,
                    AveragePrice = orderSearchRow.AveragePrice ?? 0.00,
                    InternalOrderStatus = orderSearchRow.InternalOrderStatus,
                    ExternalOrderStatus = orderSearchRow.ExternalOrderStatus,
                    Lifetime = orderSearchRow.Lifetime,
                    ExecutionInstructions = orderSearchRow.ExecutionInstructions,
                    Currency = orderSearchRow.Currency,
                    PrimaryClientOrderID = orderSearchRow.PrimaryClientOrderID,
                    SecondaryClientOrderID = orderSearchRow.SecondaryClientOrderID,
                    ExpiryDateTime = orderSearchRow.ExpiryDateTime,
                    OrderGroup = orderSearchRow.OrderGroup,
                    OrderDetails = orderSearchRow.OrderDetails,
                    DoneVolumeToday = orderSearchRow.DoneVolumeToday ?? 0.00,
                    DoneValueToday = orderSearchRow.DoneValueToday ?? 0.00,
                    StateDescription = orderSearchRow.StateDescription,
                    CreateDateTime = orderSearchRow.CreateDateTime,
                    UpdateDateTime = orderSearchRow.UpdateDateTime,
                    WorkingGroup = orderSearchRow.WorkingGroup,
                    WorkedByUserCode = orderSearchRow.WorkedByUserCode,
                    OrderVWAPStartTime = orderSearchRow.OrderVWAPStartTime,
                    OrderVWAPEndTime = orderSearchRow.OrderVWAPEndTime,
                    StartMarketVolume = orderSearchRow.StartMarketVolume ?? 0.00,
                    StartMarketValue = orderSearchRow.StartMarketValue ?? 0.00,
                    DestinationVolume = orderSearchRow.DestinationVolume ?? 0.00,
                    DestinationPrice = orderSearchRow.DestinationPrice ?? 0.00,
                    DestinationStatus = orderSearchRow.DestinationStatus,
                    DestinationOrderNumber = orderSearchRow.DestinationOrderNumber,
                    DestinationUserID = orderSearchRow.DestinationUserID,
                    Organisation = orderSearchRow.Organisation,
                    AverageFXRate = orderSearchRow.AverageFXRate ?? 0.00,
                    Principal = orderSearchRow.Principal ?? false,
                    LastErrorNumber = orderSearchRow.LastErrorNumber ?? 0,
                    CustomColumns = orderSearchRow.CustomColumns,
                    SecurityType = orderSearchRow.SecurityType ?? 0,
                    OrderMatchID = orderSearchRow.OrderMatchID,
                    MarketDataOrderNumber = orderSearchRow.MarketDataOrderNumber ?? 0,
                    SideCode = orderSearchRow.SideCode,
                    OrderGiver = orderSearchRow.OrderGiver,
                    OrderTaker = orderSearchRow.OrderTaker,
                    BackOfficeStatus = orderSearchRow.BackOfficeStatus,
                    BackOfficeStatusDescription = orderSearchRow.BackOfficeStatusDescription,
                    EffectiveDoneVolume = orderSearchRow.EffectiveDoneVolume ?? 0.00,
                    SideDescription = orderSearchRow.SideDescription,
                    //ExecutionInstructionsDictionary = orderSearchRow.ExecutionInstructionsDictionary,
                    PriceMultiplier = orderSearchRow.PriceMultiplier ?? 1.0,
                    SettlementDoneValueTotal = orderSearchRow.SettlementDoneValueTotal ?? 0.00,
                    SettlementDoneValueToday = orderSearchRow.SettlementDoneValueToday ?? 0.00,
                    SettlementAveragePrice = orderSearchRow.SettlementAveragePrice ?? 0.00,
                    TrailerCodes = orderSearchRow.TrailerCodes,
                    LastActionUserCode = orderSearchRow.LastActionUserCode,
                    ClientSequenceNumber = orderSearchRow.ClientSequenceNumber ?? 0,
                    MarketDetail = orderSearchRow.MarketDetail,
                    PostTradeStatusNumber = orderSearchRow.PostTradeStatusNumber ?? 0,
                    OrderLockedUserCode = orderSearchRow.OrderLockedUserCode,
                    OrderLockedDateTime = orderSearchRow.OrderLockedDateTime,
                    BackOfficeProvider = orderSearchRow.BackOfficeProvider,
                    BasketName = orderSearchRow.BasketName,
                    EstimatedPrice = orderSearchRow.EstimatedPrice ?? 0.00,
                    RootParentOrderCreatorUserCode = orderSearchRow.RootParentOrderCreatorUserCode,
                    OrderValue = orderSearchRow.OrderValue ?? 0.00,
                    OrderRoutingType = orderSearchRow.OrderRoutingType,
                    AlgoVolume = orderSearchRow.AlgoVolume ?? 0.00,
                    AlgoPrice = orderSearchRow.AlgoPrice ?? 0.00,
                    AlgoInMarketCount = orderSearchRow.AlgoInMarketCount ?? 0,
                    AdvisorCode = orderSearchRow.AdvisorCode,
                    EstimatedVolume = orderSearchRow.EstimatedVolume ?? 0.00,
                    EstimatedValue = orderSearchRow.EstimatedValue ?? 0.00,
                    //ExtraOrderDetails = orderSearchRow.ExtraOrderDetails,
                    WarehouseVolume = orderSearchRow.WarehouseVolume ?? 0.00,
                    SecurityDescription = orderSearchRow.SecurityDescription,
                    StartVolume = orderSearchRow.StartVolume ?? 0.00,
                    OrderParkedDateTime = orderSearchRow.OrderParkedDateTime,
                    OrderParkedPreviousElapsedTime = orderSearchRow.OrderParkedPreviousElapsedTime ?? 0,
                    OrderLockedPreviousElapsedTime = orderSearchRow.OrderLockedPreviousElapsedTime ?? 0,
                    UncommittedValue = orderSearchRow.UncommittedValue ?? 0.00,
                };
            }
            public IosPlusTradeModel TradeToRecord(TradeGetByUserDataRow tradeRow)
            {
                var tradeTemp = JsonConvert.SerializeObject(tradeRow);
                return PopulateTradeRecord(tradeRow, _referenceDataCache);
            }

            public IosPlusAuditTrailModel AuditTrailToRecord(AuditTrailGetByUserDataRow auditTrailRow)
            {
                var auditTrailTemp = JsonConvert.SerializeObject(auditTrailRow);
                return PopulateAuditTrailRecord(auditTrailRow, _referenceDataCache);
            }
            public IosPlusOrderSearchModel OrderSearchToRecord(OrderSearchGetByUserDataRow orderSearchRow)
            {
                var auditTrailTemp = JsonConvert.SerializeObject(orderSearchRow);
                return PopulateOrderSearchRecord(orderSearchRow, _referenceDataCache);
            }

            public async Task GenerateReport()
            {
                var binding = WebServicesClient.CreateHttpBinding();
                var remoteAddress = new EndpointAddress(_config.SoapEndpoint);

                _iressClient = new WS.Iress.IRESSSoapClient(binding, remoteAddress);
                WebServicesClient.Configure(_iressClient.InnerChannel);
                WebServicesClient.Configure(_iressClient.Endpoint);

                _iosClient = new IOSPLUSSoapClient(binding, remoteAddress);
                WebServicesClient.Configure(_iosClient.InnerChannel);
                WebServicesClient.Configure(_iosClient.Endpoint);

                await LoginIressAsync();
                if (_iressSessionKey == null)
                    return;

                await LoginIosPlusAsync();
                if (_iosPlusSessionKey == null)
                    return;

                var runDate = _config.ReportDate != null ? _config.ReportDate.Value : DateTime.Now;
                var isFullPath = Path.IsPathRooted(_config.OutputDirectory);
                string outputPath = isFullPath ? _config.OutputDirectory : string.Format("{0}\\out", Directory.GetCurrentDirectory());
                if (!Directory.Exists(outputPath))
                {
                    _logger.LogInformation($"Folder '{outputPath}' does not exist. ");
                    Directory.CreateDirectory(outputPath);
                    _logger.LogInformation($"Created folder '{outputPath}' because it does not exist. ");
                }
                string reportDate = runDate.ToString("yyyyMMdd");
                string createTime = DateTime.Now.ToString("HHmmss");

                switch (_config.ReportType)
                {
                    case 0: // Trades 
                        {
                            var trades = await GetTradesAsync(runDate);

                            // Now we have our trades, request the security reference data (ISIN/SEDOL) via SecurityInformationGet
                            var securityList =  from s in trades
                                                where s.SecurityCode != null && s.Exchange != null
                                                select new { SecurityText = s.SecurityCode + "." + s.Exchange };
                            var uniqueSecurityList = securityList.Select(x => x.SecurityText).Distinct();
                            await _referenceDataCache.Populate(uniqueSecurityList);
                            
                            // Now map out the trades and reference data into the format we want to store in our CSV file
                            var tradeRecords = trades.Select(TradeToRecord).Where((x) => x != null);

                            // Write out the retrieved trades to file.
                            string fileName = $"{outputPath}\\trade_extract_for_{reportDate}_{_config.IosPlusServer.ToLower()}_at_{createTime}.csv";
                            WriteReport(fileName, tradeRecords);
                            break;
                        }
                    case 1: // Audit Trail
                        {
                            var auditTrail = await GetAuditTrailAsync(runDate);

                            // Now we have our audit trail records, request the security reference data (ISIN/SEDOL) via SecurityInformationGet
                            var securityList = from s in auditTrail
                                               where s.SecurityCode != null && s.Exchange != null
                                               select new { SecurityText = s.SecurityCode + "." + s.Exchange };
                            var uniqueSecurityList = securityList.Select(x => x.SecurityText).Distinct();
                            await _referenceDataCache.Populate(uniqueSecurityList);

                            // Now map out the audit trail and reference data into the format we want to store in our CSV file
                            var auditTrailRecords = auditTrail.Select(AuditTrailToRecord).Where((x) => x != null);

                            // Write out the retrieved audit trail to file.
                            string fileName = $"{outputPath}\\audittrail_extract_for_{reportDate}_{_config.IosPlusServer.ToLower()}_at_{createTime}.csv";
                            WriteReport(fileName, auditTrailRecords);
                            break;
                        }
                    case 2: // Order Search
                        {
                            var orderSearch = await GetOrderSearchAsync(runDate);

                            // Now we have our order search records, requesting the security reference data (ISIN/SEDOL) via SecurityInformationGet
                            var securityList = from s in orderSearch
                                               where s.SecurityCode != null && s.Exchange != null
                                               select new { SecurityText = s.SecurityCode + "." + s.Exchange };
                            var uniqueSecurityList = securityList.Select(x => x.SecurityText).Distinct();
                            await _referenceDataCache.Populate(uniqueSecurityList);

                            // Now map out the order search and reference data into the format we want to store in our CSV file
                            var orderSearchRecords = orderSearch.Select(OrderSearchToRecord).Where((x) => x != null);

                            // Write out the retrieved order search to file.
                            string fileName = $"{outputPath}\\ordersearch_extract_for_{reportDate}_{_config.IosPlusServer.ToLower()}_at_{createTime}.csv";
                            WriteReport(fileName, orderSearchRecords);
                            break;
                        }
                    default:
                        {
                            Debug.Assert(false); // We already validated the report types when loading config. Shouldn't hit this.
                            break;
                        }
                }
            }
        }

        public class ReportConfiguration
        {
            public string OutputDirectory
            {
                get; set;
            }
            public string LogDirectory
            {
                get; set;
            }

            public string SoapEndpoint
            {
                get; set;
            }

            public string IosPlusServer
            {
                get; set;
            }

            public string UserName
            {
                get; set;
            }

            public string Company
            {
                get; set;
            }

            public string Password
            {
               get; set;
            }

            public DateTime? ReportDate
            {
                get; set;
            }

            public short? ReportType
            {
                get; set;
            }

            public bool Validate()
            {
                if (ReportType == null || ReportType < 0 || ReportType > 2)
                    return false;

                if (ReportDate == null)
                    return false;

                if (OutputDirectory == null || OutputDirectory == "")
                    return false;

                if (LogDirectory == null || LogDirectory == "")
                    return false;

                if (SoapEndpoint == null || SoapEndpoint == "")
                    return false;

                if (IosPlusServer == null || IosPlusServer == "")
                    return false;

                if (UserName == null || UserName == "")
                    return false;

                if (Company == null || Company == "")
                    return false;

                if (Password == null || Password == "")
                    return false;

                return true;
            }
        }

        public class CommandlineOptions
        {
            [Option('t', "report_type", HelpText = "(Default: 0) Report type to run - 0: Trades, 1: Audit trail, 2: Order Search")]
            public short? ReportType
            {
                get; set;
            }

            [Option('r', "report_date", HelpText = "(Default: TODAY) Date to run report for - use the form yyyymmdd i.e. 20190514 for 14th May 2019")]
            public DateTime? ReportDate
            {
                get; set;
            }
        }

        //private static void ConfigureServices(IServiceCollection services)
        //{
        //    services.AddLogging(configure => configure.AddConsole())
        //            .AddTransient<IosPlusApi>();
        //}

        static async Task Main(string[] args)
		{
            //var services = new ServiceCollection();
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddFilter("Microsoft", LogLevel.Warning)
                    .AddFilter("System", LogLevel.Warning)
                    .AddFilter("NonHostConsoleApp.Program", LogLevel.Debug)
                    .AddConsole();
            });
            ILogger logger = loggerFactory.CreateLogger<Program>();
            //services.AddSingleton<ILoggerFactory>(loggerFactory);

            var opts = new CommandlineOptions();
            var result = Parser.Default
                .ParseArguments<CommandlineOptions>(args)
                .WithParsed(parsed => opts = parsed);

            if (result.Tag == ParserResultType.NotParsed)
            {
                // Help text requested, or parsing failed. Exit.
                logger.LogError("Unable to parse command line arguments.");
                return;
            }

            // Load in configuration such as Web Services V4 SOAP endpoint, IOS+ server etc..
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            var reportConfig = new ReportConfiguration();
            reportConfig.ReportType = opts.ReportType.HasValue ? opts.ReportType.Value : (short)0;
            reportConfig.ReportDate = opts.ReportDate.HasValue ? opts.ReportDate.Value : DateTime.Now;
            reportConfig.UserName = configuration["iosplusdownload_UserName"];
            reportConfig.Company = configuration["iosplusdownload_Company"];
            reportConfig.Password = configuration["iosplusdownload_Password"];
            reportConfig.SoapEndpoint = configuration["iosplusdownload_SoapEndpoint"];
            reportConfig.IosPlusServer = configuration["iosplusdownload_IosPlusServer"];
            reportConfig.OutputDirectory = configuration["iosplusdownload_OutputDirectory"] != null ? configuration["OutputDirectory"] : "./output";
            reportConfig.LogDirectory = configuration["iosplusdownload_LogDirectory"] != null ? configuration["LogDirectory"] : "./log/";
            
            if (!reportConfig.Validate())
            {
                logger.LogError("Invalid configuration provided.");
                return;
            }

            // Generate the report
            await using var iosPlusApi = new IosPlusApi(loggerFactory, reportConfig);
            await iosPlusApi.GenerateReport();

            logger.LogInformation("Completed.");
        }
    }
}
