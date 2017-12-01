using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace XzonControlPanel.Security
{
    public class FingerPrint
    {
        private static string _fingerPrint = string.Empty;
        public static string Value
        {
            get
            {
                if (string.IsNullOrEmpty(_fingerPrint))
                {
                    _fingerPrint = GetHash($"{CpuId}{BiosId}{BaseId}{DiskId}{VideoId}{MacId}{Environment.MachineName}");
                }

                return _fingerPrint.Substring(0,10);
            }
        }
        private static string GetHash(string s)
        {
            return SecurityHelper.GetHashSha256(s);
        }

        //Return a hardware identifier
        private static string Identifier(string wmiClass, string wmiProperty, string wmiMustBeTrue)
        {
            string result = "";
            var mc = new ManagementClass(wmiClass);
            //var moc = mc.GetInstances();
            var moc = mc.GetInstances().Cast<ManagementObject>().ToList();
            foreach (var mo in moc)
            {
                //var mo = (ManagementObject)o;
                if (mo[wmiMustBeTrue].ToString() == "True")
                {
                    //Only get the first one
                    if (result == "")
                    {
                        try
                        {
                            result = mo[wmiProperty].ToString();
                            break;
                        }
                        catch
                        {
                            //Do nothing
                        }
                    }
                }
            }
            return result;
        }
        //Return a hardware identifier
        private static string Identifier(string wmiClass, string wmiProperty)
        {
            string result = "";
            var mc = new ManagementClass(wmiClass);
            var moc = mc.GetInstances().Cast<ManagementObject>().ToList();
            foreach (var mo in moc)
            {
                //var mo = (ManagementObject)o;
                //Only get the first one
                if (result == "")
                {
                    try
                    {
                        result = mo[wmiProperty].ToString();
                        break;
                    }
                    catch
                    {
                        //Do Nothing
                    }
                }
            }
            return result;
        }
        private static string CpuId
        {
            get
            {
                //Uses first CPU identifier available in order of preference
                //Don't get all identifiers, as it is very time consuming
                string retVal = Identifier("Win32_Processor", "UniqueId");
                if (retVal == "") //If no UniqueID, use ProcessorID
                {
                    retVal = Identifier("Win32_Processor", "ProcessorId");
                    if (retVal == "") //If no ProcessorId, use Name
                    {
                        retVal = Identifier("Win32_Processor", "Name");
                        if (retVal == "") //If no Name, use Manufacturer
                        {
                            retVal = Identifier("Win32_Processor", "Manufacturer");
                        }
                        //Add clock speed for extra security
                        retVal += Identifier("Win32_Processor", "MaxClockSpeed");
                    }
                }
                return retVal;
            }
        }
        //BIOS Identifier
        private static string BiosId => Identifier("Win32_BIOS", "Manufacturer") + Identifier("Win32_BIOS", "SMBIOSBIOSVersion") + Identifier("Win32_BIOS", "IdentificationCode") + Identifier("Win32_BIOS", "SerialNumber") + Identifier("Win32_BIOS", "ReleaseDate") + Identifier("Win32_BIOS", "Version");

        //Main physical hard drive ID
        private static string DiskId => Identifier("Win32_DiskDrive", "Model") + Identifier("Win32_DiskDrive", "Manufacturer") + Identifier("Win32_DiskDrive", "Signature") + Identifier("Win32_DiskDrive", "TotalHeads");
        //Motherboard ID
        private static string BaseId => Identifier("Win32_BaseBoard", "Model") + Identifier("Win32_BaseBoard", "Manufacturer") + Identifier("Win32_BaseBoard", "Name") + Identifier("Win32_BaseBoard", "SerialNumber");

        //Primary video controller ID
        private static string VideoId => Identifier("Win32_VideoController", "DriverVersion") + Identifier("Win32_VideoController", "Name");
        //First enabled network card ID
        private static string MacId => Identifier("Win32_NetworkAdapterConfiguration", "MACAddress", "IPEnabled");
    }
}
