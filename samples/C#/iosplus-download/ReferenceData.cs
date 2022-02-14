//using Microsoft.Extensions.DependencyInjection;

namespace IosPlusDownload
{
    partial class Program
    {
        public class ReferenceData
        {
            private string _SEDOL;
            private string _ISIN;

            public string SEDOL
            {
                get { return _SEDOL; }
                set { _SEDOL = value; }
            }

            public string ISIN
            {
                get { return _ISIN; }
                set { _ISIN = value; }
            }

            public ReferenceData(string SEDOL, string ISIN)
            {
                this.SEDOL = SEDOL;
                this.ISIN = ISIN;
            }
        }
    }
}
