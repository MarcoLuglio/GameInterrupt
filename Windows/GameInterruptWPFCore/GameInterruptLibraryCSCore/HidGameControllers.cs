using GameInterruptLibraryCSCore.Controllers;
using GameInterruptLibraryCSCore.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;



namespace GameInterruptLibraryCSCore
{

	public static class HidGameControllers
	{

		public static void findControllers()
		{
			lock (Devices) // TODO will this be called by multiple threads? why?
			{
				var hidGameControllers = HidGameControllers.EnumerateHidControllersMatching(vendorIdProductIdInfoArray: HidGameControllers.dualShock4CompatibleDevices);
				hidGameControllers = hidGameControllers
					.Where(hidController => IsRealDS4(hidController))
					.OrderBy<HidDevice, ConnectionType>((HidDevice hidGameController) =>
					{ // Sort Bluetooth first in case USB is also connected on the same controller.
						return DualShock4Controller.HidConnectionType(hidGameController);
					});

				var tempHidGameControllers = hidGameControllers.ToList(); // TODO it's already an IEnumerable, why tolist()?? Just to create a copy? Why creating a copy?
																		  // TODO purgeHiddenExclusiveDevices();
				tempHidGameControllers.AddRange(HidGameControllers.disabledDevices);
				int hidGameControllersCount = tempHidGameControllers.Count();
				string devicePlural = "device" + (hidGameControllersCount == 0 || hidGameControllersCount > 1 ? "s" : "");
				//Log.LogToGui("Found " + hidGameControllersCount + " possible " + devicePlural + ". Examining " + devicePlural + ".", false);

				for (int i = 0; i < hidGameControllersCount; i++)
				//foreach (HidDevice hDevice in hDevices)
				{
					var tempHidGameController = tempHidGameControllers[i];
					if (tempHidGameController.Description == "HID-compliant vendor-defined device")
					{
						continue; // ignore the Nacon Revolution Pro programming interface
					}

					if (HidGameControllers.devicePaths.Contains(tempHidGameController.DevicePath))
					{
						continue; // BT/USB endpoint already open once
					}

					if (!tempHidGameController.IsOpen)
					{
						tempHidGameController.OpenDevice(HidGameControllers.isExclusiveMode);
						if (!tempHidGameController.IsOpen && HidGameControllers.isExclusiveMode)
						{
							try
							{
								// Check if running with elevated permissions
								WindowsIdentity identity = WindowsIdentity.GetCurrent();
								WindowsPrincipal principal = new WindowsPrincipal(identity);
								bool elevated = principal.IsInRole(WindowsBuiltInRole.Administrator);

								if (!elevated)
								{
									// Tell the client to launch routine to re-enable a device
									RequestElevationArgs eleArgs = new RequestElevationArgs(devicePathToInstanceId(tempHidGameController.DevicePath));
									RequestElevation?.Invoke(eleArgs);
									if (eleArgs.StatusCode == RequestElevationArgs.STATUS_SUCCESS)
									{
										tempHidGameController.OpenDevice(HidGameControllers.isExclusiveMode);
									}
								}
								else
								{
									HidGameControllers.ReEnableDevice(devicePathToInstanceId(tempHidGameController.DevicePath));
									tempHidGameController.OpenDevice(HidGameControllers.isExclusiveMode);
								}
							}
							catch (Exception)
							{
								// FIXME log this!
							}
						}

						// TODO in exclusive mode, try to hold both open when both are connected
						if (HidGameControllers.isExclusiveMode && !tempHidGameController.IsOpen)
						{
							tempHidGameController.OpenDevice(isExclusive: false);
						}
					}

					if (tempHidGameController.IsOpen)
					{
						string serial = tempHidGameController.ReadSerial();
						bool validSerial = !serial.Equals(HidDevice.blankSerial);
						if (validSerial && deviceSerials.Contains(serial))
						{
							// happens when the BT endpoint already is open and the USB is plugged into the same host
							if (HidGameControllers.isExclusiveMode && tempHidGameController.IsExclusive
								&& !HidGameControllers.disabledDevices.Contains(tempHidGameController))
							{
								// Grab reference to exclusively opened HidDevice so device
								// stays hidden to other processes
								HidGameControllers.disabledDevices.Add(tempHidGameController);
								//DevicePaths.Add(tempHidGameController.DevicePath);
							}

							continue;
						}
						else
						{
							var vendorIdProductIdInfo = dualShock4CompatibleDevices.Single(
								x => x.vendorId == tempHidGameController.Attributes.VendorId
								&& x.productId == tempHidGameController.Attributes.ProductId
							);

							var ds4Device = new DualShock4Controller(tempHidGameController, vendorIdProductIdInfo.name, vendorIdProductIdInfo.featureSet);
							//ds4Device.Removal += On_Removal;
							if (!ds4Device.ExitOutputThread)
							{
								HidGameControllers.Devices.Add(tempHidGameController.DevicePath, ds4Device);
								HidGameControllers.devicePaths.Add(tempHidGameController.DevicePath);
								HidGameControllers.deviceSerials.Add(serial);
							}
						}
					}
				}
			}
		}

