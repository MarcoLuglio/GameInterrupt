using GameInterruptLibraryCSCore.Controllers;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GameInterruptLibraryCSCore
{

	public sealed class ControlService
	{

		public bool Start(bool showlog = true)
		{
			/*
			// inside scputil
			public static bool getUseExclusiveMode()
			{
				return m_Config.useExclusiveMode; // attached to hide DS4 controller
			} 
			*/
			HidGameControllers.isExclusiveMode = false; // getUseExclusiveMode();

			/*if (showlog)
			{
				LogDebug(DS4WinWPF.Properties.Resources.SearchingController);
				LogDebug(DS4Devices.isExclusiveMode ? DS4WinWPF.Properties.Resources.UsingExclusive : DS4WinWPF.Properties.Resources.UsingShared);
			}*/

			try
			{
				// Assign Initial Devices
				/*foreach (OutSlotDevice slotDevice in outputslotMan.OutputSlots)
				{
					if (slotDevice.CurrentReserveStatus ==
						OutSlotDevice.ReserveStatus.Permanent)
					{
						OutputDevice outDevice = EstablishOutDevice(0, slotDevice.PermanentType);
						outputslotMan.DeferredPlugin(outDevice, -1, outputDevices, slotDevice.PermanentType);
					}
				}*/

				HidGameControllers.FindControllers();
				var devices = HidGameControllers.Devices.Values;

				//DS4LightBar.defaultLight = false;

				int i = 0;
				for (var devEnum = devices.GetEnumerator(); devEnum.MoveNext(); i++)
				{
					var device = devEnum.Current;

					/*if (showlog)
						LogDebug(DS4WinWPF.Properties.Resources.FoundController + " " + device.GetMacAddress() + " (" + device.GetConnectionType() + ") (" +
							device.DisplayName + ")");*/

					Task task = new Task(() => {
						Thread.Sleep(5);
						// WarnExclusiveModeFailure(device); // just log in the UI
					});
					task.Start();

					//DS4Controllers[i] = device;
					//slotManager.AddController(device, i);
					/*device.Removal += this.On_DS4Removal;
					device.Removal += DS4Devices.On_Removal;
					device.SyncChange += this.On_SyncChange;
					device.SyncChange += DS4Devices.UpdateSerial;
					device.SerialChange += this.On_SerialChange;
					device.ChargingChanged += CheckQuickCharge;*/

					//device.LightBarColor = getMainColor(i);

					/*if (!getDInputOnly(i) && device.isSynced())
					{
						//useDInputOnly[i] = false;
						PluginOutDev(i, device);

					}
					else
					{
						useDInputOnly[i] = true;
						Global.activeOutDevType[i] = OutContType.None;
					}*/





					/*int tempIdx = i;
					device.Report += (sender, e) =>
					{
						this.On_Report(sender, e, tempIdx);
					};*/




					/*DualShock4Controller.ReportHandler<EventArgs> tempEvnt = (sender, args) =>
					{
						DualShockPadMeta padDetail = new DualShockPadMeta();
						GetPadDetailForIdx(tempIdx, ref padDetail);
					};
					device.MotionEvent = tempEvnt;

					if (_udpServer != null)
					{
						device.Report += tempEvnt;
					}*/



					// device.StartUpdate(); // TODO constructor already does this



					/*
					if (showlog)
					{
						if (File.Exists(appdatapath + "\\Profiles\\" + ProfilePath[i] + ".xml"))
						{
							string prolog = DS4WinWPF.Properties.Resources.UsingProfile.Replace("*number*", (i + 1).ToString()).Replace("*Profile name*", ProfilePath[i]);
							LogDebug(prolog);
							AppLogger.LogToTray(prolog);
						}
						else
						{
							string prolog = DS4WinWPF.Properties.Resources.NotUsingProfile.Replace("*number*", (i + 1).ToString());
							LogDebug(prolog);
							AppLogger.LogToTray(prolog);
						}
					}

					if (i >= 4) // out of Xinput devices!
						break;
					*/
				}
			}
			catch (Exception exception)
			{
				System.Diagnostics.Debug.WriteLine($"{exception.Message}");
			}

			//running = true;

			// TODO
			/*
			runHotPlug = true;
			ServiceStarted?.Invoke(this, EventArgs.Empty);
			RunningChanged?.Invoke(this, EventArgs.Empty);
			*/

			return true;

		}

	}

}
