//using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Threading.Tasks;
using WS.Iress;

namespace IosPlusDownload
{

    partial class Program
    {
        public class ReferenceDataCache
        {
            public IosPlusApi _iosPlusApi;

           
            public Dictionary<string, ReferenceData> ReferenceDataLookup
            {
                get;
            }

            public ReferenceDataCache(IosPlusApi iosPlusApi)
            {
                _iosPlusApi = iosPlusApi;
                ReferenceDataLookup = new Dictionary<string, ReferenceData>();
            }

            public void UpdateCache(SecurityInformationGetDataRow securityInformationGetDataRow)
            {
                var securityExchangeKey = $"{securityInformationGetDataRow.SecurityCode}.{securityInformationGetDataRow.Exchange}";
                if (!ReferenceDataLookup.ContainsKey(securityExchangeKey))
                    ReferenceDataLookup.Add(securityExchangeKey, new ReferenceData(securityInformationGetDataRow.SEDOL, securityInformationGetDataRow.ISIN));
            }
            
            public async Task Populate(IEnumerable<string> uniqueSecurityList)
            {
                var securityInformationRows = await _iosPlusApi.GetSecurityInformationAsync(uniqueSecurityList);
                foreach (var securityInformationRow in securityInformationRows)
                    this.UpdateCache(securityInformationRow);
            }
        }
    }
}
