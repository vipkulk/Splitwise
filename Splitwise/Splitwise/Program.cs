using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace Splitwise
{
    class Program
    {
        static async Task Main(string[] args)
        {
            await SplitwiseUpload();

        }
        static async Task SplitwiseUpload()
        {
            UserCredential credential;
            string[] Scopes = { GmailService.Scope.GmailReadonly, GmailService.ScopeConstants.MailGoogleCom };
            string ApplicationName = "Splitwise";
            string userId = "mailexpense13@gmail.com";

            using (var stream =
                new FileStream(@"C:\Users\VipulKulkarni\Downloads\secrets.json", FileMode.Open, FileAccess.Read))
            {
                string credPath = "token.json";
                credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    Scopes,
                    "user",
                    CancellationToken.None,
                    new FileDataStore(credPath, true)).Result;
                Console.WriteLine("Credential file saved to: " + credPath);
            }

            // Create Gmail API service.
            var service = new GmailService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });

            // Define parameters of request.
            UsersResource.MessagesResource.ListRequest request = service.Users.Messages.List(userId);
            request.Q = "in:inbox";
            // List messages.
            IList<Message> messages = request.Execute().Messages;
            //Console.WriteLine($"Message Count :{messages?.Count}");
            if (messages != null && messages.Count > 0)
            {
                foreach (var message in messages)
                {
                    var receipt = new ReceiptData();
                    var messageDetails = service.Users.Messages.Get(userId, message.Id).Execute();
                    Console.WriteLine("Subject: " + messageDetails.Payload.Headers.FirstOrDefault(h => h.Name == "Subject")?.Value);
                    Console.WriteLine("From: " + messageDetails.Payload.Headers.FirstOrDefault(h => h.Name == "From")?.Value);
                    if (messageDetails.Payload.Parts != null)
                    {
                        foreach (var part in messageDetails.Payload.Parts)
                        {
                            if (part.Filename != null && part.Body.AttachmentId != null)
                            {
                                var attachment = service.Users.Messages.Attachments.Get(userId, message.Id, part.Body.AttachmentId).Execute();
                                var bytes = FromBase64UrlSafeString(attachment.Data);
                                var base64string = Convert.ToBase64String(bytes);
                                var receiptRequest = new Request()
                                {
                                    file = base64string
                                };
                                var transactionData = await GetReceiptData(receiptRequest);
                                if (string.IsNullOrEmpty(transactionData.Total))
                                {
                                    Console.WriteLine("Cant process receipt trying later");
                                    return;
                                }
                                receipt.Cost = Regex.Match(transactionData.Total.Replace(',','.').Trim(new char[] {'*', '€'}), @"[+-]?([0-9]*[.])?[0-9]+").ToString();
                                receipt.Date = transactionData.Date;
                                receipt.FromEmail = messageDetails.Payload.Headers.FirstOrDefault(h => h.Name == "From")?.Value;
                                receipt.Description = $"Ex:{transactionData.Merchant}:{transactionData.Date}";
                                receipt.FileName = part.Filename;
                                receipt.File = new StreamContent(new MemoryStream(bytes));
                                var subject = messageDetails.Payload.Headers.FirstOrDefault(h => h.Name == "Subject")?.Value;
                                receipt.Group = (subject?.ToLower() == "common")
                                                ? Groups.Vlaskamp
                                                : Groups.VipulRajesh;

                                var response = await UploadReceipt(receipt);
                                if (response)
                                {
                                    Console.WriteLine("Receipt Uploaded");
                                    service.Users.Messages.Trash(userId, message.Id).Execute();
                                }

                            }
                        }
                    }

                }
            }
            else
            {
                Console.WriteLine("No messages found.");
                Console.ReadLine();
            }
        }

        static async Task<bool> UploadReceipt(ReceiptData data)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
            var client = new HttpClient();
            //var file = @"C:\Users\VipulKulkarni\Downloads\AH_kassabon_12-03-24_1858_1216.pdf";
            if (data.FromEmail.Contains("Vipul")) //from user1
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "<tokenfromsplitwiseofuser1>");
            }
            else if (data.FromEmail.Contains("rajesh")) //from user2
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "<tokenfromsplitwiseofuser2>");
            }
            else
            {
                return false;
            }
            var boundary = $"--------------------------{Guid.NewGuid().ToString("N").ToUpper()}";

            //using (var fileStream = new FileStream(file, FileMode.Open, FileAccess.Read))
            using (var content = new MultipartFormDataContent(boundary))
            {
                content.Headers.Remove("Content-Type");
                content.Headers.TryAddWithoutValidation("Content-Type", $"multipart/form-data; boundary={boundary}");
                using (var fileContent = data.File)
                {
                    fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data");
                    fileContent.Headers.ContentDisposition.Name = "\"receipt\"";
                    fileContent.Headers.ContentDisposition.FileName = $"\"{data.FileName}\"";
                    //fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                    content.Add(fileContent);
                    content.Add(new StringContent(data.Cost), "cost");
                    content.Add(new StringContent(data.Description), "description");
                    content.Add(new StringContent($"This is AI generated transaction. Please check amount with amount from receipt"), "details");
                    content.Add(new StringContent("EUR"), "currency_code");
                    content.Add(new StringContent(data.Group), "group_id");
                    content.Add(new StringContent("true"), "split_equally");
                    var response =await client.PostAsync("https://secure.splitwise.com/api/v3.0/create_expense", content);
                    var expenses = await response.Content.ReadFromJsonAsync<SplitwiseExpenseResponse>();
                    if(expenses.expenses.Count != 1) 
                        return false;
                    if (expenses.expenses[0].id <= 0) 
                        return false;
                    return true;
                }
            }
        }
        static byte[] FromBase64UrlSafeString(string base64UrlSafeString)
        {
            string paddedBase64 = base64UrlSafeString.PadRight(base64UrlSafeString.Length + (4 - base64UrlSafeString.Length % 4) % 4, '=');
            return Convert.FromBase64String(paddedBase64.Replace('-', '+').Replace('_', '/'));
        }

        static async Task<TransactionDetails> GetReceiptData(Request request)
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("api-key", "<api-set-in-powerautomateapi>");
                using StringContent jsonContent = new(
                    JsonSerializer.Serialize(new
                    {
                        file = request.file
                     }),
                       Encoding.UTF8,
                          "application/json");
                var response = await client.PostAsync("<powerautomateurl>",
                     jsonContent);
                return await response.Content.ReadFromJsonAsync<TransactionDetails>();
            };

        }

    }
}
