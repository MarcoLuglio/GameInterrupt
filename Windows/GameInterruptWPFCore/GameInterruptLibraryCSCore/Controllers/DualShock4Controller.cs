using System;
using System.Collections.Generic;
using System.Text;



namespace GameInterruptLibraryCSCore.Controllers
{

	public sealed class DualShock4Controller
	{

		#region static

		public static ConnectionType HidConnectionType(HidDevice hidGameController)
		{
			ConnectionType result = ConnectionType.Usb;
			if (hidGameController.Capabilities.InputReportByteLength == 64)
			{
				if (hidGameController.Capabilities.NumberFeatureDataIndices == 22)
				{
					result = ConnectionType.SonyWirelessAdapter;
				}
			}
			else
			{
				result = ConnectionType.Bluetooth;
			}

			return result;
		}

		#endregion

		public DualShock4Controller(HidDevice hidGameController, string displayName, VendorIdProductIdFeatureSet featureSet = VendorIdProductIdFeatureSet.DefaultDS4)
		{

			// TODO
			this.ExitOutputThread = false;

		}

		public bool ExitOutputThread
		{
			get;
			private set;
		}

	}

	// TODO see how to use this for Xbox
	public enum ConnectionType : byte
	{
		Bluetooth,
		SonyWirelessAdapter,
		Usb
	}; // Prioritize Bluetooth when both BT and USB are connected.

}
