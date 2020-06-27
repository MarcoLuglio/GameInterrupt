using System;
using System.Collections.Generic;
using System.Text;



namespace GameInterruptLibraryCSCore
{

	public static class HidGameControllers
	{

		private const int GAME_CONTROLLER_USAGE_PAGE = 0x05;

		public static IEnumerable<HidDevice> EnumerateHidDualShock4(VendorIdProductIdInfo[] vendorIdProductIdInfoArray)
		{
			var foundHidDevices = new List<HidDevice>();
			var vendorIdProductIdInfoArrayLen = vendorIdProductIdInfoArray.Length;
			var temp = HidDevices.EnumerateHidDevices();

			for (var deviceEnumerator = temp.GetEnumerator(); deviceEnumerator.MoveNext();)
			//for (int i = 0, len = temp.Count(); i < len; i++)
			{
				var deviceInfo = deviceEnumerator.Current;
				//DeviceInfo x = temp.ElementAt(i);
				var tempDevice = new HidDevice(deviceInfo.Path, deviceInfo.Description);
				//AppLogger.LogToGui($"DEBUG: HID#{iDebugDevCount} Path={x.Path}  Description={x.Description}  VID={tempDevice.Attributes.VendorHexId}  PID={tempDevice.Attributes.ProductHexId}  Usage=0x{tempDevice.Capabilities.Usage.ToString("X")}  Version=0x{tempDevice.Attributes.Version.ToString("X")}", false);
				var found = false;
				for (int j = 0; !found && j < vendorIdProductIdInfoArrayLen; j++)
				{
					VendorIdProductIdInfo vendorIdProductIdInfo = vendorIdProductIdInfoArray[j];
					if (tempDevice.Capabilities.Usage == GAME_CONTROLLER_USAGE_PAGE &&
						tempDevice.Attributes.VendorId == vendorIdProductIdInfo.vendorId &&
						tempDevice.Attributes.ProductId == vendorIdProductIdInfo.productId)
					{
						found = true;
						foundHidDevices.Add(tempDevice);
					}
				}
			}

			return foundHidDevices;
		}

	}

}
