using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Threading.Tasks;



// Originally from DS4Windows project
namespace GameInterruptLibraryCSCore
{

	public sealed class HidDevice
	{

		#region static

		private static SafeFileHandle OpenHandle(String devicePathName, Boolean isExclusive, bool enumerate)
		{
			SafeFileHandle hidHandle;
			uint access = enumerate ? 0 : NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE; // TODO 0 means no access??

			if (isExclusive)
			{
				hidHandle = NativeMethods.CreateFile(
					lpFileName: devicePathName,
					dwDesiredAccess: access,
					dwShareMode: 0, // TODO magic number
					lpSecurityAttributes: IntPtr.Zero, // pointer to nothing - no securitiy attributes
					dwCreationDisposition: NativeMethods.OpenExisting,
					dwFlagsAndAttributes: 0x20000000 | 0x80000000 | 0x100 | NativeMethods.FILE_FLAG_OVERLAPPED, // TODO magic bytes
					hTemplateFile: 0
				);
			}
			else
			{
				hidHandle = NativeMethods.CreateFile(
					lpFileName: devicePathName,
					dwDesiredAccess: access,
					dwShareMode: NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
					lpSecurityAttributes: IntPtr.Zero, // pointer to nothing - no securitiy attributes
					dwCreationDisposition: NativeMethods.OpenExisting,
					dwFlagsAndAttributes: 0x20000000 | 0x80000000 | 0x100 | NativeMethods.FILE_FLAG_OVERLAPPED, // TODO magic bytes
					hTemplateFile: 0
				);
			}

			return hidHandle;
		}

		public const string blankSerial = "00:00:00:00:00:00";

		#endregion

		internal HidDevice(string devicePath, string description = null)
		{
			this.devicePath = devicePath;
			this.description = description;

			try
			{
				var hidHandle = OpenHandle(this.devicePath, isExclusive: false, enumerate: true);

				// this.deviceAttributes = GetDeviceAttributes(hidHandle);
				// this.deviceCapabilities = GetDeviceCapabilities(hidHandle);

				hidHandle.Close();
			}
			catch (Exception exception)
			{
				System.Diagnostics.Debug.WriteLine(exception.Message);
				throw new Exception(string.Format("Error querying HID device '{0}'.", devicePath), exception);
			}
		}

		public void OpenDevice(bool isExclusive)
		{
			if (this.IsOpen)
			{
				return;
			}

			try
			{
				if (this.SafeReadHandle == null || this.SafeReadHandle.IsInvalid)
				{
					this.SafeReadHandle = OpenHandle(this.devicePath, isExclusive, enumerate: false);
				}
			}
			catch (Exception exception)
			{
				IsOpen = false;
				throw new Exception("Error opening HID device.", exception);
			}

			this.IsOpen = !this.SafeReadHandle.IsInvalid;
			this.IsExclusive = isExclusive;
		}

		public bool ReadFeatureData(ref byte[] inputBuffer)
		{
			return NativeMethods.HidD_GetFeature(this.SafeReadHandle.DangerousGetHandle(), inputBuffer, inputBuffer.Length);
		}

		public bool WriteOutputReportViaInterrupt(byte[] outputBuffer, int timeout)
		{
			try
			{
				if (this.SafeReadHandle == null)
				{
					this.SafeReadHandle = OpenHandle(this.devicePath, isExclusive: true, enumerate: false);
				}
				if (this.FileStream == null && !this.SafeReadHandle.IsInvalid)
				{
					this.FileStream = new FileStream(this.SafeReadHandle, FileAccess.ReadWrite, outputBuffer.Length, true);
				}
				if (this.FileStream != null && this.FileStream.CanWrite && !this.SafeReadHandle.IsInvalid)
				{
					this.FileStream.Write(outputBuffer, 0, outputBuffer.Length);
					return true;
				}
				else
				{
					return false;
				}
			}
			catch (Exception)
			{
				return false;
			}

		}

		public bool WriteAsyncOutputReportViaInterrupt(byte[] outputBuffer)
		{
			try
			{
				if (this.SafeReadHandle == null)
				{
					this.SafeReadHandle = OpenHandle(this.devicePath, isExclusive: true, enumerate: false);
				}
				if (this.FileStream == null && !this.SafeReadHandle.IsInvalid)
				{
					this.FileStream = new FileStream(this.SafeReadHandle, FileAccess.ReadWrite, outputBuffer.Length, true);
				}
				if (this.FileStream != null && this.FileStream.CanWrite && !this.SafeReadHandle.IsInvalid)
				{
					Task writeTask = this.FileStream.WriteAsync(outputBuffer, 0, outputBuffer.Length);
					return true;
				}
				else
				{
					return false;
				}
			}
			catch (Exception)
			{
				return false;
			}

		}

		public void OpenFileStream(int reportSize)
		{
			if (this.FileStream == null && !this.SafeReadHandle.IsInvalid)
			{
				this.FileStream = new FileStream(this.SafeReadHandle, FileAccess.ReadWrite, reportSize, true);
			}
		}

		public bool IsFileStreamOpen()
		{
			bool result = false;
			if (this.FileStream != null)
			{
				result = !this.FileStream.SafeFileHandle.IsInvalid && !this.FileStream.SafeFileHandle.IsClosed;
			}

			return result;
		}

