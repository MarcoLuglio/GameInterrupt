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

		public const int ACC_RES_PER_G = 8192;

		public const int GYRO_RES_IN_DEG_SEC = 16;

		#endregion

		public DualShock4Controller(HidDevice hidGameController, string displayName, VendorIdProductIdFeatureSet featureSet = VendorIdProductIdFeatureSet.DefaultDS4)
		{

			// TODO
			this.hidGameController = hidGameController;
			this.connectionType = HidConnectionType(this.hidGameController);
			this.macAddress = this.hidGameController.ReadSerial(); // TODO rename this so it is clearer
			this.ExitOutputThread = false;

			if (!this.hidGameController.IsFileStreamOpen())
			{
				this.hidGameController.OpenFileStream(this.hidGameController.Capabilities.InputReportByteLength); // TODO check if this changes when in Bluetooth or USB
			}

			// TODO give the option to skip calibration if we are not interested in gyro events...
			this.RefreshCalibration(this.hidGameController);

			// sendOutputReport(true, true, false); // initialize the output report (don't force disconnect the gamepad on initialization even if writeData fails because some fake DS4 gamepads don't support writeData over BT)
			this.StartUpdate();

		}

		public void StartUpdate()
		{
			//this.inputReportErrorCount = 0;

			if (this.inputReportThread == null)
			{
				if (this.connectionType == ConnectionType.Bluetooth)
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
					timeoutCheckThread.Start();*/
				}
				else
				{
					//this.ds4Output = new Thread(OutReportCopy); // USB, but refactor this later
					this.outputReportThread = new Thread(this.PerformDs4Output);
					this.outputReportThread.Priority = ThreadPriority.Normal;
					this.outputReportThread.Name = "DS4 Arr Copy thread: " + this.macAddress;
					this.outputReportThread.IsBackground = true;
					this.outputReportThread.Start();
				}

				this.inputReportThread = new Thread(this.PerformDs4Input);
				this.inputReportThread.Priority = ThreadPriority.AboveNormal;
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

			try
			{
				while (!this.ExitInputThread)
				{

					var report = new byte[64];
					var success = this.hidGameController.ReadWithFileStream(report);

					if (!success)
					{
						Thread.Sleep(100);
						continue; // TODO I should give a timeout
					}

					// TODO check how this looks like for USB and probably there's a better way to poll this data
					if (report[0] == 0 || report[0] == 128)
					{
						continue;
					}

					this.mainButtons = report[5 + bluetoothOffset];

					this.triangleButton = (this.mainButtons & 0b10000000) == 0b10000000;
					this.circleButton = (this.mainButtons & 0b01000000) == 0b01000000;
					this.squareButton = (this.mainButtons & 0b00010000) == 0b00010000;
					this.crossButton = (this.mainButtons & 0b00100000) == 0b00100000;

					this.directionalPad = (byte)(this.mainButtons & 0b00001111); // TODO check if this cast works as expected
					/*
					this.upButton: (this.directionalPad == 0 || this.directionalPad == 1 || this.directionalPad == 7),
					this.rightButton: (this.directionalPad == 2 || this.directionalPad == 1 || this.directionalPad == 3),
					this.downButton: (this.directionalPad == 4 || this.directionalPad == 3 || this.directionalPad == 5),
					this.leftButton: (this.directionalPad == 6 || this.directionalPad == 5 || this.directionalPad == 7),
					*/

					this.secondaryButtons = report[6 + bluetoothOffset];

					this.l1 = (this.secondaryButtons & 0b00000001) == 0b00000001;
					this.r1 = (this.secondaryButtons & 0b00000010) == 0b00000010;
					this.l2 = (this.secondaryButtons & 0b00000100) == 0b00000100;
					this.r2 = (this.secondaryButtons & 0b00001000) == 0b00001000;

					this.l3 = (this.secondaryButtons & 0b01000000) == 0b01000000;
					this.r3 = (this.secondaryButtons & 0b10000000) == 0b10000000;

					this.shareButton = (this.secondaryButtons & 0b00010000) == 0b00010000;
					this.optionsButton = (this.secondaryButtons & 0b00100000) == 0b00100000;

					this.psButton = (report[7 + bluetoothOffset] & 0b00000001) == 0b00000001;

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

					this.trackpadButton = (report[7 + bluetoothOffset] & 0b00000010) == 0b00000010;

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

		public void RefreshCalibration(HidDevice hidGameController)
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

			this.SetCalibrationData(
				ref calibrationFeatureReport,
				this.connectionType == ConnectionType.Usb
			);

		}

		private void ApplyCalibs(
			ref int yaw, ref int pitch, ref int roll,
			ref int accelX, ref int accelY, ref int accelZ
		) {

			/*CalibData current = this.calibrationData[0];
			temInt = pitch - current.bias;
			pitch = temInt = (int)(temInt * (current.sensNumer / (float)current.sensDenom));

			current = this.calibrationData[1];
			temInt = yaw - current.bias;
			yaw = temInt = (int)(temInt * (current.sensNumer / (float)current.sensDenom));

			current = this.calibrationData[2];
			temInt = roll - current.bias;
			roll = temInt = (int)(temInt * (current.sensNumer / (float)current.sensDenom));

			current = this.calibrationData[3];
			temInt = accelX - current.bias;
			accelX = temInt = (int)(temInt * (current.sensNumer / (float)current.sensDenom));

			current = this.calibrationData[4];
			temInt = accelY - current.bias;
			accelY = temInt = (int)(temInt * (current.sensNumer / (float)current.sensDenom));

			current = this.calibrationData[5];
			temInt = accelZ - current.bias;
			accelZ = temInt = (int)(temInt * (current.sensNumer / (float)current.sensDenom));*/

		}

		public void SetCalibrationData(ref byte[] calibrationReport, bool fromUSB)
		{

			this.calibration[0].bias = (short)((ushort)(calibrationReport[2] << 8) | calibrationReport[1]);
			this.calibration[1].bias = (short)((ushort)(calibrationReport[4] << 8) | calibrationReport[3]);
			this.calibration[2].bias = (short)((ushort)(calibrationReport[6] << 8) | calibrationReport[5]);

			int pitchPlus;
			int pitchMinus;
			int yawPlus;
			int yawMinus;
			int rollPlus;
			int rollMinus;

			if (!fromUSB)
			{
				pitchPlus = (short)((ushort)(calibrationReport[8] << 8) | calibrationReport[7]);
				yawPlus = (short)((ushort)(calibrationReport[10] << 8) | calibrationReport[9]);
				rollPlus = (short)((ushort)(calibrationReport[12] << 8) | calibrationReport[11]);
				pitchMinus = (short)((ushort)(calibrationReport[14] << 8) | calibrationReport[13]);
				yawMinus = (short)((ushort)(calibrationReport[16] << 8) | calibrationReport[15]);
				rollMinus = (short)((ushort)(calibrationReport[18] << 8) | calibrationReport[17]);
			}
			else
			{
				pitchPlus = (short)((ushort)(calibrationReport[8] << 8) | calibrationReport[7]);
				pitchMinus = (short)((ushort)(calibrationReport[10] << 8) | calibrationReport[9]);
				yawPlus = (short)((ushort)(calibrationReport[12] << 8) | calibrationReport[11]);
				yawMinus = (short)((ushort)(calibrationReport[14] << 8) | calibrationReport[13]);
				rollPlus = (short)((ushort)(calibrationReport[16] << 8) | calibrationReport[15]);
				rollMinus = (short)((ushort)(calibrationReport[18] << 8) | calibrationReport[17]);
			}

			// gyroscope

			var gyroSpeedPlus = (short)((ushort)(calibrationReport[20] << 8) | calibrationReport[19]);
			var gyroSpeedMinus = (short)((ushort)(calibrationReport[22] << 8) | calibrationReport[21]);

			var gyroSpeed2x = gyroSpeedPlus + gyroSpeedMinus;
			this.calibration[0].sensNumer = gyroSpeed2x * GYRO_RES_IN_DEG_SEC;
			this.calibration[0].sensDenom = pitchPlus - pitchMinus;

			this.calibration[1].sensNumer = gyroSpeed2x * GYRO_RES_IN_DEG_SEC;
			this.calibration[1].sensDenom = yawPlus - yawMinus;

			this.calibration[2].sensNumer = gyroSpeed2x * GYRO_RES_IN_DEG_SEC;
			this.calibration[2].sensDenom = rollPlus - rollMinus;

			// acceleration

			var accelXPlus = (short)((ushort)(calibrationReport[24] << 8) | calibrationReport[23]);
			var accelXMinus = (short)((ushort)(calibrationReport[26] << 8) | calibrationReport[25]);

			var accelYPlus = (short)((ushort)(calibrationReport[28] << 8) | calibrationReport[27]);
			var accelYMinus = (short)((ushort)(calibrationReport[30] << 8) | calibrationReport[29]);

			var accelZPlus = (short)((ushort)(calibrationReport[32] << 8) | calibrationReport[31]);
			var accelZMinus = (short)((ushort)(calibrationReport[34] << 8) | calibrationReport[33]);

			var accelRange = accelXPlus - accelXMinus;
			this.calibration[3].bias = accelXPlus - accelRange / 2;
			this.calibration[3].sensNumer = 2 * ACC_RES_PER_G;
			this.calibration[3].sensDenom = accelRange;

			accelRange = accelYPlus - accelYMinus;
			this.calibration[4].bias = accelYPlus - accelRange / 2;
			this.calibration[4].sensNumer = 2 * ACC_RES_PER_G;
			this.calibration[4].sensDenom = accelRange;

			accelRange = accelZPlus - accelZMinus;
			this.calibration[5].bias = accelZPlus - accelRange / 2;
			this.calibration[5].sensNumer = 2 * ACC_RES_PER_G;
			this.calibration[5].sensDenom = accelRange;

			// Check that denom will not be zero.
			this.calibrationDone = this.calibration[0].sensDenom != 0
				&& this.calibration[1].sensDenom != 0
				&& this.calibration[2].sensDenom != 0
				&& accelRange != 0;

		}

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

		#region input and output reports

		private Thread inputReportThread;

		public bool ExitInputThread
		{
			get;
			private set;
		}

		private Thread outputReportThread;

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

		public int bias;
		public int sensNumer;
		public int sensDenom;
	}

}
