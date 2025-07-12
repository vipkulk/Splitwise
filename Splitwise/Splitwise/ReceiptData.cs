using System.IO;
using System.Net.Http;

namespace Splitwise
{
    public sealed class ReceiptData
    {
        public string Cost { get; set; }
        public string From { get; set; }
        public StreamContent File { get; set; }
        public string FileName { get; set; }
        public string Date { get; set; }
        public string FromEmail { get; set; }
        public string Description { get; set; }
        public string Group { get; set; }
    }
}
