using GameInterruptLibraryCSCore.Util;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

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

		public static byte[] BLUETOOTH__HEADER_FOR_CRC32 = { 0xA1 };

		public static byte[] BLUETOOTH_CALIBRATION_HEADER_FOR_CRC32 = { 0xA3 };

		public const int ACC_RES_PER_G = 8192; // TODO means 1G is 8192 (1 and a half byte) 0b0000_0000_0000 ??

		public const int GYRO_RES_IN_DEG_SEC = 16; // means 1 degree/second is 16 (4 bits) 0b0000 ??

		#endregion

		public DualShock4Controller(HidDevice hidGameController, string displayName, VendorIdProductIdFeatureSet featureSet = VendorIdProductIdFeatureSet.DefaultDS4)
		{

			this.hidGameController = hidGameController;
			this.connectionType = HidConnectionType(this.hidGameController);
			this.macAddress = this.hidGameController.ReadSerial(); // TODO rename this so it is clearer
			this.ExitOutputThread = false;

			if (!this.hidGameController.IsFileStreamOpen())
			{
				this.hidGameController.OpenFileStream(this.hidGameController.Capabilities.InputReportByteLength); // TODO check if this changes when in Bluetooth or USB
			}

			System.Diagnostics.Debug.WriteLine($" input report length: {this.hidGameController.Capabilities.InputReportByteLength}");

			if ((featureSet & VendorIdProductIdFeatureSet.NoGyroCalib) != VendorIdProductIdFeatureSet.NoGyroCalib) {
				this.UpdateCalibrationData(this.hidGameController);
			}

			// initialize the output report (don't force disconnect the gamepad on initialization even if writeData fails because some fake DS4 gamepads don't support writeData over BT)
			// this.SendOutputReport(true, true, false);

			this.StartUpdates();

		}

		public void StartUpdates()
		{

			if (this.inputReportThread == null)
			{
				/*if (this.connectionType == ConnectionType.Bluetooth)
				{
					this.outputReportThread = new Thread(this.PerformDs4Output);
					this.outputReportThread.Priority = ThreadPriority.Normal;
					this.outputReportThread.Name = "DS4 Output thread: " + this.macAddress;
					this.outputReportThread.IsBackground = true;
					this.outputReportThread.Start();

					/*timeoutCheckThread = new Thread(TimeoutTestThread);
					timeoutCheckThread.Priority = ThreadPriority.BelowNormal;
					timeoutCheckThread.Name = "DS4 Timeout thread: " + this.macAddress;
					timeoutCheckThread.IsBackground = true;
					timeoutCheckThread.Start();* /
				}
				else
				{
					//this.ds4Output = new Thread(OutReportCopy); // USB, but refactor this later
					this.outputReportThread = new Thread(this.PerformDs4Output);
					this.outputReportThread.Priority = ThreadPriority.Normal;
					this.outputReportThread.Name = "DS4 Arr Copy thread: " + this.macAddress;
					this.outputReportThread.IsBackground = true;
					this.outputReportThread.Start();
				}*/

				this.inputReportThread = new Thread(this.PerformDs4Input);
				//this.inputReportThread.Priority = ThreadPriority.AboveNormal;
				this.inputReportThread.Priority = ThreadPriority.Normal;
				this.inputReportThread.Name = "DS4 Input thread: " + this.macAddress;
				this.inputReportThread.IsBackground = true;
				this.inputReportThread.Start();
			}
			else
			{
				Console.WriteLine("Thread already running for DS4: " + this.macAddress);
			}

		}

		#region parse report from controller to host (input report)

		public void PerformDs4Input()
		{

			var bluetoothOffset = this.connectionType == ConnectionType.Bluetooth ? 2 : 0;
			NativeMethods.HidD_SetNumInputBuffers(this.hidGameController.SafeReadHandle.DangerousGetHandle(), 2); // TODO check what this does

			try
			{
				while (!this.ExitInputThread)
				{

					readWaitEv.Set();

					var report = new byte[this.hidGameController.Capabilities.InputReportByteLength]; // it is 500+, but this is only used when audio is transmitted, the actual values are 64 for buttons I think
					var success = this.hidGameController.ReadWithFileStream(report);

					readWaitEv.Wait();
					readWaitEv.Reset();

					// TODO check how this looks like for USB and probably there's a better way to poll this data
					if (!success)
					{
						Thread.Sleep(100);
						continue; // TODO I should give a timeout?
					}

					Thread.Sleep(100); // TODO for debugging purposes only

					/*
					usb
					====

					report[0] 0000_0001 *3 0x01 1 report protocol

					bluetooth
					=========

					report[0] 0001_0001 0x17 23 not in HID descriptor, but could be 0x01 or 0x11 too?

					report[1] 1100_0000 0xC0 192 | 0b10000_0000 is hid | 0b0100_0000 contains CRC | 0b0011_1111 bluetooth reporting interval, see table below
					report[2] 0000_0000 *3 0x00 0


					bluetooth with IMU
					==================

					report[0] 0001_0001 bt only 0x17 23 not in HID descriptor, but could be 0x11 too??

					report[1] 1100_0000 bt only 0xC0 192 | 0b10000_0000 is hid | 0b0100_0000 contains CRC | 0b0011_1111 bluetooth reporting interval, see table below
					report[2] 0000_0000 *3 0x00 0

					// note from DS4Windows
					// If the incoming data packet doesn't have the native DS4 type (0x11) in BT mode then the gamepad sends PC-friendly 0x01 data packets even in BT mode. Switch over to accept 0x01 data packets in BT mode.

					// note 2 from DS4Windows, but for a different report, not sure it it applies here
					The lower 6 bits of report[1] field of the Bluetooth report
					control the interval at which Dualshock 4 reports data:
					0x00 - 1ms
					0x01 - 1ms
					0x02 - 2ms
					0x3E - 62ms
					0x3F - disabled

					// Note from linux DS4 driver
					The default behavior of the Dualshock 4 is to send reports using report type 1 when running over Bluetooth. However, when feature
					report 2 is requested during the controller initialization it starts sending input reports in report 17. Since report 17 is undefined
					in the default HID descriptor, the HID layer won't generate events. While it is possible (and this was done before) to fixup the HID
					descriptor to add this mapping, it was better to do this manually. The reason is there were various pieces software both open and closed
					source, relying on the descriptors to be the same across various operating systems. If the descriptors wouldn't match some
					applications e.g. games on Wine would not be able to function due to different descriptors, which such applications are not parsing.
					*/

					// TODO put the constants somewhere else

					const byte REPORT_PROTOCOL_INPUT_SIMPLE = 0x01; // 0b0000_0001
					const byte REPORT_PROTOCOL_INPUT_IMU_11 = 0x11; // 0b0001_0001
					const byte REPORT_PROTOCOL_INPUT_IMU_17 = 0x17; // 0b0001_0001

					var reportProtocol = report[0];
					System.Diagnostics.Debug.WriteLine($"report protocol {Convert.ToString(reportProtocol, 2).PadLeft(8, '0')}");
					if (reportProtocol != REPORT_PROTOCOL_INPUT_SIMPLE
						&& reportProtocol != REPORT_PROTOCOL_INPUT_IMU_11
						&& reportProtocol != REPORT_PROTOCOL_INPUT_IMU_17
						)
					{
						continue;
					}

					/*switch (reportProtocol)
					{

						case REPORT_PROTOCOL_INPUT_SIMPLE:
							// check CRC of 10, 11, 12 and 13? // could't get this simple report
							var calibrationReportCrc32Seed = Crc32.Compute(
								ref DualShock4Controller.BLUETOOTH_CALIBRATION_HEADER_FOR_CRC32
							);
							var calibrationReportCrc32 = ~ Crc32.Compute( // note the ~ to flip the bits of the result
								ref calibrationFeatureReport,
								bufferLength: DS4_CALIBRATION_FEATURE_REPORT_LEN_BLUETOOTH,
								seed: calibrationReportCrc32Seed
							);
							break;

						case REPORT_PROTOCOL_INPUT_IMU_11:
							// fallthrough
						case REPORT_PROTOCOL_INPUT_IMU_17:
							// check CRC of 60, 61, 62 and 63 ??
							break;

						default:
							continue; // will continue act on the switch or loop? do I need

					}*/

					UInt32 reportCrc32 = report[10]
						| (UInt32)(report[11] << 8)
						| (UInt32)(report[12] << 16)
						| (UInt32)(report[13] << 24);

					var inputSimpleReportCrc32 = ~Crc32.Compute( // note the ~ to flip the bits of the result
						ref report,
						bufferLength: 10 //DS4_INPUT_SIMPLE_REPORT_LEN_BLUETOOTH
					);

					this.mainButtons = report[5 + bluetoothOffset];

					this.triangleButton = (this.mainButtons & 0b_1000_0000) == 0b_1000_0000;
					this.circleButton = (this.mainButtons & 0b_0100_0000) == 0b_0100_0000;
					this.squareButton = (this.mainButtons & 0b_0001_0000) == 0b_0001_0000;
					this.crossButton = (this.mainButtons & 0b_0010_0000) == 0b_0010_0000;

					this.directionalPad = (byte)(this.mainButtons & 0b_0000_1111); // TODO check if this cast works as expected
					/*
					this.upButton: (this.directionalPad == 0 || this.directionalPad == 1 || this.directionalPad == 7),
					this.rightButton: (this.directionalPad == 2 || this.directionalPad == 1 || this.directionalPad == 3),
					this.downButton: (this.directionalPad == 4 || this.directionalPad == 3 || this.directionalPad == 5),
					this.leftButton: (this.directionalPad == 6 || this.directionalPad == 5 || this.directionalPad == 7),
					*/

					this.secondaryButtons = report[6 + bluetoothOffset];

					this.l1 = (this.secondaryButtons & 0b_0000_0001) == 0b_0000_0001;
					this.r1 = (this.secondaryButtons & 0b_0000_0010) == 0b_0000_0010;
					this.l2 = (this.secondaryButtons & 0b_0000_0100) == 0b_0000_0100;
					this.r2 = (this.secondaryButtons & 0b_0000_1000) == 0b_0000_1000;

					this.l3 = (this.secondaryButtons & 0b_0100_0000) == 0b_0100_0000;
					this.r3 = (this.secondaryButtons & 0b_1000_0000) == 0b_1000_0000;

					this.shareButton = (this.secondaryButtons & 0b_0001_0000) == 0b_0001_0000;
					this.optionsButton = (this.secondaryButtons & 0b_0010_0000) == 0b_0010_0000;

					this.psButton = (report[7 + bluetoothOffset] & 0b_0000_0001) == 0b_0000_0001;

					this.reportIterator = (byte)(report[7 + bluetoothOffset] >> 2); // [7] 	Counter (counts up by 1 per report), I guess this is only relevant to bluetooth

					if (this.previousMainButtons != this.mainButtons
						|| this.previousSecondaryButtons != this.secondaryButtons
						|| this.previousPsButton != this.psButton
						|| this.previousTrackpadButton != this.trackpadButton
						) {

						System.Diagnostics.Debug.WriteLine($"square: {this.squareButton}");

						/*DispatchQueue.main.async {
							NotificationCenter.default.post(
								name: GamePadButtonChangedNotification.Name,
								object: GamePadButtonChangedNotification(
									leftTriggerButton: self.l2,
									leftShoulderButton: self.l1,
									minusButton: false,
									leftSideTopButton: false,
									leftSideBottomButton: false,
									upButton: (self.directionalPad == 0 || self.directionalPad == 1 || self.directionalPad == 7),
									rightButton: (self.directionalPad == 2 || self.directionalPad == 1 || self.directionalPad == 3),
									downButton: (self.directionalPad == 4 || self.directionalPad == 3 || self.directionalPad == 5),
									leftButton: (self.directionalPad == 6 || self.directionalPad == 5 || self.directionalPad == 7),
									socialButton: self.shareButton,
									leftStickButton: self.l3,
									trackPadButton: self.trackpadButton,
									centralButton: self.psButton,
									rightStickButton: self.r3,
									rightAuxiliaryButton: self.optionsButton,
									faceNorthButton: self.triangleButton,
									faceEastButton: self.circleButton,
									faceSouthButton: self.crossButton,
									faceWestButton: self.squareButton,
									rightSideBottomButton: false,
									rightSideTopButton: false,
									plusButton: false,
									rightShoulderButton: self.r1,
									rightTriggerButton: self.r2
								)
							)
						}*/

						this.previousMainButtons = this.mainButtons;

						this.previousSquareButton = this.squareButton;
						this.previousCrossButton = this.crossButton;
						this.previousCircleButton = this.circleButton;
						this.previousTriangleButton = this.triangleButton;

						this.previousDirectionalPad = this.directionalPad;

						this.previousSecondaryButtons = this.secondaryButtons;

						this.previousL1 = this.l1;
						this.previousR1 = this.r1;
						this.previousL2 = this.l2;
						this.previousR2 = this.r2;
						this.previousL3 = this.l3;
						this.previousR3 = this.r3;

						this.previousShareButton = this.shareButton;
						this.previousOptionsButton = this.optionsButton;

						this.previousPsButton = this.psButton;
						this.previousTrackpadButton = this.trackpadButton;

					}

					// analog buttons
					// origin left top
					this.leftStickX = report[1 + bluetoothOffset]; // 0 left
					this.leftStickY = report[2 + bluetoothOffset]; // 0 up
					this.rightStickX = report[3 + bluetoothOffset];
					this.rightStickY = report[4 + bluetoothOffset];
					this.leftTrigger = report[8 + bluetoothOffset]; // 0 - 255
					this.rightTrigger = report[9 + bluetoothOffset]; // 0 - 255

					if (this.previousLeftStickX != this.leftStickX
						|| this.previousLeftStickY != this.leftStickY
						|| this.previousRightStickX != this.rightStickX
						|| this.previousRightStickY != this.rightStickY
						|| this.previousLeftTrigger != this.leftTrigger
						|| this.previousRightTrigger != this.rightTrigger
						) {

						/*DispatchQueue.main.async {
							NotificationCenter.default.post(
								name: GamePadAnalogChangedNotification.Name,
								object: GamePadAnalogChangedNotification(
									leftStickX: Int16(this.leftStickX),
									leftStickY: Int16(this.leftStickY),
									rightStickX: Int16(this.rightStickX),
									rightStickY: Int16(this.rightStickY),
									leftTrigger: this.leftTrigger,
									rightTrigger: this.rightTrigger
								)
							)
						}*/

						this.previousLeftStickX = this.leftStickX;
						this.previousLeftStickY = this.leftStickY;
						this.previousRightStickX = this.rightStickX;
						this.previousRightStickY = this.rightStickY;
						this.previousLeftTrigger = this.leftTrigger;
						this.previousRightTrigger = this.rightTrigger;

					}

					// trackpad

					this.trackpadButton = (report[7 + bluetoothOffset] & 0b_0000_0010) == 0b_0000_0010;

					// gyro - not all reports will have this, need to see how to check before calculating them

					// TODO calibrate these values and check the x, y, z order

					this.gyroX = (Int16)(report[14 + bluetoothOffset] << 8) | (Int16)report[13 + bluetoothOffset];
					this.gyroY = (Int16)(report[16 + bluetoothOffset] << 8) | (Int16)report[15 + bluetoothOffset];
					this.gyroZ = (Int16)(report[18 + bluetoothOffset] << 8) | (Int16)report[17 + bluetoothOffset];

					this.accelX = (Int16)(report[20 + bluetoothOffset] << 8) | (Int16)report[19 + bluetoothOffset];
					this.accelY = (Int16)(report[22 + bluetoothOffset] << 8) | (Int16)report[21 + bluetoothOffset];
					this.accelZ = (Int16)(report[24 + bluetoothOffset] << 8) | (Int16)report[23 + bluetoothOffset];

					// battery

					this.cableConnected = ((report[30 + bluetoothOffset] >> 4) & 0b_0000_0001) == 1;
					this.batteryLevel = (byte)(report[30 + bluetoothOffset] & 0b_0000_1111);

					if (!this.cableConnected || this.batteryLevel > 10) {
						this.batteryCharging = false;
					}
					else
					{
						this.batteryCharging = true;
		  			}

					// on usb battery tanges from 0 to 9, but on bluetooth the range is 0 to 10
					if (!this.cableConnected && this.batteryLevel < 10) {
						this.batteryLevel += 1;
					}

					if (this.previousBatteryLevel != this.batteryLevel) {

						this.previousBatteryLevel = this.batteryLevel;
			
						/*DispatchQueue.main.async {
							NotificationCenter.default.post(
								name: GamePadBatteryChangedNotification.Name,
								object: GamePadBatteryChangedNotification(
									battery: self.batteryLevel,
									batteryMin: 0,
									batteryMax: 8
								)
							)
						}*/

					}

					/*
					[30] 	EXT/HeadSet/Earset: bitmask

					01111011 is headset with mic (0x7B)
					00111011 is headphones (0x3B)
					00011011 is nothing attached (0x1B)
					00001000 is bluetooth? (0x08)
					00000101 is ? (0x05)
					*/

				}
			}
			catch (ThreadInterruptedException ex)
			{
				// TODO
			}

		}

		public void HandleSixaxis(ref byte gyro, ref byte accel, /*DS4State state,*/ double elapsedDelta)
		{
			/*int currentYaw = (short)((ushort)(gyro[3] << 8) | gyro[2]);
			int currentPitch = (short)((ushort)(gyro[1] << 8) | gyro[0]);
			int currentRoll = (short)((ushort)(gyro[5] << 8) | gyro[4]);
			int AccelX = (short)((ushort)(accel[1] << 8) | accel[0]);
			int AccelY = (short)((ushort)(accel[3] << 8) | accel[2]);
			int AccelZ = (short)((ushort)(accel[5] << 8) | accel[4]);

			if (calibrationDone)
				applyCalibs(ref currentYaw, ref currentPitch, ref currentRoll, ref AccelX, ref AccelY, ref AccelZ);

			SixAxisEventArgs args = null;
			if (AccelX != 0 || AccelY != 0 || AccelZ != 0)
			{
				if (SixAccelMoved != null)
				{
					sPrev.copy(now);
					now.populate(currentYaw, currentPitch, currentRoll,
						AccelX, AccelY, AccelZ, elapsedDelta, sPrev);

					args = new SixAxisEventArgs(state.ReportTimeStamp, now);
					state.Motion = now;
					SixAccelMoved(this, args);
				}
			}*/
		}

		#endregion


		#region send report from host to controller (output report)

		public void PerformDs4Output()
		{

			/*tempStamp = (uint)((ushort)(inputReport[11] << 8) | inputReport[10]);
			if (timeStampInit == false)
			{
				timeStampInit = true;
				deltaTimeCurrent = tempStamp * 16u / 3u;
			}
			else if (timeStampPrevious > tempStamp)
			{
				tempDelta = ushort.MaxValue - timeStampPrevious + tempStamp + 1u;
				deltaTimeCurrent = tempDelta * 16u / 3u;
			}
			else
			{
				tempDelta = tempStamp - timeStampPrevious;
				deltaTimeCurrent = tempDelta * 16u / 3u;
			}

			timeStampPrevious = tempStamp;
			elapsedDeltaTime = 0.000001 * deltaTimeCurrent; // Convert from microseconds to seconds
			cState.elapsedTime = elapsedDeltaTime;
			cState.totalMicroSec = pState.totalMicroSec + deltaTimeCurrent;

			// has a, infinite while
			// Store Gyro and Accel values
			//Array.Copy(inputReport, 13, gyro, 0, 6);
			//Array.Copy(inputReport, 19, accel, 0, 6);
			fixed (byte* pbInput = &inputReport[13], pbGyro = gyro, pbAccel = accel)
			{
				for (int i = 0; i < 6; i++)
				{
					pbGyro[i] = pbInput[i];
				}

				for (int i = 6; i < 12; i++)
				{
					pbAccel[i - 6] = pbInput[i];
				}

				sixAxis.handleSixaxis(ref pbGyro, ref pbAccel, cState, elapsedDeltaTime);
			}*/

			try
			{
				while (!this.ExitOutputThread)
				{
					/*if (currentRumble)
					{
						lock (outputReport)
						{
							result = writeOutput();
						}

						currentRumble = false;
						if (!result)
						{
							currentRumble = true;
							exitOutputThread = true;
							int thisError = Marshal.GetLastWin32Error();
							if (lastError != thisError)
							{
								Console.WriteLine(Mac.ToString() + " " + System.DateTime.UtcNow.ToString("o") + "> encountered write failure: " + thisError);
								//Log.LogToGui(Mac.ToString() + " encountered write failure: " + thisError, true);
								lastError = thisError;
							}
						}
					}

					if (!currentRumble)
					{
						lastError = 0;
						lock (outReportBuffer)
						{
							Monitor.Wait(outReportBuffer);
							fixed (byte* byteR = outputReport, byteB = outReportBuffer)
							{
								for (int i = 0, arlen = BT_OUTPUT_CHANGE_LENGTH; i < arlen; i++)
									byteR[i] = byteB[i];
							}
							//outReportBuffer.CopyTo(outputReport, 0);
							if (outputPendCount > 1)
								outputPendCount--;
							else if (outputPendCount == 1)
							{
								outputPendCount--;
								standbySw.Restart();
							}
							else
								standbySw.Restart();
						}

						currentRumble = true;
					}*/
				}
			}
			catch (ThreadInterruptedException ex)
			{
				// TODO
			}

		}

		#endregion

		#region gyroscope calibration

		public void UpdateCalibrationData(HidDevice hidGameController)
		{
			const int DS4_CALIBRATION_FEATURE_REPORT_HEADER_USB = 0x02;
			const int DS4_CALIBRATION_FEATURE_REPORT_LEN_USB = 37; // TODO not sure about this

			const int DS4_CALIBRATION_FEATURE_REPORT_HEADER_BLUETOOTH = 0x05;
			const int DS4_CALIBRATION_FEATURE_REPORT_LEN_BLUETOOTH = 37;
			const int DS4_CALIBRATION_FEATURE_REPORT_LEN_BLUETOOTH_WITH_CRC32 = DS4_CALIBRATION_FEATURE_REPORT_LEN_BLUETOOTH + 4;

			const byte NUMBER_OF_TRIES = 5;

			var calibrationFeatureReport = new byte[DS4_CALIBRATION_FEATURE_REPORT_LEN_BLUETOOTH_WITH_CRC32];
			calibrationFeatureReport[0] = DS4_CALIBRATION_FEATURE_REPORT_HEADER_BLUETOOTH;

			if (this.connectionType == ConnectionType.Bluetooth)
			{
				calibrationFeatureReport[0] = DS4_CALIBRATION_FEATURE_REPORT_HEADER_USB;
				hidGameController.ReadFeatureData(ref calibrationFeatureReport);
			}
			else
			{
				hidGameController.ReadFeatureData(ref calibrationFeatureReport, DS4_CALIBRATION_FEATURE_REPORT_LEN_USB);
			}

			if (this.connectionType == ConnectionType.Bluetooth)
			{
	
				for (int tries = 1; tries < NUMBER_OF_TRIES; tries++) // TODO improve with iterators instead of checking limits
				{

					// little endian TODO BitConverter https://docs.microsoft.com/pt-br/dotnet/api/system.bitconverter.toint32?view=netcore-3.1
					UInt32 reportCrc32 = calibrationFeatureReport[DS4_CALIBRATION_FEATURE_REPORT_LEN_BLUETOOTH]
						| (UInt32)(calibrationFeatureReport[DS4_CALIBRATION_FEATURE_REPORT_LEN_BLUETOOTH + 1] << 8)
						| (UInt32)(calibrationFeatureReport[DS4_CALIBRATION_FEATURE_REPORT_LEN_BLUETOOTH + 2] << 16)
						| (UInt32)(calibrationFeatureReport[DS4_CALIBRATION_FEATURE_REPORT_LEN_BLUETOOTH + 3] << 24);

					/*
					// linux line 1628
					crc = crc32_le(0xFFFFFFFF, DualShock4Controller.BLUETOOTH_CALIBRATION_HEADER_FOR_CRC32, 1);
					crc = ~ crc32_le(crc, buf, DS4_CALIBRATION_FEATURE_REPORT_LEN_BLUETOOTH);
					report_crc = get_unaligned_le32(&buf[DS4_CALIBRATION_FEATURE_REPORT_LEN_BLUETOOTH]);

					// ds4windows
					var calcCrc32 = ~ Crc32Algorithm.Compute(DualShock4Controller.BLUETOOTH_CALIBRATION_HEADER_FOR_CRC32);
					calcCrc32 = ~ Crc32Algorithm.CalculateBasicHash(ref calcCrc32, ref calibrationFeatureReport, 0, DS4_CALIBRATION_FEATURE_REPORT_LEN_BLUETOOTH);
					*/

					var calibrationReportCrc32Seed = Crc32.Compute(
						ref DualShock4Controller.BLUETOOTH_CALIBRATION_HEADER_FOR_CRC32
					);
					var calibrationReportCrc32 = ~ Crc32.Compute( // note the ~ to flip the bits of the result
						ref calibrationFeatureReport,
						bufferLength: DS4_CALIBRATION_FEATURE_REPORT_LEN_BLUETOOTH,
						seed: calibrationReportCrc32Seed
					);

					break; // FIXME figure out which bytes are the CRC validation, the last ones are all 0
					if (reportCrc32 == calibrationReportCrc32)
					{
						break;
					}

					if (tries >= 5)
					{
						System.Diagnostics.Debug.WriteLine("Gyro Calibration Failed"); // TODO show in gui
						return;
					}

					hidGameController.ReadFeatureData(ref calibrationFeatureReport);
				}

				/*if (hidGameController.Attributes.ProductId == 0x5C4 && hidGameController.Attributes.VendorId == 0x054C && sixAxis.fixupInvertedGyroAxis())
				{
					System.Diagnostics.Debug.WriteLine($"Automatically fixed inverted YAW gyro axis in DS4 v.1 BT gamepad ({this.macAddress})");
				}*/

			}

			this.ParseCalibrationFeatureReport(
				ref calibrationFeatureReport,
				this.connectionType == ConnectionType.Usb
			);

		}

		public void ParseCalibrationFeatureReport(ref byte[] calibrationReport, bool fromUSB)
		{

			// gyroscopes

			int pitchPlus = 0;
			int pitchMinus = 0;
			int yawPlus = 0;
			int yawMinus = 0;
			int rollPlus = 0;
			int rollMinus = 0;

			if (!fromUSB)
			{
				pitchPlus  = (short)((ushort)(calibrationReport[8]  << 8) | calibrationReport[7]);
				yawPlus    = (short)((ushort)(calibrationReport[10] << 8) | calibrationReport[9]);
				rollPlus   = (short)((ushort)(calibrationReport[12] << 8) | calibrationReport[11]);
				pitchMinus = (short)((ushort)(calibrationReport[14] << 8) | calibrationReport[13]);
				yawMinus   = (short)((ushort)(calibrationReport[16] << 8) | calibrationReport[15]);
				rollMinus  = (short)((ushort)(calibrationReport[18] << 8) | calibrationReport[17]);
			}
			else
			{
				pitchPlus  = (short)((ushort)(calibrationReport[8]  << 8) | calibrationReport[7]);
				pitchMinus = (short)((ushort)(calibrationReport[10] << 8) | calibrationReport[9]);
				yawPlus    = (short)((ushort)(calibrationReport[12] << 8) | calibrationReport[11]);
				yawMinus   = (short)((ushort)(calibrationReport[14] << 8) | calibrationReport[13]);
				rollPlus   = (short)((ushort)(calibrationReport[16] << 8) | calibrationReport[15]);
				rollMinus  = (short)((ushort)(calibrationReport[18] << 8) | calibrationReport[17]);
			}

			this.calibration[Calibration.GyroPitchIndex].plusValue = pitchPlus;
			this.calibration[Calibration.GyroPitchIndex].minusValue = pitchMinus;

			this.calibration[Calibration.GyroYawIndex].plusValue = yawPlus;
			this.calibration[Calibration.GyroYawIndex].minusValue = yawMinus;

			this.calibration[Calibration.GyroRollIndex].plusValue = rollPlus;
			this.calibration[Calibration.GyroRollIndex].minusValue = rollMinus;

			this.calibration[Calibration.GyroPitchIndex].sensorBias = (short)((ushort)(calibrationReport[2] << 8) | calibrationReport[1]);
			this.calibration[Calibration.GyroYawIndex].sensorBias   = (short)((ushort)(calibrationReport[4] << 8) | calibrationReport[3]);
			this.calibration[Calibration.GyroRollIndex].sensorBias  = (short)((ushort)(calibrationReport[6] << 8) | calibrationReport[5]);

			this.gyroSpeedPlus  = (short)((ushort)(calibrationReport[20] << 8) | calibrationReport[19]);
			this.gyroSpeedMinus = (short)((ushort)(calibrationReport[22] << 8) | calibrationReport[21]);
			this.gyroSpeed2x = gyroSpeedPlus + gyroSpeedMinus;

			// accelerometers

			var accelXPlus  = (short)((ushort)(calibrationReport[24] << 8) | calibrationReport[23]);
			var accelXMinus = (short)((ushort)(calibrationReport[26] << 8) | calibrationReport[25]);

			var accelYPlus  = (short)((ushort)(calibrationReport[28] << 8) | calibrationReport[27]);
			var accelYMinus = (short)((ushort)(calibrationReport[30] << 8) | calibrationReport[29]);

			var accelZPlus  = (short)((ushort)(calibrationReport[32] << 8) | calibrationReport[31]);
			var accelZMinus = (short)((ushort)(calibrationReport[34] << 8) | calibrationReport[33]);

			this.calibration[Calibration.AccelXIndex].plusValue   = accelXPlus;
			this.calibration[Calibration.AccelXIndex].minusValue  = accelXMinus;

			this.calibration[Calibration.AccelYIndex].plusValue   = accelYPlus;
			this.calibration[Calibration.AccelYIndex].minusValue  = accelYMinus;

			this.calibration[Calibration.AccelZIndex].plusValue   = accelZPlus;
			this.calibration[Calibration.AccelZIndex].minusValue  = accelZMinus;

			// TODO why does this need to be an integer?
			this.calibration[Calibration.AccelXIndex].sensorBias = accelXPlus - ((accelXPlus - accelXMinus) / 2);
			this.calibration[Calibration.AccelYIndex].sensorBias = accelYPlus - ((accelYPlus - accelYMinus) / 2);
			this.calibration[Calibration.AccelZIndex].sensorBias = accelZPlus - ((accelZPlus - accelZMinus) / 2);

		}

		private void ApplyCalibs(
			ref int yaw, ref int pitch, ref int roll,
			ref int accelX, ref int accelY, ref int accelZ
		) {

			pitch = DualShock4Controller.ApplyGyroCalibration(
				pitch,
				this.calibration[Calibration.GyroPitchIndex].sensorBias,
				this.gyroSpeed2x,
				sensorResolution: GYRO_RES_IN_DEG_SEC,
				sensorRange: this.calibration[Calibration.GyroPitchIndex].plusValue - this.calibration[Calibration.GyroPitchIndex].minusValue
			);

			yaw = DualShock4Controller.ApplyGyroCalibration(
				yaw,
				this.calibration[Calibration.GyroYawIndex].sensorBias,
				this.gyroSpeed2x,
				sensorResolution: GYRO_RES_IN_DEG_SEC,
				sensorRange: this.calibration[Calibration.GyroYawIndex].plusValue - this.calibration[Calibration.GyroYawIndex].minusValue
			);

			roll = DualShock4Controller.ApplyGyroCalibration(
				roll,
				this.calibration[Calibration.GyroRollIndex].sensorBias,
				this.gyroSpeed2x,
				sensorResolution: GYRO_RES_IN_DEG_SEC,
				sensorRange: this.calibration[Calibration.GyroRollIndex].plusValue - this.calibration[Calibration.GyroRollIndex].minusValue
			);

			accelX = DualShock4Controller.ApplyAccelCalibration(
				accelX,
				this.calibration[Calibration.AccelXIndex].sensorBias,
				sensorResolution: ACC_RES_PER_G,
				sensorRange: this.calibration[Calibration.AccelXIndex].plusValue - this.calibration[Calibration.AccelXIndex].minusValue
			);

			accelY = DualShock4Controller.ApplyAccelCalibration(
				accelY,
				this.calibration[Calibration.AccelYIndex].sensorBias,
				sensorResolution: ACC_RES_PER_G,
				sensorRange: this.calibration[Calibration.AccelYIndex].plusValue - this.calibration[Calibration.AccelYIndex].minusValue
			);

			accelZ = DualShock4Controller.ApplyAccelCalibration(
				accelZ,
				this.calibration[Calibration.AccelZIndex].sensorBias,
				sensorResolution: ACC_RES_PER_G,
				sensorRange: this.calibration[Calibration.AccelZIndex].plusValue - this.calibration[Calibration.AccelZIndex].minusValue
			);

		}

		private static int ApplyGyroCalibration(int sensorRawValue, int sensorBias, int gyroSpeed2x, int sensorResolution, int sensorRange)
		{
			int calibratedValue = 0; // TODO not sure why I would need this to be an integer

			// plus and minus values are symmetrical, so bias is also 0
			if (sensorRange == 0)
			{
				calibratedValue = sensorRawValue * gyroSpeed2x * sensorResolution;
				return calibratedValue;
			}

			calibratedValue = ((sensorRawValue - sensorBias) * gyroSpeed2x * sensorResolution) / sensorRange;
			return calibratedValue;
		}

		private static int ApplyAccelCalibration(int sensorRawValue, int sensorBias, int sensorResolution, int sensorRange)
		{
			int calibratedValue = 0; // TODO not sure why I would need this to be an integer

			// plus and minus values are symmetrical, so bias is also 0
			if (sensorRange == 0)
			{
				calibratedValue = sensorRawValue * 2 * sensorResolution;
				return calibratedValue;
			}

			calibratedValue = ((sensorRawValue - sensorBias) * 2 * sensorResolution) / sensorRange;
			return calibratedValue;
		}

		private int gyroSpeedPlus = 0;
		private int gyroSpeedMinus = 0;
		private int gyroSpeed2x = 0;

		// TODO change this to a struct or object with properties, array with indexes is kind of ugly
		private Calibration[] calibration = {
			new Calibration(),
			new Calibration(),
			new Calibration(),
			new Calibration(),
			new Calibration(),
			new Calibration()
		};

		private bool calibrationDone = false;

		#endregion

		// public event ReportHandler<EventArgs> Report = null; // TODO not sure I'll do this with events, probably a queue

		private HidDevice hidGameController; // TODO maybe make this readonly

		private ConnectionType connectionType;

		private string macAddress;

		private ManualResetEventSlim readWaitEv = new ManualResetEventSlim();

		#region input and output reports

		private Thread? inputReportThread;

		public bool ExitInputThread
		{
			get;
			private set;
		}

		private Thread? outputReportThread;

		public bool ExitOutputThread
		{
			get;
			private set;
		}

		//private Thread timeoutCheckThread;

		/// contains triangle, circle, cross, square and directional pad buttons
		byte mainButtons = 0;
		byte previousMainButtons = 8; // dpad neutral position is 8

		// top button
		bool triangleButton = false;
		bool previousTriangleButton = false;

		// right button
		bool circleButton = false;
		bool previousCircleButton = false;

		// bottom button
		bool crossButton = false;
		bool previousCrossButton = false;

		// left button
		bool squareButton = false;
		bool previousSquareButton = false;

		byte directionalPad = 0;
		byte previousDirectionalPad = 0;

		/// contains the shoulder buttons, triggers (digital input), thumbstick buttons, share and options buttons
		byte secondaryButtons = 0;
		byte previousSecondaryButtons = 0;

		// shoulder buttons
		bool l1 = false;
		bool previousL1 = false;
		bool r1 = false;
		bool previousR1 = false;
		/// digital reading for left trigger
		/// for the analog reading see leftTrigger
		bool l2 = false;
		bool previousL2 = false;
		/// digital reading for right trigger
		/// for the analog reading see rightTrigger
		bool r2 = false;
		bool previousR2 = false;

		// thumbstick buttons
		bool l3 = false;
		bool previousL3 = false;
		bool r3 = false;
		bool previousR3 = false;

		// other buttons

		bool shareButton = false;
		bool previousShareButton = false;
		bool optionsButton = false;
		bool previousOptionsButton = false;

		bool psButton = false;
		bool previousPsButton = false;

		// analog buttons

		byte leftStickX = 0; // TODO transform to Int16 because of xbox? or do this in the notification?
		byte previousLeftStickX = 0;
		byte leftStickY = 0;
		byte previousLeftStickY = 0;
		byte rightStickX = 0;
		byte previousRightStickX = 0;
		byte rightStickY = 0;
		byte previousRightStickY = 0;

		byte leftTrigger = 0;
		byte previousLeftTrigger = 0;
		byte rightTrigger = 0;
		byte previousRightTrigger = 0;

		// trackpad

		bool trackpadButton = false;
		bool previousTrackpadButton = false;

		bool trackpadTouch0IsActive = false;
		bool previousTrackpadTouch0IsActive = false;
		byte trackpadTouch0Id = 0;
		byte trackpadTouch0X = 0;
		byte trackpadTouch0Y = 0;

		bool trackpadTouch1IsActive = false;
		bool previousTrackpadTouch1IsActive = false;
		byte trackpadTouch1Id = 0;
		byte trackpadTouch1X = 0;
		byte trackpadTouch1Y = 0;

		// inertial measurement unit

		float gyroX = 0;
		float gyroY = 0;
		float gyroZ = 0;

		float accelX = 0;
		float accelY = 0;
		float accelZ = 0;

		float rotationZ = 0;

		// battery

		bool cableConnected = false;
		bool batteryCharging = false;
		byte batteryLevel = 0; // 0 to 9 on USB, 0 - 10 on Bluetooth
		byte previousBatteryLevel = 0;

		// misc

		byte reportIterator = 0;
		byte previousReportIterator = 0;

		#endregion

	}

	// TODO see how to use this for Xbox
	public enum ConnectionType : byte
	{
		Usb,
		SonyWirelessAdapter,
		Bluetooth
	};

	// TODO make this a struct maybe?
	internal class Calibration
	{
		public const int GyroPitchIndex = 0;
		public const int GyroYawIndex = 1;
		public const int GyroRollIndex = 2;
		public const int AccelXIndex = 3;
		public const int AccelYIndex = 4;
		public const int AccelZIndex = 5;

		public int plusValue;
		public int minusValue;
		public int sensorBias;
	}

}
