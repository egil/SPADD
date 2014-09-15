using System;
using System.Security.Permissions;
using Microsoft.SharePoint;
using Microsoft.SharePoint.Administration;
using Microsoft.SharePoint.Utilities;
using Microsoft.SharePoint.Workflow;

namespace SPADD.FileChangedMonitor2010
{
    /// <summary>
    /// List Item Events
    /// </summary>
    public class FileChangedEventReceiver : SPItemEventReceiver
    {
        /// <summary>
        /// An item was added.
        /// </summary>
        public override void ItemAdded(SPItemEventProperties properties)
        {
            base.ItemAdded(properties);

            // Only process item added for Document Libraries
            if (properties.List.BaseType == SPBaseType.DocumentLibrary)
            {
                SPSecurity.RunWithElevatedPrivileges(() => AddFileInfo(properties));
            }
        }

        /// <summary>
        /// An item was updated.
        /// </summary>
        public override void ItemUpdated(SPItemEventProperties properties)
        {
            base.ItemUpdated(properties);

            // Only process item updated for Document Libraries
            if (properties.List.BaseType == SPBaseType.DocumentLibrary)
            {
                SPSecurity.RunWithElevatedPrivileges(() => AddFileInfo(properties));
            }
        }

        /// <summary>
        /// An attachment was added to the item.
        /// </summary>
        public override void ItemAttachmentAdded(SPItemEventProperties properties)
        {
            base.ItemAttachmentAdded(properties);
            SPSecurity.RunWithElevatedPrivileges(() => AddFileInfo(properties));
        }

        private void AddFileInfo(SPItemEventProperties properties)
        {
            try
            {
                var url = properties.WebUrl + "/" + properties.AfterUrl;
                var fileName = properties.AfterUrl;

                using (var site = new SPSite(properties.Web.Site.ID))
                using (var web = site.RootWeb)
                {
                    var list = web.Lists[LogList.ListName];
                    var listItems = list.Items;

                    var item = listItems.Add();
                    item["Title"] = fileName;
                    item["URL"] = url;
                    item.Update();
                }

                ULSLog.LogDebug(String.Format("Added {0} to {1} list", url, LogList.ListName));
            }
            catch (Exception ex)
            {
                ULSLog.LogError(ex);
            }
        }
    }
}