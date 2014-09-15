using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using Microsoft.SharePoint;
using Microsoft.SharePoint.Administration;
using Microsoft.SharePoint.WebControls;

namespace SPADD.FileChangedMonitor2010.Features.FileChangedMonitor2010Feature
{
    [Guid("14e423a0-ad44-4372-a429-a61b1b2d41ef")]
    public class FileChangedMonitor2010FeatureEventReceiver : SPFeatureReceiver
    {
        public override void FeatureActivated(SPFeatureReceiverProperties properties)
        {
            var site = properties.Feature.Parent as SPSite;
            SPWeb web = site.RootWeb;
            try
            {
                LogList.AddList(web);

                // http://msdn.microsoft.com/en-us/library/microsoft.sharepoint.spsecurity.runwithelevatedprivileges(v=office.14).aspx
                SPSecurity.RunWithElevatedPrivileges(() =>
                {
                    using (var epsite = new SPSite(site.ID))
                    {
                        AddCleanupTimerJob(epsite, epsite.WebApplication);
                    }
                });

                AddEventReceivers(web);
            }
            catch (Exception ex)
            {
                ULSLog.LogError(ex);                
                site.Features.Remove(properties.Feature.DefinitionId);
            }
            finally
            {
                site.Dispose();
                web.Dispose();
            }
        }

        public override void FeatureDeactivating(SPFeatureReceiverProperties properties)
        {
            var site = properties.Feature.Parent as SPSite;
            SPWeb web = site.RootWeb;
            try
            {                
                RemoveEventReceivers(web);

                // http://msdn.microsoft.com/en-us/library/microsoft.sharepoint.spsecurity.runwithelevatedprivileges(v=office.14).aspx
                SPSecurity.RunWithElevatedPrivileges(() =>
                {
                    using (var epsite = new SPSite(site.ID))
                    {
                        RemoveCleanupTimerJob(epsite.WebApplication);
                    }
                });

                LogList.DeleteList(web);
            }
            catch (Exception ex)
            {
                ULSLog.LogError(ex);
            }
            finally
            {
                site.Dispose();
                web.Dispose();
            }
        }


        private static void AddEventReceivers(SPWeb web)
        {
            var documentContentType = web.ContentTypes[new SPContentTypeId("0x01")];
            AddEventReceiver(documentContentType, SPEventReceiverType.ItemAdded, typeof(FileChangedEventReceiver), 1000);
            AddEventReceiver(documentContentType, SPEventReceiverType.ItemUpdated, typeof(FileChangedEventReceiver), 1001);
            AddEventReceiver(documentContentType, SPEventReceiverType.ItemAttachmentAdded, typeof(FileChangedEventReceiver), 1002);
            
            web.Update();
            
            ULSLog.LogMessage("Successfully installed global event receivers on base item content type.");
        }

        private static void RemoveEventReceivers(SPWeb web)
        {
            var documentContentType = web.ContentTypes[new SPContentTypeId("0x01")];
            RemoveEventReceiver(documentContentType, SPEventReceiverType.ItemAdded, typeof (FileChangedEventReceiver));
            RemoveEventReceiver(documentContentType, SPEventReceiverType.ItemUpdated, typeof (FileChangedEventReceiver));
            RemoveEventReceiver(documentContentType, SPEventReceiverType.ItemAttachmentAdded, typeof (FileChangedEventReceiver));
            
            web.Update();

            ULSLog.LogMessage("Successfully uninstalled global event receivers on base item content type.");
        }

        static void AddEventReceiver(SPContentType contentType, SPEventReceiverType type, Type target, int sequenceNumber)
        {
            // Remove event receiver it is already attached
            RemoveEventReceiver(contentType, type, target);

            // Add event receiver
            var eventReceiver = contentType.EventReceivers.Add();
            eventReceiver.Type = type;
            eventReceiver.Assembly = target.Assembly.FullName;
            eventReceiver.Class = target.FullName;
            eventReceiver.SequenceNumber = sequenceNumber;
            eventReceiver.Update();

            contentType.Update(true, false);
        }  

        static void RemoveEventReceiver(SPContentType contentType, SPEventReceiverType type, Type target)
        {
            foreach (SPEventReceiverDefinition definition in contentType.EventReceivers)
            {
                if (definition.Class == target.FullName && definition.Assembly == target.Assembly.FullName && definition.Type == type)
                {
                    definition.Delete();
                    contentType.Update(true, false);
                    break;
                }
            }
        }

        static void AddCleanupTimerJob(SPSite site, SPWebApplication webApplication)
        {
            // Remove job if it exists.
            RemoveCleanupTimerJob(webApplication);

            // Create the job.
            var job = new CleanupLogListTimerJob(CleanupLogListTimerJob.JobName, webApplication);

            var schedule = new SPHourlySchedule();
            schedule.BeginMinute = 0;
            schedule.EndMinute = 59;
            job.Schedule = schedule;
            job.Update();

            var settings = new CleanupLogListTimerJobSettings(webApplication, Guid.NewGuid());
            settings.ListName = LogList.ListName;
            settings.SiteCollectionId = site.ID;
            settings.SiteId = site.RootWeb.ID;

            settings.Update(true);

            ULSLog.LogMessage("Sucessfully added settings and timer job to Web Application.");
        }

        private static void RemoveCleanupTimerJob(SPWebApplication webApplication)
        {
            foreach (var job in webApplication.JobDefinitions)
            {
                if (job.Name == CleanupLogListTimerJob.JobName)
                {
                    job.Delete();
                    break;
                }
            }

            var settings = webApplication.GetChild<CleanupLogListTimerJobSettings>(CleanupLogListTimerJobSettings.SettingsName);
            if (settings != null) settings.Delete();
            ULSLog.LogMessage("Sucessfully removed settings and timer job from Web Application.");
        }
    }
}
