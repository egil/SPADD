using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.SharePoint;
using Microsoft.SharePoint.Administration;

namespace SPADD.FileChangedMonitor2010
{
    public class ULSLog : SPDiagnosticsServiceBase
    {
        public const string ProductName = "SPADD";
        private static ULSLog _current;

        public static ULSLog Current
        {
            get
            {
                if (_current == null)
                {
                    _current = new ULSLog();
                }
                return _current;
            }
        }

        private ULSLog() : base(ProductName, SPFarm.Local) { }

        protected override IEnumerable<SPDiagnosticsArea> ProvideAreas()
        {
            var areas = new List<SPDiagnosticsArea>        
            {            
                new SPDiagnosticsArea(ProductName, new List<SPDiagnosticsCategory>            
                {                
                    new SPDiagnosticsCategory("Error", TraceSeverity.High, EventSeverity.Error),
                    new SPDiagnosticsCategory("Warning", TraceSeverity.Medium, EventSeverity.Warning),
                    new SPDiagnosticsCategory("Logging", TraceSeverity.Verbose, EventSeverity.Verbose),
                    new SPDiagnosticsCategory("Debugging", TraceSeverity.Verbose, EventSeverity.Verbose)
                })        
            };
            return areas;
        }

        private string MapTraceSeverity(TraceSeverity traceSeverity)
        {
            switch (traceSeverity)
            {
                case TraceSeverity.High:
                    return "Error";
                case TraceSeverity.Medium:
                    return "Warning";
                case TraceSeverity.Verbose:
                default:
                    return "Debugging";
            }
        }

        public static void Log(TraceSeverity traceSeverity, Exception ex)
        {
            SPSecurity.RunWithElevatedPrivileges(
                () =>
                    {
                        var category = Current.Areas[ProductName].Categories["Error"];
                        Current.WriteTrace(0, category, TraceSeverity.High, ex.Message);
                        Current.WriteTrace(0, category, TraceSeverity.High, ex.ToString());
                    });
        }

        public static void Log(TraceSeverity traceSeverity, string message, Exception ex)
        {
            SPSecurity.RunWithElevatedPrivileges(
                () =>
                    {
                        var category = Current.Areas[ProductName].Categories["Error"];
                        Current.WriteTrace(0, category, TraceSeverity.High, ex.Message);
                        Current.WriteTrace(0, category, TraceSeverity.High, ex.ToString());
                    });
        }

        public static void LogError(Exception ex)
        {
            SPSecurity.RunWithElevatedPrivileges(
                () =>
                    {
                        var category = Current.Areas[ProductName].Categories["Error"];
                        Current.WriteTrace(0, category, TraceSeverity.High, ex.Message);
                        Current.WriteTrace(0, category, TraceSeverity.High, ex.ToString());
                    });
        }

        public static void LogError(Exception ex, string message)
        {
            SPSecurity.RunWithElevatedPrivileges(
                () =>
                    {
                        var category = Current.Areas[ProductName].Categories["Error"];
                        Current.WriteTrace(0, category, TraceSeverity.High, ex.Message);
                        Current.WriteTrace(0, category, TraceSeverity.High, ex.ToString());
                    });
        }

        public static void LogError(string message, string stackTrace)
        {
            SPSecurity.RunWithElevatedPrivileges(
                () =>
                    {
                        var category = Current.Areas[ProductName].Categories["Error"];
                        Current.WriteTrace(0, category, TraceSeverity.High, message);
                    });
        }

        public static void LogWarning(string message)
        {
            SPSecurity.RunWithElevatedPrivileges(
                () =>
                    {
                        var category = Current.Areas[ProductName].Categories["Warning"];
                        Current.WriteTrace(1, category, TraceSeverity.Medium, message);
                    });
        }

        public static void LogMessage(string message)
        {
            SPSecurity.RunWithElevatedPrivileges(
                () =>
                    {
                        var category = Current.Areas[ProductName].Categories["Logging"];
                        Current.WriteTrace(1, category, TraceSeverity.Verbose, message);
                    });
        }

        public static void LogDebug(string message)
        {
            SPSecurity.RunWithElevatedPrivileges(
                () =>
                    {
                        var category = Current.Areas[ProductName].Categories["Debugging"];
                        Current.WriteTrace(1, category, TraceSeverity.Verbose, message);
                    });
        }
    }
}