		// TODO why do I need this?
		private static bool IsRealDS4(HidDevice hidGameController)
		{
			string deviceInstanceId = devicePathToInstanceId(hidGameController.DevicePath);
			string numberForUI = HidGameControllers.GetDeviceProperty(
				deviceInstanceId,
				NativeMethods.DEVPKEY_Device_UINumber
			);
			return string.IsNullOrEmpty(numberForUI);
		}

		// TODO maybbe this and the associated code that checks this should go in the hid devices class
		private static string devicePathToInstanceId(string devicePath)
		{
			string deviceInstanceId = devicePath;
			deviceInstanceId = deviceInstanceId.Remove(0, deviceInstanceId.LastIndexOf('\\') + 1);
			deviceInstanceId = deviceInstanceId.Remove(deviceInstanceId.LastIndexOf('{'));
			deviceInstanceId = deviceInstanceId.Replace('#', '\\');
			if (deviceInstanceId.EndsWith("\\"))
			{
				deviceInstanceId = deviceInstanceId.Remove(deviceInstanceId.Length - 1);
			}

			return deviceInstanceId;
		}

		private static string GetDeviceProperty(string deviceInstanceId, NativeMethods.DEVPROPKEY prop)
		{
			string result = string.Empty;
			NativeMethods.SP_DEVINFO_DATA deviceInfoData = new NativeMethods.SP_DEVINFO_DATA();
			deviceInfoData.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(deviceInfoData);
			var dataBuffer = new byte[4096];
			ulong propertyType = 0;
			var requiredSize = 0;

			Guid hidGuid = new Guid();
			NativeMethods.HidD_GetHidGuid(ref hidGuid);
			IntPtr deviceInfoSet = NativeMethods.SetupDiGetClassDevs(ref hidGuid, deviceInstanceId, 0, NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);
			NativeMethods.SetupDiEnumDeviceInfo(deviceInfoSet, 0, ref deviceInfoData);
			if (NativeMethods.SetupDiGetDeviceProperty(deviceInfoSet, ref deviceInfoData, ref prop, ref propertyType,
					dataBuffer, dataBuffer.Length, ref requiredSize, 0))
			{
				result = dataBuffer.ToUTF16String();
			}

			if (deviceInfoSet.ToInt64() != NativeMethods.INVALID_HANDLE_VALUE)
			{
				NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);
			}

			return result;
		}

		public static IEnumerable<HidDevice> EnumerateHidControllersMatching(VendorIdProductIdInfo[] vendorIdProductIdInfoArray)
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

