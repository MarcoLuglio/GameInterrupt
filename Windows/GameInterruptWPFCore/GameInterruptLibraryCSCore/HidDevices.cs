using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;



// Originally from DS4Windows project
namespace GameInterruptLibraryCSCore
{
	public static class HidDevices
	{

		public static IEnumerable<DeviceInfo> EnumerateHidDevices()
		{
			var devices = new List<DeviceInfo>();
			var hidClass = HidDevices.HidClassGuid;
			var deviceInfoSet = NativeMethods.SetupDiGetClassDevs(
				classGuid: ref hidClass,
				enumerator: null,
				hwndParent: 0,
				flags: NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE
			);

			if (deviceInfoSet.ToInt64() != NativeMethods.INVALID_HANDLE_VALUE)
			{
				var deviceInfoData = CreateDeviceInfoData();
				var deviceIndex = 0;

				while (NativeMethods.SetupDiEnumDeviceInfo(deviceInfoSet, deviceIndex, ref deviceInfoData))
				{
					deviceIndex += 1;

					var deviceInterfaceData = new NativeMethods.SP_DEVICE_INTERFACE_DATA();
					deviceInterfaceData.cbSize = Marshal.SizeOf(deviceInterfaceData);
					var deviceInterfaceIndex = 0;

					while (NativeMethods.SetupDiEnumDeviceInterfaces(deviceInfoSet, ref deviceInfoData, ref hidClass, deviceInterfaceIndex, ref deviceInterfaceData))
					{
						deviceInterfaceIndex++;
						var devicePath = GetDevicePath(deviceInfoSet, deviceInterfaceData);
						var description = GetBusReportedDeviceDescription(deviceInfoSet, ref deviceInfoData) ??
										  GetDeviceDescription(deviceInfoSet, ref deviceInfoData);
						devices.Add(new DeviceInfo {
							Path = devicePath,
							Description = description
						});
					}
				}

				NativeMethods.SetupDiDestroyDeviceInfoList(deviceInfoSet);

			}

			return devices;
		}

		private static NativeMethods.SP_DEVINFO_DATA CreateDeviceInfoData()
		{
			var deviceInfoData = new NativeMethods.SP_DEVINFO_DATA();

			deviceInfoData.cbSize = Marshal.SizeOf(deviceInfoData);
			deviceInfoData.DevInst = 0;
			deviceInfoData.ClassGuid = Guid.Empty;
			deviceInfoData.Reserved = IntPtr.Zero;

			return deviceInfoData;
		}

		/// <summary>
		/// Document how that looks like for the controllers
		/// </summary>
		/// <param name="deviceInfoSet"></param>
		/// <param name="deviceInterfaceData"></param>
		/// <returns></returns>
		private static string GetDevicePath(IntPtr deviceInfoSet, NativeMethods.SP_DEVICE_INTERFACE_DATA deviceInterfaceData)
		{
			var bufferSize = 0;
			const int intPtrByteSize32Bits = 4;
			const int intPtrByteSize64Bits = 8;
			int deviceInterfaceDetailDataSize = intPtrByteSize64Bits;
			if (IntPtr.Size == intPtrByteSize32Bits)
			{
				deviceInterfaceDetailDataSize = intPtrByteSize32Bits + Marshal.SystemDefaultCharSize;
			}
			var deviceInterfaceDetailData = new NativeMethods.SP_DEVICE_INTERFACE_DETAIL_DATA { Size = deviceInterfaceDetailDataSize };

			NativeMethods.SetupDiGetDeviceInterfaceDetailBuffer(
				deviceInfoSet,
				ref deviceInterfaceData,
				deviceInterfaceDetailData: IntPtr.Zero, // no details
				deviceInterfaceDetailDataSize: 0,
				requiredSize: ref bufferSize,
				deviceInfoData: IntPtr.Zero
			);

			var success = NativeMethods.SetupDiGetDeviceInterfaceDetail(
				deviceInfoSet,
				ref deviceInterfaceData,
				ref deviceInterfaceDetailData,
				deviceInterfaceDetailDataSize: bufferSize,
				requiredSize: ref bufferSize,
				deviceInfoData: IntPtr.Zero
			);

			if (success)
			{
				return deviceInterfaceDetailData.DevicePath;
			}

			return null;

		}

		private static string GetBusReportedDeviceDescription(IntPtr deviceInfoSet, ref NativeMethods.SP_DEVINFO_DATA devinfoData)
		{
			var descriptionBuffer = new byte[1024];

			if (Environment.OSVersion.Version.Major > 5) // why??
			{
				ulong propertyType = 0;
				var requiredSize = 0;

				var _continue = NativeMethods.SetupDiGetDeviceProperty(
					deviceInfoSet,
					ref devinfoData,
					ref NativeMethods.DEVPKEY_Device_BusReportedDeviceDesc,
					ref propertyType,
					descriptionBuffer,
					descriptionBuffer.Length,
					ref requiredSize,
					0
				);

				if (_continue)
				{
					return descriptionBuffer.ToUTF16String();
				}
			}

			return null;
		}

		/// <summary>
		/// Document how that looks like for the controllers
		/// </summary>
		/// <param name="deviceInfoSet"></param>
		/// <param name="devinfoData"></param>
		/// <returns></returns>
		private static string GetDeviceDescription(IntPtr deviceInfoSet, ref NativeMethods.SP_DEVINFO_DATA devinfoData)
		{
			var descriptionBuffer = new byte[1024];

			var requiredSize = 0;
			var type = 0;

			NativeMethods.SetupDiGetDeviceRegistryProperty(
				deviceInfoSet,
				ref devinfoData,
				NativeMethods.SPDRP_DEVICEDESC,
				ref type,
				descriptionBuffer,
				descriptionBuffer.Length,
				ref requiredSize
			);

			return descriptionBuffer.ToUTF8String();
		}

		/// <summary>
		/// Document how that looks like for the controllers
		/// Hid class should probably look something like this: {745a17a0-74d3-11d0-b6fe-00a0c90f57da}
		/// </summary>
		private static Guid HidClassGuid
		{
			get
			{
				if (lazyHidClassGuid.Equals(Guid.Empty))
				{
					NativeMethods.HidD_GetHidGuid(ref lazyHidClassGuid);
				}
				return lazyHidClassGuid;
			}
		}

		private static Guid lazyHidClassGuid = Guid.Empty;

	}

}
