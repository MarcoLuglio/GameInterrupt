using System;
using System.Collections.Generic;
using System.Text;



namespace GameInterruptLibraryCSCore
{

	public static class HidGameControllers
	{

		private const int GAME_CONTROLLER_USAGE_PAGE = 0x05;

		public static IEnumerable<HidDevice> EnumerateHidDualShock4(VendorIdProductIdInfo[] deviceInfo)
		{
			var foundHidDevices = new List<HidDevice>();
			var deviceInfoLen = deviceInfo.Length;
			IEnumerable<DeviceInfo> temp = HidDevices.EnumerateHidDevices();

			for (var devEnum = temp.GetEnumerator(); devEnum.MoveNext();)
			//for (int i = 0, len = temp.Count(); i < len; i++)
			{
				DeviceInfo x = devEnum.Current;
				//DeviceInfo x = temp.ElementAt(i);
				HidDevice tempDev = new HidDevice(x.Path, x.Description);
				//AppLogger.LogToGui($"DEBUG: HID#{iDebugDevCount} Path={x.Path}  Description={x.Description}  VID={tempDev.Attributes.VendorHexId}  PID={tempDev.Attributes.ProductHexId}  Usage=0x{tempDev.Capabilities.Usage.ToString("X")}  Version=0x{tempDev.Attributes.Version.ToString("X")}", false);
				bool found = false;
				for (int j = 0; !found && j < deviceInfoLen; j++)
				{
					VendorIdProductIdInfo tempInfo = deviceInfo[j];
					if (tempDev.Capabilities.Usage == GAME_CONTROLLER_USAGE_PAGE &&
						tempDev.Attributes.VendorId == tempInfo.vendorId &&
						tempDev.Attributes.ProductId == tempInfo.productId)
					{
						found = true;
						foundHidDevices.Add(tempDev);
					}
				}
			}

			return foundHidDevices;
		}

	}

}