		// TODO looks like some of this code is redundant, maybe refactor later
		public static void ReEnableDevice(string deviceInstanceId)
		{

			bool success;
			var hidGuid = new Guid();
			NativeMethods.HidD_GetHidGuid(ref hidGuid);

			IntPtr deviceInfoSet = NativeMethods.SetupDiGetClassDevs(
				ref hidGuid,
				deviceInstanceId,
				0,
				NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE
			);
			NativeMethods.SP_DEVINFO_DATA deviceInfoData = new NativeMethods.SP_DEVINFO_DATA();
			deviceInfoData.cbSize = Marshal.SizeOf(deviceInfoData);

			success = NativeMethods.SetupDiEnumDeviceInfo(
				deviceInfoSet,
				0,
				ref deviceInfoData
			);

			if (!success)
			{
				throw new Exception("Error getting device info data, error code = " + Marshal.GetLastWin32Error());
			}

			success = NativeMethods.SetupDiEnumDeviceInfo(
				deviceInfoSet,
				1,
				ref deviceInfoData
			); // Checks that we have a unique device

			if (success)
			{
				throw new Exception("Can't find unique device");
			}

			NativeMethods.SP_PROPCHANGE_PARAMS propChangeParams = new NativeMethods.SP_PROPCHANGE_PARAMS();
			propChangeParams.classInstallHeader.cbSize = Marshal.SizeOf(propChangeParams.classInstallHeader);
			propChangeParams.classInstallHeader.installFunction = NativeMethods.DIF_PROPERTYCHANGE;
			propChangeParams.stateChange = NativeMethods.DICS_DISABLE;
			propChangeParams.scope = NativeMethods.DICS_FLAG_GLOBAL;
			propChangeParams.hwProfile = 0;

			success = NativeMethods.SetupDiSetClassInstallParams(
				deviceInfoSet,
				ref deviceInfoData,
				ref propChangeParams,
				Marshal.SizeOf(propChangeParams)
			);

			if (!success)
			{
				throw new Exception("Error setting class install params, error code = " + Marshal.GetLastWin32Error());
			}

			success = NativeMethods.SetupDiCallClassInstaller(NativeMethods.DIF_PROPERTYCHANGE, deviceInfoSet, ref deviceInfoData);
			// TEST: If previous SetupDiCallClassInstaller fails, just continue
			// otherwise device will likely get permanently disabled.
			/*
			if (!success)
			{
				throw new Exception("Error disabling device, error code = " + Marshal.GetLastWin32Error());
			}
			*/

			//System.Threading.Thread.Sleep(50);
			HidGameControllers.stopWatch.Restart();
			while (HidGameControllers.stopWatch.ElapsedMilliseconds < 100)
			{
				// Use SpinWait to keep control of current thread. Using Sleep could potentially
				// cause other events to get run out of order
				System.Threading.Thread.SpinWait(100);
			}
			HidGameControllers.stopWatch.Stop();

			propChangeParams.stateChange = NativeMethods.DICS_ENABLE;

			success = NativeMethods.SetupDiSetClassInstallParams(
				deviceInfoSet,
				ref deviceInfoData,
				ref propChangeParams,
				Marshal.SizeOf(propChangeParams)
			);

			if (!success)
			{
				throw new Exception("Error setting class install params, error code = " + Marshal.GetLastWin32Error());
			}

			success = NativeMethods.SetupDiCallClassInstaller(
				NativeMethods.DIF_PROPERTYCHANGE,
				deviceInfoSet,
				ref deviceInfoData
			);

			if (!success)
			{
				throw new Exception("Error enabling device, error code = " + Marshal.GetLastWin32Error());
			}

			//System.Threading.Thread.Sleep(50);
			/*sw.Restart();
			while (sw.ElapsedMilliseconds < 50)
			{
				// Use SpinWait to keep control of current thread. Using Sleep could potentially
				// cause other events to get run out of order
				System.Threading.Thread.SpinWait(100);
			}
			sw.Stop();
			*/

			NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);

		}

		public static event RequestElevationDelegate RequestElevation;

		private const int GAME_CONTROLLER_USAGE_PAGE = 0x05;

		// TODO does this needs to be internal? Can it be private
		internal const int SONY_VENDOR_ID = 0x054C;
		internal const int RAZER_VENDOR_ID = 0x1532;
		internal const int NACON_VENDOR_ID = 0x146B;
		internal const int HORI_VENDOR_ID = 0x0F0D;

		private const int SONY_WIRELESS_ADAPTER_PRODUCT_ID = 0xBA0;
		private const int DUALSHOCK4_V1_PRODUCT_ID = 0x5C4;
		private const int DUALSHOCK4_V2_PRODUCT_ID = 0x09CC;

		private static VendorIdProductIdInfo[] dualShock4CompatibleDevices =
		{
			new VendorIdProductIdInfo(SONY_VENDOR_ID,  SONY_WIRELESS_ADAPTER_PRODUCT_ID, "Sony Wireless Adapter"),
			new VendorIdProductIdInfo(SONY_VENDOR_ID,  DUALSHOCK4_V1_PRODUCT_ID, "DualShock 4 v.1"),
			new VendorIdProductIdInfo(SONY_VENDOR_ID,  DUALSHOCK4_V2_PRODUCT_ID, "DualShock 4 v.2"),

			new VendorIdProductIdInfo(RAZER_VENDOR_ID, 0x1000, "Razer Raiju PS4"),
			new VendorIdProductIdInfo(RAZER_VENDOR_ID, 0x1007, "Razer Raiju Tournament Edition"), // (wired)
			new VendorIdProductIdInfo(RAZER_VENDOR_ID, 0x1004, "Razer Raiju Ultimate Edition USB"), // (wired)
			new VendorIdProductIdInfo(RAZER_VENDOR_ID, 0x100A, "Razer Raiju Tournament Edition BT", VendorIdProductIdFeatureSet.OnlyInputData0x01 | VendorIdProductIdFeatureSet.OnlyOutputData0x05 | VendorIdProductIdFeatureSet.NoBatteryReading | VendorIdProductIdFeatureSet.NoGyroCalib), // Razer Raiju Tournament Edition (BT). Incoming report data is in "ds4 USB format" (32 bytes) in BT. Also, WriteOutput uses "usb" data packet type in BT.
			new VendorIdProductIdInfo(RAZER_VENDOR_ID, 0x1009, "Razer Raiju Ultimate Edition BT", VendorIdProductIdFeatureSet.OnlyInputData0x01 | VendorIdProductIdFeatureSet.OnlyOutputData0x05 | VendorIdProductIdFeatureSet.NoBatteryReading | VendorIdProductIdFeatureSet.NoGyroCalib), // Razer Raiju Ultimate Edition (BT)

			new VendorIdProductIdInfo(NACON_VENDOR_ID, 0x0D08, "Nacon Revolution Unlimited Pro"),
			new VendorIdProductIdInfo(NACON_VENDOR_ID, 0x0D10, "Nacon Revolution Infinite"), // Nacon Revolution Infinite (sometimes known as Revol Unlimited Pro v2?). Touchpad, gyro, rumble, "led indicator" lightbar.
			new VendorIdProductIdInfo(NACON_VENDOR_ID, 0x0D13, "Nacon Revol Pro v.3"),
			new VendorIdProductIdInfo(NACON_VENDOR_ID, 0x0D02, "Nacon Revol Pro v.2", VendorIdProductIdFeatureSet.NoGyroCalib), // Nacon Revolution Pro v1 and v2 don't support DS4 gyro calibration routines
			new VendorIdProductIdInfo(NACON_VENDOR_ID, 0x0D01, "Nacon Revol Pro v.1", VendorIdProductIdFeatureSet.NoGyroCalib),

			new VendorIdProductIdInfo(HORI_VENDOR_ID,  0x00EE, "Hori PS4 Mini"),
			new VendorIdProductIdInfo(HORI_VENDOR_ID,  0x0084, "Hori Fighting Cmd"), // Hori Fighting Commander (special kind of gamepad without touchpad or sticks. There is a hardware switch to alter d-pad type between dpad and LS/RS)
			new VendorIdProductIdInfo(HORI_VENDOR_ID,  0x0066, "Horipad FPS Plus", VendorIdProductIdFeatureSet.NoGyroCalib), // Horipad FPS Plus (wired only. No light bar, rumble and Gyro/Accel sensor. Cannot Hide "HID-compliant vendor-defined device" in USB Composite Device. Other feature works fine.)

			new VendorIdProductIdInfo(0x7545, 0x0104, "Armor 3 Level Up Cobra"),
			new VendorIdProductIdInfo(0x2E95, 0x7725, "Scuf Vantage"),
			new VendorIdProductIdInfo(0x11C0, 0x4001, "PS4 Fun"),
			new VendorIdProductIdInfo(SONY_VENDOR_ID,  0x05C5, "CronusMax (PS4 Mode)"), // CronusMax (PS4 Output Mode)
			new VendorIdProductIdInfo(0x0C12, 0x57AB, "Warrior Joypad JS083", VendorIdProductIdFeatureSet.NoGyroCalib), // Warrior Joypad JS083 (wired). Custom lightbar color doesn't work, but everything else works OK (except touchpad and gyro because the gamepad doesnt have those).
			new VendorIdProductIdInfo(0x0C12, 0x0E16, "Steel Play MetalTech") // Steel Play Metaltech P4 (wired)
		};

		public static bool isExclusiveMode = false; // TODO when does this changes to true?

		private static Dictionary<string, DualShock4Controller> Devices = new Dictionary<string, DualShock4Controller>();

		private static List<HidDevice> disabledDevices = new List<HidDevice>();

		private static HashSet<string> devicePaths = new HashSet<string>();

		private static HashSet<string> deviceSerials = new HashSet<string>();

		private static Stopwatch stopWatch = new Stopwatch();

	}

}