		public string ReadSerial()
		{
			if (this.serial != null)
			{
				return this.serial;
			}

			// FIXME this logic only works for DualShock4
			// Some devices don't have MAC address (especially gamepads with USB only suports in PC). If the serial number reading fails 
			// then use dummy zero MAC address, because there is a good chance the gamepad stll works in DS4Windows app (the code would throw
			// an index out of bounds exception anyway without IF-THEN-ELSE checks after trying to read a serial number).

			if (this.Capabilities.InputReportByteLength == 64)
			{
				byte[] buffer = new byte[16];
				buffer[0] = 18; // TODO give a name to this report
				if (this.ReadFeatureData(ref buffer))
				{
					this.serial = String.Format(
						"{0:X02}:{1:X02}:{2:X02}:{3:X02}:{4:X02}:{5:X02}",
						buffer[6], // 0
						buffer[5], // 1
						buffer[4], // 2
						buffer[3], // 3
						buffer[2], // 4
						buffer[1]  // 5
					);
				}
			}
			else
			{
				byte[] buffer = new byte[126];
#if WIN64
				ulong bufferLen = 126;
#else
				uint bufferLen = 126;
#endif
				if (NativeMethods.HidD_GetSerialNumberString(this.SafeReadHandle.DangerousGetHandle(), buffer, bufferLen))
				{
					string MACAddr = System.Text.Encoding.Unicode.GetString(buffer).Replace("\0", string.Empty).ToUpper();
					MACAddr = $"{MACAddr[0]}{MACAddr[1]}:{MACAddr[2]}{MACAddr[3]}:{MACAddr[4]}{MACAddr[5]}:{MACAddr[6]}{MACAddr[7]}:{MACAddr[8]}{MACAddr[9]}:{MACAddr[10]}{MACAddr[11]}";
					this.serial = MACAddr;
				}
			}

			// If serial# reading failed then generate a dummy MAC address based on HID device path (WinOS generated runtime unique value based on connected usb port and hub or BT channel).
			// The device path remains the same as long the gamepad is always connected to the same usb/BT port, but may be different in other usb ports. Therefore this value is unique
			// as long the same device is always connected to the same usb port.
			if (this.serial == null)
			{
				string MACAddr = string.Empty;

				// TODO figure a way to show this
				// AppLogger.LogToGui($"WARNING: Failed to read serial# from a gamepad ({this.Attributes.VendorHexId}/{this.Attributes.ProductHexId}). Generating MAC address from a device path. From now on you should connect this gamepad always into the same USB port or BT pairing host to keep the same device path.", true);

				try
				{
					// Substring: \\?\hid#vid_054c&pid_09cc&mi_03#7&1f882A25&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030} -> \\?\hid#vid_054c&pid_09cc&mi_03#7&1f882A25&0&0001#
					int endPos = this.DevicePath.LastIndexOf('{');
					if (endPos < 0)
						endPos = this.DevicePath.Length;

					// String array: \\?\hid#vid_054c&pid_09cc&mi_03#7&1f882A25&0&0001# -> [0]=\\?\hidvid_054c, [1]=pid_09cc, [2]=mi_037, [3]=1f882A25, [4]=0, [5]=0001
					string[] devPathItems = this.DevicePath.Substring(0, endPos).Replace("#", "").Replace("-", "").Replace("{", "").Replace("}", "").Split('&');

					if (devPathItems.Length >= 3)
					{
						MACAddr = devPathItems[devPathItems.Length - 3].ToUpper()                   // 1f882A25
								  + devPathItems[devPathItems.Length - 2].ToUpper()                 // 0
								  + devPathItems[devPathItems.Length - 1].TrimStart('0').ToUpper(); // 0001 -> 1
					}
					else if (devPathItems.Length >= 1)
					{
						// Device and usb hub and port identifiers missing in devicePath string. Fallback to use vendor and product ID values and 
						// take a number from the last part of the devicePath. Hopefully the last part is a usb port number as it usually should be.
						MACAddr = this.Attributes.VendorId.ToString("X4")
								  + this.Attributes.ProductId.ToString("X4")
								  + devPathItems[devPathItems.Length - 1].TrimStart('0').ToUpper();
					}

					if (!string.IsNullOrEmpty(MACAddr))
					{
						MACAddr = MACAddr.PadRight(12, '0');
						this.serial = $"{MACAddr[0]}{MACAddr[1]}:{MACAddr[2]}{MACAddr[3]}:{MACAddr[4]}{MACAddr[5]}:{MACAddr[6]}{MACAddr[7]}:{MACAddr[8]}{MACAddr[9]}:{MACAddr[10]}{MACAddr[11]}";
					}
					else
					{
						// Hmm... Shold never come here. Strange format in devicePath because all identifier items of devicePath string are missing.
						this.serial = HidDevice.blankSerial;
					}
				}
				catch (Exception e)
				{
					// TODO figure a way to show this
					//AppLogger.LogToGui($"ERROR: Failed to generate runtime MAC address from device path {this.DevicePath}. {e.Message}", true);
					this.serial = HidDevice.blankSerial;
				}
			}

			return serial;
		}

		public bool IsOpen { get; private set; }

		public bool IsExclusive { get; private set; }

		public SafeFileHandle SafeReadHandle { get; private set; }

		public FileStream FileStream { get; private set; }

		public string Description { get { return this.description; } }

		public string DevicePath { get { return this.devicePath; } }

		public HidDeviceAttributes Attributes { get; }

		public HidDeviceCapabilities Capabilities { get; }

		private readonly string description;

		private readonly string devicePath;

		private string serial = null;

	}

}
