using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Threading.Tasks;



// Originally from DS4Windows project
namespace GameInterruptLibraryCSCore
{

	public sealed class HidDevice
	{

		private static SafeFileHandle OpenHandle(String devicePathName, Boolean isExclusive, bool enumerate)
		{
			SafeFileHandle hidHandle;
			uint access = enumerate ? 0 : NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE;

			if (isExclusive)
			{
				hidHandle = NativeMethods.CreateFile(
					lpFileName: devicePathName,
					dwDesiredAccess: access,
					dwShareMode: 0,
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

		public bool WriteOutputReportViaInterrupt(byte[] outputBuffer, int timeout)
		{
			try
			{
				if (this.safeReadHandle == null)
				{
					this.safeReadHandle = OpenHandle(this.devicePath, isExclusive: true, enumerate: false);
				}
				if (this.fileStream == null && !this.safeReadHandle.IsInvalid)
				{
					this.fileStream = new FileStream(this.safeReadHandle, FileAccess.ReadWrite, outputBuffer.Length, true);
				}
				if (this.fileStream != null && this.fileStream.CanWrite && !this.safeReadHandle.IsInvalid)
				{
					this.fileStream.Write(outputBuffer, 0, outputBuffer.Length);
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
				if (this.safeReadHandle == null)
				{
					this.safeReadHandle = OpenHandle(this.devicePath, isExclusive: true, enumerate: false);
				}
				if (this.fileStream == null && !this.safeReadHandle.IsInvalid)
				{
					this.fileStream = new FileStream(this.safeReadHandle, FileAccess.ReadWrite, outputBuffer.Length, true);
				}
				if (this.fileStream != null && this.fileStream.CanWrite && !this.safeReadHandle.IsInvalid)
				{
					Task writeTask = this.fileStream.WriteAsync(outputBuffer, 0, outputBuffer.Length);
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

		public SafeFileHandle safeReadHandle { get; private set; }

		public FileStream fileStream { get; private set; }

		private readonly string description;

		private readonly string devicePath;

		// private readonly HidDeviceAttributes deviceAttributes;

		// private readonly HidDeviceCapabilities deviceCapabilities;

	}

}
