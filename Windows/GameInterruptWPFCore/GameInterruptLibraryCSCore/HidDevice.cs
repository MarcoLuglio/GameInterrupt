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

	}

}
