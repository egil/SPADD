using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.SharePoint.Client;
using Microsoft.SharePoint.Utilities;

namespace SPADD.DownloadService
{
    public partial class SPADDDownloadService : ServiceBase
    {
        private Uri _site;
        private string _list;
        private ICredentials _credentials;
        private Timer _timer;
        private Timer _logTimer;
        private DateTime? _lastDownloadDateTime;
        private int _concurrentDownloads;
        private double _interval;
        private BlockingCollection<Uri> _queue;
        private bool _running;
        private ConcurrentQueue<string> _downloadLog;

        public SPADDDownloadService()
        {
            InitializeComponent();
            if (!EventLog.SourceExists(Log.Source))
            {
                EventLog.CreateEventSource(Log.Source, Log.Log);
            }
        }

        protected override void OnStart(string[] args)
        {
            _site = new Uri(Properties.Settings.Default.Site);
            _list = Properties.Settings.Default.List;
            _credentials = string.IsNullOrWhiteSpace(Properties.Settings.Default.Username) ||
                           string.IsNullOrWhiteSpace(Properties.Settings.Default.Domain) ||
                           string.IsNullOrWhiteSpace(Properties.Settings.Default.Password)
                               ? CredentialCache.DefaultCredentials
                               : new NetworkCredential(Properties.Settings.Default.Username,
                                                       Properties.Settings.Default.Password,
                                                       Properties.Settings.Default.Domain);

            _concurrentDownloads = Properties.Settings.Default.ConcurrentDownloads;
            _interval = Properties.Settings.Default.RefreshInterval.TotalMilliseconds;

            // If not in debug mode, we set out hourly logging to event log of downloaded files
            if (!Properties.Settings.Default.DebugMode)
            {
                _downloadLog = new ConcurrentQueue<string>();
                _logTimer = new Timer(TimeSpan.FromHours(1).TotalMilliseconds) { AutoReset = true };
                _logTimer.Elapsed += LogTimerElapsed;
                _logTimer.Start();
            }

            // Set up timer
            _timer = new Timer(_interval);
            _timer.Elapsed += DownloadLatestFileList;
            _timer.AutoReset = false;

            // Make sure last download time has been reset
            _lastDownloadDateTime = null;

            // Create new queue
            _queue = new BlockingCollection<Uri>(new ConcurrentQueue<Uri>());

            // First we do an initial download
            DownloadLatestFileList(null, null);

            // Start downloaders 
            _running = true;
            for (int i = 0; i < _concurrentDownloads; i++) Task.Factory.StartNew(DownloaderTask);

            _timer.Start();
        }

        protected override void OnStop()
        {
            _running = false;
            _queue.CompleteAdding();
            _timer.Stop();
            _timer.Dispose();

            if (!Properties.Settings.Default.DebugMode)
            {
                _logTimer.Stop();
                _logTimer.Dispose();
                LogTimerElapsed(null, null);
                _downloadLog = null;
            }
        }

        void LogTimerElapsed(object sender, ElapsedEventArgs e)
        {
            string file;
            if (!_downloadLog.TryPeek(out file)) return;

            var msg = new StringBuilder();
            msg.AppendLine("Downloaded files in the last hour:");
            msg.AppendLine();

            while (_downloadLog.TryDequeue(out file))
            {
                msg.AppendLine(file);
            }

            Log.WriteEntry(msg.ToString(), EventLogEntryType.Information);
        }

        private void DownloadLatestFileList(object sender, ElapsedEventArgs e)
        {
            var clientContext = new ClientContext(_site) { Credentials = _credentials };
            try
            {
                var spList = clientContext.Web.Lists.GetByTitle(_list);
                clientContext.Load(spList);
                try
                {
                    clientContext.ExecuteQuery();
                }
                catch (Exception ex)
                {
                    Log.WriteEntry(_site.AbsoluteUri + Environment.NewLine + ex.Message, EventLogEntryType.Error);
                    throw;
                }

                if (spList != null && spList.ItemCount > 0)
                {
                    var camlQuery = CreateQuery(_lastDownloadDateTime);

                    ListItemCollection listItems = spList.GetItems(camlQuery);
                    clientContext.Load(listItems);
                    try
                    {
                        if (Properties.Settings.Default.DebugMode)
                            Log.WriteEntry(
                                "Looking for changed files with query: " + Environment.NewLine +
                                camlQuery.ViewXml, EventLogEntryType.Information);

                        clientContext.ExecuteQuery();
                    }
                    catch (Exception ex)
                    {
                        Log.WriteEntry(_site.AbsoluteUri + Environment.NewLine + ex.Message, EventLogEntryType.Error);
                        throw;
                    }

                    if (Properties.Settings.Default.DebugMode)                    
                    {
                        var sb = new StringBuilder("Found " + listItems.Count + " changed files.");
                        sb.AppendLine();
                        foreach (var listItem in listItems)
                        {                            
                            var createdValue = listItem.FieldValues["Created"];
                            var urlValue = listItem["URL"] as FieldUrlValue;
                            var url = urlValue == null ? "" : urlValue.Url;
                            sb.AppendLine(createdValue + ": " + url);
                        }                        
                        Log.WriteEntry(sb.ToString(), EventLogEntryType.Information);
                    }
                    
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
            finally
            {
                clientContext.Dispose();

                // Restart timer, it will trigger a download after the interval has passed
                if (_running)
                {
                    _timer.Start();
                }
            }
        }

        private void DownloaderTask()
        {
            using (var server = new WebClient { Credentials = _credentials })
            {
                while (_running)
                {
                    // Get the next file to download
                    Uri file = null;
                    try
                    {
                        file = _queue.Take();
                    }
                    catch (InvalidOperationException)
                    {
                        continue;
                    }

                    var tempFile = Path.GetTempFileName();
                    try
                    {
                        if (Properties.Settings.Default.DebugMode) Log.WriteEntry("Downloading: " + file.AbsoluteUri, EventLogEntryType.Information);

                        server.DownloadFile(file, tempFile);

                        if (Properties.Settings.Default.DebugMode)
                        {
                            Log.WriteEntry("Downloaded: " + file.AbsoluteUri, EventLogEntryType.Information);
                        }
                        else
                        {
                            _downloadLog.Enqueue(file.AbsoluteUri);
                        }
                    }
                    catch (WebException ex)
                    {
                        Log.WriteEntry(file.AbsoluteUri + Environment.NewLine + ex.Message, EventLogEntryType.Error);
                    }
                    finally
                    {
                        System.IO.File.Delete(tempFile);
                    }
                }
            }
        }

        private static CamlQuery CreateQuery(DateTime? lastDownloadDateTime)
        {
            var camlQuery = new CamlQuery();
            camlQuery.ViewXml = "<View><Query><OrderBy><FieldRef Name='Created' /></OrderBy>";

            if (lastDownloadDateTime.HasValue)
            {
                camlQuery.ViewXml +=
                    "<Where><Gt><FieldRef Name='Created' /><Value Type='DateTime' IncludeTimeValue='TRUE'>" +
                    SPUtility.CreateISO8601DateTimeFromSystemDateTime(lastDownloadDateTime.Value) +
                    "</Value></Gt></Where>";
            }

            camlQuery.ViewXml += "</Query><ViewFields><FieldRef Name='Created' /><FieldRef Name='URL' /></ViewFields></View>";
            camlQuery.DatesInUtc = false;

            return camlQuery;
        }
    }
}
