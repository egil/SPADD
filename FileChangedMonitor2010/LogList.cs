using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.SharePoint;

namespace SPADD.FileChangedMonitor2010
{
    public class LogList
    {
        public const string ListName = "FileChangedLog";

        public static void AddList(SPWeb web)
        {
            if (web.Lists.TryGetList(ListName) == null)
            {
                web.AllowUnsafeUpdates = true;
                var listGuid = web.Lists.Add(ListName, "A list of the most recent files and documents that have changed on this site and subsites.", SPListTemplateType.GenericList);
                var list = web.Lists[listGuid];

                list.Hidden = true;
                list.EnableAttachments = false;
                list.EnableSyndication = true;
                list.NoCrawl = true;                
                list.Update();

                list.Fields.Add(web.Fields.GetFieldByInternalName("URL"));
                list.Update();

                var createdField = list.Fields["Created"];
                createdField.Indexed = true;
                createdField.Update();

                var view = list.DefaultView;
                view.ViewFields.Add("URL");
                view.ViewFields.Add("Created");
                const string viewQuery = @"<OrderBy><FieldRef Name='Created' Ascending='FALSE' /></OrderBy>";
                view.Query = viewQuery;

                view.Update();
                list.Update();

                ULSLog.LogMessage(string.Format("Successfully added {0} list to site. Direct URL = {1}/{2}",
                                                ListName,
                                                list.ParentWeb.Url, list.RootFolder.Url));
            }
        }

        public static void DeleteList(SPWeb web)
        {
            var list = web.Lists.TryGetList(ListName);
            if (list != null)
            {
                list.Delete();
                ULSLog.LogMessage(string.Format("Successfully removed {0} list from site", ListName));
            }
            else
            {
                ULSLog.LogWarning("Unable to find log list on site.");
            }
        }
    }
}
