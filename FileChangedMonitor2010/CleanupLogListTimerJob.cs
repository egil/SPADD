using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.SharePoint;
using Microsoft.SharePoint.Administration;
using Microsoft.SharePoint.Utilities;

namespace SPADD.FileChangedMonitor2010
{
    public class CleanupLogListTimerJob : SPJobDefinition
    {
        public const string JobName = "SPADD: Remove old entires from " + LogList.ListName + " list";

        public CleanupLogListTimerJob() : base() { }

        public CleanupLogListTimerJob(string jobName, SPWebApplication webApplication) : base(jobName, webApplication, null, SPJobLockType.Job)
        {
            Title = jobName;
        }

        public override void Execute(Guid targetInstanceId)
        {
            var settings = this.WebApplication.GetChild<CleanupLogListTimerJobSettings>(CleanupLogListTimerJobSettings.SettingsName);

            if (settings == null)
            {
                ULSLog.LogWarning("Unable to retrieve cleanup timer job settings. Could not clean up.");
                return;
            }

            using (var site = new SPSite(settings.SiteCollectionId))
            using (var web = site.RootWeb)
            {
                var list = web.Lists.TryGetList(LogList.ListName);

                if (list != null)
                {
                    var time = SPUtility.CreateISO8601DateTimeFromSystemDateTime(DateTime.Now.AddHours(-1.0));
                    var query = new SPQuery();
                    query.ViewFields = "<FieldRef Name='Created' />";
                    query.Query = "<Where><Leq><FieldRef Name='Created' /><Value Type='DateTime' IncludeTimeValue='TRUE'>" + time + "</Value></Leq></Where>";

                    var items = list.GetItems(query);
                    var removed = 0;
                    for (int i = items.Count - 1; i >= 0; i--)
                    {
                        items.Delete(i);
                        removed++;
                    }

                    ULSLog.LogMessage("Removed " + removed + " old entires in " + LogList.ListName);
                }
                else
                {
                    ULSLog.LogWarning(LogList.ListName + " not found on site. Could not clean up.");
                }
            }
        }
    }

    public class CleanupLogListTimerJobSettings: SPPersistedObject
    {
        public static string SettingsName = "CleanupLogListTimerJobSettings";

        [Persisted]
        private string _listName;

        [Persisted]
        private Guid _siteCollectionId = Guid.Empty;
        
        [Persisted]
        private Guid _siteId = Guid.Empty;

        public CleanupLogListTimerJobSettings() { }
        public CleanupLogListTimerJobSettings(SPPersistedObject parent, Guid id)
            : base(SettingsName, parent, id) { }

        public string ListName { get { return _listName; } internal set { _listName = value; } }
        public Guid SiteCollectionId { get { return _siteCollectionId; } internal set { _siteCollectionId = value; } }
        public Guid SiteId { get { return _siteId; } internal set { _siteId = value; } }
    }
}
