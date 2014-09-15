using System;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.SharePoint;
using Microsoft.SharePoint.Client;
using Microsoft.SharePoint.Utilities;
using System.Timers;

namespace DownloadApplication
{
    class Program
    {
        class ChangedFiles
        {
            private readonly Uri _site;
            private readonly string _list;
            private readonly NetworkCredential _credentials;
            private bool _isStarted = false;
            private readonly Timer _timer;
            private DateTime? _lastDownloadDateTime;
            private readonly ushort _concurrentDownloads;
            private readonly double _interval;
            private BlockingCollection<Uri> _queue;

            public ChangedFiles(Uri site, string list, NetworkCredential credentials, double interval = 60000, ushort concurrentDownloads = 2)
            {
                _site = site;
                _list = list;
                _credentials = credentials;
                _interval = interval;
                _concurrentDownloads = concurrentDownloads;
                _timer = new Timer(_interval);
                _timer.Elapsed += DownloadLatestFileList;
                _timer.AutoReset = false;
            }

            public void Start()
            {
                if (!_isStarted)
                {
                    _isStarted = true;
                    _queue = new BlockingCollection<Uri>(new ConcurrentQueue<Uri>());
                    DownloadLatestFileList(null, null);

                    // Start downloaders 
                    for (int i = 0; i < _concurrentDownloads; i++)
                        Task.Run(() => Downloader());
                }
                _timer.Start();
            }            

            public void Pause()
            {
                _timer.Stop();
            }

            public void Stop()
            {
                _isStarted = false;
                _timer.Stop();
                _queue.CompleteAdding();
            }

            private void DownloadLatestFileList(object sender, ElapsedEventArgs e)
            {
                using (var clientContext = new ClientContext(_site))
                {
                    List spList = clientContext.Web.Lists.GetByTitle(_list);
                    clientContext.Load(spList);
                    clientContext.ExecuteQuery();

                    if (spList != null && spList.ItemCount > 0)
                    {                       
                        var camlQuery = CreateQuery();

                        ListItemCollection listItems = spList.GetItems(camlQuery);
                        clientContext.Load(listItems);
                        clientContext.ExecuteQuery();
                        
                        // Push new items to queue
                        foreach (var listItem in listItems)
                        {
                            var createdValue = listItem.FieldValues["Created"];
                            if (!(createdValue is DateTime)) continue;
                            _lastDownloadDateTime = (DateTime)createdValue;
                            
                            var urlValue = listItem["URL"] as FieldUrlValue;
                            if (urlValue == null) continue;

                            Uri uri;
                            if (!Uri.TryCreate(urlValue.Url, UriKind.Absolute, out uri)) continue;

                            // Make sure the download queue has not been stopped
                            if (!_queue.IsAddingCompleted)
                            {
                                // Make sure the file is not already added to the queue
                                if (_queue.All(x => x.AbsoluteUri != uri.AbsoluteUri)) _queue.Add(uri);
                            }
                            else
                            {
                                // If the download queue has been stopped, we break out of the for each...
                                break;
                            }
                        }
                    }
                }

                // Restart timer, it will trigger a download after the interval has passed
                if (_isStarted)
                {
                    _timer.Start();
                }
            }

            private CamlQuery CreateQuery()
            {
                var camlQuery = new CamlQuery();
                camlQuery.ViewXml = "<View><Query><OrderBy><FieldRef Name='Created' /></OrderBy>";

                if (_lastDownloadDateTime.HasValue)
                {
                    camlQuery.ViewXml +=
                        "<Where><Gt><FieldRef Name='Created' /><Value Type='DateTime' IncludeTimeValue='TRUE'>" +
                        SPUtility.CreateISO8601DateTimeFromSystemDateTime(_lastDownloadDateTime.Value) +
                        "</Value></Gt></Where>";
                }

                camlQuery.ViewXml += "</Query><ViewFields><FieldRef Name='Created' /><FieldRef Name='URL' /></ViewFields></View>";
                camlQuery.DatesInUtc = false;

                return camlQuery;
            }

            private void Downloader()
            {
                var server = new WebClient {Credentials = _credentials};
                while (_isStarted)
                {
                    // Get the next file to download
                    Uri file = null;
                    try { 
                        file = _queue.Take();
                    }
                    catch (InvalidOperationException) { continue; }

                    // Download the file to a temporary file, delete the temp file once download is completed
                    var tempFile = Path.GetTempFileName();
                    try
                    {
                        Console.WriteLine("Downloading: " + file);
                        server.DownloadFile(file, tempFile);
                    }
                    catch (WebException) { }
                    finally
                    {
                        System.IO.File.Delete(tempFile);
                    }
                }
            }
        }

        static void Main(string[] args)
        {
            NameValueCollection settings = ConfigurationManager.AppSettings;

            var listName = "FileChangedLog";
            var siteUrl = new Uri("http://xxxx.xxxxx.xxx");
            var credentials = new NetworkCredential("xxxxx", "xxxxx", "xxxxx");
            var downloadQueue = new ChangedFiles(siteUrl, listName, credentials, 10, 0);
            downloadQueue.Start();
            Console.ReadLine();
            downloadQueue.Stop();
        }


    }
}
